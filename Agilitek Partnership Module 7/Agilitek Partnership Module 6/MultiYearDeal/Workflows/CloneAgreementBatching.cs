using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Model;
using Newtonsoft.Json.Linq;
using System;
using System.Activities;
using System.Activities.Presentation.Debug;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.IdentityModel.Metadata;
using System.Linq;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.Json;

namespace MultiYearDeal.Workflows
{

    public class CloneAgreementOpportunityData
    {
        public string AgreementId { get; set; }

        public string CloneAgreementId { get; set; }

        public List<string> Opportunities { get; set; }
    }


    public class CloneAgreementBatching : CodeActivity
    {
        #region Retreived Input Parameters
        [Input("AgreementId")]
        public InArgument<string> AgreementId { get; set; }

        [Input("InputActionName")]
        public InArgument<string> InputActionName { get; set; }

        [Output("OutputActionName")]
        public OutArgument<string> OutputActionName { get; set;}

        [Output("ClonedAgreementOppData")]
        public OutArgument<string> ClonedAgreementOppData { get; set; }

        [Output("ClonedAgreementId")] // Daljeet Oct 11 2025
        public OutArgument<string> ClonedAgreementId { get; set; }


        [Input("InputClonedAgreementOppData")]
        public InArgument<string> InputClonedAgreementOppData { get; set; }


        [Input("InputProductGuidList")]
        public InArgument<string> InputProductGuidList { get; set; }

        [Output("OutputProductGuidList")]
        public OutArgument<string> OutputProductGuidList { get; set; }

        [Output("TotalOppoCount")]
        public OutArgument<int> TotalOppoCount { get; set; }

        [Input("InputTotalOppoCount")]
        public InArgument<int> InputTotalOppoCount { get; set; }


        [Input("IsFirstYearClone")]
        public InArgument<bool> IsFirstYearClone { get; set; }    

        [Input("IsNotes")]
        public InArgument<bool> IsNotes { get; set; }

        [Output("IsMoreOppClone")]
        public OutArgument<bool> IsMoreOppClone { get; set; }
        #endregion



