using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Plugins;
using MultiYearDeal.Workflows;
using Newtonsoft.Json.Linq;
using System;
using System.Activities;
using System.CodeDom;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Security.Principal;
using System.Web.Services.Description;


namespace MultiYearDeal
{
    public class CreateOpportunity : CodeActivity
    {
        [Input("AgreementEntityReference")]
        [ReferenceTarget("ats_agreement")]
        public InArgument<EntityReference> AgreementEntityReference { get; set; }

        [Output("iscreateopportunitybatching")]
        public OutArgument<string> IsCreateOpportunityBatching { get; set; }

        [Output("Action")]
        public OutArgument<string> Action { get; set; }

        [Input("InputAction")]
        public InArgument<string> InputAction { get; set; }

        [Output("isFromBatching")]
        public OutArgument<bool> isFromBatching { get; set; }

        [Output("isDeleteOpportunity")]
        public OutArgument<bool> isDeleteOpportunity { get; set; }

        [Output("OpportunityEntityReference")]
        [ReferenceTarget("opportunity")]
        public OutArgument<EntityReference> OpportunityEntityReference { get; set; }

        enum ats_type_opp
        {
            New_Business = 100000000,
            Existing_Business = 100000001,
            Renewal_Upgrade = 100000002
        }

        enum ats_type_agreement
        {
            New = 114300000,
            Renewal = 114300001
        }


        private class SeasonInfo
        {
            public EntityReference SeasonRef { get; set; }
            public string SeasonName { get; set; }   // ex: "2025" or "2025 - Playoff"
            public int SeasonYear { get; set; }      // numeric year part
            public bool IsPlayoff { get; set; }
        }

        private class PendingPackageLink
        {
            public Entity ComponentOppProdTemp { get; set; }
            public Guid CreatedComponentOppProdId { get; set; }
            public string AgreementOppProd { get; set; }
            public EntityReference PackageLineProductId { get; set; }
        }

        public class OpportunityDataExtension
        {
            public Guid OpportunityId { get; set; }
            public string StartSeasonName { get; set; }
            public decimal? EscalationValue { get; set; }
            public Money DealValue { get; set; }
            public Money ManualAmount { get; set; }
            public OptionSetValue EscalationType { get; set; }
        }


        protected override void Execute(CodeActivityContext context)
        {
            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            ITracingService tracingService = context.GetExtension<ITracingService>();

            Process(service, tracingService, context);
        }

        private sealed class SeasonSortKey
        {
            public int StartYear { get; set; }
            public int TypeRank { get; set; } // 0 = Transition, 1 = Normal
            public int EndYear { get; set; }
        }

