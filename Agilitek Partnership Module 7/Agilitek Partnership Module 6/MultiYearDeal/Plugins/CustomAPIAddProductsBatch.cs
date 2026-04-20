using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Workflows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MultiYearDeal.Plugins
{
    // -------------------------------------------------------------------------
    // ats_AddProductsBatch — OLI-level bulk create for the Agreement Cart.
    // -------------------------------------------------------------------------
    //
    // V3 (20-Apr-2026) — key changes vs. V2:
    //
    //   * Processing unit is the OLI (opportunity-product row), not the
    //     "product request + its components per season" group. The timer is
    //     checked BETWEEN batches of OLIs so a single big package no longer
    //     swallows the whole 90 s budget in one atomic transaction.
    //
    //   * Batch size is controlled by the D365 environment variable
    //     `ats_OliBatchSize` (default 1, clamped to [1, 100]). Values > 1
    //     commit via `CreateMultipleRequest` in chunks of that size; value 1
    //     uses plain `service.Create` per row for finest-grained progress
    //     and per-row failure capture.
    //
    //   * Every OLI gets a pre-assigned GUID (`OliSpec.oliId`) BEFORE any
    //     create happens. Package components' `ats_packagelineitem` carries
    //     the main OLI's pre-assigned id, so parent/child linkage survives
    //     being split across HTTP round-trips.
    //
    //   * Recalculation is NO LONGER done here. The plugin only creates
    //     OLIs; it reports `touchedOpportunityIds` so the PCF can call the
    //     new `ats_RecalculateOpportunities` endpoint after all OLI
    //     creation is done.
    //
    //   * The plugin accepts EITHER `products` (first call — expands to
    //     OLIs server-side) OR `olis` (resume — uses the pre-resolved
    //     specs as-is). The discriminator is which input is non-empty.
    //
    // Contract:
    //   Input parameters (strings):
    //     agreementId   : GUID of the ats_agreement.
    //     products      : JSON array of ProductRequest. Used on first call.
    //     olis          : JSON array of OliSpec. Used on resume. Wins if both present.
    //     softTimeoutMs : optional int override of the default 90 s.
    //
    //   Output parameter:
    //     response      : JSON string:
    //     {
    //       success: bool,
    //       agreementId: string,
    //       processedOliCount: int,              // committed in THIS call
    //       totalOliCount: int,                  // size of THIS call's input
    //       createdOpportunityProductIds: [string],
    //       failedOlis: [{ oliId, productId, reason }],
    //       leftoverOlis: [OliSpec],
    //       touchedOpportunityIds: [string],     // opps that got OLIs in THIS call
    //       message?: string,
    //       errorCode?: "ibs_missing" | "rate_missing" | "input_invalid" | "transaction_failed"
    //     }
    //
    // Ordering guarantee:
    //   When products are expanded to OLIs, the main OLI of a package is
    //   always inserted into the list BEFORE its components. The batch
    //   loop preserves insertion order inside a `CreateMultipleRequest`
    //   EntityCollection, so the platform sees parent-before-children.
    //   Even if a package splits across two batches, the main's batch
    //   commits first → the component's batch finds the parent persisted.
    //
    // Failure semantics (per-batch):
    //   * oliBatchSize == 1:
    //       single `service.Create` fails → that OLI lands in `failedOlis`,
    //       loop continues.
    //   * oliBatchSize >  1:
    //       `CreateMultipleRequest` is all-or-nothing per batch. If the
    //       batch throws, EVERY OLI in that batch is recorded in
    //       `failedOlis` with a `"sibling row in batch failed: ..."` prefix
    //       so the operator can tell which rows were victims vs root cause.
    //       The loop continues with the next batch.
    //
    // Forward-progress guarantee:
    //   At least one BATCH is processed per call before the soft-timeout
    //   can yield. This is what keeps the PCF's no-progress guard happy
    //   when pre-loop prep (bulk IBS/Rate resolution) is slow.
    // -------------------------------------------------------------------------
    public class CustomAPIAddProductsBatch : IPlugin
    {
        private const int RateTypeSeason = 114300000;
        private const int RateTypeIndividual = 114300001;
        private const int DefaultSoftTimeoutMs = 90_000; // 1 min 30 s
        private const int DefaultOliBatchSize = 1;
        private const int MaxOliBatchSize = 100;
        private const string EnvVarOliBatchSize = "ats_OliBatchSize";

        public void Execute(IServiceProvider serviceProvider)
        {
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var pluginContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(pluginContext.UserId);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "AddProductsBatch v3: begin");

                string agreementIdStr = GetInputString(pluginContext, "agreementId");
                string productsJson = GetInputString(pluginContext, "products");
                string olisJson = GetInputString(pluginContext, "olis");
                int softTimeoutMs = GetInputInt(pluginContext, "softTimeoutMs", DefaultSoftTimeoutMs);

                int oliBatchSize = Math.Max(1, Math.Min(
                    EnvVarReader.ReadInt(service, EnvVarOliBatchSize, DefaultOliBatchSize),
                    MaxOliBatchSize));

                TraceHelper.Trace(tracingService,
                    "AddProductsBatch v3: oliBatchSize={0} (env var {1}; TTL cache) softTimeoutMs={2}",
                    oliBatchSize, EnvVarOliBatchSize, softTimeoutMs);

                if (string.IsNullOrWhiteSpace(agreementIdStr) || !Guid.TryParse(agreementIdStr, out var agreementId))
                {
                    Respond(pluginContext, new BatchResult
                    {
                        success = false,
                        agreementId = agreementIdStr,
                        errorCode = "input_invalid",
                        message = "agreementId is missing or not a valid GUID."
                    });
                    return;
                }

                List<OliSpec> oliSpecs;

                // Discriminator: olis wins if present (resume path); else expand products (first-call path).
                if (!string.IsNullOrWhiteSpace(olisJson) && olisJson != "[]")
                {
                    try
                    {
                        oliSpecs = JsonSerializer.Deserialize<List<OliSpec>>(olisJson)
                                   ?? new List<OliSpec>();
                    }
                    catch (Exception ex)
                    {
                        Respond(pluginContext, new BatchResult
                        {
                            success = false,
                            agreementId = agreementIdStr,
                            errorCode = "input_invalid",
                            message = "olis JSON could not be deserialized: " + ex.Message
                        });
                        return;
                    }
                    TraceHelper.Trace(tracingService,
                        "AddProductsBatch v3: resume path — received {0} OLI spec(s)",
                        oliSpecs.Count);
                }
                else
                {
                    List<ProductRequest> requests;
                    try
                    {
                        requests = JsonSerializer.Deserialize<List<ProductRequest>>(productsJson ?? "[]")
                                   ?? new List<ProductRequest>();
                    }
                    catch (Exception ex)
                    {
                        Respond(pluginContext, new BatchResult
                        {
                            success = false,
                            agreementId = agreementIdStr,
                            errorCode = "input_invalid",
                            message = "products JSON could not be deserialized: " + ex.Message
                        });
                        return;
                    }

                    if (requests.Count == 0)
                    {
                        Respond(pluginContext, EmptyResult(agreementIdStr));
                        return;
                    }

                    // Expand products → OLIs (first-call path). This runs the bulk
                    // IBS/Rate resolve once and pre-assigns every OLI's GUID.
                    try
                    {
                        oliSpecs = ExpandProductsToOlis(
                            service, agreementId, requests, tracingService);
                    }
                    catch (InvalidOperationException invEx)
                    {
                        // IBS / Rate not resolvable — abort cleanly.
                        Respond(pluginContext, new BatchResult
                        {
                            success = false,
                            agreementId = agreementIdStr,
                            errorCode = invEx.Message.StartsWith("Rate ", StringComparison.OrdinalIgnoreCase)
                                ? "rate_missing" : "ibs_missing",
                            message = invEx.Message
                        });
                        return;
                    }

                    TraceHelper.Trace(tracingService,
                        "AddProductsBatch v3: first-call path — expanded {0} product request(s) → {1} OLI spec(s)",
                        requests.Count, oliSpecs.Count);
                }

                if (oliSpecs.Count == 0)
                {
                    Respond(pluginContext, EmptyResult(agreementIdStr));
                    return;
                }

                // ---- Commit OLIs in batches until timer yields ----
                var createdIds = new List<string>();
                var failedOlis = new List<FailedOli>();
                var touchedOppIds = new HashSet<string>();
                List<OliSpec> leftover = null;
                int processedCount = 0;

                int cursor = 0;
                while (cursor < oliSpecs.Count)
                {
                    // Forward-progress guard: only yield AFTER at least one batch committed.
                    if (cursor > 0 && stopwatch.ElapsedMilliseconds >= softTimeoutMs)
                    {
                        leftover = oliSpecs.GetRange(cursor, oliSpecs.Count - cursor);
                        TraceHelper.Trace(tracingService,
                            "AddProductsBatch v3: soft-timeout at cursor={0}, yielding {1} leftover OLI(s)",
                            cursor, leftover.Count);
                        break;
                    }

                    int take = Math.Min(oliBatchSize, oliSpecs.Count - cursor);
                    var batch = oliSpecs.GetRange(cursor, take);

                    if (take == 1)
                    {
                        ProcessSingle(service, batch[0], createdIds, failedOlis, touchedOppIds, tracingService);
                    }
                    else
                    {
                        ProcessBulk(service, batch, createdIds, failedOlis, touchedOppIds, tracingService);
                    }

                    processedCount += take;
                    cursor += take;
                }

                Respond(pluginContext, new BatchResult
                {
                    success = failedOlis.Count == 0,
                    agreementId = agreementIdStr,
                    processedOliCount = processedCount,
                    totalOliCount = oliSpecs.Count,
                    createdOpportunityProductIds = createdIds,
                    failedOlis = failedOlis,
                    leftoverOlis = leftover ?? new List<OliSpec>(),
                    touchedOpportunityIds = touchedOppIds.ToList(),
                    message = failedOlis.Count > 0
                        ? string.Format("{0} OLI(s) failed; remainder committed.", failedOlis.Count)
                        : null,
                    errorCode = failedOlis.Count > 0 ? "transaction_failed" : null
                });

                TraceHelper.Trace(tracingService,
                    "AddProductsBatch v3: processed={0}/{1} created={2} failed={3} touchedOpps={4} leftover={5} elapsed={6}ms",
                    processedCount, oliSpecs.Count,
                    createdIds.Count, failedOlis.Count, touchedOppIds.Count,
                    leftover?.Count ?? 0,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracingService.Trace("AddProductsBatch v3: unexpected exception: {0}", ex);
                throw new InvalidPluginExecutionException("AddProductsBatch failed.", ex);
            }
        }

        // ---- Per-row commit (oliBatchSize == 1) ----
        private static void ProcessSingle(
            IOrganizationService service, OliSpec spec,
            List<string> createdIds, List<FailedOli> failedOlis,
            HashSet<string> touchedOppIds, ITracingService tracingService)
        {
            try
            {
                var entity = BuildEntityFromSpec(spec);
                service.Create(entity);
                createdIds.Add(spec.oliId);
                if (!string.IsNullOrWhiteSpace(spec.opportunityId)) touchedOppIds.Add(spec.opportunityId);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService,
                    "AddProductsBatch v3: single-create failed oliId={0}: {1}",
                    spec.oliId, ex.Message);
                failedOlis.Add(new FailedOli
                {
                    oliId = spec.oliId,
                    productId = spec.productId,
                    reason = ex.Message
                });
            }
        }

        // ---- Bulk commit (oliBatchSize > 1) ----
        private static void ProcessBulk(
            IOrganizationService service, List<OliSpec> batch,
            List<string> createdIds, List<FailedOli> failedOlis,
            HashSet<string> touchedOppIds, ITracingService tracingService)
        {
            var ec = new EntityCollection { EntityName = "opportunityproduct" };
            foreach (var s in batch) ec.Entities.Add(BuildEntityFromSpec(s));

            try
            {
                service.Execute(new CreateMultipleRequest { Targets = ec });
                // All rows in the batch committed atomically with their pre-assigned ids.
                foreach (var s in batch)
                {
                    createdIds.Add(s.oliId);
                    if (!string.IsNullOrWhiteSpace(s.opportunityId)) touchedOppIds.Add(s.opportunityId);
                }
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService,
                    "AddProductsBatch v3: bulk-create failed ({0} row(s)): {1}",
                    batch.Count, ex.Message);

                // All-or-nothing per batch — every row in this batch is marked failed.
                foreach (var s in batch)
                {
                    failedOlis.Add(new FailedOli
                    {
                        oliId = s.oliId,
                        productId = s.productId,
                        reason = "sibling row in batch failed: " + ex.Message
                    });
                }
            }
        }

        // ---- Entity builder ----
        private static Entity BuildEntityFromSpec(OliSpec spec)
        {
            var e = new Entity("opportunityproduct");
            if (Guid.TryParse(spec.oliId, out var oliGuid)) e.Id = oliGuid;

            if (Guid.TryParse(spec.opportunityId, out var oppGuid))
                e["opportunityid"] = new EntityReference("opportunity", oppGuid);
            if (Guid.TryParse(spec.productId, out var prodGuid))
                e["productid"] = new EntityReference("product", prodGuid);
            if (Guid.TryParse(spec.ibsId, out var ibsGuid))
                e["ats_inventorybyseason"] = new EntityReference("ats_inventorybyseason", ibsGuid);
            if (Guid.TryParse(spec.rateId, out var rateGuid))
                e["ats_rate"] = new EntityReference("ats_rate", rateGuid);
            if (!string.IsNullOrWhiteSpace(spec.uomId) && Guid.TryParse(spec.uomId, out var uomGuid))
                e["uomid"] = new EntityReference("uom", uomGuid);

            int qtyUnits = spec.qtyUnits;
            int qtyEvents = spec.qtyEvents <= 0 ? 1 : spec.qtyEvents;
            e["ats_quantity"] = qtyUnits;
            e["ats_quantityofevents"] = qtyEvents;

            decimal pricePerUnit = spec.rate;
            e["priceperunit"] = new Money(pricePerUnit);
            e["ats_sellingrate"] = new Money(pricePerUnit);

            decimal multiplier = spec.rateType == RateTypeSeason ? qtyUnits : (decimal)qtyUnits * qtyEvents;
            decimal unadjustedTotal = pricePerUnit * multiplier;
            e["ats_unadjustedtotalprice"] = new Money(unadjustedTotal);

            if (spec.hardCost > 0m) e["ats_hardcost"] = new Money(spec.hardCost);
            if (spec.productionCost > 0m) e["ats_productioncost"] = new Money(spec.productionCost);
            if (!string.IsNullOrWhiteSpace(spec.description)) e["description"] = spec.description;
            if (!string.IsNullOrWhiteSpace(spec.agreementOppProductTag))
                e["ats_agreementopportunityproduct"] = spec.agreementOppProductTag;

            if (!string.IsNullOrWhiteSpace(spec.packageLineItemOliId)
                && Guid.TryParse(spec.packageLineItemOliId, out var parentOliGuid))
            {
                e["ats_packagelineitem"] = new EntityReference("opportunityproduct", parentOliGuid);
            }

            return e;
        }

        // ---- Expansion: products → OliSpecs ----
        private static List<OliSpec> ExpandProductsToOlis(
            IOrganizationService service,
            Guid agreementId,
            List<ProductRequest> requests,
            ITracingService tracingService)
        {
            // Agreement + opportunities lookup (minimal ColumnSet).
            var opportunities = service.RetrieveMultiple(new QueryExpression("opportunity")
            {
                ColumnSet = new ColumnSet("opportunityid", "ats_startseason"),
                NoLock = true,
                Criteria =
                {
                    Conditions = { new ConditionExpression("ats_agreement", ConditionOperator.Equal, agreementId) }
                }
            }).Entities;

            if (opportunities.Count == 0)
                throw new InvalidOperationException("No opportunities linked to this agreement.");

            var seasonToOpportunity = new Dictionary<Guid, Guid>();
            foreach (var opp in opportunities)
            {
                var seasonRef = opp.GetAttributeValue<EntityReference>("ats_startseason");
                if (seasonRef != null) seasonToOpportunity[seasonRef.Id] = opp.Id;
            }

            // Collect the (product, season) pairs we need to resolve.
            var productIdSet = new HashSet<Guid>();
            var seasonIdSet = new HashSet<Guid>();
            foreach (var req in requests)
            {
                if (req == null || string.IsNullOrWhiteSpace(req.ProductId)) continue;
                if (Guid.TryParse(req.ProductId, out var pId)) productIdSet.Add(pId);
                foreach (var sId in ParseSeasonIds(req.seasonIds)) seasonIdSet.Add(sId);

                if (req.PackageComponents != null)
                {
                    foreach (var comp in req.PackageComponents)
                    {
                        if (comp == null || string.IsNullOrWhiteSpace(comp.ProductId)) continue;
                        if (Guid.TryParse(comp.ProductId, out var cpId)) productIdSet.Add(cpId);
                        foreach (var sId in ParseSeasonIds(comp.seasonIds ?? req.seasonIds))
                            seasonIdSet.Add(sId);
                    }
                }
            }

            var resolution = ResolveBulk(service, productIdSet, seasonIdSet, tracingService);

            // UOM lookup — one round-trip, reused across every OLI.
            Guid uomId = LookupUom(service, "Unit_of_Measure");
            string uomIdStr = uomId != Guid.Empty ? uomId.ToString() : null;

            var specs = new List<OliSpec>();

            foreach (var req in requests)
            {
                if (req == null || string.IsNullOrWhiteSpace(req.ProductId)) continue;
                if (!Guid.TryParse(req.ProductId, out var productGuid)) continue;

                var seasonGuids = ParseSeasonIds(req.seasonIds).ToList();
                if (seasonGuids.Count == 0) continue;

                // One tag per request — shared across the main + all its components across seasons.
                var tag = string.IsNullOrWhiteSpace(req.AgreementOpportunityProductTag)
                    ? Guid.NewGuid().ToString()
                    : req.AgreementOpportunityProductTag;

                foreach (var seasonGuid in seasonGuids)
                {
                    if (!seasonToOpportunity.TryGetValue(seasonGuid, out var oppId))
                        continue;

                    var key = MakePairKey(productGuid, seasonGuid);
                    if (!resolution.Ibs.TryGetValue(key, out var ibs))
                        throw new InvalidOperationException(
                            string.Format("IBS missing for product {0} season {1}; run CheckProductsAvailability first.",
                                productGuid, seasonGuid));

                    int rateType = NormalizeRateType(req.RateType);
                    if (!ibs.RatesByType.TryGetValue(rateType, out var rate))
                        throw new InvalidOperationException(
                            string.Format("Rate ({0}) missing for product {1} season {2}; run CheckProductsAvailability first.",
                                req.RateType, productGuid, seasonGuid));

                    // Main OLI — pre-assigned GUID.
                    var mainOliId = Guid.NewGuid();
                    specs.Add(new OliSpec
                    {
                        oliId = mainOliId.ToString(),
                        opportunityId = oppId.ToString(),
                        productId = productGuid.ToString(),
                        ibsId = ibs.IbsId.ToString(),
                        rateId = rate.RateId.ToString(),
                        rateType = rateType,
                        qtyUnits = req.QtyUnits,
                        qtyEvents = req.QtyEvents <= 0 ? 1 : req.QtyEvents,
                        rate = req.Rate > 0m ? req.Rate : rate.Price,
                        hardCost = req.HardCost,
                        productionCost = req.ProductionCost,
                        description = req.Description,
                        uomId = uomIdStr,
                        agreementOppProductTag = tag,
                        packageLineItemOliId = null
                    });

                    // Components (if any) — each gets its own OLI pointing at the main's pre-assigned id.
                    if (req.IsPackage && req.PackageComponents != null && req.PackageComponents.Count > 0)
                    {
                        foreach (var comp in req.PackageComponents)
                        {
                            if (comp == null || string.IsNullOrWhiteSpace(comp.ProductId)) continue;
                            if (!Guid.TryParse(comp.ProductId, out var compProductGuid)) continue;

                            var compSeasonList = string.IsNullOrWhiteSpace(comp.seasonIds)
                                ? new List<Guid> { seasonGuid }
                                : ParseSeasonIds(comp.seasonIds).ToList();
                            if (!compSeasonList.Contains(seasonGuid)) continue;

                            var compKey = MakePairKey(compProductGuid, seasonGuid);
                            if (!resolution.Ibs.TryGetValue(compKey, out var compIbs))
                                throw new InvalidOperationException(
                                    string.Format("IBS missing for component {0} season {1}; run CheckProductsAvailability first.",
                                        compProductGuid, seasonGuid));

                            int compRateType = NormalizeRateType(comp.RateType);
                            if (!compIbs.RatesByType.TryGetValue(compRateType, out var compRate))
                                throw new InvalidOperationException(
                                    string.Format("Rate ({0}) missing for component {1} season {2}; run CheckProductsAvailability first.",
                                        comp.RateType, compProductGuid, seasonGuid));

                            specs.Add(new OliSpec
                            {
                                oliId = Guid.NewGuid().ToString(),
                                opportunityId = oppId.ToString(),
                                productId = compProductGuid.ToString(),
                                ibsId = compIbs.IbsId.ToString(),
                                rateId = compRate.RateId.ToString(),
                                rateType = compRateType,
                                qtyUnits = comp.QtyUnits,
                                qtyEvents = comp.QtyEvents <= 0 ? 1 : comp.QtyEvents,
                                rate = comp.Rate > 0m ? comp.Rate : compRate.Price,
                                hardCost = comp.HardCost,
                                productionCost = comp.ProductionCost,
                                description = comp.Description,
                                uomId = uomIdStr,
                                agreementOppProductTag = tag,
                                packageLineItemOliId = mainOliId.ToString()
                            });
                        }
                    }
                }
            }

            return specs;
        }

        // ---- Bulk IBS/Rate resolution (unchanged from V2) ----
        private static BulkResolution ResolveBulk(
            IOrganizationService service,
            HashSet<Guid> productIds,
            HashSet<Guid> seasonIds,
            ITracingService tracingService)
        {
            var resolution = new BulkResolution();
            if (productIds.Count == 0 || seasonIds.Count == 0) return resolution;

            var sb = new StringBuilder();
            sb.Append("<fetch no-lock='true'>");
            sb.Append("<entity name='ats_inventorybyseason'>");
            sb.Append("<attribute name='ats_inventorybyseasonid' />");
            sb.Append("<attribute name='ats_product' />");
            sb.Append("<attribute name='ats_season' />");
            sb.Append("<filter type='and'>");
            AppendInFilter(sb, "ats_product", productIds);
            AppendInFilter(sb, "ats_season", seasonIds);
            sb.Append("</filter>");
            sb.Append("<link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='outer' alias='R'>");
            sb.Append("<attribute name='ats_rateid' />");
            sb.Append("<attribute name='ats_ratetype' />");
            sb.Append("<attribute name='ats_price' />");
            sb.Append("<attribute name='ats_inactive' />");
            sb.Append("</link-entity>");
            sb.Append("</entity>");
            sb.Append("</fetch>");

            var ec = service.RetrieveMultiple(new FetchExpression(sb.ToString()));
            TraceHelper.Trace(tracingService, "Bulk IBS/Rate resolution returned {0} row(s)", ec.Entities.Count);

            foreach (var row in ec.Entities)
            {
                var productRef = row.GetAttributeValue<EntityReference>("ats_product");
                var seasonRef = row.GetAttributeValue<EntityReference>("ats_season");
                if (productRef == null || seasonRef == null) continue;

                var key = MakePairKey(productRef.Id, seasonRef.Id);
                if (!resolution.Ibs.TryGetValue(key, out var info))
                {
                    info = new IbsInfo { IbsId = row.Id, RatesByType = new Dictionary<int, RateInfo>() };
                    resolution.Ibs[key] = info;
                }

                if (row.Contains("R.ats_rateid") && row.Contains("R.ats_ratetype"))
                {
                    bool inactive = row.Contains("R.ats_inactive")
                        && (bool)((AliasedValue)row["R.ats_inactive"]).Value;
                    if (inactive) continue;

                    int rt = ((OptionSetValue)((AliasedValue)row["R.ats_ratetype"]).Value).Value;
                    var rateId = (Guid)((AliasedValue)row["R.ats_rateid"]).Value;
                    decimal price = 0m;
                    if (row.Contains("R.ats_price") && ((AliasedValue)row["R.ats_price"]).Value is Money money)
                        price = money.Value;

                    info.RatesByType[rt] = new RateInfo { RateId = rateId, Price = price };
                }
            }

            return resolution;
        }

        private static Guid LookupUom(IOrganizationService service, string settingKey)
        {
            var q = new QueryExpression("ats_agiliteksettings")
            {
                ColumnSet = new ColumnSet("ats_value"),
                TopCount = 1,
                NoLock = true,
                Criteria = { Conditions = { new ConditionExpression("ats_key", ConditionOperator.Equal, settingKey) } }
            };
            var ec = service.RetrieveMultiple(q);
            if (ec.Entities.Count == 0) return Guid.Empty;
            var v = ec.Entities[0].GetAttributeValue<string>("ats_value");
            return Guid.TryParse(v, out var g) ? g : Guid.Empty;
        }

        // ---- Utilities ----
        private static string MakePairKey(Guid productId, Guid seasonId)
            => productId.ToString("N") + "|" + seasonId.ToString("N");

        private static HashSet<Guid> ParseSeasonIds(string csv)
        {
            var set = new HashSet<Guid>();
            if (string.IsNullOrWhiteSpace(csv)) return set;
            foreach (var part in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (Guid.TryParse(part.Trim(), out var g)) set.Add(g);
            return set;
        }

        private static int NormalizeRateType(string t)
            => string.Equals(t, "Season", StringComparison.OrdinalIgnoreCase) ? RateTypeSeason : RateTypeIndividual;

        private static void AppendInFilter(StringBuilder sb, string attr, HashSet<Guid> values)
        {
            sb.Append("<condition attribute='").Append(attr).Append("' operator='in'>");
            foreach (var v in values) sb.Append("<value>").Append(v.ToString()).Append("</value>");
            sb.Append("</condition>");
        }

        private static string GetInputString(IPluginExecutionContext ctx, string name)
        {
            if (ctx.InputParameters.Contains(name) && ctx.InputParameters[name] != null)
                return ctx.InputParameters[name].ToString();
            return null;
        }

        private static int GetInputInt(IPluginExecutionContext ctx, string name, int fallback)
        {
            if (ctx.InputParameters.Contains(name) && ctx.InputParameters[name] != null
                && int.TryParse(ctx.InputParameters[name].ToString(), out var parsed))
                return parsed;
            return fallback;
        }

        private static void Respond(IPluginExecutionContext ctx, BatchResult result)
        {
            ctx.OutputParameters["response"] = JsonSerializer.Serialize(result);
        }

        private static BatchResult EmptyResult(string agreementId) => new BatchResult
        {
            success = true,
            agreementId = agreementId,
            processedOliCount = 0,
            totalOliCount = 0,
            createdOpportunityProductIds = new List<string>(),
            failedOlis = new List<FailedOli>(),
            leftoverOlis = new List<OliSpec>(),
            touchedOpportunityIds = new List<string>()
        };

        // ============================================================
        // DTOs — wire-compatible with the PCF-side TypeScript interfaces.
        // Property names are intentionally lowercase camelCase to match.
        // System.Text.Json honours C# property casing by default, so naming
        // these in camelCase is how we get lowercase keys in the output JSON.
        // ============================================================

        public class OliSpec
        {
            public string oliId { get; set; }
            public string opportunityId { get; set; }
            public string productId { get; set; }
            public string ibsId { get; set; }
            public string rateId { get; set; }
            public int rateType { get; set; }
            public int qtyUnits { get; set; }
            public int qtyEvents { get; set; }
            public decimal rate { get; set; }
            public decimal hardCost { get; set; }
            public decimal productionCost { get; set; }
            public string description { get; set; }
            public string uomId { get; set; }
            public string agreementOppProductTag { get; set; }
            public string packageLineItemOliId { get; set; }
        }

        // Shared base for ProductRequest + PackageComponent (same fields,
        // different role). System.Text.Json picks up public properties on the
        // concrete subclass including inherited ones.
        public abstract class BaseProductFields
        {
            public string ProductId { get; set; }
            public string ProductName { get; set; }
            public string RateType { get; set; }
            public decimal Rate { get; set; }
            public string RateId { get; set; }
            public decimal HardCost { get; set; }
            public decimal ProductionCost { get; set; }
            public int QtyUnits { get; set; }
            public int QtyEvents { get; set; }
            public string seasonIds { get; set; }
            public string Description { get; set; }
        }

        public class ProductRequest : BaseProductFields
        {
            public string packageLineId { get; set; }
            public bool IsPackage { get; set; }
            public List<PackageComponent> PackageComponents { get; set; }
            public string AgreementOpportunityProductTag { get; set; }
        }

        public class PackageComponent : BaseProductFields { }

        private class BulkResolution
        {
            public Dictionary<string, IbsInfo> Ibs { get; }
                = new Dictionary<string, IbsInfo>(StringComparer.OrdinalIgnoreCase);
        }

        private class IbsInfo
        {
            public Guid IbsId;
            public Dictionary<int, RateInfo> RatesByType;
        }

        private class RateInfo
        {
            public Guid RateId;
            public decimal Price;
        }

        public class BatchResult
        {
            public bool success { get; set; }
            public string agreementId { get; set; }
            public int processedOliCount { get; set; }
            public int totalOliCount { get; set; }
            public List<string> createdOpportunityProductIds { get; set; }
            public List<FailedOli> failedOlis { get; set; }
            public List<OliSpec> leftoverOlis { get; set; }
            public List<string> touchedOpportunityIds { get; set; }
            public string message { get; set; }
            public string errorCode { get; set; }
        }

        public class FailedOli
        {
            public string oliId { get; set; }
            public string productId { get; set; }
            public string reason { get; set; }
        }
    }
}
