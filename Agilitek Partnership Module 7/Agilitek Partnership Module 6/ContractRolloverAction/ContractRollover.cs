using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;

// -------------------------------------------------------------------------
// Phase C.2 (focused) refactor — plan: atomic-jumping-rabin.md §Phase C.2
//
// Conservative scope: batches the per-row opportunity-product Create call
// (line ~265 of the prior file) via ExecuteMultipleRequest. The N+1 SELECT
// pattern (rolloverFetch / nextIbsQuery / nextRateQuery / defaultIBSFetch
// per product) is intentionally left in place — same reasoning as CloneDeal:
// safer to defer until a golden-diff harness is in place. The
// `NextOppId.Set(...)` call is also moved out of the loop because it
// always wrote the same value on every iteration.
// -------------------------------------------------------------------------

namespace ContractRolloverAction
{
    public class ContractRollover : CodeActivity
    {
        [Input("OpportunityId")]
        public InArgument<string> OpportunityId { get; set; }

        [Output("NextOppId")]
        public OutArgument<string> NextOppId { get; set; }

        private const string oppProdFetch = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
              <entity name='opportunityproduct'>
                <attribute name='productid' />
                <attribute name='opportunityproductid' />
                <attribute name='priceperunit' />
                <attribute name='ats_unadjustedtotalprice' />
                <attribute name='ats_sellingrate' />
                <attribute name='ats_rate' />
                <attribute name='ats_quantityofevents' />
                <attribute name='ats_quantity' />
                <attribute name='ats_inventorybyseason' />
                <attribute name='ats_hardcost' />
                <attribute name='ats_division' />
                <attribute name='ats_adjustedtotalprice' />
                <attribute name='ats_legaldefinition' />
                <attribute name='ats_legaldefinitionformatted' />
                <attribute name='ats_overwritelegaldefinition' />
                <attribute name='uomid' />
                <attribute name='description' />
                <order attribute='productid' descending='false' />
                <filter type='and'>
                  <condition attribute='opportunityid' operator='eq' value='~oppId~' />
                </filter>
                <link-entity name='opportunity' from='opportunityid' to='opportunityid' visible='false' link-type='outer' alias='opp'>
                  <attribute name='ats_startseason' />
                  <attribute name='ats_agreementparentopportunity' />
                </link-entity>
                <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' visible='false' link-type='outer' alias='rate'>
                  <attribute name='ats_ratetype' />
                </link-entity>
              </entity>
            </fetch>";

        private const string defaultIBSFetch = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
              <entity name='ats_inventorybyseason'>
                <attribute name='ats_inventorybyseasonid' />
                <attribute name='ats_name' />
                <order attribute='ats_name' descending='false' />
                <filter type='and'>
                  <condition attribute='ats_season' operator='eq' value='{~seasonId~}' />
                  <condition attribute='statecode' operator='eq' value='0' />
                  <filter type='or'>
                    <condition attribute='ats_name' operator='like' value='%Placeholder%' />
                    <condition attribute='ats_name' operator='like' value='%Default%' />
                  </filter>
                </filter>
              </entity>
            </fetch>";

        public const string rolloverFetch = @"
            <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
              <entity name='ats_inventoryrolloverpreparation'>
                <attribute name='ats_nextyearseason' />
                <attribute name='ats_nextyearrate' />
                <attribute name='ats_nextyearproduct' />
                <attribute name='ats_nextyearinventorybyseason' />
                <filter type='and'>
                  <condition attribute='ats_rolloverstatus' operator='eq' value='114300002' />
                  <condition attribute='ats_prioryearinventorybyseason' operator='eq' value='~ibsId~' />
                  <condition attribute='ats_nextyearseason' operator='eq' value='{~seasonId~}' />
                  <condition attribute='ats_ratetype' operator='eq' value='~rateType~' />
                  <filter type='or'>
                    <condition attribute='ats_prioryearrate' operator='eq' value='~rateId~' />
                    <condition attribute='ats_prioryearrate' operator='null' />
                  </filter>
                </filter>
                <link-entity name='ats_rate' from='ats_rateid' to='ats_nextyearrate' visible='false' link-type='outer' alias='rate'>
                  <attribute name='ats_price' />
                  <attribute name='ats_hardcost' />
                </link-entity>
              </entity>
            </fetch>";