        protected override void Execute(CodeActivityContext context)
        {

            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            ITracingService tracingService = context.GetExtension<ITracingService>();

            string functionName = "Execute";
            Guid clonedAgreementId = Guid.Empty;
            try
            {

                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Tracing initialized");



                TraceHelper.Trace(tracingService, "Start Function: {0}", functionName);

                #region initializing the variables from the input parameters
                string agreementId = AgreementId.Get(context) ?? string.Empty;
                string outputClonedAgreementId = string.Empty;
                bool isFirstYearClone = IsFirstYearClone.Get(context);
                bool isNotes = IsNotes.Get(context);
                string actionName = InputActionName.Get(context) ?? string.Empty;
                TraceHelper.Trace(tracingService, "Input Parameters: agreementId:{0},clonedAgreementId:{1}, isFirstYearClone: {2},isNotes: {3}", agreementId, outputClonedAgreementId, isFirstYearClone, isNotes);
                bool isComingFromAgreement = false;
                int totalOppCountFromAgrement = -1;
                #endregion

                CloneAgreementOpportunityData agreementOpportunityDataFromAgreement = null;
                if (agreementId != string.Empty)// Cloned Agreegment Executed for the 1st time
                {
                    TraceHelper.Trace(tracingService, "Cloned Agreement executed, Proceeding for creating the clone agreement record");

                    CloneAgreementOpportunityData agreementOpportunityData = new CloneAgreementOpportunityData();
                    agreementOpportunityData.AgreementId = agreementId;

                    //retreiving the Agreement record details and associated opportunities also
                    TraceHelper.Trace(tracingService, "agreementId: {0}", agreementId);

                    // Build Fetch
                    string fetchXml = $@"
                                <fetch>
                                  <entity name='ats_agreement'>
                                    <attribute name='ats_name' />
                                    <attribute name='ats_startseason' />
                                    <attribute name='ats_account' />
                                    <attribute name='ats_closedate' />
                                    <attribute name='ats_type' />
                                    <attribute name='ats_stage' />
                                    <attribute name='ats_contractlengthyears' />
                                    <filter>
                                      <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                                    </filter>
                                    <link-entity name='opportunity' from='ats_agreement' to='ats_agreementid' link-type='inner' alias='Opp'>
                                      <attribute name='opportunityid' />
                                      <attribute name='name' />
                                      <attribute name='actualclosedate' />
                                      <attribute name='actualvalue' />
                                      <attribute name='ats_agreement' />
                                      <attribute name='ats_agreementenddate' />
                                      <attribute name='ats_agreementstartdate' />
                                      <attribute name='ats_dealvalue' />
                                      <attribute name='ats_agencyamount' />
                                      <attribute name='ats_alternate' />
                                      <attribute name='ats_barteramount' />
                                      <attribute name='ats_barterterms' />
                                      <attribute name='ats_billingcontact' />
                                      <attribute name='ats_billingterms' />
                                      <attribute name='ats_bpfstatus' />
                                      <attribute name='ats_cashamount' />
                                      <attribute name='ats_contactid' />
                                      <attribute name='ats_contractterms' />
                                      <attribute name='ats_escalationtype' />
                                      <attribute name='ats_escalationvalue' />
                                      <attribute name='ats_exclusivityterms' />
                                      <attribute name='ats_financenotes' />
                                      <attribute name='ats_manualamount' />
                                      <attribute name='ats_percentofrate' />
                                      <attribute name='ats_percentofratecard' />
                                      <attribute name='ats_playoffround1game1' />
                                      <attribute name='ats_playoffround1game2' />
                                      <attribute name='ats_playoffround1game3' />
                                      <attribute name='ats_playoffround2' />
                                      <attribute name='ats_playoffround3' />
                                      <attribute name='ats_playoffround4' />
                                      <attribute name='ats_playoffterms' />
                                      <attribute name='ats_pricingmode' />
                                      <attribute name='ats_season' />
                                      <attribute name='ats_settingpercentofratecardtarget' />
                                      <attribute name='ats_stage' />
                                      <attribute name='ats_startseason' />
                                      <attribute name='ats_targetamount' />
                                      <attribute name='ats_ticketingnotes' />
                                      <attribute name='ats_totalhardcost' />
                                      <attribute name='ats_totalplayoffrevenue' />
                                      <attribute name='ats_totalratecard' />
                                      <attribute name='ats_totalseasonrevenue' />
                                      <attribute name='ats_tradeamount' />
                                      <attribute name='ats_type' />
                                      <attribute name='budgetamount' />
                                      <attribute name='discountamount' />
                                      <attribute name='exchangerate' />
                                      <attribute name='freightamount' />
                                      <attribute name='parentaccountid' />
                                      <attribute name='qualificationcomments' />
                                      <attribute name='quotecomments' />
                                      <attribute name='totalamount' />
                                       <link-entity name='ats_season' from='ats_seasonid' to='ats_startseason' alias='Season'>
                                          <attribute name='ats_name' />
                                          <order attribute='ats_name' descending='false' />
                                        </link-entity>
                                    </link-entity>
                                  </entity>
                                </fetch>";

                    // Fetch the records
                    EntityCollection results = service.RetrieveMultiple(new FetchExpression(fetchXml));

                    //creating the Agreement clone record
                    Entity entityAgreementRecord = results[0];

                    #region Creating the clone Agreement record
                    TraceHelper.Trace(tracingService, "Proceed for the Agreement");
                    Guid seasonId = entityAgreementRecord.GetAttributeValue<EntityReference>("ats_startseason")?.Id ?? Guid.Empty;
                    TraceHelper.Trace(tracingService, "Season ID: {0}", seasonId);
                    Guid accountId = entityAgreementRecord.GetAttributeValue<EntityReference>("ats_account")?.Id ?? Guid.Empty;
                    TraceHelper.Trace(tracingService, "Account ID: {0}", accountId);
                    Entity agreement = new Entity("ats_agreement");
                    OptionSetValue stage = entityAgreementRecord.GetAttributeValue<OptionSetValue>("ats_stage");
                    TraceHelper.Trace(tracingService, "Stage: {0}", stage?.Value);
                    string agreementName = entityAgreementRecord.GetAttributeValue<string>("ats_name") ?? string.Empty;
                    TraceHelper.Trace(tracingService, "Original Agreement Name: {0}", agreementName);
                    agreement["ats_name"] = agreementName + " [Cloned]";
                    TraceHelper.Trace(tracingService, "Cloned Agreement Name: agreementName");
                    //agreement["ats_startdate"] = DateTime.Now;

                    if (isFirstYearClone)// contract length is 1
                    {
                        agreement["ats_contractlengthyears"] = 1;
                    }
                    else
                    {
                        agreement["ats_contractlengthyears"] = entityAgreementRecord.GetAttributeValue<int?>("ats_contractlengthyears") ?? 0;
                    }

                    agreement["ats_startseason"] = new EntityReference("ats_season", seasonId);
                    agreement["ats_account"] = new EntityReference("account", accountId);
                    agreement["ats_type"] = entityAgreementRecord.GetAttributeValue<OptionSetValue>("ats_type");
                    agreement["ats_stage"] = stage != null ? new OptionSetValue(stage.Value) : null;
                    agreement["ats_isclone"] = true;
                    DateTime? closedDate = entityAgreementRecord.GetAttributeValue<DateTime?>("ats_closedate");
                    if (closedDate.HasValue)
                    {
                        agreement["ats_closedate"] = closedDate.Value;
                        TraceHelper.Trace(tracingService, "Closed Date set: {0}", closedDate.Value);
                    }

                    clonedAgreementId = service.Create(agreement);
                    TraceHelper.Trace(tracingService, "Ageement record is created: {0}", clonedAgreementId);
                    ClonedAgreementId.Set(context, clonedAgreementId.ToString()); // Daljeet Oct 11 2025
                    #endregion

                    List<string> opportunityGuids = new List<string>();
                    //loop iteration is for storing the associated opportunities of the agreement
                    foreach (var row in results.Entities)
                    {
                        TraceHelper.Trace(tracingService, "Loop started for adding the Associated Opportunities");
                        opportunityGuids.Add(row.GetAttributeValue<AliasedValue>("Opp.opportunityid").Value.ToString());
                        TraceHelper.Trace(tracingService, "Opportunity ID added: {0}", row.GetAttributeValue<AliasedValue>("Opp.opportunityid").Value.ToString());
                    }

                    TraceHelper.Trace(tracingService, "Total Opportunities found: {0}", opportunityGuids.Count);

                    agreementOpportunityData.Opportunities = opportunityGuids;
                    agreementOpportunityData.CloneAgreementId = clonedAgreementId.ToString();
                    TraceHelper.Trace(tracingService, "Cloned Agreemend is been created, Cloned Agreement Id: {0}", clonedAgreementId);
                    string serializedData = JsonSerializer.Serialize(agreementOpportunityData);
                    TraceHelper.Trace(tracingService, "serializedData Json: {0}", serializedData);
                    ClonedAgreementOppData.Set(context, serializedData);
                    OutputActionName.Set(context, "CreateClonedOpportunities");
                    TotalOppoCount.Set(context, opportunityGuids.Count);
                    IsMoreOppClone.Set(context, true);
                    TraceHelper.Trace(tracingService, "now returning");
                    ClonedAgreementId.Set(context, clonedAgreementId.ToString()); // Daljeet Oct 11 2025
                    TraceHelper.Trace(tracingService, "Setting ClonedAgreementGuid 1");
                    isComingFromAgreement = true;
                    agreementOpportunityDataFromAgreement = agreementOpportunityData;
                    totalOppCountFromAgrement = opportunityGuids.Count;
                    //return;
                }

                String inputActionName = InputActionName.Get(context) ?? string.Empty;
                bool isNotesFlag = IsNotes.Get(context);

                //validation - Creating the new Opportunity and Opportunity Products
                if (inputActionName == "CreateClonedOpportunities" || isComingFromAgreement)
                {
                    TraceHelper.Trace(tracingService, "inputAction name= CreateClonedOpportunities");
                    string inputClonedAgreementOppData = string.Empty;
                    //retrieval of the Opportunity, Agreement Id, Cloned Agreeement Id from the input parameter
                    if (isComingFromAgreement)
                    {
                        inputClonedAgreementOppData = clonedAgreementId.ToString();
                    }
                    else
                    {
                        inputClonedAgreementOppData = InputClonedAgreementOppData.Get(context) ?? string.Empty;
                    }
                    TraceHelper.Trace(tracingService, "inputClonedAgreementOppData: {0}", inputClonedAgreementOppData);
                    //deserializing the input parameter
                    CloneAgreementOpportunityData agreementOpportunityData = null;

                    if (isComingFromAgreement)
                    {
                        agreementOpportunityData = agreementOpportunityDataFromAgreement;
                    }
                    else
                    {
                        agreementOpportunityData = JsonSerializer.Deserialize<CloneAgreementOpportunityData>(inputClonedAgreementOppData);
                    }

                    string originalAgreementId = agreementOpportunityData.AgreementId ?? string.Empty;
                    string cloneAgreementId = agreementOpportunityData.CloneAgreementId ?? string.Empty;
                    TraceHelper.Trace(tracingService, "originalAgreementId: {0}, cloneAgreementId: {1}", originalAgreementId, cloneAgreementId);
                    clonedAgreementId = Guid.Parse(cloneAgreementId);
                    int totalOppoCount = -1;
                    if (isComingFromAgreement)
                    {
                        totalOppoCount = totalOppCountFromAgrement;
                    }
                    else
                    {
                        totalOppoCount = InputTotalOppoCount.Get(context);
                    }

                    int retreievedOpportunityCount = agreementOpportunityData.Opportunities.Count;

                    //retrival of the opportunties associated with the Agreement
                    List<string> opportunityIds = agreementOpportunityData.Opportunities;
                    string opportunityIdsString = opportunityIds[0];
                    TraceHelper.Trace(tracingService, "opportunityIdsString: {0}", opportunityIdsString);
                    Guid opportunityId = Guid.Parse(opportunityIdsString);
                    TraceHelper.Trace(tracingService, "opportunityId: {0}", opportunityId);

                    List<string> productGuidList = new List<string>();
                    if (totalOppoCount != retreievedOpportunityCount)
                    {
                        TraceHelper.Trace(tracingService, "Retreiving the product Guid list from the input");
                        string inputProductGuidList = InputProductGuidList.Get(context) ?? string.Empty;
                        productGuidList = JsonSerializer.Deserialize<List<string>>(inputProductGuidList);
                    }

                    //#region Creating the clone Opportunities
                    #region Cloning the Opportunities
                    string opportunityRetrieveFetchxml = $@"
                                        <fetch>
                                          <entity name='opportunity'>
                                            <attribute name='opportunityid' />
                                            <attribute name='name' />
                                            <attribute name='actualclosedate' />
                                            <attribute name='actualvalue' />
                                            <attribute name='ats_agreement' />
                                            <attribute name='ats_agreementenddate' />
                                            <attribute name='ats_agreementstartdate' />
                                            <attribute name='ats_dealvalue' />
                                            <attribute name='ats_agencyamount' />
                                            <attribute name='ats_alternate' />
                                            <attribute name='ats_barteramount' />
                                            <attribute name='ats_barterterms' />
                                            <attribute name='ats_billingcontact' />
                                            <attribute name='ats_billingterms' />
                                            <attribute name='ats_bpfstatus' />
                                            <attribute name='ats_cashamount' />
                                            <attribute name='ats_contactid' />
                                            <attribute name='ats_contractterms' />
                                            <attribute name='ats_escalationtype' />
                                            <attribute name='ats_escalationvalue' />
                                            <attribute name='ats_exclusivityterms' />
                                            <attribute name='ats_financenotes' />
                                            <attribute name='ats_manualamount' />
                                            <attribute name='ats_percentofrate' />
                                            <attribute name='ats_percentofratecard' />
                                            <attribute name='ats_playoffround1game1' />
                                            <attribute name='ats_playoffround1game2' />
                                            <attribute name='ats_playoffround1game3' />
                                            <attribute name='ats_playoffround2' />
                                            <attribute name='ats_playoffround3' />
                                            <attribute name='ats_playoffround4' />
                                            <attribute name='ats_playoffterms' />
                                            <attribute name='ats_pricingmode' />
                                            <attribute name='ats_season' />
                                            <attribute name='ats_settingpercentofratecardtarget' />
                                            <attribute name='ats_stage' />
                                            <attribute name='ats_startseason' />
                                            <attribute name='ats_targetamount' />
                                            <attribute name='ats_ticketingnotes' />
                                            <attribute name='ats_totalhardcost' />
                                            <attribute name='ats_totalplayoffrevenue' />
                                            <attribute name='ats_totalratecard' />
                                            <attribute name='ats_totalseasonrevenue' />
                                            <attribute name='ats_tradeamount' />
                                            <attribute name='ats_type' />
                                            <attribute name='budgetamount' />
                                            <attribute name='discountamount' />
                                            <attribute name='exchangerate' />
                                            <attribute name='freightamount' />
                                            <attribute name='parentaccountid' />
                                            <attribute name='qualificationcomments' />
                                            <attribute name='quotecomments' />
                                            <attribute name='totalamount' />
                                            <filter>
                                              <condition attribute='opportunityid' operator='eq' value='{opportunityId}' />
                                            </filter>
                                            <link-entity name='ats_season' from='ats_seasonid' to='ats_startseason' alias='Season'>
                                              <attribute name='ats_name' />
                                              <order attribute='ats_name' descending='false' />
                                            </link-entity>
                                          </entity>
                                        </fetch>";

                    EntityCollection opportunityResults = service.RetrieveMultiple(new FetchExpression(opportunityRetrieveFetchxml));

                    Guid newOppId = Guid.Empty;

                    foreach (var row in opportunityResults.Entities)
                    {
                        TraceHelper.Trace(tracingService, "Proceeding to clone opportunity...");

                        Guid originalOppId = row.Id; // Since entity is opportunity
                        TraceHelper.Trace(tracingService, "Original Opportunity ID: {0}", originalOppId);

                        Entity opp = new Entity("opportunity");

                        // Clone opportunity name
                        if (row.Contains("name")) opp["name"] = row.GetAttributeValue<string>("name");

                        // Reference to the cloned agreement
                        opp["ats_agreement"] = new EntityReference("ats_agreement", clonedAgreementId);

                        // Copy attributes safely
                        if (row.Contains("actualclosedate")) opp["actualclosedate"] = row.GetAttributeValue<DateTime?>("actualclosedate");
                        if (row.Contains("actualvalue")) opp["actualvalue"] = row.GetAttributeValue<Money>("actualvalue");
                        if (row.Contains("ats_agreementstartdate")) opp["ats_agreementstartdate"] = row.GetAttributeValue<DateTime?>("ats_agreementstartdate");
                        if (row.Contains("ats_agreementenddate")) opp["ats_agreementenddate"] = row.GetAttributeValue<DateTime?>("ats_agreementenddate");
                        if (row.Contains("ats_dealvalue")) opp["ats_dealvalue"] = row.GetAttributeValue<Money>("ats_dealvalue");
                        if (row.Contains("ats_agencyamount")) opp["ats_agencyamount"] = row.GetAttributeValue<Money>("ats_agencyamount");
                        if (row.Contains("ats_alternate")) opp["ats_alternate"] = row.GetAttributeValue<bool>("ats_alternate");
                        if (row.Contains("ats_barteramount")) opp["ats_barteramount"] = row.GetAttributeValue<Money>("ats_barteramount");
                        if (row.Contains("ats_barterterms")) opp["ats_barterterms"] = row.GetAttributeValue<string>("ats_barterterms");
                        if (row.Contains("ats_billingcontact")) opp["ats_billingcontact"] = row.GetAttributeValue<EntityReference>("ats_billingcontact");
                        if (row.Contains("ats_billingterms")) opp["ats_billingterms"] = row.GetAttributeValue<string>("ats_billingterms");
                        if (row.Contains("ats_bpfstatus")) opp["ats_bpfstatus"] = row.GetAttributeValue<OptionSetValue>("ats_bpfstatus");
                        if (row.Contains("ats_cashamount")) opp["ats_cashamount"] = row.GetAttributeValue<Money>("ats_cashamount");
                        if (row.Contains("ats_contactid")) opp["ats_contactid"] = row.GetAttributeValue<EntityReference>("ats_contactid");
                        if (row.Contains("ats_contractterms")) opp["ats_contractterms"] = row.GetAttributeValue<string>("ats_contractterms");
                        if (row.Contains("ats_escalationtype")) opp["ats_escalationtype"] = row.GetAttributeValue<OptionSetValue>("ats_escalationtype");

                        if (row.Contains("ats_escalationvalue"))
                        {
                            var val = row["ats_escalationvalue"];
                            if (val is decimal decimalVal) opp["ats_escalationvalue"] = new Money(decimalVal);
                            else if (val is Money moneyVal) opp["ats_escalationvalue"] = moneyVal;
                        }

                        if (row.Contains("ats_exclusivityterms")) opp["ats_exclusivityterms"] = row.GetAttributeValue<string>("ats_exclusivityterms");
                        if (row.Contains("ats_financenotes")) opp["ats_financenotes"] = row.GetAttributeValue<string>("ats_financenotes");
                        if (row.Contains("ats_manualamount")) opp["ats_manualamount"] = row.GetAttributeValue<Money>("ats_manualamount");
                        if (row.Contains("ats_percentofrate")) opp["ats_percentofrate"] = row.GetAttributeValue<decimal?>("ats_percentofrate");
                        if (row.Contains("ats_percentofratecard")) opp["ats_percentofratecard"] = row.GetAttributeValue<decimal?>("ats_percentofratecard");
                        if (row.Contains("ats_playoffround1game1")) opp["ats_playoffround1game1"] = row.GetAttributeValue<int?>("ats_playoffround1game1");
                        if (row.Contains("ats_playoffround1game2")) opp["ats_playoffround1game2"] = row.GetAttributeValue<int?>("ats_playoffround1game2");
                        if (row.Contains("ats_playoffround1game3")) opp["ats_playoffround1game3"] = row.GetAttributeValue<int?>("ats_playoffround1game3");
                        if (row.Contains("ats_playoffround2")) opp["ats_playoffround2"] = row.GetAttributeValue<int?>("ats_playoffround2");
                        if (row.Contains("ats_playoffround3")) opp["ats_playoffround3"] = row.GetAttributeValue<int?>("ats_playoffround3");
                        if (row.Contains("ats_playoffround4")) opp["ats_playoffround4"] = row.GetAttributeValue<int?>("ats_playoffround4");
                        if (row.Contains("ats_playoffterms")) opp["ats_playoffterms"] = row.GetAttributeValue<string>("ats_playoffterms");
                        if (row.Contains("ats_pricingmode")) opp["ats_pricingmode"] = row.GetAttributeValue<OptionSetValue>("ats_pricingmode");
                        if (row.Contains("ats_season")) opp["ats_season"] = row.GetAttributeValue<EntityReference>("ats_season");
                        if (row.Contains("ats_settingpercentofratecardtarget")) opp["ats_settingpercentofratecardtarget"] = row.GetAttributeValue<EntityReference>("ats_settingpercentofratecardtarget");
                        if (row.Contains("ats_stage")) opp["ats_stage"] = row.GetAttributeValue<OptionSetValue>("ats_stage");
                        if (row.Contains("ats_startseason")) opp["ats_startseason"] = row.GetAttributeValue<EntityReference>("ats_startseason");

                        if (row.Contains("ats_targetamount"))
                        {
                            var targetVal = row["ats_targetamount"];
                            if (targetVal is decimal decVal) opp["ats_targetamount"] = new Money(decVal);
                            else if (targetVal is Money moneyVal) opp["ats_targetamount"] = moneyVal;
                        }

                        if (row.Contains("ats_ticketingnotes")) opp["ats_ticketingnotes"] = row.GetAttributeValue<string>("ats_ticketingnotes");

                        if (row.Contains("ats_totalhardcost"))
                        {
                            var val = row["ats_totalhardcost"];
                            if (val is decimal decVal) opp["ats_totalhardcost"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["ats_totalhardcost"] = moneyVal;
                        }

                        if (row.Contains("ats_totalplayoffrevenue"))
                        {
                            var val = row["ats_totalplayoffrevenue"];
                            if (val is decimal decVal) opp["ats_totalplayoffrevenue"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["ats_totalplayoffrevenue"] = moneyVal;
                        }

                        if (row.Contains("ats_totalratecard"))
                        {
                            var val = row["ats_totalratecard"];
                            if (val is decimal decVal) opp["ats_totalratecard"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["ats_totalratecard"] = moneyVal;
                        }

                        if (row.Contains("ats_totalseasonrevenue"))
                        {
                            var val = row["ats_totalseasonrevenue"];
                            if (val is decimal decVal) opp["ats_totalseasonrevenue"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["ats_totalseasonrevenue"] = moneyVal;
                        }

                        if (row.Contains("ats_tradeamount"))
                        {
                            var val = row["ats_tradeamount"];
                            if (val is decimal decVal) opp["ats_tradeamount"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["ats_tradeamount"] = moneyVal;
                        }

                        if (row.Contains("ats_type")) opp["ats_type"] = row.GetAttributeValue<OptionSetValue>("ats_type");

                        if (row.Contains("budgetamount"))
                        {
                            var val = row["budgetamount"];
                            if (val is decimal decVal) opp["budgetamount"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["budgetamount"] = moneyVal;
                        }

                        if (row.Contains("discountamount"))
                        {
                            var val = row["discountamount"];
                            if (val is decimal decVal) opp["discountamount"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["discountamount"] = moneyVal;
                        }

                        if (row.Contains("exchangerate"))
                        {
                            var val = row["exchangerate"];
                            if (val is decimal decVal) opp["exchangerate"] = decVal;
                            else if (val is double doubleVal) opp["exchangerate"] = Convert.ToDecimal(doubleVal);
                        }

                        if (row.Contains("freightamount"))
                        {
                            var val = row["freightamount"];
                            if (val is decimal decVal) opp["freightamount"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["freightamount"] = moneyVal;
                        }

                        TraceHelper.Trace(tracingService, "ewrfew");

                        if (row.Contains("parentaccountid")) opp["parentaccountid"] = row.GetAttributeValue<EntityReference>("parentaccountid");

                        TraceHelper.Trace(tracingService, "ewrfew");

                        if (row.Contains("qualificationcomments")) opp["qualificationcomments"] = row.GetAttributeValue<string>("qualificationcomments");
                        if (row.Contains("quotecomments")) opp["quotecomments"] = row.GetAttributeValue<string>("quotecomments");

                        if (row.Contains("totalamount"))
                        {
                            var val = row["totalamount"];
                            if (val is decimal decVal) opp["totalamount"] = new Money(decVal);
                            else if (val is Money moneyVal) opp["totalamount"] = moneyVal;
                        }

                        // Finally create the opportunity
                        newOppId = service.Create(opp);
                        //clonedOpportunities.Add(originalOppId, newOppId);
                        TraceHelper.Trace(tracingService, "Cloned opportunity created: {0}", newOppId);
                    }
                    #endregion

                    #region clone the opportunity Products from primary opportunity
                    // Retrieval of the opportunity products related to the original opportunity with additional attributes and filter
                    string fetchOliClone = $@"
                        <fetch>
                          <entity name='opportunityproduct'>
                            <attribute name='productid' />
                            <attribute name='ats_quantity' />
                            <attribute name='priceperunit' />
                            <attribute name='ats_quantityofevents' />
                            <attribute name='baseamount' />
                            <attribute name='ats_sellingrate' />
                            <attribute name='uomid' />
                            <attribute name='ats_adjustedtotalprice' />
                            <attribute name='ats_hardcost' />
                            <attribute name='ats_inventorybyseason' />
                            <attribute name='ats_manualpriceoverride' />
                            <attribute name='ats_rate' />
                            <attribute name='ats_unadjustedtotalprice' />
                            <attribute name='ats_agreementopportunityproduct' />
                            <attribute name='description' />

                            <filter>
                              <condition attribute='opportunityid' operator='eq' value='{opportunityId}' />
                              <condition attribute='ats_packagelineitem' operator='null' />
                            </filter>

                            <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Product'>
                             <filter type='or'>
                                  <condition attribute='ats_ispackage' operator='eq' value='0' />
                                  <condition attribute='ats_ispackage' operator='null' />
                             </filter>
                            </link-entity>

                          </entity>
                        </fetch>";

                    EntityCollection oliCloneResults = service.RetrieveMultiple(new FetchExpression(fetchOliClone));
                    TraceHelper.Trace(tracingService, "oliCloneResults.Entities.Count: {0}", oliCloneResults.Entities.Count);
                    int count = 0;

                    foreach (var oliRow in oliCloneResults.Entities)
                    {
                        bool ispackageOLI = false;
                        Guid packageOLIId = Guid.Empty;
                        bool isComponentOLI = false;
                        CreateOLIWithPackageAndCompOppProd(packageOLIId, isComponentOLI, ispackageOLI, totalOppoCount, retreievedOpportunityCount, isNotes, newOppId, ref count, oliRow, tracingService, service);
                    }
                    #endregion

                    #region retreving and creating the package and component opportunity products
                    //retreival of the package and component Opportunity products 
                    string fetchPackageOlis = $@"
                            <fetch>
                              <entity name='opportunityproduct'>
                                <attribute name='productid' />
                                <attribute name='ats_quantity' />
                                <attribute name='priceperunit' />
                                <attribute name='ats_quantityofevents' />
                                <attribute name='baseamount' />
                                <attribute name='ats_sellingrate' />
                                <attribute name='uomid' />
                                <attribute name='ats_adjustedtotalprice' />
                                <attribute name='ats_hardcost' />
                                <attribute name='ats_inventorybyseason' />
                                <attribute name='ats_manualpriceoverride' />
                                <attribute name='ats_rate' />
                                <attribute name='ats_unadjustedtotalprice' />
                                <attribute name='ats_agreementopportunityproduct' />
                                <attribute name='description' />
                                <attribute name='ats_packagetemplate'/>
                                <attribute name='ats_packagelineitem'/>

                                <filter>
                                  <condition attribute='opportunityid' operator='eq' value='{opportunityId}' />
                                  <condition attribute='ats_packagelineitem' operator='null' />
                                </filter>

                                <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Product'>
                                  <attribute name='name' />
                                  <filter>
                                    <condition attribute='ats_ispackage' operator='eq' value='1' />
                                  </filter>
                                </link-entity>

                              </entity>
                            </fetch>";

                    EntityCollection oliPackageCloneResults = service.RetrieveMultiple(new FetchExpression(fetchPackageOlis));
                    TraceHelper.Trace(tracingService, "oliCloneResults.Entities.Count: {0}", oliPackageCloneResults.Entities.Count);
                    int countPackageOLis = 0;

                    foreach (var oliRow in oliPackageCloneResults.Entities)
                    {
                        bool ispackageOLI = true;
                        Guid packageOLIId = Guid.Empty;
                        bool isComponentOLI = false;
                        CreateOLIWithPackageAndCompOppProd(packageOLIId, isComponentOLI, ispackageOLI, totalOppoCount, retreievedOpportunityCount, isNotes, newOppId, ref countPackageOLis, oliRow, tracingService, service);
                    }

                    TraceHelper.Trace(tracingService, "Package and component OLis are created");
                    #endregion

                    TraceHelper.Trace(tracingService, "the processed opportunity from the json.: {0}", agreementOpportunityData.Opportunities.Count);

                    //removing the opportunity from the opportunity json
                    agreementOpportunityData.Opportunities.RemoveAt(0);
                    TraceHelper.Trace(tracingService, "Removed the processed opportunity from the json.: {0}", agreementOpportunityData.Opportunities.Count);

                    //serializing the json
                    ClonedAgreementOppData.Set(context, JsonSerializer.Serialize(agreementOpportunityData));

                    TraceHelper.Trace(tracingService, "Opportunitie with the Opp product is cloned.");
                    if (agreementOpportunityData.Opportunities.Count > 0 && !isFirstYearClone)
                    {
                        IsMoreOppClone.Set(context, true);
                        TraceHelper.Trace(tracingService, "More opportunities to process.");
                    }
                    else
                    {
                        IsMoreOppClone.Set(context, false);
                        TraceHelper.Trace(tracingService, "No more opportunities to process.");
                    }

                    var json = JsonSerializer.Serialize(productGuidList.Select(x => x?.ToString() ?? "").ToList());

                    //string inputProductGuidListSerialize = ; 
                    OutputProductGuidList.Set(context, json);
                    //ClonedAgreementOppData.Set(context, serializedData);
                    OutputActionName.Set(context, "CreateClonedOpportunities");
                    TotalOppoCount.Set(context, totalOppoCount);
                    //IsMoreOppClone.Set(context, true);
                    //agreementOpportunityData.CloneAgreementId = clonedAgreementId.ToString();
                    //ClonedAgreementId.Set(context, agreementOpportunityData.ToString()); 
                    ClonedAgreementId.Set(context, clonedAgreementId.ToString()); // Daljeet Oct 11 2025
                    TraceHelper.Trace(tracingService, "Setting ClonedAgreementGuid 2");
                    TraceHelper.Trace(tracingService, "Action name and total opp counts are initialized");

                    if (agreementOpportunityData.CloneAgreementId != string.Empty)
                    {
                        Guid agreementGuid = Guid.Parse(agreementOpportunityData.CloneAgreementId);
                        TraceHelper.Trace(tracingService, "agreementId: {0}", agreementGuid);
                        //Proceeding for calculating the total deal value of the Agreement
                        AgreementCartAction agreementObj = new AgreementCartAction();
                        agreementObj.updateTotalDealValAgree(agreementGuid, service, tracingService);
                        TraceHelper.Trace(tracingService, "updateTotalDealValAgree executed sucessfully.");
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Not valid Agreement Id is passed");
                    }

                    //return;
                }

                TraceHelper.Trace(tracingService, "Exit Function: {0}", functionName);


            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }

        }

        /// <summary>
        /// creating the package and component opportunity products for the cloned opportunity
        /// </summary>
        /// <param name="oliClone"></param>
        /// <param name="tracingService"></param>
        /// <param name="service"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public void CreateOLIWithPackageAndCompOppProd(Guid packageOLIId, bool isComponentOLI, bool ispackageOLI, int totalOppoCount,int retreievedOpportunityCount, bool isNotes, Guid newOppId, ref int count, Entity oliRow, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "CreatePackageAndCompOppProd";
            try
            {
                TraceHelper.Trace(tracingService, "Enter Function: {0}", functionName);

                Entity oliClone = new Entity("opportunityproduct");
                oliClone["opportunityid"] = new EntityReference("opportunity", newOppId);

                // productid
                var productId = oliRow.GetAttributeValue<EntityReference>("productid");
                TraceHelper.Trace(tracingService, "ProductId: {0}", productId != null ? productId.Id.ToString() : "null");
                oliClone["productid"] = productId;

                // ats_quantity
                var quantity = oliRow.GetAttributeValue<int?>("ats_quantity");
                TraceHelper.Trace(tracingService, "Quantity: {0}", quantity.HasValue ? quantity.Value.ToString() : "null");
                if (quantity.HasValue) oliClone["ats_quantity"] = quantity.Value;

                // priceperunit
                var pricePerUnit = oliRow.GetAttributeValue<Money>("priceperunit");
                TraceHelper.Trace(tracingService, "Price Per Unit: {0}", pricePerUnit != null ? pricePerUnit.Value.ToString() : "null");
                if (pricePerUnit != null) oliClone["priceperunit"] = pricePerUnit;

                // ats_quantityofevents
                var quantityOfEvents = oliRow.GetAttributeValue<int?>("ats_quantityofevents");
                TraceHelper.Trace(tracingService, "Quantity of Events: {0}", quantityOfEvents.HasValue ? quantityOfEvents.Value.ToString() : "null");
                if (quantityOfEvents.HasValue) oliClone["ats_quantityofevents"] = quantityOfEvents.Value;

                //Sunny(29-Nov-2025)
                //Adding the package line item and package template reference if exists in the original opportunity product
                bool isPackageTemplate = false;
                isPackageTemplate = oliRow.Contains("ats_packagetemplate");

                if (isPackageTemplate && !isComponentOLI)
                {
                    oliClone["ats_packagetemplate"] = oliRow.Contains("ats_packagetemplate") ? oliRow["ats_packagetemplate"] : null;
                }

                if (isComponentOLI)
                {
                    oliClone["ats_packagelineitem"] = new EntityReference("opportunityproduct", packageOLIId);
                }

                // baseamount
                if (oliRow.Attributes.TryGetValue("baseamount", out var baseAmountObj))
                {
                    if (baseAmountObj is Money moneyVal)
                    {
                        oliClone["baseamount"] = moneyVal;
                    }
                    else if (baseAmountObj is decimal decimalVal)
                    {
                        oliClone["baseamount"] = new Money(decimalVal);
                    }
                    else if (baseAmountObj is double doubleVal)
                    {
                        oliClone["baseamount"] = new Money((decimal)doubleVal);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "baseamount type not handled: {0}", baseAmountObj.GetType());
                    }
                }
                TraceHelper.Trace(tracingService, "Base Amount set in oliClone.");

                // ats_sellingrate
                if (oliRow.Attributes.TryGetValue("ats_sellingrate", out var sellingRateObj))
                {
                    if (sellingRateObj is Money moneySellingRate)
                    {
                        TraceHelper.Trace(tracingService, "Selling Rate (Money): {0}", moneySellingRate.Value.ToString());
                        oliClone["ats_sellingrate"] = moneySellingRate;
                    }
                    else if (sellingRateObj is decimal decimalSellingRate)
                    {
                        TraceHelper.Trace(tracingService, "Selling Rate (Decimal): {0}", decimalSellingRate.ToString());
                        oliClone["ats_sellingrate"] = new Money(decimalSellingRate);
                    }
                    else if (sellingRateObj is double doubleSellingRate)
                    {
                        TraceHelper.Trace(tracingService, "Selling Rate (Double): {0}", doubleSellingRate.ToString());
                        oliClone["ats_sellingrate"] = new Money((decimal)doubleSellingRate);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "ats_sellingrate type not handled: {0}", sellingRateObj.GetType());
                    }
                }
                else
                {
                    TraceHelper.Trace(tracingService, "ats_sellingrate attribute not found.");
                }
                TraceHelper.Trace(tracingService, "Selling Rate set in oliClone.");

                // uomid
                var uomId = oliRow.GetAttributeValue<EntityReference>("uomid");
                TraceHelper.Trace(tracingService, "UOM Id: {0}", uomId != null ? uomId.Id.ToString() : "null");
                oliClone["uomid"] = uomId;

                // ats_adjustedtotalprice
                if (oliRow.Attributes.TryGetValue("ats_adjustedtotalprice", out var adjustedTotalPriceObj))
                {
                    if (adjustedTotalPriceObj is Money moneyAdjustedTotal)
                    {
                        oliClone["ats_adjustedtotalprice"] = moneyAdjustedTotal;
                    }
                    else if (adjustedTotalPriceObj is decimal decAdjustedTotal)
                    {
                        oliClone["ats_adjustedtotalprice"] = new Money(decAdjustedTotal);
                    }
                    else if (adjustedTotalPriceObj is double dblAdjustedTotal)
                    {
                        oliClone["ats_adjustedtotalprice"] = new Money((decimal)dblAdjustedTotal);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "ats_adjustedtotalprice type not handled: {0}", adjustedTotalPriceObj.GetType());
                    }
                }

                // ats_hardcost
                if (oliRow.Attributes.TryGetValue("ats_hardcost", out var hardCostObj))
                {
                    if (hardCostObj is Money moneyHardCost)
                    {
                        oliClone["ats_hardcost"] = moneyHardCost;
                    }
                    else if (hardCostObj is decimal decHardCost)
                    {
                        oliClone["ats_hardcost"] = new Money(decHardCost);
                    }
                    else if (hardCostObj is double dblHardCost)
                    {
                        oliClone["ats_hardcost"] = new Money((decimal)dblHardCost);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "ats_hardcost type not handled: {0}", hardCostObj.GetType());
                    }
                }

                // ats_inventorybyseason
                var inventoryBySeason = oliRow.GetAttributeValue<EntityReference>("ats_inventorybyseason");
                TraceHelper.Trace(tracingService, "Inventory By Season Id: {0}", inventoryBySeason != null ? inventoryBySeason.Id.ToString() : "null");
                if (inventoryBySeason != null)
                {
                    oliClone["ats_inventorybyseason"] = inventoryBySeason;
                }

                // ats_manualpriceoverride (assuming this is boolean)
                var manualPriceOverride = oliRow.GetAttributeValue<bool?>("ats_manualpriceoverride");
                TraceHelper.Trace(tracingService, "Manual Price Override: {0}", manualPriceOverride.HasValue ? manualPriceOverride.Value.ToString() : "null");
                if (manualPriceOverride.HasValue) oliClone["ats_manualpriceoverride"] = manualPriceOverride.Value;

                // ats_rate (assuming decimal)
                var atsRate = oliRow.GetAttributeValue<EntityReference>("ats_rate");
                TraceHelper.Trace(tracingService, "ats_rate Id: {0}", atsRate != null ? atsRate.Id.ToString() : "null");
                if (atsRate != null)
                {
                    oliClone["ats_rate"] = atsRate;
                }

                // ats_unadjustedtotalprice
                if (oliRow.Attributes.TryGetValue("ats_unadjustedtotalprice", out var unadjustedTotalPriceObj))
                {
                    if (unadjustedTotalPriceObj is Money moneyUnadjustedTotal)
                    {
                        oliClone["ats_unadjustedtotalprice"] = moneyUnadjustedTotal;
                    }
                    else if (unadjustedTotalPriceObj is decimal decUnadjustedTotal)
                    {
                        oliClone["ats_unadjustedtotalprice"] = new Money(decUnadjustedTotal);
                    }
                    else if (unadjustedTotalPriceObj is double dblUnadjustedTotal)
                    {
                        oliClone["ats_unadjustedtotalprice"] = new Money((decimal)dblUnadjustedTotal);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "ats_unadjustedtotalprice type not handled: {0}", unadjustedTotalPriceObj.GetType());
                    }
                }

                if (isNotes) //valdating if user wants to clone the notes field
                {
                    // description
                    var description = oliRow.GetAttributeValue<string>("description");
                    TraceHelper.Trace(tracingService, "Description: {0}", description ?? "null");
                    if (!string.IsNullOrEmpty(description)) oliClone["description"] = description;
                }

                AgreementCartAction agrrementObj = new AgreementCartAction();
                string uniqueGuid = agrrementObj.UniqueGuidGeneration(service, tracingService);
                if (totalOppoCount == retreievedOpportunityCount)
                {
                    if (uniqueGuid != null)
                    {
                        oliClone["ats_agreementopportunityproduct"] = uniqueGuid;
                        TraceHelper.Trace(tracingService, "Unique Guid: {0}", uniqueGuid);
                    }
                }

                oliClone["ats_agreementopportunityproduct"] = oliRow.GetAttributeValue<string>("ats_agreementopportunityproduct");

                TraceHelper.Trace(tracingService, "---------------------------------------------------------------------------------------------------------------");

                Guid newOliCloneId = service.Create(oliClone);
                TraceHelper.Trace(tracingService, "Opportunity Product clone record is created: {0}", newOliCloneId);
                count++;

                //validating the package OLi and creating the component OLis
                if (ispackageOLI)
                {
                    TraceHelper.Trace(tracingService, "Creating component OLIs for the package OLI.");
                    // Fetch component OLIrelated to the package product
                    string fetchChildPackageOlis = $@"
                                    <fetch>
                                      <entity name='opportunityproduct'>
                                        <attribute name='productid' />
                                        <attribute name='ats_quantity' />
                                        <attribute name='priceperunit' />
                                        <attribute name='ats_quantityofevents' />
                                        <attribute name='baseamount' />
                                        <attribute name='ats_sellingrate' />
                                        <attribute name='uomid' />
                                        <attribute name='ats_adjustedtotalprice' />
                                        <attribute name='ats_hardcost' />
                                        <attribute name='ats_inventorybyseason' />
                                        <attribute name='ats_manualpriceoverride' />
                                        <attribute name='ats_rate' />
                                        <attribute name='ats_unadjustedtotalprice' />
                                        <attribute name='ats_agreementopportunityproduct' />
                                        <attribute name='ats_packagetemplate' />
                                        <attribute name='ats_packagelineitem' />
                                        <attribute name='description' />

                                        <filter>
                                          <condition attribute='ats_packagelineitem' operator='eq' value='{oliRow.Id}' />
                                        </filter>

                                        <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Product'>
                                          <attribute name='name' />
                                        </link-entity>

                                      </entity>
                                    </fetch>";

                    EntityCollection oliPackageComponentCloneResults = service.RetrieveMultiple(new FetchExpression(fetchChildPackageOlis));
                    TraceHelper.Trace(tracingService, "oliCloneResults.Entities.Count: {0}", oliPackageComponentCloneResults.Entities.Count);
                    int countPackageOLis = 0;

                    foreach (var oliComponentRow in oliPackageComponentCloneResults.Entities)
                    {
                        bool isPackageOLI = false;
                        bool isComponentOLIs = true;
                        CreateOLIWithPackageAndCompOppProd(newOliCloneId, isComponentOLIs, isPackageOLI, totalOppoCount, retreievedOpportunityCount, isNotes, newOppId, ref countPackageOLis, oliComponentRow, tracingService, service);
                    }

                    TraceHelper.Trace(tracingService, "Component OLis are cloned for Cloned Opp prod: {0}", newOliCloneId);
                }

                TraceHelper.Trace(tracingService, "Exit Function: {0}", functionName);

            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
        }
    }
}