        private SeasonSortKey GetSeasonSortKey(string seasonName)
        {
            var key = new SeasonSortKey
            {
                StartYear = int.MaxValue,
                EndYear = int.MaxValue,
                TypeRank = 1
            };

            if (string.IsNullOrWhiteSpace(seasonName))
                return key;

            var s = seasonName.Trim();

            // Extract starting year (must start with digits)
            var startDigits = new string(s.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(startDigits, out int startYear))
                return key;

            key.StartYear = startYear;

            // Transition comes before normal range
            if (s.IndexOf("transition", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                key.TypeRank = 0;
                key.EndYear = startYear;
                return key;
            }

            // Try to extract end year (2027 - 2028)
            var digitGroups = new List<string>();
            var buffer = new List<char>();

            foreach (var ch in s)
            {
                if (char.IsDigit(ch))
                    buffer.Add(ch);
                else if (buffer.Count > 0)
                {
                    digitGroups.Add(new string(buffer.ToArray()));
                    buffer.Clear();
                }
            }
            if (buffer.Count > 0)
                digitGroups.Add(new string(buffer.ToArray()));

            if (digitGroups.Count >= 2 && int.TryParse(digitGroups[1], out int endYear))
                key.EndYear = endYear;
            else
                key.EndYear = startYear;

            return key;
        }

        public void Process(IOrganizationService service, ITracingService tracingService = null, CodeActivityContext context = null, EntityReference agreementRefER = null, string inputActionNameER = null)
        {
            string functionName = "Execute";
            try
            {
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Tracing initialized");

                #region Total deal value of Agreement Update
                var agreementRef = context != null ? AgreementEntityReference.Get(context) : agreementRefER;
                //var agreementRef = AgreementEntityReference.Get(context);
                if (agreementRef == null || agreementRef.Id == Guid.Empty)
                    throw new InvalidPluginExecutionException("AgreementEntityReference is null/empty.");

                //string inputActionName = InputAction.Get(context) ?? string.Empty;
                string inputActionName = string.Empty;


                if (context == null)
                {
                    inputActionName = inputActionNameER;
                }
                else
                {
                    inputActionName = InputAction.Get(context) ?? string.Empty;
                }

                //tracingService.Trace($"Input Action: {inputActionName}");
                TraceHelper.Trace(tracingService, "InputAction: {0}", inputActionName);

                // If your existing logic calls totalDealValueAgreement on demand
                if (inputActionName == "CalculateTotalDealValueAgreement")
                {
                    tracingService.Trace("Input Action is CalculateTotalDealValueAgreement");
                    AgreementCartAction aggCartactObjj = new AgreementCartAction();
                    aggCartactObjj.totalDealValueAgreement(agreementRef.Id.ToString(), tracingService, service);
                    if (context != null) IsCreateOpportunityBatching.Set(context, "false");
                    Logging.Log("total deal value of the agreement updated successfully", tracingService);
                    return;
                }
                #endregion

                // Retrieve Agreement (keep ColumnSet minimal if possible, but using true as your old code)
                Entity agreementRecord = service.Retrieve(agreementRef.LogicalName, agreementRef.Id, new ColumnSet(true));

                Guid agreementId = agreementRecord.Id;
                string agreementName = agreementRecord.GetAttributeValue<string>("ats_name");
                EntityReference accountRef = agreementRecord.GetAttributeValue<EntityReference>("ats_account");
                EntityReference startSeasonRef = agreementRecord.GetAttributeValue<EntityReference>("ats_startseason");
                int contractLength = agreementRecord.GetAttributeValue<int>("ats_contractlengthyears");

                int agreementTypeValue = 0;
                if (agreementRecord.Attributes.Contains("ats_type"))
                    agreementTypeValue = ((OptionSetValue)agreementRecord["ats_type"]).Value;

                decimal totalDealValue = agreementRecord.Attributes.Contains("ats_totaldealvalue")
                    ? (decimal)agreementRecord["ats_totaldealvalue"]
                    : 0m;

                string contactStartDate = agreementRecord.Attributes.Contains("ats_contractstartdate")
                    ? ((DateTime)agreementRecord["ats_contractstartdate"]).Date.ToString()
                    : string.Empty;

                string contactEndDate = agreementRecord.Attributes.Contains("ats_contractenddate")
                    ? ((DateTime)agreementRecord["ats_contractenddate"]).Date.ToString()
                    : string.Empty;

                TraceHelper.Trace(
      tracingService,
      "AgreementId: {0}, Name: {1}, ContractLength: {2}, AgreementTypeValue: {3}",
      agreementId,
      agreementName,
      contractLength,
      agreementTypeValue);

                //Here the main logic start for the season
                //startSeasonRef --> this is the start season of the Agreement 
                //retreiving the startseason record which belongs to the Agreement record
                Entity agreementStartSeason = service.Retrieve("ats_season", startSeasonRef.Id, new ColumnSet("ats_name"));
                Dictionary<int, Entity> seasonDict = null;
                if (agreementStartSeason.Contains("ats_name"))
                {
                    string seasonName = (string)agreementStartSeason["ats_name"];

                    OpportunitesImpactPlugin oppImpactPlugin = new OpportunitesImpactPlugin();
                    seasonDict = oppImpactPlugin.GetSeasonsChainBySeasonName(service, seasonName, tracingService, contractLength);

                }
                else
                {
                    throw new InvalidPluginExecutionException($"Season:{startSeasonRef.Id}, Name is null");
                }


                if (seasonDict == null || seasonDict.Count == 0)
                    throw new InvalidPluginExecutionException("Season chain is empty.");


                TraceHelper.Trace(tracingService, "Now Proceeding with getting the 1st and last entity details in the seasonDict");
                //Getting the last season to get the end season year
                int lastSeasonKey = seasonDict.Keys.Max();
                int firstSeasonKey = seasonDict.Keys.Min();
                TraceHelper.Trace(tracingService, "lastSeasonKey: {0}, and firstSeasonKey: {1}", lastSeasonKey, firstSeasonKey);
                Entity firstSeasonEntity = seasonDict[firstSeasonKey];

                Entity lastSeasonEntity = seasonDict[lastSeasonKey];
                string lastSesonName = lastSeasonEntity.GetAttributeValue<string>("ats_name") ?? string.Empty;
                string firstSeasonName = firstSeasonEntity.GetAttributeValue<string>("ats_name") ?? string.Empty;


                TraceHelper.Trace(tracingService, "firstSeasonName: {0}, and lastSesonName: {1}", firstSeasonName, lastSesonName);



                foreach (var kvp in seasonDict)
                {
                    int seasonIndex = kvp.Key;
                    Entity seasonEntity = kvp.Value;

                    string seasonName = seasonEntity.GetAttributeValue<string>("ats_name") ?? string.Empty;

                    TraceHelper.Trace(
                        tracingService,
                        "Season Index: {0}, Season Name: {1}",
                        seasonIndex,
                        seasonName
                    );

                }





                //Now retrieving the opportunities from the Agreement 

                // 1) Retrieve all Opportunities for the Agreement (single fetch)
                QueryExpression opportunityQuery = new QueryExpression("opportunity")
                {
                    ColumnSet = new ColumnSet(
                        "opportunityid",
                        "ats_startseason",
                        "ats_dealvalue",
                        "ats_escalationvalue",
                        "ats_escalationtype",
                        "ats_manualamount"
                    ),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                                    {
                                        new ConditionExpression(
                                            "ats_agreement",
                                            ConditionOperator.Equal,
                                            agreementId
                                        )
                                    }
                    },
                    Orders =
                                {
                                    new OrderExpression(
                                        "ats_startseason",
                                        OrderType.Ascending
                                    )
                                }
                };

                EntityCollection opportunityCollection = service.RetrieveMultiple(opportunityQuery);

                var sortedOpportunityCollection = opportunityCollection.Entities
    .Where(e => e.Contains("ats_startseason"))
    .OrderBy(e =>
        GetSeasonSortKey(
            e.GetAttributeValue<EntityReference>("ats_startseason")?.Name
        ).StartYear)
    .ThenBy(e =>
        GetSeasonSortKey(
            e.GetAttributeValue<EntityReference>("ats_startseason")?.Name
        ).TypeRank)     // Transition first
    .ThenBy(e =>
        GetSeasonSortKey(
            e.GetAttributeValue<EntityReference>("ats_startseason")?.Name
        ).EndYear)      // Range after transition
    .ToList();

                // Build a dictionary of existing opp by seasonId (fast lookup)
                var oppBySeasonId = sortedOpportunityCollection
                    .Where(o => o.GetAttributeValue<EntityReference>("ats_startseason") != null)
                    .ToDictionary(o => o.GetAttributeValue<EntityReference>("ats_startseason").Id, o => o);

                foreach (var s in oppBySeasonId)
                {
                    TraceHelper.Trace(tracingService, "oppBySeasonId Key:" + s.Key.ToString() + ", value:" + s.Value.ToString() + ", s.Value.Id:" + s.Value.Id.ToString());
                }

                //var oppBySeasonId = opportunityCollection.Entities
                //                    .Where(o => o.GetAttributeValue<EntityReference>("ats_startseason") != null)
                //                    .GroupBy(o => o.GetAttributeValue<EntityReference>("ats_startseason").Id)
                //                    .ToDictionary(g => g.Key, g => g.First());


                TraceHelper.Trace(
                    tracingService,
                    "Existing opp count for agreement: {0}",
                    opportunityCollection.Entities.Count
                );

                // 2) Identify last opportunity and products to copy(only once)
                Guid existingOpportunityId = Guid.Empty;
                Guid lastSeasonId = Guid.Empty;
                decimal dealValueLastOpp = 0m;

                OpportunityDataExtension oppData = null;
                bool isManualEscalate = false;

                if (sortedOpportunityCollection.Count > 0)
                {
                    Entity lastOpp = sortedOpportunityCollection.Last();
                    existingOpportunityId = lastOpp.Id;
                    EntityReference lastSeason = lastOpp.GetAttributeValue<EntityReference>("ats_startseason");
                    if (lastSeason != null) lastSeasonId = lastSeason.Id;

                    dealValueLastOpp = lastOpp.Contains("ats_dealvalue") && lastOpp["ats_dealvalue"] != null
                        ? ((Money)lastOpp["ats_dealvalue"]).Value
                        : 0m;

                    if (lastOpp.Attributes.Contains("ats_escalationvalue") && lastOpp["ats_escalationvalue"] != null)
                    {
                        isManualEscalate = true;
                        oppData = new OpportunityDataExtension
                        {
                            OpportunityId = lastOpp.Id,
                            StartSeasonName = lastSeason?.Name,
                            EscalationValue = lastOpp.GetAttributeValue<Money>("ats_escalationvalue")?.Value,
                            DealValue = lastOpp.GetAttributeValue<Money>("ats_dealvalue"),
                            ManualAmount = lastOpp.GetAttributeValue<Money>("ats_manualamount"),
                            EscalationType = lastOpp.GetAttributeValue<OptionSetValue>("ats_escalationtype")
                        };
                    }
                }

                #region opportunity Delete based on the contract length 
                // 3) Remove opportunities outside the new season range (and delete them)
                List<Entity> oppsToDelete = new List<Entity>();

                if (contractLength != sortedOpportunityCollection.Count)
                {


                    for (int i = sortedOpportunityCollection.Count - 1; i >= contractLength; i--)
                    {
                        Entity opp = sortedOpportunityCollection[i];
                        oppsToDelete.Add(opp);
                    }
                    TraceHelper.Trace(tracingService, "total count of oppsToDelete: {0}", oppsToDelete.Count);

                    if (oppsToDelete.Count > 0)
                    {
                        AgreementCartAction aggCartactObj = new AgreementCartAction();

                        foreach (var opp in oppsToDelete)
                        {
                            service.Delete("opportunity", opp.Id);
                            Logging.Log($"Deleted Opportunity with ID: {opp.Id}", tracingService);
                        }

                        if (context != null) isDeleteOpportunity.Set(context, true);

                        // update deal value once after deletes (not per delete)
                        aggCartactObj.totalDealValueAgreement(agreementRef.Id.ToString(), tracingService, service);

                        TraceHelper.Trace(tracingService, "Deleted opportunities beyond range. Exiting.");

                        return;
                    }
                }
                #endregion

                //// Update agreement total deal value once (like your old behavior)
                //{
                //    AgreementCartAction aggCartactObj = new AgreementCartAction();
                //    aggCartactObj.totalDealValueAgreement(agreementRef.Id.ToString(), tracingService, service);
                //    Logging.Log("total deal value Agreement updated", tracingService);
                //}

                // 4) Load settingId ONCE (your AgilitekSettings lookup)
                Guid settingId = GetAgilitekSettingId(service, tracingService, "Opportunity Target Amount");

                // 5) Get BPF workflowId ONCE (avoid querying workflow per opp)
                Guid? bpfWorkflowId = GetBpfWorkflowId(service, tracingService, "ats_opportunitybpf");

                //bool isZeroContractLength = false;

                //validating for the contract lenght, and return if its, Zero. 
                if (contractLength == 0)
                {
                    TraceHelper.Trace(tracingService, "Contract Length is Zero.");
                    return;
                }

                // Agreement end season ref (used for updates/creates)
                //retreving the end season based on the seasonDistinct --> ContractLength

                EntityReference agreementEndSeasonRef = new EntityReference("ats_season", lastSeasonEntity.Id);

                TraceHelper.Trace(tracingService, "agreementEndSeasonRef.Logical Name: {0}, and agreementEndSeasonRef.Id: {1}", agreementEndSeasonRef.LogicalName, agreementEndSeasonRef.Id);

                // 7) Update existing opportunities ONCE 
                //    Only update those that exist, and only once per opp.

                foreach (var kvp in oppBySeasonId)
                {
                    var opp = kvp.Value;
                    var seasonRef = opp.GetAttributeValue<EntityReference>("ats_startseason");
                    if (seasonRef == null) continue;

                    var match = seasonDict.FirstOrDefault(vp => vp.Value.Id == seasonRef.Id);

                    int existingSeasonKey = match.Value == null ? -1 : match.Key;


                    if (existingSeasonKey < firstSeasonKey || existingSeasonKey > lastSeasonKey || existingSeasonKey == -1)
                        continue;


                    Entity update = new Entity("opportunity", opp.Id);

                    // Set ATS TYPE based on first season or later seasons
                    // Determine if this opp is first season
                    bool isFirstSeason = false;
                    if (existingSeasonKey == 0)//1st season basedon the Agreement starting season
                        isFirstSeason = true;

                    update["ats_type"] = new OptionSetValue(GetOppType(agreementTypeValue, isFirstSeason));
                    update["ats_contractlengthinyears"] = contractLength;

                    if (!string.IsNullOrWhiteSpace(contactStartDate))
                        update["ats_agreementstartdate"] = Convert.ToDateTime(contactStartDate).Date;
                    if (!string.IsNullOrWhiteSpace(contactEndDate))
                        update["ats_agreementenddate"] = Convert.ToDateTime(contactEndDate).Date;

                    update["ats_agreementtotalvalue"] = totalDealValue;
                    update["ats_agreementstartseason"] = startSeasonRef;
                    update["ats_agreementendseason"] = agreementEndSeasonRef;

                    service.Update(update);
                    TraceHelper.Trace(tracingService, "Exisitng opportunity updated Sucessfully, Trgeting the opportuniy type.");

                }



                // 8) Retrieve products to copy ONCE from last opp (if any)
                List<Entity> productsToCopy = new List<Entity>();
                GetOpportunityProducts getOpportunityProducts = new GetOpportunityProducts();

                if (existingOpportunityId != Guid.Empty)
                {
                    productsToCopy = getOpportunityProducts.RetrieveOpportunityProducts(existingOpportunityId, service, true, tracingService);
                    Logging.Log($"Retrieved {productsToCopy.Count} products from last Opportunity {existingOpportunityId}", tracingService);
                }

                // 9) Create missing opportunities (still supports your "one at a time batching" behavior)
                //    We'll create at most 1 new opp in this run (as you do currently)
                int opportunitiesCreated = 0;
                Guid lastTouchedOppId = Guid.Empty;

                // Track newly created opps for rollup calculation at end
                List<Guid> newlyCreatedOppIds = new List<Guid>();



                int totalSeasonCount = seasonDict?.Count ?? 0;
                TraceHelper.Trace(tracingService, $"totalSeasonCount:{totalSeasonCount}");

                EntityReference lastSeaonOpportunityEntityReference = null;
                Entity lastSeasonOpportunity = null;
                int exisitngSeasonOppKey = -1;

                if (oppBySeasonId == null || oppBySeasonId.Count == 0)
                {
                    TraceHelper.Trace(tracingService, "No exisitng opp found.");

                }
                else
                {
                    lastSeasonOpportunity = oppBySeasonId
                                         .LastOrDefault()
                                         .Value;



                    lastSeaonOpportunityEntityReference =
         lastSeasonOpportunity.GetAttributeValue<EntityReference>("ats_startseason");



                    if (lastSeaonOpportunityEntityReference == null || lastSeaonOpportunityEntityReference.Id == Guid.Empty)
                    {
                        TraceHelper.Trace(tracingService, "lastSeaonOpportunityEntityReference is missing");
                    }
                    else
                    {
                        var match = seasonDict.FirstOrDefault(kvp => kvp.Value.Id == lastSeaonOpportunityEntityReference.Id);

                        if (match.Value == null)
                        {
                            TraceHelper.Trace(tracingService,
                                "Start season from lastSeasonOpportunity not found in seasonDict. SeasonId: {0}",
                                lastSeaonOpportunityEntityReference.Id);
                        }
                        else
                        {
                            exisitngSeasonOppKey = match.Key;
                            TraceHelper.Trace(tracingService, "exisitngSeasonOppKey: {0}", exisitngSeasonOppKey);
                        }
                    }

                }

                int totalexistingSeasonByOpp = -1;

                foreach (var season in seasonDict.OrderBy(s => s.Key))
                {
                    int seasonOrder = season.Key;
                    Entity seasonEntity = season.Value;

                    if (exisitngSeasonOppKey != -1) 
                    {
                        if (seasonOrder <= exisitngSeasonOppKey) //exisitng opportunities would be bypass and no opportunity creation would be done 
                        {
                            TraceHelper.Trace(tracingService, "seasonOrder BYpass: {0}", seasonOrder);
                            totalexistingSeasonByOpp++;
                            continue;
                        }
                    }


                    string seasonName = seasonEntity.Contains("ats_name") ? (string)seasonEntity["ats_name"] : throw new InvalidPluginExecutionException($"Season name is missing in this Season record id: {seasonEntity.Id}");

                    TraceHelper.Trace(tracingService, "seasonOrder: {0}, and seasonName: {1}", seasonOrder, seasonName);

                    // Create new Opportunity
                    Entity oppCreate = new Entity("opportunity");

                    oppCreate["name"] = $"{agreementName} - {seasonName}";
                    oppCreate["parentaccountid"] = accountRef;
                    oppCreate["ats_agreement"] = agreementRef;
                    oppCreate["ats_startseason"] = new EntityReference(seasonEntity.LogicalName, seasonEntity.Id);

                    //Sunny(20-2-26)--> Initilaizing the Opportunity Season Sorted Order field based on the season disct Key
                    TraceHelper.Trace(tracingService, "seasonOrder for the Opportunity: {0}", seasonOrder);
                    oppCreate["ats_opportunityseasonsortedorder"] = seasonOrder; 

                    bool isFirstSeason = false;
                    if (opportunityCollection.Entities.Count == 0)
                    {
                        isFirstSeason = true;
                    }

                    oppCreate["ats_type"] = new OptionSetValue(GetOppType(agreementTypeValue, isFirstSeason));

                    oppCreate["ats_contractlengthinyears"] = contractLength;

                    if (!string.IsNullOrWhiteSpace(contactStartDate))
                        oppCreate["ats_agreementstartdate"] = Convert.ToDateTime(contactStartDate).Date;
                    if (!string.IsNullOrWhiteSpace(contactEndDate))
                        oppCreate["ats_agreementenddate"] = Convert.ToDateTime(contactEndDate).Date;

                    // pricing/escalation logic from your old code
                    if (oppData != null && oppData.EscalationValue.HasValue && oppData.DealValue != null && oppData.EscalationType != null)
                    {
                        oppCreate["ats_pricingmode"] = new OptionSetValue(559240001); // escalated
                        oppCreate["ats_dealvalue"] = oppData.DealValue;
                        oppCreate["ats_escalationvalue"] = new Money(oppData.EscalationValue.Value);
                        oppCreate["ats_manualamount"] = oppData.ManualAmount;
                        oppCreate["ats_escalationtype"] = oppData.EscalationType;
                    }
                    else
                    {
                        oppCreate["ats_pricingmode"] = new OptionSetValue(559240000); // default
                        oppCreate["ats_dealvalue"] = dealValueLastOpp;
                    }

                    oppCreate["ats_agreementtotalvalue"] = totalDealValue;
                    oppCreate["ats_agreementstartseason"] = startSeasonRef;
                    oppCreate["ats_agreementendseason"] = agreementEndSeasonRef;


                    if (settingId != Guid.Empty)
                        oppCreate["ats_settingpercentofratecardtarget"] = new EntityReference("ats_agiliteksettings", settingId);

                    Guid newOpportunityId = service.Create(oppCreate);
                    newlyCreatedOppIds.Add(newOpportunityId);

                    lastTouchedOppId = newOpportunityId;
                    opportunitiesCreated++;

                    // Create BPF instance (if workflow found)
                    if (bpfWorkflowId.HasValue)
                    {
                        CreateBpfInstance(service, tracingService, bpfWorkflowId.Value, newOpportunityId);
                    }


                    if (isManualEscalate)
                    {
                        try
                        {
                            TotalEsclateRevenue totalEscRevenue = new TotalEsclateRevenue();
                            totalEscRevenue.individualEscalateRevenue(
                                string.Empty,
                                false,
                                string.Empty,
                                "UpdateOpportunity",
                                newOpportunityId.ToString(),
                                string.Empty,
                                0m,
                                service,
                                tracingService
                            );

                        }
                        catch (Exception ex)
                        {
                            TraceHelper.Trace(tracingService, "Manual escalation call failed (non-blocking): {0}", ex.Message);
                        }
                    }

                    // Copy products (service.Create only)
                    if (productsToCopy != null && productsToCopy.Count > 0)
                    {
                        CopyProductsToNewOpportunity_OptimizedNoBatch(
                            service,
                            tracingService,
                            getOpportunityProducts,
                            productsToCopy,
                            newOpportunityId,
                            seasonEntity.Id,
                            lastSeasonId
                        );
                    }


                    // Your existing behavior: only create ONE opportunity per run
                    if (opportunitiesCreated == 1)
                    {
                        TraceHelper.Trace(tracingService, "Single opportunity created (batching behavior). Created for seasonYear={0}. Breaking.", seasonEntity.Id);
                        break;
                    }

                }

                // 10) Rollup calculation AFTER creates (not inside product loop)
                // (Optional; if still slow, remove this and let rollup happen async/system)
                foreach (var oppId in newlyCreatedOppIds)
                {
                    TryCalculateRollup(service, tracingService, "opportunity", oppId, "ats_totalratecard");
                }

                // 11) Set batching output flags 

                // Output: iscreateopportunitybatching
                // If there are still missing opps, set true else false
                bool hasMissingOpps = opportunitiesCreated == 1;

                //retrieival of the Agreement record to check the total count of opp
                QueryExpression opportunityQueryUpdated = new QueryExpression("opportunity")
                {
                    ColumnSet = new ColumnSet(
                      "opportunityid",
                      "ats_startseason",
                      "ats_dealvalue",
                      "ats_escalationvalue",
                      "ats_escalationtype",
                      "ats_manualamount"
                  ),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                                    {
                                        new ConditionExpression(
                                            "ats_agreement",
                                            ConditionOperator.Equal,
                                            agreementId
                                        )
                                    }
                    },
                    Orders =
                                {
                                    new OrderExpression(
                                        "ats_startseason",
                                        OrderType.Ascending
                                    )
                                }
                };

                EntityCollection opportunityCollectionUpdated = service.RetrieveMultiple(opportunityQueryUpdated);

                if (opportunityCollectionUpdated.Entities.Count == seasonDict.Count)
                {
                    if (context != null) IsCreateOpportunityBatching.Set(context, "false");
                }
                else
                {
                    if (context != null) IsCreateOpportunityBatching.Set(context, hasMissingOpps ? "true" : "false");
                }

                TraceHelper.Trace(tracingService, "IsCreateOpportunityBatching={0}", hasMissingOpps);


                if (lastTouchedOppId == Guid.Empty && sortedOpportunityCollection.Count > 0)
                {
                    // if no new created, return first existing (or last)
                    lastTouchedOppId = sortedOpportunityCollection[0].Id;
                }

                if (context != null) OpportunityEntityReference.Set(context, lastTouchedOppId == Guid.Empty
                    ? null
                    : new EntityReference("opportunity", lastTouchedOppId));

                if (context != null) Action.Set(context, "ReCalcOppLinesAgreement");
                if (context != null) isFromBatching.Set(context, true);
                if (context != null) isDeleteOpportunity.Set(context, false);

                TraceHelper.Trace(
                    tracingService,
                    "Done. lastTouchedOppId={0}",
                    lastTouchedOppId
                );


            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}");
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracing"></param>
        /// <param name="entityName"></param>
        /// <param name="entityId"></param>
        /// <param name="fieldName"></param>
        private static void TryCalculateRollup(IOrganizationService service, ITracingService tracing, string entityName, Guid entityId, string fieldName)
        {
            try
            {
                TraceHelper.Trace(
                    tracing,
                    "TryCalculateRollup start. entityName={0}, entityId={1}, fieldName={2}",
                    entityName,
                    entityId,
                    fieldName
                );

                var req = new CalculateRollupFieldRequest
                {
                    Target = new EntityReference(entityName, entityId),
                    FieldName = fieldName
                };

                service.Execute(req);

                TraceHelper.Trace(
                    tracing,
                    "TryCalculateRollup success. entityName={0}, entityId={1}, fieldName={2}",
                    entityName,
                    entityId,
                    fieldName
                );
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(
                    tracing,
                    "Rollup calc failed (non-blocking) for {0}:{1} field:{2}. Error: {3}",
                    entityName,
                    entityId,
                    fieldName,
                    ex.Message
                );
            }
        }


