using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Workflows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace MultiYearDeal.Plugins
{
    // -------------------------------------------------------------------------
    // ats_RecalculateOpportunities — opportunity totals recalc with the same
    // soft-timeout / leftover / forward-progress pattern the Add-Products
    // flow uses.
    // -------------------------------------------------------------------------
    //
    // Purpose:
    //   Recalculate the aggregate totals on one or more opportunities:
    //     ats_dealvalue, budgetamount, ats_totalhardcost, ats_cashamount,
    //     ats_playoffeligiblerevenue,
    //   plus the per-line ats_adjustedtotalprice and ats_unadjustedtotalprice,
    //   using the same algorithm the legacy RateApply workflow uses (automatic
    //   vs. manual pricing, take-a-penny rounding, playoff-eligible revenue).
    //
    //   This endpoint is called AFTER ats_AddProductsBatch finishes creating
    //   all OLIs (so that each opportunity is recalculated exactly once,
    //   against the final state of its lines).
    //
    // Contract:
    //   Input parameters:
    //     opportunityIds : JSON array of GUIDs (or CSV of GUIDs — we accept both).
    //     softTimeoutMs  : optional int override of the default 90 s.
    //
    //   Output:
    //     response : JSON string:
    //       {
    //         success: bool,
    //         processedCount: int,
    //         totalCount: int,
    //         recalculatedOpportunityIds: [string],
    //         leftoverOpportunityIds:     [string],
    //         failedOpportunities: [ { opportunityId, reason } ],
    //         message?: string,
    //         errorCode?: "input_invalid" | "recalc_failed"
    //       }
    //
    // Forward-progress guarantee:
    //   Always recalculates at least one opportunity per call before the
    //   soft-timeout can yield — matches the same policy as AddProductsBatch
    //   so the PCF's no-progress guard doesn't trip.
    // -------------------------------------------------------------------------
    public class CustomAPIRecalculateOpportunities : IPlugin
    {
        private const int DefaultSoftTimeoutMs = 90_000;
        private const int PricingModeAutomatic = 559240000;

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
                TraceHelper.Trace(tracingService, "RecalculateOpportunities: begin");

                string idsInput = GetInputString(pluginContext, "opportunityIds");
                int softTimeoutMs = GetInputInt(pluginContext, "softTimeoutMs", DefaultSoftTimeoutMs);

                var oppIds = ParseGuidList(idsInput);
                if (oppIds == null)
                {
                    Respond(pluginContext, new RecalcResult
                    {
                        success = false,
                        errorCode = "input_invalid",
                        message = "opportunityIds could not be parsed. Supply either a JSON array of GUIDs or a CSV."
                    });
                    return;
                }

                if (oppIds.Count == 0)
                {
                    Respond(pluginContext, new RecalcResult
                    {
                        success = true,
                        processedCount = 0,
                        totalCount = 0,
                        recalculatedOpportunityIds = new List<string>(),
                        leftoverOpportunityIds = new List<string>(),
                        failedOpportunities = new List<FailedOpp>()
                    });
                    return;
                }

                var recalculated = new List<Guid>();
                var failed = new List<FailedOpp>();
                List<Guid> leftover = null;

                for (int idx = 0; idx < oppIds.Count; idx++)
                {
                    // Forward-progress guard — always recalc at least one.
                    if (idx > 0 && stopwatch.ElapsedMilliseconds >= softTimeoutMs)
                    {
                        leftover = oppIds.GetRange(idx, oppIds.Count - idx);
                        TraceHelper.Trace(tracingService,
                            "RecalculateOpportunities: soft-timeout at idx={0}, yielding {1} leftover",
                            idx, leftover.Count);
                        break;
                    }

                    var oppId = oppIds[idx];
                    try
                    {
                        RecalculateOpportunity(service, oppId, tracingService);
                        recalculated.Add(oppId);
                    }
                    catch (Exception ex)
                    {
                        TraceHelper.Trace(tracingService,
                            "RecalculateOpportunities: recalc failed opp={0}: {1}",
                            oppId, ex.Message);
                        failed.Add(new FailedOpp
                        {
                            opportunityId = oppId.ToString(),
                            reason = ex.Message
                        });
                    }
                }

                Respond(pluginContext, new RecalcResult
                {
                    success = failed.Count == 0,
                    processedCount = recalculated.Count + failed.Count,
                    totalCount = oppIds.Count,
                    recalculatedOpportunityIds = recalculated.Select(g => g.ToString()).ToList(),
                    leftoverOpportunityIds = (leftover ?? new List<Guid>()).Select(g => g.ToString()).ToList(),
                    failedOpportunities = failed,
                    message = failed.Count > 0
                        ? string.Format("{0} opportunity(ies) failed to recalculate.", failed.Count)
                        : null,
                    errorCode = failed.Count > 0 ? "recalc_failed" : null
                });

                TraceHelper.Trace(tracingService,
                    "RecalculateOpportunities: processed={0}/{1} recalculated={2} failed={3} leftover={4} elapsed={5}ms",
                    recalculated.Count + failed.Count, oppIds.Count,
                    recalculated.Count, failed.Count,
                    leftover?.Count ?? 0, stopwatch.ElapsedMilliseconds);
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                tracingService.Trace("RecalculateOpportunities: unexpected exception: {0}", ex);
                throw new InvalidPluginExecutionException("RecalculateOpportunities failed.", ex);
            }
        }

        // ---------------------------------------------------------------
        // Opportunity recalc — port of the algorithm used by RateApply and
        // the earlier inline recalc in AddProductsBatch. Same formulas, same
        // ats_* attribute writes.
        // ---------------------------------------------------------------
        private static void RecalculateOpportunity(
            IOrganizationService service, Guid oppId, ITracingService tracingService)
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
            decimal manualAmount = 0m, tradeAmount = 0m;
            decimal automaticAmount = 0m, passthroughAmount = 0m, hardCostAmount = 0m;

            // Pass 1 — aggregates.
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

            decimal netAmount = (automaticAmount != passthroughAmount)
                ? automaticAmount - passthroughAmount : automaticAmount;
            decimal factor = 0m;
            if (manualAmount != 0m && netAmount != 0m && (manualAmount - passthroughAmount) != 0m)
                factor = (automaticAmount - manualAmount) / netAmount;

            decimal dealValue = (pricingMode == PricingModeAutomatic) ? automaticAmount : manualAmount;
            decimal playoffEligibleRevenue = 0m;
            var rowUpdates = new List<Entity>(lines.Count);

            // Pass 2 — per-line adjusted totals + playoff-eligible revenue.
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

            ExecuteUpdatesInBatches(service, rowUpdates, 100);

            var oppUpdate = new Entity("opportunity") { Id = oppId };
            oppUpdate["ats_dealvalue"] = new Money(dealValue);
            oppUpdate["budgetamount"] = new Money(automaticAmount);
            oppUpdate["ats_totalhardcost"] = new Money(hardCostAmount);
            oppUpdate["ats_cashamount"] = new Money(dealValue - tradeAmount);
            oppUpdate["ats_playoffeligiblerevenue"] = new Money(playoffEligibleRevenue);
            service.Update(oppUpdate);
        }

        private static void ExecuteUpdatesInBatches(IOrganizationService service, List<Entity> entities, int batchSize)
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

        // ---- Input parsing: accept JSON array OR CSV ----
        private static List<Guid> ParseGuidList(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<Guid>();

            var trimmed = input.Trim();
            if (trimmed.StartsWith("["))
            {
                try
                {
                    var asStrings = JsonSerializer.Deserialize<List<string>>(trimmed) ?? new List<string>();
                    var guids = new List<Guid>();
                    foreach (var s in asStrings)
                        if (Guid.TryParse(s, out var g)) guids.Add(g);
                    return guids;
                }
                catch
                {
                    return null; // unparseable JSON → caller returns input_invalid.
                }
            }

            // CSV fallback.
            var list = new List<Guid>();
            foreach (var p in trimmed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (Guid.TryParse(p.Trim(), out var g)) list.Add(g);
            return list;
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

        private static void Respond(IPluginExecutionContext ctx, RecalcResult result)
        {
            ctx.OutputParameters["response"] = JsonSerializer.Serialize(result);
        }

        // ---- DTOs (lowercase camelCase to match PCF) ----
        public class RecalcResult
        {
            public bool success { get; set; }
            public int processedCount { get; set; }
            public int totalCount { get; set; }
            public List<string> recalculatedOpportunityIds { get; set; }
            public List<string> leftoverOpportunityIds { get; set; }
            public List<FailedOpp> failedOpportunities { get; set; }
            public string message { get; set; }
            public string errorCode { get; set; }
        }

        public class FailedOpp
        {
            public string opportunityId { get; set; }
            public string reason { get; set; }
        }
    }
}
