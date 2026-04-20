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
    // ats_AddProductsBatch — bulk "add products" for the Agreement Cart.
    // -------------------------------------------------------------------------
    //
    // V2 (17-Apr-2026) changes vs. the MVP:
    //   1. Package / template products are now handled here (no legacy
    //      fallback). Each package ProductRequest can carry a
    //      PackageComponents array. For a package, we build one "main"
    //      opp-product per (product, opportunity) pair and one component
    //      opp-product per component per that pair, all linked via
    //      `ats_packagelineitem` on the components. The main opp-product
    //      Id is pre-assigned (Entity.Id = Guid.NewGuid()) so we can
    //      reference it from the components within the same
    //      ExecuteTransactionRequest.
    //   2. Soft-timeout: a Stopwatch is started at the top of Execute.
    //      After each ProductRequest's opp-products are staged+committed,
    //      we check elapsed time. At >= SoftTimeoutMs (default 90 s), we
    //      stop, return whatever was processed so far, and hand back the
    //      unprocessed product requests in `leftoverProducts` so the PCF
    //      can call us again with exactly that list. The PCF loops until
    //      the leftover list is empty.
    //   3. Response casing normalised to lowercase camelCase throughout
    //      so the PCF consumer stays stable.
    //   4. Per-ProductRequest commit: instead of a single cross-product
    //      transaction, we commit one ProductRequest at a time (main +
    //      its components) so (a) the soft-timeout can yield cleanly
    //      after any number of products, and (b) a failure on one
    //      product doesn't roll back all the successful ones. The per-
    //      product commit is still atomic (main + components together).
    //
    // Contract:
    //   Input parameters (strings):
    //     agreementId : GUID of the ats_agreement.
    //     products    : JSON array of ProductRequest (see bottom of file).
    //     softTimeoutMs (optional): int — override default 90000.
    //
    //   Output parameter (string, JSON):
    //     {
    //       success: bool,
    //       agreementId: string,
    //       createdOpportunityProductIds: [string],
    //       touchedOpportunityIds: [string],
    //       processedCount: int,    // how many ProductRequests completed this call
    //       totalCount: int,        // total ProductRequests this call received
    //       leftoverProducts: [ProductRequest],
    //                              // unprocessed requests — send them back to resume
    //       failedProducts: [ { productId, reason } ],
    //                              // products that errored mid-flight (consistency)
    //       message?: string,
    //       errorCode?: "ibs_missing" | "rate_missing" |
    //                   "transaction_failed" | "input_invalid"
    //     }
    // -------------------------------------------------------------------------
    public class CustomAPIAddProductsBatch : IPlugin
    {
        private const int RateTypeSeason = 114300000;
        private const int RateTypeIndividual = 114300001;
        private const int PricingModeAutomatic = 559240000;
        private const int DefaultSoftTimeoutMs = 90_000; // 1 min 30 s

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
                TraceHelper.Trace(tracingService, "AddProductsBatch v2: begin");

                string agreementIdStr = GetInputString(pluginContext, "agreementId");
                string productsJson = GetInputString(pluginContext, "products");
                int softTimeoutMs = GetInputInt(pluginContext, "softTimeoutMs", DefaultSoftTimeoutMs);

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
                    Respond(pluginContext, new BatchResult
                    {
                        success = true,
                        agreementId = agreementIdStr,
                        createdOpportunityProductIds = new List<string>(),
                        touchedOpportunityIds = new List<string>(),
                        processedCount = 0,
                        totalCount = 0,
                        leftoverProducts = new List<ProductRequest>(),
                        failedProducts = new List<FailedProduct>()
                    });
                    return;
                }

                // ---- One-time prep: agreement + opportunities + uom ----
                Entity agreement = service.Retrieve(
                    "ats_agreement", agreementId,
                    new ColumnSet("ats_startseason", "ats_bpfstatus"));

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
                {
                    Respond(pluginContext, new BatchResult
                    {
                        success = false,
                        agreementId = agreementIdStr,
                        errorCode = "input_invalid",
                        message = "No opportunities linked to this agreement."
                    });
                    return;
                }

                var seasonToOpportunity = new Dictionary<Guid, Guid>();
                foreach (var opp in opportunities)
                {
                    var seasonRef = opp.GetAttributeValue<EntityReference>("ats_startseason");
                    if (seasonRef != null) seasonToOpportunity[seasonRef.Id] = opp.Id;
                }

                Guid uomId = LookupUom(service, "Unit_of_Measure");
                EntityReference uomRef = uomId != Guid.Empty ? new EntityReference("uom", uomId) : null;

                // ---- Resolve IBS + Rate once for every (product, season) pair we need ----
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
                            // components share the parent's seasons unless they specify their own
                            foreach (var sId in ParseSeasonIds(comp.seasonIds ?? req.seasonIds))
                                seasonIdSet.Add(sId);
                        }
                    }
                }

                var resolution = ResolveBulk(service, productIdSet, seasonIdSet, tracingService);

                // ---- Process products one by one, yielding on soft timeout ----
                var createdIds = new List<string>();
                var touchedOppIds = new HashSet<Guid>();
                var failed = new List<FailedProduct>();
                int processedCount = 0;
                int totalCount = requests.Count;
                List<ProductRequest> leftover = null;

                for (int idx = 0; idx < requests.Count; idx++)
                {
                    // Soft-timeout check — guarantees forward progress per call by
                    // requiring `idx > 0`. Otherwise, if the pre-loop prep
                    // (ResolveBulk / Agreement retrieve / Opportunities retrieve /
                    // UOM lookup) alone consumed the budget, we'd yield without
                    // processing a single product and the PCF's no-progress guard
                    // would trip. By guaranteeing at least one product is processed
                    // per call, we ensure the loop always makes progress even if a
                    // single iteration blows past the soft-timeout.
                    if (idx > 0 && stopwatch.ElapsedMilliseconds >= softTimeoutMs)
                    {
                        leftover = requests.GetRange(idx, requests.Count - idx);
                        TraceHelper.Trace(tracingService,
                            "AddProductsBatch v2: soft timeout at idx={0}, yielding {1} leftover",
                            idx, leftover.Count);
                        break;
                    }

                    var req = requests[idx];
                    try
                    {
                        var oneProduct = ProcessOneProductRequest(
                            service, req, seasonToOpportunity, resolution, uomRef, tracingService);

                        createdIds.AddRange(oneProduct.CreatedIds);
                        foreach (var o in oneProduct.TouchedOpps) touchedOppIds.Add(o);
                        processedCount++;
                    }
                    catch (Exception perProdEx)
                    {
                        // Per-product failure: record it, keep going so the
                        // client can see a per-product verdict instead of a
                        // wholesale batch reject. The current product's main
                        // + components were wrapped in a transaction, so its
                        // rows were rolled back by the platform already.
                        TraceHelper.Trace(tracingService,
                            "AddProductsBatch v2: product {0} failed: {1}",
                            req?.ProductId, perProdEx.Message);
                        failed.Add(new FailedProduct
                        {
                            productId = req?.ProductId,
                            reason = perProdEx.Message
                        });
                        processedCount++;
                    }
                }

                // ---- Recalculate touched opps ONCE at the end ----
                foreach (var oppId in touchedOppIds)
                {
                    try
                    {
                        RecalculateOppProdLinesInline(service, oppId, tracingService);
                    }
                    catch (Exception recalcEx)
                    {
                        TraceHelper.Trace(tracingService,
                            "AddProductsBatch v2: recalc of opp {0} failed: {1}",
                            oppId, recalcEx.Message);
                        // recalc failure does not invalidate the creates — they persist
                    }
                }

                Respond(pluginContext, new BatchResult
                {
                    success = failed.Count == 0,
                    agreementId = agreementIdStr,
                    createdOpportunityProductIds = createdIds,
                    touchedOpportunityIds = touchedOppIds.Select(g => g.ToString()).ToList(),
                    processedCount = processedCount,
                    totalCount = totalCount,
                    leftoverProducts = leftover ?? new List<ProductRequest>(),
                    failedProducts = failed,
                    message = failed.Count > 0
                        ? string.Format("{0} product(s) failed; remainder committed.", failed.Count)
                        : null,
                    errorCode = failed.Count > 0 ? "transaction_failed" : null
                });

                TraceHelper.Trace(tracingService,
                    "AddProductsBatch v2: processed={0}/{1} created={2} opps={3} leftover={4} failed={5} elapsed={6}ms",
                    processedCount, totalCount, createdIds.Count, touchedOppIds.Count,
                    leftover?.Count ?? 0, failed.Count, stopwatch.ElapsedMilliseconds);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracingService.Trace("AddProductsBatch v2: unexpected exception: {0}", ex);
                throw new InvalidPluginExecutionException("AddProductsBatch failed.", ex);
            }
        }

        // --- Per-product processing ---------------------------------------

        private class SingleProductResult
        {
            public List<string> CreatedIds = new List<string>();
            public HashSet<Guid> TouchedOpps = new HashSet<Guid>();
        }

        private static SingleProductResult ProcessOneProductRequest(
            IOrganizationService service,
            ProductRequest req,
            Dictionary<Guid, Guid> seasonToOpportunity,
            BulkResolution resolution,
            EntityReference uomRef,
            ITracingService tracingService)
        {
            var result = new SingleProductResult();

            if (req == null || string.IsNullOrWhiteSpace(req.ProductId))
                throw new InvalidOperationException("Empty ProductRequest.");
            if (!Guid.TryParse(req.ProductId, out var productGuid))
                throw new InvalidOperationException("Invalid ProductId: " + req.ProductId);

            var seasonGuids = ParseSeasonIds(req.seasonIds).ToList();
            if (seasonGuids.Count == 0) return result; // nothing to do

            // For each (target) season, build main opp-product + any component
            // opp-products, commit the whole thing atomically via ExecuteTransactionRequest.
            foreach (var seasonGuid in seasonGuids)
            {
                if (!seasonToOpportunity.TryGetValue(seasonGuid, out var oppId))
                    continue; // silently skip seasons not in the agreement

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

                // Pre-assign the main opp-product's Id so the components created
                // inside the same transaction can reference it via ats_packagelineitem.
                var mainId = Guid.NewGuid();
                var mainEntity = BuildOppProduct(
                    newId: mainId,
                    opportunityId: oppId,
                    productId: productGuid,
                    ibsId: ibs.IbsId,
                    rateId: rate.RateId,
                    rateType: rateType,
                    req: ProjectToBase(req),
                    agreementOppProductUniqueId: null, // unique tag below keeps package siblings together
                    uomRef: uomRef,
                    rateCardPrice: rate.Price,
                    packageLineItemId: null);

                // Stamp the ats_agreementopportunityproduct tag so downstream code
                // can still "group OLIs by tag" — matches the legacy behaviour.
                if (string.IsNullOrWhiteSpace(req.AgreementOpportunityProductTag))
                    req.AgreementOpportunityProductTag = Guid.NewGuid().ToString();
                mainEntity["ats_agreementopportunityproduct"] = req.AgreementOpportunityProductTag;

                var txnRequests = new List<OrganizationRequest>();
                txnRequests.Add(new CreateRequest { Target = mainEntity });

                // Components — if this is a package and PackageComponents was sent.
                if (req.IsPackage && req.PackageComponents != null && req.PackageComponents.Count > 0)
                {
                    foreach (var comp in req.PackageComponents)
                    {
                        if (comp == null || string.IsNullOrWhiteSpace(comp.ProductId)) continue;
                        if (!Guid.TryParse(comp.ProductId, out var compProductGuid)) continue;

                        // Components without their own seasonIds inherit the parent's.
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

                        var compEntity = BuildOppProduct(
                            newId: Guid.NewGuid(),
                            opportunityId: oppId,
                            productId: compProductGuid,
                            ibsId: compIbs.IbsId,
                            rateId: compRate.RateId,
                            rateType: compRateType,
                            req: comp,
                            agreementOppProductUniqueId: null,
                            uomRef: uomRef,
                            rateCardPrice: compRate.Price,
                            packageLineItemId: mainId);

                        compEntity["ats_agreementopportunityproduct"] = req.AgreementOpportunityProductTag;
                        txnRequests.Add(new CreateRequest { Target = compEntity });
                    }
                }

                var txn = new ExecuteTransactionRequest
                {
                    Requests = new OrganizationRequestCollection(),
                    ReturnResponses = true
                };
                foreach (var r in txnRequests) txn.Requests.Add(r);

                var resp = (ExecuteTransactionResponse)service.Execute(txn);
                foreach (var r in resp.Responses.Cast<CreateResponse>())
                    result.CreatedIds.Add(r.id.ToString());

                result.TouchedOpps.Add(oppId);
            }

            return result;
        }

        private static Entity BuildOppProduct(
            Guid newId,
            Guid opportunityId,
            Guid productId,
            Guid ibsId,
            Guid rateId,
            int rateType,
            BaseProductFields req,
            string agreementOppProductUniqueId,
            EntityReference uomRef,
            decimal rateCardPrice,
            Guid? packageLineItemId)
        {
            var e = new Entity("opportunityproduct") { Id = newId };
            e["opportunityid"] = new EntityReference("opportunity", opportunityId);
            e["productid"] = new EntityReference("product", productId);
            e["ats_inventorybyseason"] = new EntityReference("ats_inventorybyseason", ibsId);
            e["ats_rate"] = new EntityReference("ats_rate", rateId);
            if (uomRef != null) e["uomid"] = uomRef;

            int qtyUnits = req.QtyUnits;
            int qtyEvents = req.QtyEvents <= 0 ? 1 : req.QtyEvents;
            e["ats_quantity"] = qtyUnits;
            e["ats_quantityofevents"] = qtyEvents;

            decimal pricePerUnit = req.Rate > 0 ? req.Rate : rateCardPrice;
            e["priceperunit"] = new Money(pricePerUnit);
            e["ats_sellingrate"] = new Money(pricePerUnit);

            decimal multiplier = rateType == RateTypeSeason ? qtyUnits : (decimal)qtyUnits * qtyEvents;
            decimal unadjustedTotal = pricePerUnit * multiplier;
            e["ats_unadjustedtotalprice"] = new Money(unadjustedTotal);

            if (req.HardCost > 0m) e["ats_hardcost"] = new Money(req.HardCost);
            if (req.ProductionCost > 0m) e["ats_productioncost"] = new Money(req.ProductionCost);
            if (!string.IsNullOrWhiteSpace(req.Description)) e["description"] = req.Description;

            if (packageLineItemId.HasValue)
                e["ats_packagelineitem"] = new EntityReference("opportunityproduct", packageLineItemId.Value);

            return e;
        }

        /// <summary>
        /// Flatten the fields a ProductRequest and a PackageComponent share.
        /// Components can use the same BuildOppProduct path as the main product.
        /// </summary>
        private static BaseProductFields ProjectToBase(ProductRequest req)
            => req; // ProductRequest derives from BaseProductFields

        // --- Bulk resolution ------------------------------------------------

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
                    info = new IbsInfo
                    {
                        IbsId = row.Id,
                        RatesByType = new Dictionary<int, RateInfo>()
                    };
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

        // --- Opportunity recalculation (verbatim algorithm) ---------------

        private static void RecalculateOppProdLinesInline(
            IOrganizationService service,
            Guid oppId,
            ITracingService tracingService)
        {
            const string fetchXml = @"<fetch no-lock='true' distinct='true'>
  <entity name='opportunityproduct'>
    <attribute name='opportunityproductid' />
    <attribute name='priceperunit' />
    <attribute name='ats_quantity' />
    <attribute name='ats_quantityofevents' />
    <attribute name='ats_sellingrate' />
    <attribute name='ats_hardcost' />
    <attribute name='ats_unadjustedtotalprice' />
    <attribute name='ats_adjustedtotalprice' />
    <filter type='and'>
      <condition attribute='opportunityid' operator='eq' value='{0}' />
    </filter>
    <link-entity name='opportunity' from='opportunityid' to='opportunityid' link-type='inner' alias='Opp'>
      <attribute name='ats_pricingmode' />
      <attribute name='ats_manualamount' />
      <attribute name='ats_tradeamount' />
    </link-entity>
    <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Prod'>
      <attribute name='ats_ispassthroughcost' />
      <attribute name='ats_playoffeligible' />
    </link-entity>
  </entity>
</fetch>";

            var lines = service.RetrieveMultiple(
                new FetchExpression(string.Format(fetchXml, oppId))).Entities;
            if (lines.Count == 0) return;

            int pricingMode = 0;
            decimal manualAmount = 0m, tradeAmount = 0m, automaticAmount = 0m, passthroughAmount = 0m, hardCostAmount = 0m;

            foreach (var line in lines)
            {
                if (line.Contains("Opp.ats_pricingmode"))
                    pricingMode = ((OptionSetValue)((AliasedValue)line["Opp.ats_pricingmode"]).Value).Value;
                if (line.Contains("Opp.ats_manualamount"))
                    manualAmount = ((Money)((AliasedValue)line["Opp.ats_manualamount"]).Value).Value;
                if (line.Contains("Opp.ats_tradeamount"))
                    tradeAmount = ((Money)((AliasedValue)line["Opp.ats_tradeamount"]).Value).Value;

                bool isPassthrough = line.Contains("Prod.ats_ispassthroughcost")
                    && (bool)((AliasedValue)line["Prod.ats_ispassthroughcost"]).Value;

                int qty = line.Contains("ats_quantity") ? Convert.ToInt32(line["ats_quantity"]) : 0;
                int qtyEvents = line.Contains("ats_quantityofevents") ? Convert.ToInt32(line["ats_quantityofevents"]) : 0;
                decimal sellingRate = line.Contains("ats_sellingrate") ? ((Money)line["ats_sellingrate"]).Value : 0m;
                decimal hardCost = line.Contains("ats_hardcost") ? ((Money)line["ats_hardcost"]).Value : 0m;
                int multiplier = qty * qtyEvents;

                if (isPassthrough) passthroughAmount += sellingRate * multiplier;
                automaticAmount += sellingRate * multiplier;
                hardCostAmount += hardCost * multiplier;
            }

            decimal netAmount = (automaticAmount != passthroughAmount) ? automaticAmount - passthroughAmount : automaticAmount;
            decimal factor = 0m;
            if (manualAmount != 0m && netAmount != 0m && (manualAmount - passthroughAmount) != 0m)
                factor = (automaticAmount - manualAmount) / netAmount;

            decimal dealValue = (pricingMode == PricingModeAutomatic) ? automaticAmount : manualAmount;
            decimal playoffEligibleRevenue = 0m;
            var rowUpdates = new List<Entity>(lines.Count);

            if (pricingMode == PricingModeAutomatic)
            {
                foreach (var line in lines)
                {
                    int qty = line.Contains("ats_quantity") ? Convert.ToInt32(line["ats_quantity"]) : 0;
                    int qtyEvents = line.Contains("ats_quantityofevents") ? Convert.ToInt32(line["ats_quantityofevents"]) : 0;
                    decimal sellingRate = line.Contains("ats_sellingrate") ? ((Money)line["ats_sellingrate"]).Value : 0m;
                    decimal adj = sellingRate * qty * qtyEvents;

                    var u = new Entity("opportunityproduct") { Id = line.Id };
                    u["ats_adjustedtotalprice"] = new Money(adj);
                    u["ats_unadjustedtotalprice"] = new Money(adj);
                    rowUpdates.Add(u);

                    if (line.Contains("Prod.ats_playoffeligible")
                        && (bool)((AliasedValue)line["Prod.ats_playoffeligible"]).Value)
                        playoffEligibleRevenue += adj;
                }
            }
            else
            {
                int idx = 0, last = lines.Count - 1;
                decimal running = 0m, roundingError = 0m;

                foreach (var line in lines)
                {
                    int qty = line.Contains("ats_quantity") ? Convert.ToInt32(line["ats_quantity"]) : 0;
                    int qtyEvents = line.Contains("ats_quantityofevents") ? Convert.ToInt32(line["ats_quantityofevents"]) : 0;
                    decimal sellingRate = line.Contains("ats_sellingrate") ? ((Money)line["ats_sellingrate"]).Value : 0m;
                    bool isPassthrough = line.Contains("Prod.ats_ispassthroughcost")
                        && (bool)((AliasedValue)line["Prod.ats_ispassthroughcost"]).Value;

                    decimal unadj = sellingRate * qty * qtyEvents;
                    decimal effective = isPassthrough ? unadj : unadj - (factor * unadj);

                    if (idx == last)
                    {
                        if (isPassthrough)
                            roundingError = Math.Round(dealValue, 2) - (running + Math.Round(effective, 2));
                        else
                            effective = Math.Round(dealValue, 2) - Math.Round(running, 2);
                    }

                    var u = new Entity("opportunityproduct") { Id = line.Id };
                    u["ats_unadjustedtotalprice"] = new Money(unadj);
                    u["ats_adjustedtotalprice"] = new Money(effective);
                    rowUpdates.Add(u);

                    running += Math.Round(effective, 2);
                    idx++;

                    if (line.Contains("Prod.ats_playoffeligible")
                        && (bool)((AliasedValue)line["Prod.ats_playoffeligible"]).Value)
                        playoffEligibleRevenue += effective;
                }

                if (roundingError != 0m)
                {
                    foreach (var u in rowUpdates)
                    {
                        var source = lines.FirstOrDefault(l => l.Id == u.Id);
                        bool isPassthrough = source != null
                            && source.Contains("Prod.ats_ispassthroughcost")
                            && (bool)((AliasedValue)source["Prod.ats_ispassthroughcost"]).Value;
                        if (!isPassthrough)
                        {
                            var adj = ((Money)u["ats_adjustedtotalprice"]).Value + roundingError;
                            u["ats_adjustedtotalprice"] = new Money(adj);
                            break;
                        }
                    }
                }
            }

            ExecuteInBatches(service, rowUpdates, 100);

            var oppUpdate = new Entity("opportunity") { Id = oppId };
            oppUpdate["ats_dealvalue"] = new Money(dealValue);
            oppUpdate["budgetamount"] = new Money(automaticAmount);
            oppUpdate["ats_totalhardcost"] = new Money(hardCostAmount);
            oppUpdate["ats_cashamount"] = new Money(dealValue - tradeAmount);
            oppUpdate["ats_playoffeligiblerevenue"] = new Money(playoffEligibleRevenue);
            service.Update(oppUpdate);
        }

        private static void ExecuteInBatches(IOrganizationService service, List<Entity> entities, int batchSize)
        {
            if (entities == null || entities.Count == 0) return;
            for (int i = 0; i < entities.Count; i += batchSize)
            {
                int take = Math.Min(batchSize, entities.Count - i);
                var req = new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings { ContinueOnError = false, ReturnResponses = false },
                    Requests = new OrganizationRequestCollection()
                };
                for (int j = 0; j < take; j++)
                    req.Requests.Add(new UpdateRequest { Target = entities[i + j] });
                service.Execute(req);
            }
        }

        // --- Utilities ------------------------------------------------------

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

        // --- DTOs -----------------------------------------------------------

        // Shared base so PackageComponent and ProductRequest both feed into
        // BuildOppProduct without the caller having to special-case.
        private abstract class BaseProductFields
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
            public string seasonIds { get; set; }   // components may override; else inherit
            public string Description { get; set; }
        }

        private class ProductRequest : BaseProductFields
        {
            public string packageLineId { get; set; }
            public bool IsPackage { get; set; }
            public List<PackageComponent> PackageComponents { get; set; }

            /// <summary>Same tag stamped on every OLI produced by this request.</summary>
            public string AgreementOpportunityProductTag { get; set; }
        }

        private class PackageComponent : BaseProductFields
        {
            // Component-specific fields, if any, go here.
        }

        private class BulkResolution
        {
            public Dictionary<string, IbsInfo> Ibs { get; } = new Dictionary<string, IbsInfo>(StringComparer.OrdinalIgnoreCase);
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

        private class BatchResult
        {
            public bool success { get; set; }
            public string agreementId { get; set; }
            public List<string> createdOpportunityProductIds { get; set; }
            public List<string> touchedOpportunityIds { get; set; }
            public int processedCount { get; set; }
            public int totalCount { get; set; }
            public List<ProductRequest> leftoverProducts { get; set; }
            public List<FailedProduct> failedProducts { get; set; }
            public string message { get; set; }
            public string errorCode { get; set; }
        }

        private class FailedProduct
        {
            public string productId { get; set; }
            public string reason { get; set; }
        }
    }
}