        /// <summary>
        /// Copies products using service.Create only, but removes per-line package lookup.
        /// Creates all OLIs first, then links ats_packagelineitem in a second pass with fewer calls.
        /// </summary>
        private static void CopyProductsToNewOpportunity_OptimizedNoBatch(
        IOrganizationService service,
        ITracingService tracing,
        GetOpportunityProducts helper,
        List<Entity> productsToCopy,
        Guid newOpportunityId,
        Guid newSeasonId,
        Guid lastSeasonId)
        {
            // Defensive traces for critical inputs (common break points)
            TraceHelper.Trace(tracing, "CopyProductsToNewOpportunity_OptimizedNoBatch START");
            TraceHelper.Trace(tracing, "Inputs: newOpportunityId={0}, newSeasonId={1}, lastSeasonId={2}",
                newOpportunityId, newSeasonId, lastSeasonId);

            if (service == null)
            {
                TraceHelper.Trace(tracing, "ERROR: service is null. Exiting method.");
                return;
            }

            if (helper == null)
            {
                TraceHelper.Trace(tracing, "ERROR: helper is null. Exiting method.");
                return;
            }

            if (productsToCopy == null)
            {
                TraceHelper.Trace(tracing, "ERROR: productsToCopy is null. Exiting method.");
                return;
            }

            TraceHelper.Trace(tracing, "productsToCopy count={0}", productsToCopy.Count);

            // Pending link updates for package line mapping
            // key data we need later to link component -> its package line item
            var pendingPackageLinks = new List<PendingPackageLink>();

            // Track created oppProduct IDs (for logging)
            int createdCount = 0;
            int sourceIndex = 0;

            foreach (var source in productsToCopy)
            {
                sourceIndex++;

                if (source == null)
                {
                    TraceHelper.Trace(tracing, "WARN: source entity is null at index={0}. Skipping.", sourceIndex);
                    continue;
                }

                // Helpful to trace source identity (debugging “which line caused issue”)
                Guid sourceOppProdId = source.Id;
                TraceHelper.Trace(tracing, "Processing source OLI index={0}, sourceOppProdId={1}", sourceIndex, sourceOppProdId);

                try
                {
                    // IMPORTANT: create a NEW entity for create
                    Entity target = CloneOpportunityProductForCreate(source);
                    if (target == null)
                    {
                        TraceHelper.Trace(tracing, "ERROR: CloneOpportunityProductForCreate returned null. sourceOppProdId={0}. Skipping.", sourceOppProdId);
                        continue;
                    }

                    // Set new opp reference
                    target["opportunityid"] = new EntityReference("opportunity", newOpportunityId);

                    // Ensure InventoryBySeason for this season/product
                    EntityReference prodRef = source.GetAttributeValue<EntityReference>("productid");
                    int prodQty = source.Contains("quantity") ? Convert.ToInt32(source["quantity"]) : 0;

                    TraceHelper.Trace(tracing, "Source productid={0}, qty={1}",
                        (prodRef != null ? prodRef.Id.ToString() : "null"), prodQty);

                    EntityReference inventoryRef = null;

                    if (prodRef != null && prodRef.Id != Guid.Empty && lastSeasonId != Guid.Empty && newSeasonId != Guid.Empty)
                    {
                        try
                        {
                            TraceHelper.Trace(tracing, "EnsureInventoryForSeason: newSeasonId={0}, productId={1}, lastSeasonId={2}, prodQty={3}",
                                newSeasonId, prodRef.Id, lastSeasonId, prodQty);

                            inventoryRef = helper.EnsureInventoryForSeason(newSeasonId, prodRef.Id, lastSeasonId, prodQty, service, tracing);

                            TraceHelper.Trace(tracing, "EnsureInventoryForSeason result inventoryRef={0}",
                                (inventoryRef != null ? inventoryRef.Id.ToString() : "null"));

                            if (inventoryRef != null)
                                target["ats_inventorybyseason"] = inventoryRef;
                        }
                        catch (Exception invEx)
                        {
                            // Non-blocking but important for debugging inventory failures
                            TraceHelper.Trace(tracing, "ERROR: EnsureInventoryForSeason failed for sourceOppProdId={0}. Message={1}",
                                sourceOppProdId, invEx.Message);
                        }
                    }
                    else
                    {
                        TraceHelper.Trace(tracing, "Skipping inventory ensure due to missing refs: prodRef null/empty OR season IDs empty.");
                    }

                    // Update rate for season if needed
                    decimal hardcost = 0m;
                    if (source.Attributes.Contains("ats_rate") && source["ats_rate"] != null && inventoryRef != null)
                    {
                        try
                        {
                            var rateRef = source.GetAttributeValue<EntityReference>("ats_rate");
                            TraceHelper.Trace(tracing, "Rate update: source ats_rate={0}, inventoryRef={1}",
                                (rateRef != null ? rateRef.Id.ToString() : "null"),
                                (inventoryRef != null ? inventoryRef.Id.ToString() : "null"));

                            target["ats_rate"] = helper.GetRateForSeason(rateRef, service, inventoryRef, ref hardcost);
                            target["ats_hardcost"] = new Money(hardcost);

                            TraceHelper.Trace(tracing, "Rate updated. hardcost={0}", hardcost);
                        }
                        catch (Exception rateEx)
                        {
                            TraceHelper.Trace(tracing, "ERROR: GetRateForSeason failed for sourceOppProdId={0}. Message={1}",
                                sourceOppProdId, rateEx.Message);
                        }
                    }
                    else
                    {
                        // Useful to know why rate didn’t update
                        TraceHelper.Trace(tracing, "Skipping rate update. Has ats_rate={0}, inventoryRef={1}",
                            (source.Attributes.Contains("ats_rate") && source["ats_rate"] != null) ? "true" : "false",
                            inventoryRef != null ? inventoryRef.Id.ToString() : "null");
                    }

                    // Package link handling (DEFER linking)
                    bool isPackageLineItem = source.Attributes.Contains("ats_packagelineitem") && source["ats_packagelineitem"] != null;

                    if (isPackageLineItem)
                    {
                        TraceHelper.Trace(tracing, "Package component detected on sourceOppProdId={0}", sourceOppProdId);

                        // we will link later, after all OLIs exist
                        EntityReference prevPkgLineOppProdRef = source.GetAttributeValue<EntityReference>("ats_packagelineitem");
                        if (prevPkgLineOppProdRef != null && prevPkgLineOppProdRef.Id != Guid.Empty)
                        {
                            try
                            {
                                TraceHelper.Trace(tracing, "Retrieving prev package line: prevPkgLineOppProdId={0}", prevPkgLineOppProdRef.Id);

                                // retrieve required minimal fields ONCE for this line
                                Entity prevPkgLine = service.Retrieve(
                                    "opportunityproduct",
                                    prevPkgLineOppProdRef.Id,
                                    new ColumnSet("ats_agreementopportunityproduct", "productid")
                                );

                                string agreementOppProd = prevPkgLine.Contains("ats_agreementopportunityproduct")
                                    ? (string)prevPkgLine["ats_agreementopportunityproduct"]
                                    : string.Empty;

                                EntityReference pkgLineProductId = prevPkgLine.GetAttributeValue<EntityReference>("productid");

                                TraceHelper.Trace(tracing, "Prev package line retrieved. agreementOppProd='{0}', pkgLineProductId={1}",
                                    agreementOppProd,
                                    (pkgLineProductId != null ? pkgLineProductId.Id.ToString() : "null"));

                                // store pending info, but do NOT set ats_packagelineitem yet
                                pendingPackageLinks.Add(new PendingPackageLink
                                {
                                    ComponentOppProdTemp = target, // will be created; we will update using created ID after create
                                    AgreementOppProd = agreementOppProd,
                                    PackageLineProductId = pkgLineProductId
                                });

                                // clear packagelineitem on create to avoid wrong refs
                                if (target.Attributes.Contains("ats_packagelineitem"))
                                    target.Attributes.Remove("ats_packagelineitem");
                            }
                            catch (Exception pkgRetrieveEx)
                            {
                                // Non-blocking - still create OLI but you might miss linking
                                TraceHelper.Trace(tracing, "ERROR: retrieving prev package line failed for sourceOppProdId={0}. Message={1}",
                                    sourceOppProdId, pkgRetrieveEx.Message);
                            }
                        }
                        else
                        {
                            TraceHelper.Trace(tracing, "WARN: source has ats_packagelineitem but reference is null/empty. sourceOppProdId={0}", sourceOppProdId);
                        }
                    }

                    // Create OLI
                    Guid createdId = Guid.Empty;
                    try
                    {
                        createdId = service.Create(target);
                        createdCount++;

                        TraceHelper.Trace(tracing, "Created new OLI. createdId={0}, createdCount={1}", createdId, createdCount);
                    }
                    catch (Exception createEx)
                    {
                        TraceHelper.Trace(tracing, "ERROR: service.Create failed for sourceOppProdId={0}. Message={1}",
                            sourceOppProdId, createEx.Message);

                        // If create fails, continue to next record 

                        continue;
                    }

                    // attach created id back for pending link update
                    var lastPending = pendingPackageLinks.LastOrDefault(p => ReferenceEquals(p.ComponentOppProdTemp, target));
                    if (lastPending != null)
                    {
                        lastPending.CreatedComponentOppProdId = createdId;
                        TraceHelper.Trace(tracing, "Pending link registered for created component OLI: {0}", createdId);
                    }
                }
                catch (Exception loopEx)
                {
                    // Catch-all per record so 1 bad line doesn't break whole loop
                    TraceHelper.Trace(tracing, "ERROR: unexpected failure while processing sourceOppProdId={0}. Message={1}",
                        sourceOppProdId, loopEx.Message);
                }
            }

            TraceHelper.Trace(tracing, "Created {0} OLIs for new opportunity {1}", createdCount, newOpportunityId);
            TraceHelper.Trace(tracing, "pendingPackageLinks count={0}", pendingPackageLinks.Count);

            // SECOND PASS: Link package line item refs with ONE fetch (or few) instead of per line fetch
            if (pendingPackageLinks.Count > 0)
            {
                try
                {
                    // Build IN lists
                    var agreementOppProdVals = pendingPackageLinks
                        .Select(p => p.AgreementOppProd)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .ToList();

                    var productIds = pendingPackageLinks
                        .Select(p => p.PackageLineProductId?.Id)
                        .Where(id => id.HasValue && id.Value != Guid.Empty)
                        .Select(id => id.Value)
                        .Distinct()
                        .ToList();

                    TraceHelper.Trace(tracing, "Second pass: agreementOppProdVals count={0}, productIds count={1}",
                        agreementOppProdVals.Count, productIds.Count);

                    if (agreementOppProdVals.Count > 0 && productIds.Count > 0)
                    {
                        string fetch = BuildPackageLineFetchXml(newOpportunityId, newSeasonId, agreementOppProdVals, productIds);
                        TraceHelper.Trace(tracing, "Second pass fetchxml built. newOpportunityId={0}, newSeasonId={1}", newOpportunityId, newSeasonId);

                        var resp = service.RetrieveMultiple(new FetchExpression(fetch));
                        TraceHelper.Trace(tracing, "Second pass fetch returned {0} package line candidates.", resp.Entities.Count);

                        // map key: agreementOppProd|productId => oppProdId
                        var pkgLineMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                        foreach (var e in resp.Entities)
                        {
                            string agreementOppProd = e.Contains("ats_agreementopportunityproduct")
                                ? (string)e["ats_agreementopportunityproduct"]
                                : string.Empty;

                            var productRef = e.GetAttributeValue<EntityReference>("productid");
                            if (string.IsNullOrWhiteSpace(agreementOppProd) || productRef == null)
                                continue;

                            string key = BuildPkgKey(agreementOppProd, productRef.Id);
                            if (!pkgLineMap.ContainsKey(key))
                                pkgLineMap.Add(key, e.Id);
                        }

                        TraceHelper.Trace(tracing, "Second pass map prepared. pkgLineMap count={0}", pkgLineMap.Count);

                        int linkedCount = 0;
                        int missingMapCount = 0;

                        // Now update component lines to point to correct package line item
                        foreach (var p in pendingPackageLinks)
                        {
                            if (p.CreatedComponentOppProdId == Guid.Empty)
                            {
                                TraceHelper.Trace(tracing, "WARN: pending link has empty CreatedComponentOppProdId. Skipping.");
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(p.AgreementOppProd) || p.PackageLineProductId == null)
                            {
                                TraceHelper.Trace(tracing, "WARN: pending link has missing AgreementOppProd or PackageLineProductId. componentCreatedId={0}",
                                    p.CreatedComponentOppProdId);
                                continue;
                            }

                            string key = BuildPkgKey(p.AgreementOppProd, p.PackageLineProductId.Id);

                            if (!pkgLineMap.TryGetValue(key, out Guid pkgLineOppProdId))
                            {
                                missingMapCount++;
                                TraceHelper.Trace(tracing, "WARN: No pkg line match found for componentCreatedId={0}, key={1}",
                                    p.CreatedComponentOppProdId, key);
                                continue;
                            }

                            Entity upd = new Entity("opportunityproduct", p.CreatedComponentOppProdId);
                            upd["ats_packagelineitem"] = new EntityReference("opportunityproduct", pkgLineOppProdId);

                            try
                            {
                                service.Update(upd);
                                linkedCount++;
                            }
                            catch (Exception updEx)
                            {
                                TraceHelper.Trace(tracing, "ERROR: Linking update failed for componentCreatedId={0}. Message={1}",
                                    p.CreatedComponentOppProdId, updEx.Message);
                            }
                        }

                        TraceHelper.Trace(tracing, "Package line linking done. pending={0}, linked={1}, missingMatches={2}",
                            pendingPackageLinks.Count, linkedCount, missingMapCount);
                    }
                    else
                    {
                        TraceHelper.Trace(tracing, "Second pass skipped: agreementOppProdVals or productIds empty.");
                    } 
                }
                catch (Exception ex)
                {
                    TraceHelper.Trace(tracing, "Package link pass failed (non-blocking): {0}", ex.Message);
                }
            }

            TraceHelper.Trace(tracing, "CopyProductsToNewOpportunity_OptimizedNoBatch END");
        }

