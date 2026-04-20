using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

// -------------------------------------------------------------------------
// Phase C.2 (focused) refactor — plan: atomic-jumping-rabin.md §Phase C.2
//
// Conservative scope: this pass batches the per-row opportunity-product
// Create calls inside the rollover loop (lines ~337 and ~465 of the prior
// file) via ExecuteMultipleRequest in chunks of 100. The N+1 SELECT pattern
// (rolloverFetch / nextIbsQuery / nextRateQuery / defaultIBSFetch per
// product) is intentionally left in place because untangling the three
// modes (Renewal / Copy / Playoffs) safely requires a golden-diff harness,
// which Phase A is deferring. The batched-create change alone removes one
// HTTP round-trip per opp-product on the rollover path.
//
// Also: replaced the bare `catch { throw new InvalidOperationException }`
// with a typed catch that preserves the root-cause exception in InnerException.
// -------------------------------------------------------------------------

namespace Agilitek_Partnership
{
    public class CloneDeal : CodeActivity // Workflow Activity 
    {
        [Input("Opportunity")]
        [ReferenceTarget("opportunity")]
        public InArgument<EntityReference> Opportunity { get; set; }

        [Input("CloneMode")]
        public InArgument<string> CloneMode { get; set; }

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
                <link-entity name='product' from='productid' to='productid' visible='false' link-type='outer' alias='product'>
                  <attribute name='ats_playoffeligible' />
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

        protected override void Execute(CodeActivityContext context)
        {
            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);

            var sourceOpp = service.Retrieve("opportunity", Opportunity.Get(context).Id, new ColumnSet(true));
            string mode;
            try
            {
                // Expected Values are Renewal, Copy, Playoffs
                mode = CloneMode.Get(context);
                if (mode.ToLower() != "renewal" && mode.ToLower() != "copy" && mode.ToLower() != "playoffs")
                    throw new InvalidOperationException("Invalid Clone Mode.");
            }
            catch (Exception cloneModeEx)
            {
                // Preserve the root-cause exception so trace logs / re-thrown
                // InvalidPluginExecutionException keep stack info.
                throw new InvalidOperationException(
                    "This workflow is incorrectly configured. Please contact Agilitek Solutions.",
                    cloneModeEx);
            }

            bool advance = mode != "Copy";
            bool playoffs = mode == "Playoffs";

            if (sourceOpp.GetAttributeValue<bool>("ats_opportunitytype"))
                throw new InvalidOperationException("This operation is not valid for Ticketing Opportunities.");
            var startSeason = sourceOpp.GetAttributeValue<EntityReference>("ats_startseason");

