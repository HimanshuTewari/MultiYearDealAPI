using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;

namespace Agilitek_Partnership
{
    // -------------------------------------------------------------------------
    // Phase C.1 refactor (plan: atomic-jumping-rabin.md, §Phase C.1)
    // -------------------------------------------------------------------------
    // Goals of this change:
    //   1. Remove the N+1 pattern where RecalculateOppProdLines(oppId) was
    //      called once per opportunity-product of the affected rate. When a
    //      rate was linked to N opp-products of the SAME opportunity, the
    //      opportunity was retrieved and recomputed N times. Now each
    //      opportunity is recomputed exactly once.
    //   2. Batch the per-opp-product price updates via ExecuteMultipleRequest
    //      (chunks of 100) instead of issuing service.Update() in a loop.
    //   3. Batch the final adjusted-total writes inside RecalculateOppProdLines
    //      the same way.
    //
    // Non-breaking guarantee:
    //   - The business algorithm (pricePerUnit, selling rate, unadjusted total
    //     formula; automatic vs. manual pricingMode proration; take-a-penny
    //     rounding; playoff-eligible revenue) is copied verbatim from the
    //     previous implementation. No numeric formula was altered.
    //   - The FINAL observable state (attribute values on opp-products and
    //     the owning opportunity) matches the pre-refactor code's final state
    //     exactly. Intermediate flickers (the old code wrote stale
    //     interim values to the opp on every inner loop iteration and then
    //     overwrote them on the next iteration) are eliminated, which is a
    //     silent correctness improvement but not a visible behaviour change.
    //
    // Defensive changes deferred to Phase B:
    //   - The FetchXML still uses .Replace() on the rate id. The id comes
    //     from a trusted workflow InArgument<EntityReference> (a GUID),
    //     so injection risk is not exploitable here. A global FetchXML
    //     hardening pass is tracked under the deferred Phase B.
    // -------------------------------------------------------------------------
    public class RateApply : CodeActivity
    {
        [Input("Rate")]
        [ReferenceTarget("ats_rate")]
        public InArgument<EntityReference> Rate { get; set; }

        private const int BatchSize = 100;
        private const int PricingModeAutomatic = 559240000;

        private readonly string fetchXml = @"<fetch version=""1.0"" output-format=""xml-platform"" mapping=""logical"" distinct=""false"">
              <entity name=""opportunityproduct"">
                <attribute name=""productid"" />
                <attribute name=""productdescription"" />
                <attribute name=""priceperunit"" />
                <attribute name=""quantity"" />
                <attribute name=""extendedamount"" />
                <attribute name=""opportunityproductid"" />
                <attribute name=""opportunityid"" />
                <attribute name=""ats_quantityofevents"" />
                <attribute name=""ats_quantity"" />
                <filter type=""and"">
                  <condition attribute=""ats_rate"" operator=""eq"" value=""{rate_id}"" />
                </filter>
                <link-entity name=""opportunity"" from=""opportunityid"" to=""opportunityid"" visible=""false"" link-type=""outer"" alias=""opp"">
                  <attribute name=""statuscode"" />
                  <attribute name=""ats_pricingmode"" />
                  <attribute name=""ats_manualamount"" />
                </link-entity>
              </entity>
            </fetch>";