        private static string BuildPkgKey(string agreementOppProd, Guid productId)
        {
            return $"{agreementOppProd}|{productId.ToString("N")}";
        }

        // Fetch all package line items in new opp for this season, filtered by agreementOppProd and product list.
        private static string BuildPackageLineFetchXml(Guid opportunityId, Guid seasonId, List<string> agreementOppProds, List<Guid> productIds)
        {
            // Build <value> lists
            string agreementValues = string.Join("", agreementOppProds.Select(v => $"<value>{SecurityElementEscape(v)}</value>"));
            string productValues = string.Join("", productIds.Select(id => $"<value uitype='product'>{id}</value>"));

            // NOTE: This fetch assumes:
            // - package line items are normal opportunityproduct records
            // - they have ats_agreementopportunityproduct populated
            // - they are related to season via ats_inventorybyseason -> ats_season
            return $@"
                <fetch version='1.0' mapping='logical' distinct='false'>
                  <entity name='opportunityproduct'>
                    <attribute name='opportunityproductid' />
                    <attribute name='productid' />
                    <attribute name='ats_agreementopportunityproduct' />
                    <filter type='and'>
                      <condition attribute='opportunityid' operator='eq' value='{opportunityId}' />
                      <condition attribute='ats_agreementopportunityproduct' operator='in'>
                        {agreementValues}
                      </condition>
                      <condition attribute='productid' operator='in'>
                        {productValues}
                      </condition>
                    </filter>
                    <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='inner' alias='IBS'>
                      <link-entity name='ats_season' from='ats_seasonid' to='ats_season' link-type='inner' alias='Season'>
                        <filter>
                          <condition attribute='ats_seasonid' operator='eq' value='{seasonId}' />
                        </filter>
                      </link-entity>
                    </link-entity>
                  </entity>
                </fetch>";
        }