            // Get Season
            Guid seasonId;
            if (startSeason != null)
            {
                if (!advance) // Cloning in same season
                {
                    seasonId = startSeason.Id;
                }
                else if (playoffs) // Rolling over to playoff season
                {
                    var seasonName = startSeason.Name;
                    var nextSeasonQuery = new QueryExpression("ats_season");
                    nextSeasonQuery.Criteria = new FilterExpression();
                    nextSeasonQuery.Criteria.AddCondition("ats_name", ConditionOperator.BeginsWith, seasonName);
                    nextSeasonQuery.Criteria.AddCondition("ats_name", ConditionOperator.EndsWith, "Playoffs");
                    var season = service.RetrieveMultiple(nextSeasonQuery);

                    if (season != null && season.Entities.Count > 0)
                        seasonId = season.Entities[0].Id;
                    else
                        throw new InvalidOperationException($"{seasonName} Playoffs season not found.");
                }
                else // Rolling over to next season
                {
                    if (startSeason.Name.Contains("-") || startSeason.Name.Contains("("))
                    {
                        var seasonName = startSeason.Name;
                        var seasonYears = seasonName.Split(new char[] { '-', '(', ')' });
                        int year1 = int.Parse(seasonYears[0].Trim());
                        int year2 = int.Parse(seasonYears[1].Trim());
                        var nextSeason = seasonName.Replace(year2.ToString(), (year2 + 1).ToString()).Replace(year1.ToString(), (year1 + 1).ToString());

                        var nextSeasonQuery = new QueryExpression("ats_season");
                        nextSeasonQuery.Criteria = new FilterExpression();
                        nextSeasonQuery.Criteria.AddCondition("ats_name", ConditionOperator.Equal, nextSeason);
                        var season = service.RetrieveMultiple(nextSeasonQuery);

                        if (season != null && season.Entities.Count > 0)
                            seasonId = season.Entities[0].Id;
                        else
                            throw new InvalidOperationException($"{nextSeason} season not found.");
                    }
                    else
                    {
                        var seasonName = startSeason.Name;
                        int year = int.Parse(seasonName);
                        var nextSeason = seasonName.Replace(year.ToString(), (year + 1).ToString());

                        var nextSeasonQuery = new QueryExpression("ats_season");
                        nextSeasonQuery.Criteria = new FilterExpression();
                        nextSeasonQuery.Criteria.AddCondition("ats_name", ConditionOperator.Equal, nextSeason);
                        var season = service.RetrieveMultiple(nextSeasonQuery);

                        if (season != null && season.Entities.Count > 0)
                            seasonId = season.Entities[0].Id;
                        else
                            throw new InvalidOperationException($"{nextSeason} season not found.");
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("Opportunity has no season.");
            }

            EntityCollection oppProducts = null;

            // Early check for Playoff Eligible
            if (playoffs)
            {
                bool playoffeligible = false;
                var playoffProdQuery = oppProdFetch.Replace("~oppId~", Opportunity.Get(context).Id.ToString());
                oppProducts = service.RetrieveMultiple(new FetchExpression(playoffProdQuery));

                foreach (var op in oppProducts.Entities)
                {
                    try
                    {
                        if (op.Attributes.ContainsKey("product.ats_playoffeligible") && (bool)op.GetAttributeValue<AliasedValue>("product.ats_playoffeligible").Value)
                            playoffeligible = true;
                        else
                            continue;
                        break;
                    }
                    catch { }
                }

                if (!playoffeligible)
                    throw new InvalidOperationException("This opportunity does not contain any playoff eligible products.");
            }

            // Create Renewal Clone
            var renewalOpp = new Entity("opportunity");
            renewalOpp["ats_opportunitytype"] = false; // Partnership
            renewalOpp["ats_type"] = new OptionSetValue(100000002); // Renewal
            renewalOpp["customerid"] = sourceOpp["customerid"];
            renewalOpp["ats_startseason"] = new EntityReference("ats_season", seasonId);
            renewalOpp["ats_dealvalue"] = sourceOpp["ats_dealvalue"];
            renewalOpp["ats_manualamount"] = sourceOpp["ats_dealvalue"];
            renewalOpp["ats_pricingmode"] = new OptionSetValue(559240001); // Manual

            if (sourceOpp.Attributes.ContainsKey("ats_contactid"))
                renewalOpp["ats_contactid"] = sourceOpp["ats_contactid"];
            if (sourceOpp.Attributes.ContainsKey("ats_billingcontact"))
                renewalOpp["ats_billingcontact"] = sourceOpp["ats_billingcontact"];
            if (sourceOpp.Attributes.ContainsKey("ats_contractterms"))
                renewalOpp["ats_contractterms"] = sourceOpp["ats_contractterms"];
            if (sourceOpp.Attributes.ContainsKey("ats_billingterms"))
                renewalOpp["ats_billingterms"] = sourceOpp["ats_billingterms"];
            if (sourceOpp.Attributes.ContainsKey("ats_playoffterms"))
                renewalOpp["ats_playoffterms"] = sourceOpp["ats_playoffterms"];
            if (sourceOpp.Attributes.ContainsKey("ats_exclusivityterms"))
                renewalOpp["ats_exclusivityterms"] = sourceOpp["ats_exclusivityterms"];
            if (sourceOpp.Attributes.ContainsKey("ats_barterterms"))
                renewalOpp["ats_barterterms"] = sourceOpp["ats_barterterms"];
            if (sourceOpp.Attributes.ContainsKey("ats_ticketingnotes"))
                renewalOpp["ats_ticketingnotes"] = sourceOpp["ats_ticketingnotes"];
            if (sourceOpp.Attributes.ContainsKey("ats_financenotes"))
                renewalOpp["ats_financenotes"] = sourceOpp["ats_financenotes"];
            if (sourceOpp.Attributes.ContainsKey("ats_cashamount"))
                renewalOpp["ats_cashamount"] = sourceOpp["ats_cashamount"];
            if (sourceOpp.Attributes.ContainsKey("ats_tradeamount"))
                renewalOpp["ats_tradeamount"] = sourceOpp["ats_tradeamount"];
            if (sourceOpp.Attributes.ContainsKey("ats_agencyamount"))
                renewalOpp["ats_agencyamount"] = sourceOpp["ats_agencyamount"];
            if (sourceOpp.Attributes.ContainsKey("budgetamount"))
                renewalOpp["budgetamount"] = sourceOpp["budgetamount"];
            if (sourceOpp.Attributes.ContainsKey("ats_totalhardcost"))
                renewalOpp["ats_totalhardcost"] = sourceOpp["ats_totalhardcost"];
            if (sourceOpp.Attributes.ContainsKey("ats_percentofratecard"))
                renewalOpp["ats_percentofratecard"] = sourceOpp["ats_percentofratecard"];
            if (sourceOpp.Attributes.ContainsKey("ownerid"))
                renewalOpp["ownerid"] = sourceOpp["ownerid"];
            if (sourceOpp.Attributes.ContainsKey("ats_servicerep"))
                renewalOpp["ats_servicerep"] = sourceOpp["ats_servicerep"];

            if (!advance)
            {
                renewalOpp["ats_type"] = sourceOpp["ats_type"];
                renewalOpp["ats_alternate"] = true;
                if (sourceOpp.Attributes.ContainsKey("ats_agreementstartdate"))
                    renewalOpp["ats_agreementstartdate"] = sourceOpp["ats_agreementstartdate"];
                if (sourceOpp.Attributes.ContainsKey("ats_agreementenddate"))
                    renewalOpp["ats_agreementenddate"] = sourceOpp["ats_agreementenddate"];
            }
            else if (playoffs)
            {
                renewalOpp["ats_dealvalue"] = null;
                renewalOpp["ats_manualamount"] = null;
                renewalOpp["ats_type"] = sourceOpp["ats_type"];
            }

            // Retrieve BPV
            EntityReference bpfStage = null;
            var bpfQuery = new QueryExpression("ats_partnershipsalessteps");
            bpfQuery.Criteria = new FilterExpression();
            bpfQuery.Criteria.AddCondition("bpf_opportunityid", ConditionOperator.Equal, sourceOpp.Id);
            bpfQuery.ColumnSet = new ColumnSet(new string[] { "activestageid" });
            var bpfs = service.RetrieveMultiple(bpfQuery);
            if (bpfs != null && bpfs.Entities.Count > 0)
            {
                bpfStage = (EntityReference)bpfs.Entities[0]["activestageid"];
                if (mode == "Renewal")
                    bpfStage.Id = new Guid("b65bd214-a2a7-405c-a0c4-125c95030ac1"); // Proposal Built
                else if (mode == "Playoffs")
                    bpfStage.Id = new Guid("44123027-c676-4dca-be0c-f6848ddea393"); // Contract Built
            }

            // Start new BPF
            var renewalId = service.Create(renewalOpp);
            var bpf = new Entity("ats_partnershipsalessteps");
            bpf["bpf_opportunityid"] = new EntityReference("opportunity", renewalId);
            bpf["activestageid"] = bpfStage;
            service.Create(bpf);

            // Clone Products
            if (!playoffs)
            {
                var oppProdQuery = oppProdFetch.Replace("~oppId~", Opportunity.Get(context).Id.ToString());
                oppProducts = service.RetrieveMultiple(new FetchExpression(oppProdQuery));
            }

            if (oppProducts.Entities.Count > 0)
            {
                string defaultIBSid = "";
                string defaultIndividualRateId = "";
                string defaultSeasonRateId = "";

                // Phase C.2: collect rolled-over lines, then commit in one batched
                // ExecuteMultipleRequest at the end of the loop instead of issuing
                // one Create round-trip per line. Net effect: ~N round-trips → ~N/100.
                var deferredCreates = new List<Entity>();

                // Roll over lines for cloned opportunity
                foreach (var op in oppProducts.Entities)
                {
                    if (!advance)
                    {
                        Entity newOppProd = new Entity("opportunityproduct");
                        newOppProd["opportunityid"] = new EntityReference("opportunity", renewalId);
                        newOppProd["productid"] = op.GetAttributeValue<EntityReference>("productid");
                        newOppProd["ats_quantity"] = op.GetAttributeValue<int>("ats_quantity");
                        newOppProd["ats_quantityofevents"] = op.GetAttributeValue<int>("ats_quantityofevents");
                        newOppProd["ats_inventorybyseason"] = op.GetAttributeValue<EntityReference>("ats_inventorybyseason");
                        newOppProd["ats_rate"] = op.GetAttributeValue<EntityReference>("ats_rate");
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
                        continue;
                    }
                    else if (playoffs && !(op.Attributes.ContainsKey("product.ats_playoffeligible") && (bool)op.GetAttributeValue<AliasedValue>("product.ats_playoffeligible").Value))
                    {
                        // Skip non-playoff eligible products
                        continue;
                    }

                    string nextIbsId = null;
                    string nextRateId = null;
                    string nextProductId = op.GetAttributeValue<EntityReference>("productid").Id.ToString();

                    string ibsId = op.GetAttributeValue<EntityReference>("ats_inventorybyseason").Id.ToString();
                    string rateId = op.GetAttributeValue<EntityReference>("ats_rate").Id.ToString();
                    string rateType = ((OptionSetValue)op.GetAttributeValue<AliasedValue>("rate.ats_ratetype").Value).Value.ToString();

                    // Look for a rollover prep record that points us to the right IBS and Rate
                    var fetchQuery = rolloverFetch.Replace("~ibsId~", ibsId)
                        .Replace("~rateId~", rateId)
                        .Replace("~rateType~", rateType)
                        .Replace("~seasonId~", seasonId.ToString());

                    var nextIbsFetch = new FetchExpression(fetchQuery);
                    var nextIbs = service.RetrieveMultiple(nextIbsFetch);
                    if (nextIbs.Entities.Count > 0)
                    {
                        nextIbsId = nextIbs.Entities[0].GetAttributeValue<EntityReference>("ats_nextyearinventorybyseason").Id.ToString();
                        nextRateId = nextIbs.Entities[0].GetAttributeValue<EntityReference>("ats_nextyearrate").Id.ToString();
                        nextProductId = nextIbs.Entities[0].GetAttributeValue<EntityReference>("ats_nextyearproduct").Id.ToString();
                    }
                    else // No rollover prep record, so look for an IBS that matches season and product
                    {
                        var nextIbsQuery = new QueryExpression("ats_inventorybyseason");
                        nextIbsQuery.Criteria = new FilterExpression();
                        nextIbsQuery.Criteria.AddCondition("ats_season", ConditionOperator.Equal, seasonId);
                        nextIbsQuery.Criteria.AddCondition("ats_product", ConditionOperator.Equal, nextProductId);
                        nextIbsQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                        nextIbs = service.RetrieveMultiple(nextIbsQuery);
                        if (nextIbs.Entities.Count > 0)
                        {
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
                        if (string.IsNullOrEmpty(defaultIBSid)) // Check for default IBS and Rate
                        {
                            var defaultIBSQuery = new FetchExpression(defaultIBSFetch.Replace("~seasonId~", seasonId.ToString()));
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

                    // Roll over the Opportunity Product
                    if (!string.IsNullOrEmpty(nextRateId))
                    {
                        var sellingRate = op.GetAttributeValue<Money>("ats_sellingrate");
                        var ppu = op.GetAttributeValue<Money>("priceperunit");
                        var unadjustedTotal = op.GetAttributeValue<Money>("ats_unadjustedtotalprice");
                        var adjustedTotal = op.GetAttributeValue<Money>("ats_adjustedtotalprice");

                        if (nextRateId.ToLower() != defaultIndividualRateId.ToLower() && nextRateId.ToLower() != defaultSeasonRateId.ToLower())
                        {
                            var nextRate = service.Retrieve("ats_rate", new Guid(nextRateId), new ColumnSet(new string[] { "ats_price" }));
                            sellingRate = nextRate.GetAttributeValue<Money>("ats_price");
                            ppu = sellingRate;
                            unadjustedTotal = new Money(sellingRate.Value * op.GetAttributeValue<int>("ats_quantity") * op.GetAttributeValue<int>("ats_quantityofevents"));
                            adjustedTotal = unadjustedTotal;
                        }

                        Entity newOppProd = new Entity("opportunityproduct");
                        newOppProd["opportunityid"] = new EntityReference("opportunity", renewalId);
                        newOppProd["productid"] = new EntityReference("product", new Guid(nextProductId));
                        newOppProd["ats_quantity"] = op.GetAttributeValue<int>("ats_quantity");
                        newOppProd["ats_quantityofevents"] = op.GetAttributeValue<int>("ats_quantityofevents");
                        newOppProd["ats_inventorybyseason"] = new EntityReference("ats_inventorybyseason", new Guid(nextIbsId));
                        newOppProd["ats_rate"] = new EntityReference("ats_rate", new Guid(nextRateId));
                        newOppProd["ats_sellingrate"] = sellingRate;
                        newOppProd["ats_hardcost"] = op.GetAttributeValue<Money>("ats_hardcost");
                        newOppProd["ats_division"] = op.GetAttributeValue<string>("ats_division");
                        newOppProd["ats_unadjustedtotalprice"] = unadjustedTotal;
                        newOppProd["ats_adjustedtotalprice"] = adjustedTotal;
                        newOppProd["ats_legaldefinition"] = op.GetAttributeValue<string>("ats_legaldefinition");
                        newOppProd["ats_legaldefinitionformatted"] = op.GetAttributeValue<string>("ats_legaldefinitionformatted");
                        newOppProd["ats_overwritelegaldefinition"] = op.GetAttributeValue<bool?>("ats_overwritelegaldefinition");
                        newOppProd["priceperunit"] = ppu;
                        newOppProd["uomid"] = op.GetAttributeValue<EntityReference>("uomid");
                        newOppProd["description"] = op.GetAttributeValue<string>("description");

                        deferredCreates.Add(newOppProd);
                    }
                }

                // Flush all collected creates in a single batched round-trip.
                ExecuteCreatesInBatches(service, deferredCreates, 100);
            }
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