        protected override void Execute(CodeActivityContext executionContext)
        {
            IWorkflowContext context = executionContext.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = executionContext.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            ITracingService trace = executionContext.GetExtension<ITracingService>();

            var opportunityId = OpportunityId.Get(executionContext);
            
            var fetch = new FetchExpression(oppProdFetch.Replace("~oppId~", opportunityId));
            trace.Trace($"Fetching opportunity products for {opportunityId}.");
            var opResult = service.RetrieveMultiple(fetch);

            if (opResult.Entities.Count > 0)
            {
                trace.Trace($"Found {opResult.Entities.Count} opportunity products to roll over.");

                var currSeason = (EntityReference)opResult.Entities[0].GetAttributeValue<AliasedValue>("opp.ats_startseason").Value;
                var agrParent = (EntityReference)opResult.Entities[0].GetAttributeValue<AliasedValue>("opp.ats_agreementparentopportunity").Value;

                if (currSeason == null || agrParent == null)
                    throw new InvalidOperationException("Next season opportunity not found to rollover to.");

                // Identify next season and opportunity
                var seasonName = currSeason.Name;
                string nextSeasonName;

                if (seasonName.Contains("-") || seasonName.Contains("("))
                {
                    var seasonYears = seasonName.Split(new char[] { '-', '(', ')' });
                    int year1 = int.Parse(seasonYears[0].Trim());
                    int year2 = int.Parse(seasonYears[1].Trim());
                    nextSeasonName = seasonName.Replace(year2.ToString(), (year2 + 1).ToString()).Replace(year1.ToString(), (year1 + 1).ToString());
                }
                else
                {
                    int year = int.Parse(seasonName);
                    nextSeasonName = seasonName.Replace(year.ToString(), (year + 1).ToString());
                }

                trace.Trace($"Next season name: {nextSeasonName}");

                var nextSeasonQuery = new QueryExpression("ats_season");
                nextSeasonQuery.Criteria = new FilterExpression();
                nextSeasonQuery.Criteria.AddCondition("ats_name", ConditionOperator.Equal, nextSeasonName);
                var nextSeason = service.RetrieveMultiple(nextSeasonQuery);
                if (nextSeason.Entities.Count == 0)
                    throw new InvalidOperationException($"{nextSeasonName} season not found.");

                var nextSeasonOppQuery = new QueryExpression("opportunity");
                nextSeasonOppQuery.Criteria = new FilterExpression();
                nextSeasonOppQuery.Criteria.AddCondition("ats_agreementparentopportunity", ConditionOperator.Equal, agrParent.Id);
                nextSeasonOppQuery.Criteria.AddCondition("ats_startseason", ConditionOperator.Equal, nextSeason.Entities[0].Id);
                var nextSeasonOpp = service.RetrieveMultiple(nextSeasonOppQuery);
                if (nextSeasonOpp.Entities.Count == 0)
                    throw new InvalidOperationException($"No {nextSeasonName} opportunity found to roll over to.");

                string defaultIBSid = null;
                string defaultIndividualRateId = null;
                string defaultSeasonRateId = null;

                // Phase C.2: collect rolled-over lines, then commit in one batched
                // ExecuteMultipleRequest at the end of the loop instead of issuing
                // one Create round-trip per line.
                var deferredCreates = new List<Entity>();

                // Roll over lines for next season opportunity
                foreach (var op in opResult.Entities)
                {
                    trace.Trace($"Rolling over {op.GetAttributeValue<EntityReference>("productid").Name}.");

                    string nextIbsId = null;
                    string nextRateId = null;
                    string nextProductId = op.GetAttributeValue<EntityReference>("productid").Id.ToString();

                    var fetchQuery = rolloverFetch.Replace("~ibsId~", op.GetAttributeValue<EntityReference>("ats_inventorybyseason").Id.ToString())
                        .Replace("~rateId~", op.GetAttributeValue<EntityReference>("ats_rate").Id.ToString())
                        .Replace("~rateType~", ((OptionSetValue)op.GetAttributeValue<AliasedValue>("rate.ats_ratetype").Value).Value.ToString())
                        .Replace("~seasonId~", nextSeason.Entities[0].Id.ToString());

                    var nextIbsFetch = new FetchExpression(fetchQuery);
                    var nextIbs = service.RetrieveMultiple(nextIbsFetch);
                    if (nextIbs.Entities.Count > 0)
                    {
                        trace.Trace($"Found matching Rollover Prep record for {op.GetAttributeValue<EntityReference>("productid").Name}.");

                        nextIbsId = nextIbs.Entities[0].GetAttributeValue<EntityReference>("ats_nextyearinventorybyseason").Id.ToString();
                        nextRateId = nextIbs.Entities[0].GetAttributeValue<EntityReference>("ats_nextyearrate").Id.ToString();
                        nextProductId = nextIbs.Entities[0].GetAttributeValue<EntityReference>("ats_nextyearproduct").Id.ToString();
                    }
                    else
                    {
                        trace.Trace($"Querying based on product name {op.GetAttributeValue<EntityReference>("productid").Name}");

                        var nextIbsQuery = new QueryExpression("ats_inventorybyseason");
                        nextIbsQuery.Criteria = new FilterExpression();
                        nextIbsQuery.Criteria.AddCondition("ats_season", ConditionOperator.Equal, nextSeason.Entities[0].Id);
                        nextIbsQuery.Criteria.AddCondition("ats_product", ConditionOperator.Equal, nextProductId);
                        nextIbsQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                        nextIbs = service.RetrieveMultiple(nextIbsQuery);
                        if (nextIbs.Entities.Count > 0)
                        {
                            trace.Trace($"Matched on Product and Season for {op.GetAttributeValue<EntityReference>("productid").Name}.");

                            nextIbsId = nextIbs.Entities[0].Id.ToString();
                            var nextRateQuery = new QueryExpression("ats_rate");
                            nextRateQuery.Criteria = new FilterExpression();
                            nextRateQuery.Criteria.AddCondition("ats_inventorybyseason", ConditionOperator.Equal, nextIbsId);
                            nextRateQuery.Criteria.AddCondition("ats_ratetype", ConditionOperator.Equal, ((OptionSetValue)op.GetAttributeValue<AliasedValue>("rate.ats_ratetype").Value).Value);
                            nextRateQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                            var nextRate = service.RetrieveMultiple(nextRateQuery);
                            if (nextRate.Entities.Count > 0)
                                nextRateId = nextRate.Entities[0].Id.ToString();
                        }
                    }

                    if (string.IsNullOrEmpty(nextRateId))
                    {
                        trace.Trace($"No matching Inventory by Season found for {op.GetAttributeValue<EntityReference>("productid").Name}. Using placeholder IBS for {seasonName}.");

                        if (string.IsNullOrEmpty(defaultIBSid))
                        {
                            var defaultIBSQuery = new FetchExpression(defaultIBSFetch.Replace("~seasonId~", nextSeason.Entities[0].Id.ToString()));
                            var defaultIBSResult = service.RetrieveMultiple(defaultIBSQuery);
                            if (defaultIBSResult.Entities.Count > 0)
                            {
                                defaultIBSid = defaultIBSResult.Entities[0].Id.ToString();

                                var defaultIndividualRateQuery = new QueryExpression("ats_rate");
                                defaultIndividualRateQuery.Criteria = new FilterExpression();
                                defaultIndividualRateQuery.Criteria.AddCondition("ats_inventorybyseason", ConditionOperator.Equal, defaultIBSid);
                                defaultIndividualRateQuery.Criteria.AddCondition("ats_ratetype", ConditionOperator.Equal, 114300001);
                                defaultIndividualRateQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                                var defaultIndividualRate = service.RetrieveMultiple(defaultIndividualRateQuery);
                                if (defaultIndividualRate.Entities.Count > 0)
                                    defaultIndividualRateId = defaultIndividualRate.Entities[0].Id.ToString();

                                var defaultSeasonRateQuery = new QueryExpression("ats_rate");
                                defaultSeasonRateQuery.Criteria = new FilterExpression();
                                defaultSeasonRateQuery.Criteria.AddCondition("ats_inventorybyseason", ConditionOperator.Equal, defaultIBSid);
                                defaultSeasonRateQuery.Criteria.AddCondition("ats_ratetype", ConditionOperator.Equal, 114300000);
                                defaultSeasonRateQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                                var defaultSeasonRate = service.RetrieveMultiple(defaultSeasonRateQuery);
                                if (defaultSeasonRate.Entities.Count > 0)
                                    defaultSeasonRateId = defaultSeasonRate.Entities[0].Id.ToString();
                            }
                        }

                        if (!string.IsNullOrEmpty(defaultIBSid))
                        {
                            nextIbsId = defaultIBSid;
                            nextRateId = (int)op.GetAttributeValue<AliasedValue>("rate.ats_ratetype").Value == 114300001 ? defaultIndividualRateId : defaultSeasonRateId;
                        }
                        else
                            continue;
                    }

                    if (!string.IsNullOrEmpty(nextRateId))
                    {
                        trace.Trace($"Creating {op.GetAttributeValue<EntityReference>("productid").Name} opportunity product for {nextSeasonName}.");

                        Entity newOppProd = new Entity("opportunityproduct");
                        newOppProd["opportunityid"] = new EntityReference("opportunity", nextSeasonOpp.Entities[0].Id);
                        newOppProd["productid"] = new EntityReference("product", new Guid(nextProductId));
                        newOppProd["ats_quantity"] = op.GetAttributeValue<int>("ats_quantity");
                        newOppProd["ats_quantityofevents"] = op.GetAttributeValue<int>("ats_quantityofevents");
                        newOppProd["ats_inventorybyseason"] = new EntityReference("ats_inventorybyseason", new Guid(nextIbsId));
                        newOppProd["ats_rate"] = new EntityReference("ats_rate", new Guid(nextRateId));
                        newOppProd["ats_sellingrate"] = op.GetAttributeValue<Money>("ats_sellingrate");
                        newOppProd["ats_hardcost"] = op.GetAttributeValue<Money>("ats_hardcost");
                        newOppProd["ats_division"] = op.GetAttributeValue<string>("ats_division");
                        newOppProd["ats_unadjustedtotalprice"] = op.GetAttributeValue<Money>("ats_unadjustedtotalprice");
                        newOppProd["ats_adjustedtotalprice"] = op.GetAttributeValue<Money>("ats_adjustedtotalprice");
                        newOppProd["ats_legaldefinition"] = op.GetAttributeValue<string>("ats_legaldefinition");
                        newOppProd["ats_legaldefinitionformatted"] = op.GetAttributeValue<string>("ats_legaldefinitionformatted");
                        newOppProd["ats_overwritelegaldefinition"] = op.GetAttributeValue<bool?>("ats_overwritelegaldefinition");
                        newOppProd["priceperunit"] = op.GetAttributeValue<Money>("priceperunit");
                        newOppProd["uomid"] = op.GetAttributeValue<EntityReference>("uomid");
                        newOppProd["description"] = op.GetAttributeValue<string>("description");

                        deferredCreates.Add(newOppProd);
                    }
                    else
                    {
                        trace.Trace($"No placeholder IBS found for {seasonName}. Skipping {op.GetAttributeValue<EntityReference>("productid").Name} opportunity product.");
                    }
                }

                // Flush all collected creates in a single batched round-trip.
                ExecuteCreatesInBatches(service, deferredCreates, 100);

                // Was set on every loop iteration to the same value; lift out of the hot path.
                NextOppId.Set(executionContext, nextSeasonOpp.Entities[0].Id.ToString());
            }
            else
                throw new InvalidOperationException("No opportunity products to roll over.");
        }

        private static void ExecuteCreatesInBatches(IOrganizationService service, List<Entity> entities, int batchSize)
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
                for (int j = 0; j < take; j++) req.Requests.Add(new CreateRequest { Target = entities[i + j] });
                service.Execute(req);
            }
        }
    }
}