        // Clone opp product for create: remove disallowed/system fields
        private static Entity CloneOpportunityProductForCreate(Entity source)
        {
            Entity target = new Entity("opportunityproduct");

            // Copy attributes except common system / read-only / IDs / owner-related
            foreach (var kv in source.Attributes)
            {
                string k = kv.Key;

                // Exclude ID fields and system fields
                if (k.Equals("opportunityproductid", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("createdon", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("modifiedon", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("statecode", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("statuscode", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("overriddencreatedon", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("timezoneruleversionnumber", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("utcconversiontimezonecode", StringComparison.OrdinalIgnoreCase)) continue;
                if (k.Equals("versionnumber", StringComparison.OrdinalIgnoreCase)) continue;

                // Exclude parent lookup (we set opportunityid ourselves)
                if (k.Equals("opportunityid", StringComparison.OrdinalIgnoreCase)) continue;

                // You can exclude fields that should NOT be copied:
                // if (k.Equals("priceperunit", StringComparison.OrdinalIgnoreCase)) continue; // example

                target.Attributes[k] = kv.Value;
            }

            return target;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracing"></param>
        /// <param name="workflowId"></param>
        /// <param name="opportunityId"></param>
        private static void CreateBpfInstance(IOrganizationService service, ITracingService tracing, Guid workflowId, Guid opportunityId)
        {
            try
            {
                Entity bpfInstance = new Entity("ats_opportunitybpf");
                bpfInstance["bpf_opportunityid"] = new EntityReference("opportunity", opportunityId);
                bpfInstance["bpf_name"] = "Opportunity BPF";
                bpfInstance["statecode"] = new OptionSetValue(0);
                bpfInstance["processid"] = new EntityReference("workflow", workflowId);

                service.Create(bpfInstance);
            }
            catch (Exception ex)
            {
                tracing.Trace($"CreateBpfInstance failed (non-blocking): {ex.Message}");
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="agreementTypeValue"></param>
        /// <param name="isFirstSeason"></param>
        /// <returns></returns>
        private static int GetOppType(int agreementTypeValue, bool isFirstSeason)
        {
            // Agreement type options decide type of opp for first season, else always Existing_Business
            if (!isFirstSeason)
                return (int)ats_type_opp.Existing_Business;

            if (agreementTypeValue == (int)ats_type_agreement.New)
                return (int)ats_type_opp.New_Business;

            if (agreementTypeValue == (int)ats_type_agreement.Renewal)
                return (int)ats_type_opp.Renewal_Upgrade;

            return (int)ats_type_opp.Existing_Business;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracing"></param>
        /// <param name="bpfUniqueName"></param>
        /// <returns></returns>
        private static Guid? GetBpfWorkflowId(IOrganizationService service, ITracingService tracing, string bpfUniqueName)
        {
            try
            {
                QueryExpression workflowQuery = new QueryExpression("workflow")
                {
                    ColumnSet = new ColumnSet("workflowid"),
                    Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("type", ConditionOperator.Equal, 1),
                    new ConditionExpression("category", ConditionOperator.Equal, 4),
                    new ConditionExpression("uniquename", ConditionOperator.Equal, bpfUniqueName)
                }
            }
                };

                EntityCollection workflows = service.RetrieveMultiple(workflowQuery);
                if (workflows.Entities.Count == 0)
                {
                    TraceHelper.Trace(
                        tracing,
                        "BPF workflow '{0}' not found.",
                        bpfUniqueName
                    );
                    return null;
                }

                Guid workflowId = workflows.Entities[0].Id;

                TraceHelper.Trace(
                    tracing,
                    "BPF workflowId retrieved once: {0}",
                    workflowId
                );

                return workflowId;
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(
                    tracing,
                    "GetBpfWorkflowId failed: {0}",
                    ex.Message
                );

                return null;
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracing"></param>
        /// <param name="keyVar"></param>
        /// <returns></returns>
        private static Guid GetAgilitekSettingId(IOrganizationService service, ITracingService tracing, string keyVar)
        {
            try
            {
                string fetchXml = $@"
            <fetch top='1'>
              <entity name='ats_agiliteksettings'>
                <attribute name='ats_agiliteksettingsid' />
                <filter>
                  <condition attribute='ats_key' operator='eq' value='{SecurityElementEscape(keyVar)}' />
                </filter>
              </entity>
            </fetch>";

                var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
                if (result.Entities.Count > 0)
                {
                    var id = result.Entities[0].GetAttributeValue<Guid>("ats_agiliteksettingsid");

                    TraceHelper.Trace(
                        tracing,
                        "Found AgilitekSetting '{0}' ID: {1}",
                        keyVar,
                        id
                    );

                    return id;
                }

                TraceHelper.Trace(
                    tracing,
                    "No AgilitekSetting found for key: {0}",
                    keyVar
                );

                return Guid.Empty;
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(
                    tracing,
                    "GetAgilitekSettingId failed: {0}",
                    ex.Message
                );

                return Guid.Empty;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        private static string SecurityElementEscape(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
        }


    }

}