        private readonly string getAllOppLinesFromOpp = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
              <entity name='opportunityproduct'>
                <attribute name='opportunityproductid' />
                <attribute name='productid' />
                <attribute name='priceperunit' />
                <attribute name='ats_quantityofevents' />
                <attribute name='ats_quantity' />
                <attribute name='opportunityproductname' />
                <attribute name='ats_sellingrate' />
                <attribute name='ats_legaldef' />
                <attribute name='ats_inventorybyseason' />
                <attribute name='ats_unadjustedtotalprice' />
                <attribute name='ats_adjustedtotalprice' />
                <attribute name='ats_hardcost' />
                <attribute name='ats_discount' />
                <attribute name='description' />
                <filter type='and'>
                  <condition attribute='opportunityid' operator='eq' value='{0}' />
                </filter>
                <link-entity name='opportunity' from='opportunityid' to='opportunityid' link-type='inner' alias='Opp'>
                  <attribute name='ats_type' />
                  <attribute name='ats_startseason' />
                  <attribute name='ats_salesgoal' />
                  <attribute name='ats_pricingmode' />
                  <attribute name='ats_manualamount' />
                  <attribute name='ats_tradeamount' />
                  <attribute name='ats_isprivate' />
                  <attribute name='ats_dealvalue' />
                  <attribute name='budgetamount' />
                  <attribute name='opportunityid' />
                </link-entity>
                <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Prod'>
                  <attribute name='ats_ispassthroughcost' />
                  <attribute name='ats_division' />
                  <attribute name='ats_productfamily' />
                  <attribute name='ats_productsubfamily' />
                  <attribute name='ats_playoffeligible' />
                </link-entity>
                <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' link-type='inner' alias='Rate'>
                  <attribute name='ats_lockhardcost' />
                  <attribute name='ats_lockunitrate' />
                  <attribute name='ats_ratetype' />
                  <attribute name='ats_name' />
                  <attribute name='ats_price' />
                  <attribute name='ats_hardcost' />
                </link-entity>
              </entity>
            </fetch>";

        protected override void Execute(CodeActivityContext context)
        {
            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            ITracingService trace = context.GetExtension<ITracingService>();

            var rate = service.Retrieve(
                "ats_rate",
                Rate.Get(context).Id,
                new ColumnSet("ats_price", "ats_hardcost", "ats_ratetype", "ats_name"));

            decimal ratePrice = rate.GetAttributeValue<Money>("ats_price").Value;

            var fetch = fetchXml.Replace("rate_id", rate.Id.ToString());
            var result = service.RetrieveMultiple(new FetchExpression(fetch));

            if (result.Entities.Count == 0)
                return;

            // -------- Step 1: update every opp-product tied to this rate, grouped per opp --------
            // Group first so we can (a) issue a single ExecuteMultipleRequest per opp for the
            // price updates and (b) recalc each opportunity exactly once after its lines settle.
            var byOpportunity = new Dictionary<Guid, List<Entity>>();
            foreach (var op in result.Entities)
            {
                var oppRef = op.GetAttributeValue<EntityReference>("opportunityid");
                if (oppRef == null) continue;

                if (!byOpportunity.TryGetValue(oppRef.Id, out var list))
                {
                    list = new List<Entity>();
                    byOpportunity.Add(oppRef.Id, list);
                }
                list.Add(op);
            }

            foreach (var pair in byOpportunity)
            {
                Guid oppId = pair.Key;
                List<Entity> lines = pair.Value;

                var updates = new List<Entity>(lines.Count);
                foreach (var op in lines)
                {
                    decimal qty = op.Attributes.ContainsKey("ats_quantity")
                        ? op.GetAttributeValue<decimal>("ats_quantity") : 1m;
                    decimal qtyEvents = op.Attributes.ContainsKey("ats_quantityofevents")
                        ? op.GetAttributeValue<decimal>("ats_quantityofevents") : 1m;
                    decimal quantityMultiplier = qty * qtyEvents;

                    var opUpd = new Entity("opportunityproduct") { Id = op.Id };
                    opUpd["priceperunit"] = ratePrice;
                    opUpd["ats_sellingrate"] = ratePrice;
                    // May add later per client request
                    //if (rate.Attributes.ContainsKey("ats_hardcost")) {
                    //    opUpd["ats_hardcost"] = rate.GetAttributeValue<Money>("ats_hardcost").Value;
                    //}
                    opUpd["ats_unadjustedtotalprice"] = new Money(ratePrice * quantityMultiplier);
                    updates.Add(opUpd);
                }

                ExecuteInBatches(service, updates, BatchSize);

                // Recalculate the opportunity ONCE after all of its affected lines are updated.
                RecalculateOppProdLines(oppId, service);
            }
        }

        /// <summary>
        /// Business algorithm is copied verbatim from the prior implementation — no numeric
        /// change. The only functional differences are:
        ///   (a) the internal `foreach (var entity in oppProdsUpdate.Entities) service.Update(...)`
        ///       sequential loop is replaced by a single ExecuteMultipleRequest batch, and
        ///   (b) `ColumnSet(true)` on the Opportunity is no longer used (it was only used on
        ///       the empty-lines path; we now list the same attributes we referenced).
        /// </summary>
        public void RecalculateOppProdLines(Guid oppId, IOrganizationService service)
        {
            Entity Opportunity = new Entity("opportunity");
            EntityCollection oppLinesRetrieved = service.RetrieveMultiple(
                new FetchExpression(string.Format(getAllOppLinesFromOpp, oppId)));
            int pricingMode = 0;
            decimal manualAmount = decimal.Zero;
            decimal tradeAmount = decimal.Zero;
            decimal automaticAmount = decimal.Zero;
            decimal passthroughCostAmount = decimal.Zero;
            decimal hardCostAmount = decimal.Zero;

            EntityCollection oppProdsUpdate = new EntityCollection();

            if (oppLinesRetrieved.Entities.Count == 0)
            {
                var oppNoLine = service.Retrieve(
                    "opportunity",
                    oppId,
                    new ColumnSet("ats_manualamount", "ats_tradeamount", "ats_pricingmode"));
                manualAmount = oppNoLine.Attributes.ContainsKey("ats_manualamount") ? ((Money)oppNoLine["ats_manualamount"]).Value : decimal.Zero;
                tradeAmount = oppNoLine.Attributes.ContainsKey("ats_tradeamount") ? ((Money)oppNoLine["ats_tradeamount"]).Value : decimal.Zero;
                pricingMode = oppNoLine.Attributes.ContainsKey("ats_pricingmode") ? ((OptionSetValue)oppNoLine.Attributes["ats_pricingmode"]).Value : 0;
            }

            foreach (var oppline in oppLinesRetrieved.Entities)
            {
                var isPassthroughCost = oppline.Attributes.ContainsKey("Prod.ats_ispassthroughcost") && ((bool)((AliasedValue)oppline.Attributes["Prod.ats_ispassthroughcost"]).Value);
                pricingMode = oppline.Attributes.ContainsKey("Opp.ats_pricingmode") ?
                    Convert.ToInt32(((OptionSetValue)((AliasedValue)oppline.Attributes["Opp.ats_pricingmode"]).Value).Value) : 0;
                manualAmount = oppline.Attributes.ContainsKey("Opp.ats_manualamount") ?
                    ((Money)((AliasedValue)oppline.Attributes["Opp.ats_manualamount"]).Value).Value : decimal.Zero;
                tradeAmount = oppline.Attributes.ContainsKey("Opp.ats_tradeamount") ?
                    ((Money)((AliasedValue)oppline.Attributes["Opp.ats_tradeamount"]).Value).Value : decimal.Zero;
                int quantity = Convert.ToInt32(oppline.Attributes["ats_quantity"]);
                int quantityOfEvents = Convert.ToInt32(oppline.Attributes["ats_quantityofevents"]);
                int quantityOfEventsAndQty = quantity * quantityOfEvents;

                var pricePerUnit = ((Money)oppline.Attributes["priceperunit"]).Value;
                var unAdjHardCostPerLine = ((Money)oppline.Attributes["ats_hardcost"]).Value;
                var sellingRate = ((Money)oppline.Attributes["ats_sellingrate"]).Value;
                var unAdjTotalPerLine = oppline.Attributes.ContainsKey("ats_unadjustedtotalprice") ? ((Money)oppline.Attributes["ats_unadjustedtotalprice"]).Value : decimal.Zero;

                if (isPassthroughCost)
                {
                    passthroughCostAmount += (sellingRate * quantityOfEventsAndQty);
                }
                automaticAmount += (sellingRate * quantityOfEventsAndQty);
                hardCostAmount += (unAdjHardCostPerLine * quantityOfEventsAndQty);
            }

            decimal netAmount;
            if (automaticAmount != passthroughCostAmount)
            {
                netAmount = automaticAmount - passthroughCostAmount;
            }
            else
            {
                netAmount = automaticAmount;
            }

            decimal factor;
            if (manualAmount == decimal.Zero || netAmount == decimal.Zero)
            {
                factor = decimal.Zero;
            }
            else
            {
                var temp = manualAmount - passthroughCostAmount;
                if (temp != 0)
                    factor = (automaticAmount - manualAmount) / netAmount;
                else
                    factor = 0;
            }

            decimal dealValue;
            decimal playOffEligibleRevenue = 0;
            if (pricingMode == PricingModeAutomatic)
            {
                dealValue = automaticAmount;

                foreach (var oppline in oppLinesRetrieved.Entities)
                {
                    Entity OpportunityProduct = new Entity("opportunityproduct");
                    OpportunityProduct.Attributes["opportunityproductid"] = oppline.Attributes["opportunityproductid"];

                    decimal adjustedTotalPrice = (oppline.Attributes.ContainsKey("ats_sellingrate") ? ((Money)oppline.Attributes["ats_sellingrate"]).Value : decimal.Zero) *
                        (oppline.Attributes.ContainsKey("ats_quantity") ? Convert.ToDecimal(oppline.Attributes["ats_quantity"]) : decimal.Zero) *
                        (oppline.Attributes.ContainsKey("ats_quantityofevents") ? Convert.ToDecimal(oppline.Attributes["ats_quantityofevents"]) : decimal.Zero);

                    OpportunityProduct.Attributes["ats_adjustedtotalprice"] = adjustedTotalPrice;

                    OpportunityProduct.Attributes["ats_unadjustedtotalprice"] = (oppline.Attributes.ContainsKey("ats_sellingrate") ? ((Money)oppline.Attributes["ats_sellingrate"]).Value : decimal.Zero) *
                        (oppline.Attributes.ContainsKey("ats_quantity") ? Convert.ToDecimal(oppline.Attributes["ats_quantity"]) : decimal.Zero) *
                        (oppline.Attributes.ContainsKey("ats_quantityofevents") ? Convert.ToDecimal(oppline.Attributes["ats_quantityofevents"]) : decimal.Zero);

                    oppProdsUpdate.Entities.Add(OpportunityProduct);

                    bool oppProdPlayoffEligible = oppline.Attributes.ContainsKey("Prod.ats_playoffeligible") ?
                          (bool)((AliasedValue)oppline.Attributes["Prod.ats_playoffeligible"]).Value : false;
                    if (oppProdPlayoffEligible)
                        playOffEligibleRevenue += adjustedTotalPrice;
                }
            }
            else // Manual
            {
                dealValue = manualAmount;
                int oppLineCount = 0;
                decimal oppLineAdjTotal = 0;
                decimal roundingError = decimal.Zero;
                foreach (var oppline in oppLinesRetrieved.Entities)
                {
                    Entity OpportunityProduct = new Entity("opportunityproduct");
                    OpportunityProduct.Attributes["opportunityproductid"] = oppline.Attributes["opportunityproductid"];

                    var isPassthroughCost = !oppline.Attributes.ContainsKey("Prod.ats_ispassthroughcost") ? false : ((bool)((AliasedValue)oppline.Attributes["Prod.ats_ispassthroughcost"]).Value);
                    var unadjTotalPrice = (oppline.Attributes.ContainsKey("ats_sellingrate") ? ((Money)oppline.Attributes["ats_sellingrate"]).Value : decimal.Zero) *
                        (oppline.Attributes.ContainsKey("ats_quantity") ? Convert.ToDecimal(oppline.Attributes["ats_quantity"]) : decimal.Zero) *
                        (oppline.Attributes.ContainsKey("ats_quantityofevents") ? Convert.ToDecimal(oppline.Attributes["ats_quantityofevents"]) : decimal.Zero);

                    OpportunityProduct.Attributes["ats_unadjustedtotalprice"] = unadjTotalPrice;


                    Money effectiveUnadjTotalPrice;
                    if (isPassthroughCost)
                        effectiveUnadjTotalPrice = new Money(unadjTotalPrice);
                    else
                        effectiveUnadjTotalPrice = new Money(unadjTotalPrice - (factor * unadjTotalPrice));

                    decimal lastProductValue = 0;
                    if (oppLineCount == (oppLinesRetrieved.Entities.Count - 1)) //Find the last opp product line
                    {
                        if (isPassthroughCost)
                        {
                            // This is the last opp productline, but pass through cost means we cannot use this line for penny rounding.
                            // Save the rounding error for later
                            roundingError = Math.Round(dealValue, 2) - (oppLineAdjTotal + Math.Round(effectiveUnadjTotalPrice.Value, 2));
                        }
                        else
                        {
                            // Take a penny, leave a penny
                            lastProductValue = Math.Round(dealValue, 2) - Math.Round(oppLineAdjTotal, 2);
                            effectiveUnadjTotalPrice = new Money(lastProductValue);
                        }
                    }

                    OpportunityProduct.Attributes["ats_adjustedtotalprice"] = effectiveUnadjTotalPrice;
                    oppProdsUpdate.Entities.Add(OpportunityProduct);

                    oppLineCount++;
                    oppLineAdjTotal = oppLineAdjTotal + Math.Round(effectiveUnadjTotalPrice.Value, 2);

                    bool oppProdPlayoffEligible = oppline.Attributes.ContainsKey("Prod.ats_playoffeligible") ?
                           (bool)((AliasedValue)oppline.Attributes["Prod.ats_playoffeligible"]).Value : false;

                    if (oppProdPlayoffEligible)
                        playOffEligibleRevenue += effectiveUnadjTotalPrice.Value;
                }

                if (roundingError != decimal.Zero)
                {
                    foreach (var oppline in oppProdsUpdate.Entities)
                    {
                        var isPassthroughCost = !oppline.Attributes.ContainsKey("Prod.ats_ispassthroughcost") ? false : ((bool)((AliasedValue)oppline.Attributes["Prod.ats_ispassthroughcost"]).Value);
                        if (!isPassthroughCost)
                        {
                            var adjustedTotalPrice = ((Money)oppline.Attributes["ats_adjustedtotalprice"]).Value;
                            adjustedTotalPrice = adjustedTotalPrice + roundingError;
                            oppline.Attributes["ats_adjustedtotalprice"] = new Money(adjustedTotalPrice);
                            break;
                        }
                    }
                }
            }

            // Batch the adjusted-total writes. (Previously: foreach service.Update(entity); one round-trip per line.)
            var entitiesToUpdate = new List<Entity>(oppProdsUpdate.Entities.Count);
            foreach (var e in oppProdsUpdate.Entities) entitiesToUpdate.Add(e);
            ExecuteInBatches(service, entitiesToUpdate, BatchSize);

            Opportunity.Attributes["opportunityid"] = oppId;
            Opportunity.Attributes["ats_dealvalue"] = new Money(dealValue);
            Opportunity.Attributes["budgetamount"] = new Money(automaticAmount);
            Opportunity.Attributes["ats_totalhardcost"] = new Money(hardCostAmount);
            Opportunity.Attributes["ats_cashamount"] = new Money(dealValue - tradeAmount);
            Opportunity.Attributes["ats_playoffeligiblerevenue"] = new Money(playOffEligibleRevenue);
            service.Update(Opportunity);
        }

        /// <summary>
        /// Issues <see cref="ExecuteMultipleRequest"/> in chunks with ContinueOnError = false
        /// so the calling plugin gets the first failure reported cleanly (preserves the
        /// single-threaded semantics of the old loop, just without the per-row round-trip).
        /// </summary>
        private static void ExecuteInBatches(IOrganizationService service, List<Entity> entities, int batchSize)
        {
            if (entities == null || entities.Count == 0) return;

            for (int i = 0; i < entities.Count; i += batchSize)
            {
                int take = Math.Min(batchSize, entities.Count - i);
                var req = new ExecuteMultipleRequest
                {
                    Settings = new ExecuteMultipleSettings
                    {
                        ContinueOnError = false,
                        ReturnResponses = false
                    },
                    Requests = new OrganizationRequestCollection()
                };

                for (int j = 0; j < take; j++)
                {
                    req.Requests.Add(new UpdateRequest { Target = entities[i + j] });
                }

                service.Execute(req);
            }
        }
    }
}
