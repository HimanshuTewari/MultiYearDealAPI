using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Workflows;
using Newtonsoft.Json;
using System;
using System.Activities;
using System.Collections.Generic;
using System.IdentityModel.Metadata;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Contexts;
using System.ServiceModel.Description;
using System.ServiceModel.Security;
using System.Web.Services.Description;
using System.Web.UI.WebControls;
using System.Workflow.ComponentModel.Design;

public class OpportunityStagePlugin : IPlugin
{
    enum RateType
    {
        Season = 114300000,
        Individual = 114300001
    }

    public class StageInfo
    {
        public int Index { get; set; }
        public string StageName { get; set; }
        public Guid StageId { get; set; }
    }

    public class ConfiguredStageInfo
    {
        public string StageName { get; set; }
        public int SortOrder { get; set; }
    }

    public class FinalStageInfo
    {
        public string StageName { get; set; }
        public int SortOrder { get; set; }
        public Guid ProcessStageId { get; set; }
    }

    public void Execute(IServiceProvider serviceProvider)
    {
        ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

        string functionName = "Execute";
        try
        {
            TraceHelper.Initialize(service);
            TraceHelper.Trace(tracingService, "Tracing initialized");

            Entity target = null;
            Entity opp = null;

            TraceHelper.Trace(tracingService, "Context.MessageName={0}", context.MessageName);
            TraceHelper.Trace(tracingService, "Context.PrimaryEntityName={0}", context.PrimaryEntityName);
            TraceHelper.Trace(tracingService, "Context.PrimaryEntityId={0}", context.PrimaryEntityId);
            TraceHelper.Trace(tracingService, "Context.Depth={0}", context.Depth);

            switch (context.PrimaryEntityName)
            {
                case "ats_agreementbusinessprocessflow":
                    if (context.MessageName == "Update" && context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
                    {
                        target = (Entity)context.InputParameters["Target"];
                        TraceHelper.Trace(tracingService, "Switch hit: ats_agreementbusinessprocessflow | Message=Update | TargetId={0}", target.Id);
                        UpdateQtyPitchedSoldFromAgreement(context, tracingService, service, target, true, Guid.Empty, null);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Skip: ats_agreementbusinessprocessflow | Message={0} | HasTarget={1}", context.MessageName, context.InputParameters.Contains("Target"));
                    }
                    break;

                case "ats_opportunitybpf":
                    if (context.MessageName == "Update" && context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
                    {
                        target = (Entity)context.InputParameters["Target"];
                        TraceHelper.Trace(tracingService, "Switch hit: ats_opportunitybpf | Message=Update | TargetId={0}", target.Id);
                        UpdateQuantitiesByOpp(context, tracingService, service, target);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Skip: ats_opportunitybpf | Message={0} | HasTarget={1}", context.MessageName, context.InputParameters.Contains("Target"));
                    }
                    break;

                case "ats_agreement":
                    if (context.MessageName == "Retrieve" && context.OutputParameters.Contains("BusinessEntity"))
                    {
                        target = (Entity)context.OutputParameters["BusinessEntity"];
                        TraceHelper.Trace(tracingService, "Switch hit: ats_agreement | Message=Retrieve | BusinessEntityId={0}", target.Id);
                        UpdateBpfStatuFromAgreement(context, target, tracingService, service);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Skip: ats_agreement | Message={0} | HasBusinessEntity={1}", context.MessageName, context.OutputParameters.Contains("BusinessEntity"));
                    }
                    break;

                case "opportunity":
                    if (context.MessageName == "Update" && context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity entity)
                    {
                        Guid opportunityId = context.PrimaryEntityId;
                        TraceHelper.Trace(tracingService, "Switch hit: opportunity | Message=Update | OpportunityId={0}", opportunityId);

                        target = entity.Contains("ats_bpfstatus")
                            ? entity
                            : service.Retrieve("opportunity", opportunityId, new ColumnSet("ats_bpfstatus", "statecode"));

                        TraceHelper.Trace(tracingService, "Using target source={0} | HasBpfStatusInTarget={1}",
                            entity.Contains("ats_bpfstatus") ? "InputTarget" : "Retrieve",
                            target.Contains("ats_bpfstatus"));

                        if (context.Depth > 1)
                        {
                            TraceHelper.Trace(tracingService, "Depth > 1 (Depth={0}) -> exit to prevent recursion.", context.Depth);
                            return;
                        }

                        Entity oppData = service.Retrieve("opportunity", opportunityId, new ColumnSet("ats_agreement"));

                        if (!oppData.Contains("ats_agreement") || oppData["ats_agreement"] == null)
                        {
                            TraceHelper.Trace(tracingService, "Opportunity has no ats_agreement. Exiting. OpportunityId={0}", opportunityId);
                            return;
                        }

                        var agrRef = oppData.GetAttributeValue<EntityReference>("ats_agreement");
                        TraceHelper.Trace(tracingService, "Opportunity linked AgreementId={0}", agrRef != null ? agrRef.Id.ToString() : "null");

                        TraceHelper.Trace(tracingService, "Calling HandleSetStateOpp for OpportunityId={0}", opportunityId);
                        HandleSetStateOpp(target, service, tracingService);
                    }
                    else if (context.MessageName == "Win" || context.MessageName == "Lose")
                    {
                        TraceHelper.Trace(tracingService, "Switch hit: opportunity | Message={0}", context.MessageName);

                        if (context.InputParameters.Contains("OpportunityClose") &&
                            context.InputParameters["OpportunityClose"] is Entity oppClose &&
                            oppClose.Attributes.Contains("opportunityid") &&
                            oppClose["opportunityid"] is EntityReference oppRef)
                        {
                            TraceHelper.Trace(tracingService, "OpportunityClose found. Retrieving OpportunityId={0}", oppRef.Id);
                            target = service.Retrieve(oppRef.LogicalName, oppRef.Id, new ColumnSet(true));
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, "OpportunityClose not found or missing opportunityid. Message={0}", context.MessageName);
                        }

                        if (target == null)
                        {
                            TraceHelper.Trace(tracingService, "Target is null after Win/Lose handling. Exiting.");
                            return;
                        }

                        if (!target.Contains("ats_agreement") || target["ats_agreement"] == null)
                        {
                            TraceHelper.Trace(tracingService, "Opportunity has no ats_agreement. Exiting. OpportunityId={0}", target.Id);
                            return;
                        }

                        if (context.MessageName == "Win")
                        {
                            TraceHelper.Trace(tracingService, "Opportunity marked Won. OpportunityId={0}", target.Id);
                            HandleOpportunityWon(context, service, tracingService, target);
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, "Opportunity marked Lost. OpportunityId={0}", target.Id);
                            HandleOpportunityLost(context, service, tracingService, target);
                        }
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Skip: opportunity | Message={0}", context.MessageName);
                    }
                    break;

                default:
                    TraceHelper.Trace(tracingService, "No switch case matched. PrimaryEntityName={0}, MessageName={1}", context.PrimaryEntityName, context.MessageName);
                    break;
            }


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
    /// get the bpf status of the opportunity and call the function for update the Quantity pitched, sold, Available. 
    /// </summary>
    /// <param name="opp"></param>
    /// <param name="service"></param>
    /// <param name="tracingService"></param>
    /// <exception cref="InvalidPluginExecutionException"></exception>
    private void   HandleSetStateOpp(Entity opp ,IOrganizationService service, ITracingService tracingService)
    {
        #region function Level variables 
        string functionName = "HandleSetStateOpp";
        bool isWon = false;
        int bpfStatusValue = 0;
        bool isReopenOpp = true;
        bool isReopenWon = false; 
        bool isContainsBpfStatus = false;
        #endregion
        try
        {
            TraceHelper.Trace(tracingService, "fucntionName: {0}", functionName);

            isContainsBpfStatus = opp.Contains("ats_bpfstatus");
            if (isContainsBpfStatus)
            {
                TraceHelper.Trace(tracingService, "Opportunity contains ats_bpfstatus attribute.");
                bpfStatusValue = ((OptionSetValue)opp["ats_bpfstatus"]).Value;
                //Retrieval of the statecode value 
                // Retrieve latest status if needed
                if (opp.Attributes.Contains("statecode"))
                {
                    OptionSetValue state = (OptionSetValue)opp["statecode"];
                    if (state.Value == 1)
                    {
                        isReopenWon = true;
                    }
                    else if (state.Value == 2)
                    {
                        isReopenWon = false;
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "opportunity is open, Logic returns for the Reopen Opportunity");
                        return;
                    }

                    if (bpfStatusValue != 0)
                    {
                        UpdateQuantityPitchedSoldIBS(isReopenOpp, isReopenWon, isWon, opp, opp.Id, bpfStatusValue, service, tracingService);
                        TraceHelper.Trace(tracingService, "Returning the function because Reopen Opportunity logic is done");
                        return;
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Opportunity bpf status value is not set");
                    }
                }
            }
            else
            {
                TraceHelper.Trace(tracingService, "Opportunity does not contain ats_bpfstatus attribute or it is null.");
            }

            #region logic for updating the bulk Opportunities BPF Status values
            TraceHelper.Trace(tracingService, "proceeding for the bulk update of Opportunity bpf, 'when customer need' field is updated. ");

            TraceHelper.Trace(tracingService, "Opportunity contains ats_bpfstatus attribute, calling the function for update of the Opportunity bpf status");
            BulkOpportunityBPFStatusUpdate(opp, service, tracingService);

            #endregion

            TraceHelper.Trace(tracingService, "Exit functionName: {0}", functionName);

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
    /// update the opportunity bpf status based on the agreement bpf status and validating with the opportuntiy has Agreement or not
    /// </summary>
    /// <param name="opportunity"></param>
    /// <param name="service"></param>
    /// <param name="tracingService"></param>
    /// <exception cref="InvalidPluginExecutionException"></exception>
    public void BulkOpportunityBPFStatusUpdate(Entity opportunity, IOrganizationService service, ITracingService tracingService)
    {
        #region function Level variables
        string functionName = "BulkOpportunityBPFStatusUpdate";
        Guid opportunityId = opportunity.Id; // ✅ use actual opportunity ID 

        // Root opportunity
        //OptionSetValue opportunityBpfStatus = null;
        //EntityReference agreementLookup = null;

        // Agreement (root + BPF)
        string agreementBpfStatus = string.Empty;
        //Guid? agreementActiveStageId = null;
        string agreementProcessStageName = string.Empty;

        // Opportunity BPF
        //Guid? opportunityActiveStageId = null;
        string opportunityProcessStageName = string.Empty;
        #endregion

        try
        {
            TraceHelper.Trace(tracingService, "Function: {0} | OpportunityId: {1}", functionName, opportunityId);

            //validation the check the opportunity is associated with the agreement or not
            bool isAgreementOnOpportunity = false;
            // FetchXML query
            string fetchXml = $@"
                            <fetch>
                              <entity name='opportunity'>
                                <attribute name='ats_agreement' />
                                
                                <filter>
                                  <condition attribute='opportunityid' operator='eq' value='{opportunityId}' />
                                </filter>
                                <!-- Agreement -->
                                <link-entity name='ats_agreement' from='ats_agreementid' to='ats_agreement' link-type='outer' alias='Agreement'>
                                  <!-- Agreement BPF -->
                                  <link-entity name='ats_agreementbusinessprocessflow' from='bpf_ats_agreementid' to='ats_agreementid' link-type='outer' alias='AgreementBPF'>
                                    <!-- Stage details -->
                                    <link-entity name='processstage' from='processstageid' to='activestageid' link-type='outer' alias='AgreementProcessStage'>
                                      <attribute name='stagename' />
                                    </link-entity>
                                  </link-entity>
                                </link-entity>
                              </entity>
                            </fetch>";

            // Execute Fetch
            EntityCollection result = service.RetrieveMultiple(new FetchExpression(fetchXml));

            if (result.Entities.Count > 0)
            {
                Entity opp = result.Entities[0];
                agreementProcessStageName = opp.GetAttributeValue<AliasedValue>("AgreementProcessStage.stagename")?.Value as string;
                isAgreementOnOpportunity = opp.Contains("ats_agreement");
            }
            else
            {
                TraceHelper.Trace(tracingService, "No opportunity found for Opportunity Id: {0}", opportunityId);
            }

            #region this logic is for getting stage name and comparing and checking the both name are same or not in the json.
            TraceHelper.Trace(tracingService, "Retrieving environment variable for stage-status mapping.");
            string evSyncOppBPF = GetEnvironmentVariableValue(service, "ats_SyncOpportunityBPFStatus");
            TraceHelper.Trace(tracingService, "Environment Variable Value: {0}", evSyncOppBPF);

            // Deserialize JSON into dictionary
            var stageStatusMap = JsonConvert.DeserializeObject<Dictionary<string, int>>(evSyncOppBPF);
            TraceHelper.Trace(tracingService, "Deserialized stage-status mapping:");

            int selectedStatus = -1;
            TraceHelper.Trace(tracingService, "isAgreementOnOpportunity: {0}", isAgreementOnOpportunity);

            if (isAgreementOnOpportunity)
            {
                if (!string.IsNullOrEmpty(agreementProcessStageName) && stageStatusMap.ContainsKey(agreementProcessStageName))
                {
                    TraceHelper.Trace(tracingService, "Agreement having the process stage name");
                    selectedStatus = stageStatusMap[agreementProcessStageName];
                }
                else
                {
                    TraceHelper.Trace(tracingService, "Stage name not found in mapping: {0}, for the oppId: {1}", agreementProcessStageName, opportunity.Id);
                }
            }
            else
            {
                TraceHelper.Trace(tracingService, "Opportunity is not associated with any Agreement.");
            }
            #endregion

            if (isAgreementOnOpportunity && selectedStatus != -1)
            {
                TraceHelper.Trace(tracingService, "Opportunity is associated with an Agreement.");
                opportunity["ats_bpfstatus"] = new OptionSetValue(selectedStatus);
                TraceHelper.Trace(tracingService, "Mapped Stage: {0}, Status: {1}", agreementProcessStageName, selectedStatus);
                service.Update(opportunity);
                TraceHelper.Trace(tracingService, "Opportunity is updated");
            }
            else
            {
                TraceHelper.Trace(tracingService, "Opportunity is NOT associated with an Agreement or selectedStatus is not initialized or null.");
                return;
            }

            TraceHelper.Trace(tracingService, "Exit fucntionName: {0}", functionName);

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
    /// retrieval of the opportunity Bpf status, and calling the function to update the Quantity pitched, sold and available
    /// </summary>
    /// <param name="context"></param>
    /// <param name="service"></param>
    /// <param name="tracingService"></param>
    /// <param name="target"></param>
    /// <exception cref="InvalidPluginExecutionException"></exception>
    private void HandleOpportunityLost(IPluginExecutionContext context, IOrganizationService service, ITracingService tracingService, Entity target)
    {
        tracingService.Trace("Inside HandleOpportunityLost method.");
        #region function Level Variables
        string functionName = "HandleOpportunityLost";
        Entity opp = null;
        bool isWon = false;
        bool isReopenOpp = false;
        bool isReopenWon = false; // This variable is not used in this method, but kept for consistency with HandleSetStateOpp
        #endregion

        try
        {
            // Retrieve the BPF status OptionSet
            opp = service.Retrieve("opportunity", target.Id, new ColumnSet("ats_bpfstatus"));

            if (opp != null && opp.Contains("ats_bpfstatus"))
            {
                var optionSet = opp.GetAttributeValue<OptionSetValue>("ats_bpfstatus");
                if (optionSet != null)
                {
                    int bpfStatusValue = optionSet.Value;
                    //function responsible to update the Quantity pitched, sold, available 
                    UpdateQuantityPitchedSoldIBS(isReopenOpp, isReopenWon, isWon, target, opp.Id, bpfStatusValue, service, tracingService);
                }
            }

            TraceHelper.Trace(tracingService, "Exit functionName: {0}", functionName);
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



    private void UpdateQuantityPitchedSoldIBS(bool isReopenOpp, bool isReopenWon, bool isWon, Entity target,Guid oppId,int bpfstatusVal , IOrganizationService service,ITracingService tracingService)
    {
        #region function Level variables 
        string functionName = "UpdateQuantityPitchedSoldIBS";
        string fetchXml = string.Empty;
        EntityCollection result = null;
        #endregion
        try
        {
            tracingService.Trace($"functionName: {functionName}");
            //fetchxml retrieves the opportunity,Opp Prod, Ibs, Rate details
            fetchXml = $@"
                            <fetch>
                              <entity name='opportunity'>
                                <filter>
                                  <condition attribute='opportunityid' operator='eq' value='{oppId}' />
                                </filter>
                                <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' link-type='outer' alias='OppProd'>
                                  <attribute name='ats_quantity' />
                                  <attribute name='ats_quantityofevents' />
                                  <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='inner' alias='IBS'>
                                    <attribute name='ats_quantityavailable' />
                                    <attribute name='ats_quantitypitched' />
                                    <attribute name='ats_quantitysold' />
                                    <attribute name='ats_totalquantity' />
                                    <attribute name='ats_inventorybyseasonid'/>
                                    <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='outer' alias='Rate'>
                                      <attribute name='ats_ratetype' />
                                    </link-entity>
                                  </link-entity>
                                </link-entity>
                              </entity>
                            </fetch>";
            result = service.RetrieveMultiple(new FetchExpression(fetchXml));

            if (result.Entities != null && result.Entities.Count > 0)
            {
                foreach (var record in result.Entities)
                {
                    // --- Safely retrieve aliased values directly ---
                    int atsQuantity = record.Contains("OppProd.ats_quantity") && record["OppProd.ats_quantity"] is AliasedValue q1 && q1.Value is int? (int)q1.Value : 0;

                    int atsQuantityOfEvents = record.Contains("OppProd.ats_quantityofevents") && record["OppProd.ats_quantityofevents"] is AliasedValue q2 && q2.Value is int ? (int)q2.Value : 0;

                    int atsQuantityAvailable = record.Contains("IBS.ats_quantityavailable") && record["IBS.ats_quantityavailable"] is AliasedValue q3 && q3.Value is int ? (int)q3.Value : 0;

                    int atsQuantityPitched = record.Contains("IBS.ats_quantitypitched") && record["IBS.ats_quantitypitched"] is AliasedValue q4 && q4.Value is int ? (int)q4.Value : 0;

                    int atsQuantitySold = record.Contains("IBS.ats_quantitysold") && record["IBS.ats_quantitysold"] is AliasedValue q5 && q5.Value is int ? (int)q5.Value : 0;

                    int atsTotalQuantity = record.Contains("IBS.ats_totalquantity") && record["IBS.ats_totalquantity"] is AliasedValue q6 && q6.Value is int ? (int)q6.Value : 0;

                    Guid inventoryBySeasonId = record.Contains("IBS.ats_inventorybyseasonid") && record["IBS.ats_inventorybyseasonid"] is AliasedValue q7 && q7.Value is Guid ? (Guid)q7.Value : Guid.Empty;

                    int rateType = record.Contains("Rate.ats_ratetype") && record["Rate.ats_ratetype"] is AliasedValue q8 && q8.Value is OptionSetValue osv ? osv.Value : -1;

                    // --- Debug tracing ---
                    TraceHelper.Trace(tracingService,
                        "Opp Quantity: {0}, Events: {1}, Available: {2}, Pitched: {3}, Sold: {4}, Total: {5}, InventoryBySeasonId: {6}, RateType: {7}",
                        atsQuantity,
                        atsQuantityOfEvents,
                        atsQuantityAvailable,
                        atsQuantityPitched,
                        atsQuantitySold,
                        atsTotalQuantity,
                        inventoryBySeasonId,
                        rateType
                    );

                    Entity ibs = new Entity("ats_inventorybyseason");
                    ibs.Id = inventoryBySeasonId;

                    if (bpfstatusVal == 114300001)// Pitched Stage
                    {
                        TraceHelper.Trace(tracingService, "Pitched Stage");

                        #region logic for the Reopen button on the Opportunity
                        if (isReopenOpp)
                        {
                            TraceHelper.Trace(tracingService, "Is Reopen logic is true");
                            if (isReopenWon)
                            {
                                TraceHelper.Trace(tracingService, "Opportunity is Reopen and already won");
                                if (rateType == 114300000) //Season 
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type is Season");
                                    //decrement pitched
                                    atsQuantityPitched = atsQuantityPitched + atsQuantity;
                                    TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                    ibs["ats_quantitypitched"] = atsQuantityPitched;
                                    ////increment in sold
                                    atsQuantitySold = atsQuantitySold - atsQuantity;
                                    ibs["ats_quantitysold"] = atsQuantitySold;
                                    atsQuantityAvailable = atsQuantityAvailable + atsQuantity;
                                    ibs["ats_quantityavailable"] = atsQuantityAvailable;
                                }
                                else
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type is other then Season");
                                    atsQuantityPitched = atsQuantityPitched + (atsQuantity * atsQuantityOfEvents);
                                    TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                    ibs["ats_quantitypitched"] = atsQuantityPitched;
                                    ////increment in sold
                                    atsQuantitySold = atsQuantitySold - (atsQuantity * atsQuantityOfEvents);
                                    ibs["ats_quantitysold"] = atsQuantitySold;
                                    atsQuantityAvailable = atsQuantityAvailable + (atsQuantity * atsQuantityOfEvents);
                                    ibs["ats_quantityavailable"] = atsQuantityAvailable;
                                }
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "Opportunity is Reopen and close");
                                if (rateType == 114300000) //Season 
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type is Season");
                                    //decrement pitched
                                    atsQuantityPitched = atsQuantityPitched + atsQuantity;
                                    TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                    ibs["ats_quantitypitched"] = atsQuantityPitched;
                                }
                                else
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type is other then Season");
                                    atsQuantityPitched = atsQuantityPitched + (atsQuantity * atsQuantityOfEvents);
                                    TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                    ibs["ats_quantitypitched"] = atsQuantityPitched;
                                }
                            }
                        }
                        #endregion

                        #region logic for the button Closed as Won
                        else if (isWon)
                        {
                            if (rateType == 114300000) //Season 
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is Season");
                                //decrement pitched
                                atsQuantityPitched = atsQuantityPitched - atsQuantity;
                                TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                ibs["ats_quantitypitched"] = atsQuantityPitched;
                                ////increment in sold
                                atsQuantitySold = atsQuantitySold + atsQuantity;
                                ibs["ats_quantitysold"] = atsQuantitySold;
                                atsQuantityAvailable = atsQuantityAvailable - atsQuantity;
                                ibs["ats_quantityavailable"] = atsQuantityAvailable;
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is other then Season");
                                atsQuantityPitched = atsQuantityPitched - (atsQuantity * atsQuantityOfEvents);
                                TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                ibs["ats_quantitypitched"] = atsQuantityPitched;

                                ////increment in sold
                                atsQuantitySold = atsQuantitySold + (atsQuantity * atsQuantityOfEvents);
                                ibs["ats_quantitysold"] = atsQuantitySold;
                                atsQuantityAvailable = atsQuantityAvailable - (atsQuantity * atsQuantityOfEvents);
                                ibs["ats_quantityavailable"] = atsQuantityAvailable;
                            }
                        }
                        #endregion

                        #region logic for the button closed as Lost
                        else
                        {
                            if (rateType == 114300000) //Season 
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is Season");
                                //decrement pitched
                                atsQuantityPitched = atsQuantityPitched - atsQuantity;
                                TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                ibs["ats_quantitypitched"] = atsQuantityPitched;
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is other then Season");
                                atsQuantityPitched = atsQuantityPitched - (atsQuantity * atsQuantityOfEvents);
                                TraceHelper.Trace(tracingService, "atsQuantityPitched: {0}", atsQuantityPitched);
                                ibs["ats_quantitypitched"] = atsQuantityPitched;
                            }
                        }
                        #endregion


                    }
                    else if(bpfstatusVal == 114300003) //Closed won stage
                    {
                        TraceHelper.Trace(tracingService, "Closed Won Stage");

                        if (isReopenOpp)
                        {
                            TraceHelper.Trace(tracingService, "Is Reopen logic is true");
                            if (isReopenWon)
                            {
                                TraceHelper.Trace(tracingService, "opportunity is already in won stage");
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "Opportunity is Reopen,was closed");

                                if (rateType == 114300000) //Season 
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type is Season");
                                    atsQuantitySold = atsQuantitySold + atsQuantity;
                                    ibs["ats_quantitysold"] = atsQuantitySold;
                                    atsQuantityAvailable = atsQuantityAvailable - atsQuantity;
                                    ibs["ats_quantityavailable"] = atsQuantityAvailable;
                                }
                                else
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type is other then Season");
                                    ////increment in sold
                                    atsQuantitySold = atsQuantitySold + (atsQuantity * atsQuantityOfEvents);
                                    ibs["ats_quantitysold"] = atsQuantitySold;
                                    atsQuantityAvailable = atsQuantityAvailable - (atsQuantity * atsQuantityOfEvents);
                                    ibs["ats_quantityavailable"] = atsQuantityAvailable;
                                }
                            }
                        }
                        else if (isWon)
                        {
                            TraceHelper.Trace(tracingService, "Opportunity is Already won");
                        }
                        else
                        {
                            if (rateType == 114300000) //Season 
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is Season");

                                atsQuantitySold = atsQuantitySold - atsQuantity;
                                ibs["ats_quantitysold"] = atsQuantitySold;
                                atsQuantityAvailable = atsQuantityAvailable + atsQuantity;
                                ibs["ats_quantityavailable"] = atsQuantityAvailable;
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is other then Season");

                                atsQuantitySold = atsQuantitySold - (atsQuantity * atsQuantityOfEvents);
                                TraceHelper.Trace(tracingService, "atsQuantitySold: {0}", atsQuantitySold);
                                ibs["ats_quantitysold"] = atsQuantitySold;

                                atsQuantityAvailable = atsQuantityAvailable + (atsQuantity * atsQuantityOfEvents);
                                TraceHelper.Trace(tracingService, "atsQuantityAvailable: {0}", atsQuantityAvailable);
                                ibs["ats_quantityavailable"] = atsQuantityAvailable;
                            }
                        }


                    }
                    else if (bpfstatusVal== 114300004 || bpfstatusVal == 114300000) //Closed Lost Stage || Pre-Pitched Stage
                    {
                        TraceHelper.Trace(tracingService, "Either closed lost or pre pitched");
                        if (isReopenOpp)
                        {
                            TraceHelper.Trace(tracingService, "Opp is Reopen");
                            if (isReopenWon)
                            {
                                TraceHelper.Trace(tracingService, "Opportunity is Reopen and already won");
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "Opportunity is Reopen and close");
                            }
                        }
                        else if (isWon)
                        {
                            if (rateType == 114300000) //Season 
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is Season");

                                atsQuantitySold = atsQuantitySold + atsQuantity;
                                ibs["ats_quantitysold"] = atsQuantitySold;
                                atsQuantityAvailable = atsQuantityAvailable - atsQuantity;
                                ibs["ats_quantityavailable"] = atsQuantityAvailable;
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "Rate Type is other then Season");

                                atsQuantitySold = atsQuantitySold + (atsQuantity * atsQuantityOfEvents);
                                TraceHelper.Trace(tracingService, "atsQuantitySold: {0}", atsQuantitySold);
                                ibs["ats_quantitysold"] = atsQuantitySold;

                                atsQuantityAvailable = atsQuantityAvailable - (atsQuantity * atsQuantityOfEvents);
                                TraceHelper.Trace(tracingService, "atsQuantityAvailable: {0}", atsQuantityAvailable);
                                ibs["ats_quantityavailable"] = atsQuantityAvailable;
                            }
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, "No chnage in the Quantity pitched ");
                        }

                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "bpfstatusVal is not correct, return");
                        return;
                    }

                    service.Update(ibs);
                    TraceHelper.Trace(tracingService, "Inventory By season Quantities are updated");

                }
            }
            else
            {
                TraceHelper.Trace(tracingService, "No records found in FetchXML.");

            }

            TraceHelper.Trace(tracingService, "Exit functionName: {0}", functionName);

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
    /// Retrieval of the opportunity status feild and calling the function for updating trhe Quantity on the IBS
    /// </summary>
    /// <param name="context"></param>
    /// <param name="service"></param>
    /// <param name="tracingService"></param>
    /// <param name="target"></param>
    /// <exception cref="InvalidPluginExecutionException"></exception>
    private void HandleOpportunityWon(IPluginExecutionContext context, IOrganizationService service, ITracingService tracingService, Entity target)
    {
        tracingService.Trace("Inside HandleOpportunityWon method.");
        #region function Level Variables
        string functionName = "HandleOpportunityWon";
        Entity opp = null;
        bool isWon = true;
        bool isReopenOpp = false;
        bool isReopenWon = false;

        #endregion

        try
        {
            // Retrieve the BPF status OptionSet
            opp = service.Retrieve("opportunity", target.Id, new ColumnSet("ats_bpfstatus"));

            if (opp != null && opp.Contains("ats_bpfstatus"))
            {
                var optionSet = opp.GetAttributeValue<OptionSetValue>("ats_bpfstatus");
                if (optionSet != null)
                {
                    int bpfStatusValue = optionSet.Value;
                    UpdateQuantityPitchedSoldIBS(isReopenOpp, isReopenWon, isWon, target, opp.Id, bpfStatusValue, service, tracingService);
                }
            }

            TraceHelper.Trace(tracingService, "Exit functionName: {0}", functionName);

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
    /// Updating the BPf Status field on the Agreeement based on the Agreement BPF .
    /// </summary>
    /// <param name="context"></param>
    /// <param name="target"></param>
    /// <param name="tracingService"></param>
    /// <param name="service"></param>
    /// <exception cref="InvalidPluginExecutionException"></exception>
    public void UpdateBpfStatuFromAgreement (IPluginExecutionContext context, Entity target, ITracingService tracingService, IOrganizationService service)
    {
        string functionName = "UpdateBpfStatuFromAgreement";
        try 
        {
            TraceHelper.Trace(tracingService, "fucntionName: {0}", functionName);

            Guid agreementId = target.Id;
            TraceHelper.Trace(tracingService, "agreementId: {0}", agreementId);

            //Retrieval of the agreementbpf status entity record to get the active stage id
            EntityReference activeStageRef = null;
            string fetchXml = $@"
                    <fetch>
                      <entity name='ats_agreement'>
                        <filter>
                          <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                        </filter>
                        <link-entity name='ats_agreementbusinessprocessflow' from='bpf_ats_agreementid' to='ats_agreementid' link-type='outer' alias='AgreementBPF'>
                          <attribute name='activestageid' />
                        </link-entity>
                      </entity>
                    </fetch>";

            EntityCollection results = service.RetrieveMultiple(new FetchExpression(fetchXml));

            if (results.Entities != null && results.Entities.Count > 0)
            {
                Entity agreement = results.Entities[0];

                if (agreement.Contains("AgreementBPF.activestageid"))
                {
                    var aliased = agreement["AgreementBPF.activestageid"] as AliasedValue;
                    if (aliased != null && aliased.Value is EntityReference stageRef)
                    {
                        activeStageRef = stageRef;
                        TraceHelper.Trace(tracingService, "Active Stage ID: {0}", activeStageRef.Id);
                    }
                }
            }

            // initialize the dummy variable to pass, so that we can handle the pre- entity image.
            bool isPreEntity = false;
            UpdateQtyPitchedSoldFromAgreement(context, tracingService, service, target, isPreEntity, agreementId, activeStageRef);

            //call the function reponsible to update the bpf status field present on the Agreement.

            TraceHelper.Trace(tracingService, "Exit fucntionName: {0}", functionName);


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
    /// updating the stage of the Opportunity based on the Change in the Agreement Stage. 
    /// </summary>
    /// <param name="agreement"></param>
    /// <param name="service"></param>
    /// <param name="tracer"></param>
    private void SyncOpportunityFromAgreement(Entity bpfAgreement, IOrganizationService service, ITracingService tracingService)
    {
        #region fucntion Level Variables
        string functionName = "SyncOpportunityFromAgreement";
        string fetchXml = string.Empty;
        EntityReference agreementStageRef = null;
        Entity agreementObj = null; 
        EntityReference agreementId = null;
        #endregion
        try
        {

            // Step 1: Get the new active stage of Agreement's BPF
            if (!bpfAgreement.Attributes.Contains("activestageid")) return;

            agreementStageRef = (EntityReference)bpfAgreement["activestageid"];
            TraceHelper.Trace(tracingService, "Agree stageID : {0}", agreementStageRef.Id);

            // Retrieve the AgreementId based on the Custom BPF Agreement
            if (!bpfAgreement.Attributes.Contains("bpf_ats_agreementid"))
            {
                agreementObj = service.Retrieve("ats_agreementbusinessprocessflow", bpfAgreement.Id, new ColumnSet("bpf_ats_agreementid"));
                agreementId = agreementObj.Contains("bpf_ats_agreementid") ? (EntityReference)agreementObj["bpf_ats_agreementid"] : null;
            }
            else
            {
                agreementId = bpfAgreement.GetAttributeValue<EntityReference>("bpf_ats_agreementid");
            }
            TraceHelper.Trace(tracingService, "agreementId: {0}", agreementId.Id);

            fetchXml = $@"
               <fetch>
                 <entity name='ats_agreement'>
                   <filter>
                     <condition attribute='ats_agreementid' operator='eq' value='{agreementId.Id}' />
                   </filter>
                   <link-entity name='opportunity' from='ats_agreement' to='ats_agreementid' alias='Opp'>
                     <attribute name='opportunityid' />
                     <link-entity name='ats_opportunitybpf' from='bpf_opportunityid' to='opportunityid' link-type='outer' alias='OppBpf'>
                       <attribute name='bpf_opportunityid' />
                       <attribute name='businessprocessflowinstanceid' />
                       <attribute name='activestageid' />
                       <attribute name='processid' />
                       <link-entity name='processstage' from='processstageid' to='activestageid' alias='OppProcess'>
                         <attribute name='stagename' />
                       </link-entity>
                     </link-entity>
                   </link-entity>
                   <link-entity name='ats_agreementbusinessprocessflow' from='bpf_ats_agreementid' to='ats_agreementid' alias='AgreeBpf'>
                     <link-entity name='processstage' from='processstageid' to='activestageid' alias='AgreeProcess'>
                       <attribute name='stagename' />
                     </link-entity>
                   </link-entity>
                 </entity>
               </fetch>";

            EntityCollection results = service.RetrieveMultiple(new FetchExpression(fetchXml));

            if (results.Entities.Count == 0)
            {
                TraceHelper.Trace(tracingService, "results.Entities.Count == 0, thus return");
                return;
            }

            foreach (var record in results.Entities)
            {
                string agreementStageName = null;
                Guid opportunityId = Guid.Empty;
                Guid oppBpfId = Guid.Empty;
                Guid oppBpfProcessId = Guid.Empty;
                string opportunityStageName = null;

                // Agreement stage name
                if (record.Attributes.Contains("AgreeProcess.stagename"))
                {
                    var aliasedValue = record["AgreeProcess.stagename"] as AliasedValue;
                    if (aliasedValue != null) agreementStageName = aliasedValue.Value?.ToString();
                }
                TraceHelper.Trace(tracingService, "agreementStageName: {0}", agreementStageName ?? "null");

                // Opportunity ID
                if (record.Attributes.Contains("Opp.opportunityid"))
                {
                    var oppIdValue = record["Opp.opportunityid"] as AliasedValue;
                    if (oppIdValue?.Value is Guid oppGuid)
                    {
                        opportunityId = oppGuid;
                        TraceHelper.Trace(tracingService, "Extracted Opportunity ID: {0}", opportunityId);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Opp.opportunityid is not a Guid.");
                    }
                }
                else
                {
                    TraceHelper.Trace(tracingService, "Opp.opportunityid not found in fetch result.");
                }
                TraceHelper.Trace(tracingService, "opportunityId: {0}", opportunityId);

                // Opp BPF ID
                if (record.Attributes.Contains("OppBpf.businessprocessflowinstanceid"))
                {
                    var oppBpfVal = record["OppBpf.businessprocessflowinstanceid"] as AliasedValue;
                    if (oppBpfVal?.Value is EntityReference oppBpfRef)
                    {
                        oppBpfId = oppBpfRef.Id;
                        TraceHelper.Trace(tracingService, "Extracted Opp BPF ID from EntityReference: {0}", oppBpfId);
                    }
                    else if (oppBpfVal?.Value is Guid oppBpfGuid)
                    {
                        oppBpfId = oppBpfGuid;
                        TraceHelper.Trace(tracingService, "Extracted Opp BPF ID from Guid: {0}", oppBpfId);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Unexpected type for OppBpf.bpf_opportunityid: {0}", oppBpfVal?.Value?.GetType());
                    }
                }

                //Sunny(07-06-25)
                if (record.Attributes.Contains("OppBpf.processid"))
                {
                    var oppProcessBpfVal = record["OppBpf.processid"] as AliasedValue;
                    if (oppProcessBpfVal?.Value is EntityReference oppBpfRef)
                    {
                        oppBpfProcessId = oppBpfRef.Id;
                        TraceHelper.Trace(tracingService, "Extracted oppBpfProcessId from EntityReference: {0}", oppBpfId);
                    }
                    else if (oppProcessBpfVal?.Value is Guid oppBpfGuid)
                    {
                        oppBpfProcessId = oppBpfGuid;
                        TraceHelper.Trace(tracingService, "Extracted oppBpfProcessId from Guid: {0}", oppBpfId);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Unexpected type for OppBpf.oppBpfProcessId: {0}", oppProcessBpfVal?.Value?.GetType());
                    }
                }

                TraceHelper.Trace(tracingService, "oppBpfId: {0}", oppBpfId);

                // Opportunity stage name
                if (record.Attributes.Contains("OppProcess.stagename"))
                {
                    var aliasedValue = record["OppProcess.stagename"] as AliasedValue;
                    if (aliasedValue != null) opportunityStageName = aliasedValue.Value?.ToString();
                }
                TraceHelper.Trace(tracingService, "opportunityStageName: {0}", opportunityStageName ?? "null");

                //Sunny(05-06-25)
                //making the primaryenmtitytypecode as dynamic 
                var retrieveEntityRequest = new RetrieveEntityRequest { LogicalName = "opportunity", EntityFilters = EntityFilters.Entity };
                var retrieveEntityResponse = (RetrieveEntityResponse)service.Execute(retrieveEntityRequest);
                int opportunityObjectTypeCode = retrieveEntityResponse.EntityMetadata.ObjectTypeCode.Value;

                TraceHelper.Trace(tracingService, "opportunityObjectTypeCode: {0}", opportunityObjectTypeCode);

                // Build the FetchXML with linked entities and agreementStageName 
                string OppBPFfetchXml = $@"
                               <fetch>
                               <entity name='processstage'>
                                   <attribute name='stagename' />
                                   <attribute name='processstageid' />
                                   <filter>
                                       <condition attribute='primaryentitytypecode' operator='eq' value='{opportunityObjectTypeCode}' />
                                       <condition attribute='processid' operator='eq' value='{oppBpfProcessId}'/>
                                   </filter>
                               </entity>
                           </fetch>";

                EntityCollection result = service.RetrieveMultiple(new FetchExpression(OppBPFfetchXml));

                if (result.Entities.Count == 0)
                {
                    TraceHelper.Trace(tracingService, "No matching Opportunity BPF stage found for agreementStageName: {0}", agreementStageName);
                    return;
                }

                Guid? activeStageId = null;
                var oppBPFstageName = string.Empty;
                // Extract activeStageId and oppBpfId from result
                foreach (var entity in result.Entities)
                {
                    oppBPFstageName = entity.Attributes.Contains("stagename") ? entity["stagename"].ToString() : string.Empty;
                    TraceHelper.Trace(tracingService, "oppBPFstageName: {0}", oppBPFstageName);
                    if (entity.Attributes.Contains("processstageid") && entity.Attributes.Contains("stagename") && oppBPFstageName == agreementStageName)
                    {
                        activeStageId = entity.GetAttributeValue<Guid>("processstageid");
                        TraceHelper.Trace(tracingService, "processstageid: {0}", activeStageId);
                        break;
                    }
                }

                // Update the Opportunity BPF stage to match the Agreement BPF stage
                if (oppBpfId != Guid.Empty && activeStageId.HasValue)
                {
                    int oppBPFStatusCode = 0;

                    // Validating the status code of the 'ats_opportunitybpf'
                    Entity oppBpfObj = service.Retrieve("ats_opportunitybpf", oppBpfId, new ColumnSet("statuscode"));

                    if (oppBpfObj.Contains("statuscode"))
                    {
                        oppBPFStatusCode = ((OptionSetValue)oppBpfObj["statuscode"]).Value;
                        TraceHelper.Trace(tracingService, "oppBPFStatusCode: {0}", oppBPFStatusCode);
                    }

                    if (oppBPFStatusCode == 1) // Opp BPF is in Inactive state
                    {
                        SetStateRequest request = new SetStateRequest
                        {
                            EntityMoniker = new EntityReference("ats_opportunitybpf", oppBpfId),
                            State = new OptionSetValue(0), // 0 = Active
                            Status = new OptionSetValue(1) // 1 = default "Activated" status (check this in your system)
                        };

                        service.Execute(request);
                        TraceHelper.Trace(tracingService, "Business process flow re-activated using SetStateRequest.");
                    }

                    Entity oppBPF = new Entity("ats_opportunitybpf", oppBpfId);

                    oppBPF["activestageid"] = new EntityReference("processstage", activeStageId.Value);
                    service.Update(oppBPF);
                    TraceHelper.Trace(tracingService, "Opp BPF updated successfully. New stage ID: {0}", activeStageId.Value);

                    // Retrieve Environment Variable values
                    string evPitchedStage = GetEnvironmentVariableValue(service, "ats_PitchedStage");
                    TraceHelper.Trace(tracingService, "pitchedStage Value: {0}", evPitchedStage);

                    string evSoldStage = GetEnvironmentVariableValue(service, "ats_SoldStage");
                    TraceHelper.Trace(tracingService, "SoldStage Value: {0}", evSoldStage);
                }
                else
                {
                    TraceHelper.Trace(tracingService, "Opp BPF not updated: oppBpfIdd = {0}, activeStageId = {1}", oppBpfId, activeStageId.HasValue ? activeStageId.ToString() : "null");
                }
            }

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
    /// Update the Quantity Pitched and Quantity sold, when the Stage of the Opp gets changed as Proposal or close
    /// </summary>
    /// <param name="tracingService"></param>
    /// <param name="service"></param>
    /// <param name="target"></param>
    /// <exception cref="InvalidPluginExecutionException"></exception>
    public void UpdateQuantitiesByOpp(IPluginExecutionContext context,ITracingService tracingService, IOrganizationService service, Entity target )
    {
        string functionName = "UpdateQuantitiesByOpp";
        try
        {

            //when plugin is getting called from the agreement to uodate the opportunity, validation to update it. 
            if (context.Depth > 1)
            {
                TraceHelper.Trace(tracingService, "returning because the opp is getting called from update of Agreement Stage.");
                return;
            }

            TraceHelper.Trace(tracingService, "functionName: {0}", functionName);
            EntityReference stageRef = target.GetAttributeValue<EntityReference>("activestageid");
            if (stageRef == null) return;

            Entity preImageEntity = context.PreEntityImages.Contains("Image") ? (Entity)context.PreEntityImages["Image"] : null;

            var preActiveStageIdRef = preImageEntity.Contains("activestageid") ? (EntityReference)preImageEntity["activestageid"] : null;
            var preActiveStageId = preActiveStageIdRef.Id;

            // Retrieve the process stage to get the name 
            Entity preStageEntity = service.Retrieve("processstage", preActiveStageId, new ColumnSet("stagename"));
            string preStage = preStageEntity.GetAttributeValue<string>("stagename");
            TraceHelper.Trace(tracingService, "Current Stage Name: {0}", preStage);

            // Retrieve the process stage to get the name 
            Entity stageEntity = service.Retrieve("processstage", stageRef.Id, new ColumnSet("stagename"));
            string stage = stageEntity.GetAttributeValue<string>("stagename");
            TraceHelper.Trace(tracingService, "Current Stage Name: {0}", stage);

            TraceHelper.Trace(tracingService, "TargetId: {0}", target.Id);

            // Retrieve Environment Variable values
            string evPitchedStage = GetEnvironmentVariableValue(service, "ats_PitchedStage");
            TraceHelper.Trace(tracingService, "pitchedStage Value: {0}", evPitchedStage);

            string evSoldStage = GetEnvironmentVariableValue(service, "ats_SoldStage");
            TraceHelper.Trace(tracingService, "SoldStage Value: {0}", evSoldStage);

            //If stage matches, retrieve Opportunity Products with Inventory data 
            if (stage == evPitchedStage || stage == evSoldStage)
            {
                TraceHelper.Trace(tracingService, "stage == pitchedStage || stage == soldStage");

                var oppProducts = RetrieveOpportunityProductsWithInventory(service, target.Id, tracingService);

                TraceHelper.Trace(tracingService, "oppProducts.Entities.Count: {0}", oppProducts.Entities.Count);
                foreach (var oppProduct in oppProducts.Entities)
                {
                    int oppQty = oppProduct.GetAttributeValue<int>("ats_quantity");
                    TraceHelper.Trace(tracingService, "oppQty: {0}", oppQty);

                    int oppQtyEvents = oppProduct.GetAttributeValue<int>("ats_quantityofevents");
                    TraceHelper.Trace(tracingService, "oppQtyEvents: {0}", oppQtyEvents);

                    int quantityPitched = 0;
                    if (oppProduct.Contains("IBS.ats_quantitypitched") && oppProduct["IBS.ats_quantitypitched"] is AliasedValue pitchedAliased)
                    {
                        quantityPitched = (int)pitchedAliased.Value;
                    }
                    TraceHelper.Trace(tracingService, "quantityPitched: {0}", quantityPitched);

                    int quantitySold = 0;
                    if (oppProduct.Contains("IBS.ats_quantitysold") && oppProduct["IBS.ats_quantitysold"] is AliasedValue soldAliased)
                    {
                        quantitySold = (int)soldAliased.Value;
                    }
                    TraceHelper.Trace(tracingService, "quantitySold: {0}", quantitySold);

                    //RateType value 
                    OptionSetValue rateType = oppProduct.GetAttributeValue<AliasedValue>("Rate.ats_ratetype")?.Value as OptionSetValue;
                    int? rateTypeValue = rateType?.Value;
                    TraceHelper.Trace(tracingService, "rateTypeValue: {0}", rateTypeValue);

                    EntityReference inventoryRef = oppProduct.GetAttributeValue<EntityReference>("ats_inventorybyseason");
                    if (inventoryRef == null)
                    {
                        TraceHelper.Trace(tracingService, "inventoryRef == null");
                        continue;
                    }

                    Entity inventoryToUpdate = new Entity("ats_inventorybyseason", inventoryRef.Id);

                    if (stage == evPitchedStage)
                    {
                        if (rateTypeValue == 114300000) // Season
                        {
                            TraceHelper.Trace(tracingService, "Rate Type : Season");
                            inventoryToUpdate["ats_quantitypitched"] = quantityPitched + oppQty;
                            TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                        }
                        else //Event
                        {
                            TraceHelper.Trace(tracingService, "Rate Type : Event");
                            inventoryToUpdate["ats_quantitypitched"] = quantityPitched + (oppQty * oppQtyEvents);
                            TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                        }
                    }
                    else if (stage == evSoldStage)
                    {
                        if (rateTypeValue == 114300000) // Season
                        {
                            TraceHelper.Trace(tracingService, "Rate Type : Season");
                            inventoryToUpdate["ats_quantitysold"] = quantitySold + oppQty;
                            TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);
                            inventoryToUpdate["ats_quantitypitched"] = quantityPitched - oppQty;
                            TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                        }
                        else //Event
                        {
                            TraceHelper.Trace(tracingService, "Rate Type : Event");
                            inventoryToUpdate["ats_quantitysold"] = quantitySold + (oppQty * oppQtyEvents);
                            TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);
                            inventoryToUpdate["ats_quantitypitched"] = quantityPitched - (oppQty * oppQtyEvents);
                            TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                        }
                    }

                    service.Update(inventoryToUpdate);
                    TraceHelper.Trace(tracingService, "Inventory record updated successfully.");
                }
            }


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




    private void UpdateQtyPitchedSoldFromAgreement(IPluginExecutionContext context, ITracingService tracingService, IOrganizationService service, Entity target, bool isPreEntityImage, Guid agreementID, EntityReference activeStageRef)
    {
        string functionName = "UpdateQtyPitchedSoldFromAgreement";
        try
        {
            if (isPreEntityImage)
            {
                //Suncing the bpf of Opportunity based on the Agreement
                SyncOpportunityFromAgreement(target, service, tracingService);
            }

            // Step 1: Fetch the BPF (workflow) by its unique name
            string bpfAgreementUniqueName = "ats_agreementbusinessprocessflow";

            string fetchWorkflowXml = $@"
                          <fetch top='1'>
                            <entity name='workflow'>
                              <attribute name='workflowid' />
                              <filter>
                                <condition attribute='uniquename' operator='eq' value='{bpfAgreementUniqueName}' />
                              </filter>
                            </entity>
                          </fetch>";

            EntityCollection workflowResults = service.RetrieveMultiple(new FetchExpression(fetchWorkflowXml));

            if (workflowResults.Entities.Count == 0)
            {
                throw new InvalidPluginExecutionException($"No workflow found with unique name: {bpfAgreementUniqueName}");
            }

            Guid processId = workflowResults.Entities[0].Id;

            // Step 2: Fetch process stages for the retrieved workflowid
            string fetchStagesXml = $@"
                          <fetch>
                            <entity name='processstage'>
                              <attribute name='stagename' />
                              <attribute name='processstageid' />
                              <filter>
                                <condition attribute='processid' operator='eq' value='{processId}' />
                              </filter>
                            </entity>
                          </fetch>";

            EntityCollection stageResults = service.RetrieveMultiple(new FetchExpression(fetchStagesXml));

            List<StageInfo> stageList = new List<StageInfo>();
            int index = 0;

            foreach (Entity stages in stageResults.Entities)
            {
                string stageName = stages.GetAttributeValue<string>("stagename");
                Guid stageId = stages.GetAttributeValue<Guid>("processstageid");

                TraceHelper.Trace(tracingService, "Processing stage {0}: Name = {1}, ID = {2}", index, stageName, stageId);

                StageInfo stageInfo = new StageInfo
                {
                    Index = index,
                    StageName = stageName,
                    StageId = stageId
                };

                stageList.Add(stageInfo);
                index++;
            }

            TraceHelper.Trace(tracingService, "Stage list creation completed. Total entries: {0}", stageList.Count);


            //Sunny(13-05-25)
            // Step 1: Prepare FetchXML for custom configuration table
            string fetchAgreementBPFXml = $@"
                              <fetch>
                                <entity name='ats_agreementopportunitybpfstageconfiguration'>
                                  <attribute name='ats_bpfname' />
                                  <attribute name='ats_sortorder' />
                                  <attribute name='ats_stagename' />
                                  <filter>
                                    <condition attribute='ats_bpfname' operator='eq' value='{bpfAgreementUniqueName}' />
                                  </filter>
                                  <order attribute='ats_sortorder' />
                                </entity>
                              </fetch>";

            // Step 2: Execute Fetch
            EntityCollection configResults = service.RetrieveMultiple(new FetchExpression(fetchAgreementBPFXml));

            // Step 3: Process Results
            List<ConfiguredStageInfo> configuredStageList = new List<ConfiguredStageInfo>();
            int indexes = 0;

            foreach (Entity config in configResults.Entities)
            {
                string stageName = config.GetAttributeValue<string>("ats_stagename");
                int? sortOrder = config.GetAttributeValue<int?>("ats_sortorder");

                TraceHelper.Trace(tracingService, "Config record {0}: Stage = {1}, SortOrder = {2}", index, stageName, sortOrder);

                ConfiguredStageInfo configInfo = new ConfiguredStageInfo
                {
                    StageName = stageName,
                    SortOrder = sortOrder ?? 0 // default to 0 if null
                };

                configuredStageList.Add(configInfo);
                indexes++;
            }

            TraceHelper.Trace(tracingService, "Configured stage list created. Total: {0}", configuredStageList.Count);


            //Proceeding for the final list
            List<FinalStageInfo> finalStageList = new List<FinalStageInfo>();

            foreach (var configStage in configuredStageList)
            {
                var matchingStage = stageList.FirstOrDefault(s => s.StageName.Equals(configStage.StageName, StringComparison.OrdinalIgnoreCase));

                if (matchingStage != null)
                {
                    finalStageList.Add(new FinalStageInfo
                    {
                        StageName = configStage.StageName,
                        SortOrder = configStage.SortOrder,
                        ProcessStageId = matchingStage.StageId
                    });
                }
            }
            //Sort by SortOrder dynamically
            finalStageList = finalStageList.OrderBy(s => s.SortOrder).ToList();

            // Retrieve Environment Variable values
            string evPitchedStage = GetEnvironmentVariableValue(service, "ats_PitchedStage");
            TraceHelper.Trace(tracingService, "pitchedStage Value: {0}", evPitchedStage);

            string evSoldStage = GetEnvironmentVariableValue(service, "ats_SoldStage");
            TraceHelper.Trace(tracingService, "SoldStage Value: {0}", evSoldStage);

            // Initialize variables to hold sort order values
            int? pitchedStageSortOrder = finalStageList.FirstOrDefault(x => x.StageName.Equals(evPitchedStage, StringComparison.OrdinalIgnoreCase))?.SortOrder;

            int? soldStageSortOrder = finalStageList.FirstOrDefault(x => x.StageName.Equals(evSoldStage, StringComparison.OrdinalIgnoreCase))?.SortOrder;

            //Retireiving the Closed Lost stage. 
            // Ensure we have at least 2 stages to safely access the second last
            FinalStageInfo closedLostStage = null;
            int closedLostStageOrder = 0;
            if (finalStageList.Count >= 2)
            {
                closedLostStage = finalStageList[finalStageList.Count - 2];
                TraceHelper.Trace(tracingService, "Second last stage: {0}, SortOrder: {1}", closedLostStage.StageName, closedLostStage.SortOrder);
                closedLostStageOrder = closedLostStage.SortOrder;
            }
            else
            {
                TraceHelper.Trace(tracingService, "Final stage list has less than 2 items. Cannot determine second last stage.");
            }

            //Retreving the Closed Won Stage Order
            FinalStageInfo closedWonStage = null;
            int closedWonStageOrder = 0;
            if (finalStageList.Count >= 2)
            {
                closedWonStage = finalStageList[finalStageList.Count - 1];
                TraceHelper.Trace(tracingService, " last stage: {0}, SortOrder: {1}", closedWonStage.StageName, closedWonStage.SortOrder);
                closedWonStageOrder = closedWonStage.SortOrder;
            }
            else
            {
                TraceHelper.Trace(tracingService, "Final stage list has less than 2 items. Cannot determine second last stage.");
            }

            // Trace the results
            TraceHelper.Trace(tracingService, "PitchedStage SortOrder: {0}", pitchedStageSortOrder);
            TraceHelper.Trace(tracingService, "SoldStage SortOrder: {0}", soldStageSortOrder);

            //Sunny(07-08-25)
            //handling the target when this function calls from the agreenent entity.
            EntityReference stageRef = null;
            if (isPreEntityImage)
            {
                //Retreiving the current stage and the pre stage and initialize the sort order value to them also
                stageRef = target.GetAttributeValue<EntityReference>("activestageid");
            }
            else
            {
                stageRef = activeStageRef;
                TraceHelper.Trace(tracingService, "Stage Reg  is initialized");
            }

            if (stageRef == null) return;

            // Retrieve the process stage to get the name 
            Entity stageEntity = service.Retrieve("processstage", stageRef.Id, new ColumnSet("stagename"));
            string stage = stageEntity.GetAttributeValue<string>("stagename");
            TraceHelper.Trace(tracingService, "Current Stage Name: {0}", stage);

            Entity preImageEntity = null;

            EntityReference preActiveStageIdRef = null;
            Guid preActiveStageId = Guid.Empty;

            // Retrieve the process stage to get the name 
            Entity preStageEntity = null;
            string preStage = null;

            if (isPreEntityImage)
            {
                preImageEntity = context.PreEntityImages.Contains("Image") ? (Entity)context.PreEntityImages["Image"] : null;

                preActiveStageIdRef = preImageEntity.Contains("activestageid") ? (EntityReference)preImageEntity["activestageid"] : null;
                preActiveStageId = preActiveStageIdRef.Id;

                // Retrieve the process stage to get the name 
                preStageEntity = service.Retrieve("processstage", preActiveStageId, new ColumnSet("stagename"));
                preStage = preStageEntity.GetAttributeValue<string>("stagename");
                TraceHelper.Trace(tracingService, "Previous Stage Name: {0}", preStage);
            }
            else
            {
                preStage = stage;//Current stage only
            }

            // Get SortOrder for previous stage
            int? previousStageSortOrder = finalStageList.FirstOrDefault(x => x.StageName.Equals(preStage, StringComparison.OrdinalIgnoreCase))?.SortOrder;

            // Trace the sort orders
            TraceHelper.Trace(tracingService, "Previous Stage SortOrder: {0}", previousStageSortOrder);

            // Get SortOrder for current stage
            int? currentStageSortOrder = finalStageList.FirstOrDefault(x => x.StageName.Equals(stage, StringComparison.OrdinalIgnoreCase))?.SortOrder;

            // Trace the sort orders
            TraceHelper.Trace(tracingService, "Current Stage SortOrder: {0}", currentStageSortOrder);

            //Sunny(05-06-25)
            //Retrieving the environment variable 
            int stagEndCount = RetrieveStageEndCount(tracingService, service);


            //this logic is for comparing the stages and incrementing the Qty Pitched, Qty sold and Total Quantity. 
            if (currentStageSortOrder != 0 && previousStageSortOrder != 0 && soldStageSortOrder != 0 && pitchedStageSortOrder != 0) //Avoding the null 
            {
                Entity agreementObj = null;
                EntityReference agreementId = null;
                Entity agreementToUpdate = new Entity("ats_agreement");

                if (isPreEntityImage) //Sunny(07-08-25)
                {
                    if (!target.Attributes.Contains("bpf_ats_agreementid"))
                    {
                        agreementObj = service.Retrieve("ats_agreementbusinessprocessflow", target.Id, new ColumnSet("bpf_ats_agreementid"));
                        agreementId = agreementObj.Contains("bpf_ats_agreementid") ? (EntityReference)agreementObj["bpf_ats_agreementid"] : null;
                    }
                    else
                    {
                        agreementId = target.GetAttributeValue<EntityReference>("bpf_ats_agreementid");
                    }

                    //Sunny(04-08-25)
                    //creating the instance of the Agreement entity to update the BPF stage name field present in that.
                    // Prepare the update
                    agreementToUpdate.Id = agreementId.Id;
                }
                else
                {
                    agreementToUpdate.Id = agreementID; //
                    TraceHelper.Trace(tracingService, "Agreement Id is updated");
                }

                #region Retreiving the Product details associated on the Agreement Entity
                //Dictionary<string, EntityReference> ibsDict; 
                EntityCollection oppProducts = null;
                if (isPreEntityImage)
                {
                    oppProducts = RetrieveOpportunityProductsWithInventoryFromAgreement(service, agreementId.Id, tracingService);
                }
                else
                {
                    oppProducts = RetrieveOpportunityProductsWithInventoryFromAgreement(service, agreementID, tracingService);
                }

                TraceHelper.Trace(tracingService, "oppProducts.Entities.Count: {0}", oppProducts.Entities.Count);

                //to get the first opp SeasonId 
                int count = 0;
                Guid firstOppSeason = Guid.Empty;

                foreach (var agreement in oppProducts.Entities)
                {
                    if(count == 0)
                    {
                        TraceHelper.Trace(tracingService, "!st Opportunity");
                        firstOppSeason = (agreement.Contains("OppSeason.ats_seasonid") && agreement["OppSeason.ats_seasonid"] is AliasedValue sa && sa.Value is Guid ers) ? ers : Guid.Empty;
                        TraceHelper.Trace(tracingService, $"firstOppSeason: {firstOppSeason}");
                    }

                    TraceHelper.Trace(tracingService, "Oppid:{0}", agreement.Id); 
                    // From Opportunity (alias: Opp)
                    string opportunityName = agreement.GetAttributeValue<AliasedValue>("Opp.name")?.Value as string;
                    TraceHelper.Trace(tracingService, "Opportunity Name: {0}", opportunityName);

                    // From OpportunityProduct (alias: OppProd)
                    string oppProductName = agreement.GetAttributeValue<AliasedValue>("OppProd.opportunityproductname")?.Value as string;
                    TraceHelper.Trace(tracingService, "Opportunity Product Name: {0}", oppProductName);

                    Guid oppProductId = (agreement.Contains("OppProd.opportunityproductid") && agreement["OppProd.opportunityproductid"] is AliasedValue av &&av.Value is Guid g)? g  : Guid.Empty;

                    TraceHelper.Trace(tracingService, "Opportunity Product Name: {0}", oppProductName);

                    //If No Line Item exist in the Opportunies just simpoly reuturn the code.
                    if (string.IsNullOrWhiteSpace(oppProductName))
                    {
                        TraceHelper.Trace(tracingService, "No line item name found. Returning from logic.");
                        return;
                    }

                    // InventoryBySeason (lookup field on opportunityproduct)
                    EntityReference inventoryBySeasonId = null;
                    inventoryBySeasonId =
 (agreement.Attributes.Contains("OppProd.ats_inventorybyseason") &&
      agreement["OppProd.ats_inventorybyseason"] is AliasedValue invAliased)
         ? (EntityReference)invAliased.Value
         : null;

                    if (inventoryBySeasonId == null)
                    {
                        EntityReference productRef = null;
                        //retreival of the dictonary data based on the productId. 
                        if (agreement.Contains("OppProd.productid"))
                        {
                            productRef = (agreement.Contains("OppProd.productid") && agreement["OppProd.productid"] is AliasedValue a && a.Value is EntityReference er) ? er : null;
                            TraceHelper.Trace(tracingService, "productRef: {0}", productRef.Id); 
                        }









                        TraceHelper.Trace(tracingService, "for this opportunity Product IBS is missing, creating the new IBS for it ");
                        AgreementCartAction agreementCartAction = new AgreementCartAction();

                        //retreving the necessary fields needed to create the IBS
                        Guid startSeasonRef = Guid.Empty ;

                        bool isSeason = false;
                        isSeason = agreement.Contains("OppSeason.ats_seasonid");
                        TraceHelper.Trace(tracingService, "isSeason: {0}", isSeason);
                        //Guid primaryProductIdBatch = Guid.Empty;
                        if (isSeason)
                        {
                            startSeasonRef = (agreement.Contains("OppSeason.ats_seasonid") && agreement["OppSeason.ats_seasonid"] is AliasedValue sa && sa.Value is Guid ers) ? ers : Guid.Empty;
                            TraceHelper.Trace(tracingService, "startSeasonRefId: {0}", startSeasonRef);
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, " Season is missing.");
                        }
                        var ibsCacheBatch = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase); // season|product -> IBS
                        TraceHelper.Trace(tracingService, "IBS cached");

                        EntityReference primaryIbsRefBatch = agreementCartAction.EnsureInventoryForSeasonOptimized(
                               startSeasonRef,
                               productRef.Id,
                               firstOppSeason,
                               service,
                               tracingService,
                               ibsCacheBatch,
                               null
                           );


                        inventoryBySeasonId = primaryIbsRefBatch;
                        TraceHelper.Trace(tracingService, "new inventoryBySeasonId: {0}", inventoryBySeasonId.Id);
                        Entity oppProdIBSUpdate = new Entity("opportunityproduct");
                        oppProdIBSUpdate.Id = oppProductId;



                        oppProdIBSUpdate["ats_inventorybyseason"] =new EntityReference("ats_inventorybyseason", inventoryBySeasonId.Id);
                        service.Update(oppProdIBSUpdate);
                        tracingService.Trace("Opportunity IBS record is updated");
                    }
                    TraceHelper.Trace(tracingService, "Inventory By Season Id: {0}", inventoryBySeasonId.Id);

                    // Quantity fields
                    int? oppQty = agreement.GetAttributeValue<AliasedValue>("OppProd.ats_quantity")?.Value as int?;
                    TraceHelper.Trace(tracingService, "oppQty: {0}", oppQty);

                    int? oppQtyEvents = agreement.GetAttributeValue<AliasedValue>("OppProd.ats_quantityofevents")?.Value as int?;
                    TraceHelper.Trace(tracingService, "oppQtyEvents: {0}", oppQtyEvents);

                    // From InventoryBySeason (alias: IBS)
                    int quantityPitched = 0;
                    if (agreement.Contains("IBS.ats_quantitypitched") && agreement["IBS.ats_quantitypitched"] is AliasedValue pitchedAliased) quantityPitched = (int)pitchedAliased.Value;
                    TraceHelper.Trace(tracingService, "quantityPitched: {0}", quantityPitched);

                    int quantitySold = 0;
                    if (agreement.Contains("IBS.ats_quantitysold") && agreement["IBS.ats_quantitysold"] is AliasedValue soldAliased) quantitySold = (int)soldAliased.Value;
                    TraceHelper.Trace(tracingService, "quantitySold: {0}", quantitySold);

                    // From Rate entity (alias: Rate)
                    OptionSetValue rateType = agreement.GetAttributeValue<AliasedValue>("Rate.ats_ratetype")?.Value as OptionSetValue;
                    int? rateTypeValue = rateType?.Value;
                    TraceHelper.Trace(tracingService, "rateTypeValue: {0}", rateTypeValue);

                    Entity inventoryToUpdate = new Entity("ats_inventorybyseason", inventoryBySeasonId.Id);

                    //Sunny(19-08-25)
                    //Creating the instance for updating the field name, BPS Status present on the Opportunity
                    Guid opportunityId = Guid.Empty;

                    if (agreement.Contains("Opp.opportunityid"))
                    {
                        var aliasedValue = agreement.GetAttributeValue<AliasedValue>("Opp.opportunityid");
                        if (aliasedValue != null && aliasedValue.Value is Guid guidValue) opportunityId = guidValue;
                    }

                    TraceHelper.Trace(tracingService, "opportunityId: {0}", opportunityId);

                    Entity opp = new Entity("opportunity");
                    opp.Id = opportunityId;

                    //Sunny(05-06-2025)
                    //Adding the validation for the bpf stage count as the logic varies from single end to double end (Closed Won/ Closed Lost)
                    if (stagEndCount == 2)  //When Bpf having the 2 ends
                    {
                        TraceHelper.Trace(tracingService, "stagEndCount == 2");

                        if (currentStageSortOrder == pitchedStageSortOrder) //Pitched
                        {
                            if (previousStageSortOrder < pitchedStageSortOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "currentStageSortOrder == pitchedStageSortOrder && previousStageSortOrder < pitchedStageSortOrder");
                                //Updating the Quantity Pitched based on the Rate type
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300001); // setting up the status at pitched stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300001); // setting up the status at pitched stage 
                            service.Update(opp);
                            TraceHelper.Trace(tracingService, "pitched updated in the Agreement as well as in Opportunity");
                        }

                        if (currentStageSortOrder == soldStageSortOrder) //Closed Won  
                        {
                            TraceHelper.Trace(tracingService, "currentStageSortOrder == soldStageSortOrder");
                            if (isPreEntityImage)
                            {
                                //Updating the Total Quantity,  add sold 
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold + oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty - quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold + (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty - quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300003); // setting up the status at Closed won stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300003); // setting up the status at Closed won stage
                            service.Update(opp);
                            TraceHelper.Trace(tracingService, "closed won updated");
                        }

                        //Moving from Closed Won to  Proposal or after that but not on Closed Lost
                        else if (currentStageSortOrder >= pitchedStageSortOrder && currentStageSortOrder != closedLostStageOrder)
                        {
                            if (previousStageSortOrder == closedWonStageOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "currentStageSortOrder >= pitchedStageSortOrder && currentStageSortOrder != closedLostStageOrder && previousStageSortOrder == closedWonStageOrder");

                                //Remvoving the sold, add to pitch, Quantity increased
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                            }
                            else if (previousStageSortOrder == closedLostStageOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "currentStageSortOrder >= pitchedStageSortOrder && currentStageSortOrder != closedLostStageOrder && previousStageSortOrder == closedWonStageOrder");
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                            }
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300001); // setting up the status at pitched stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300001); // setting up the status at pitched stage
                            service.Update(opp);
                            TraceHelper.Trace(tracingService, "closed lost updated");
                        }

                        //Moving from Closed won to the the Closed Lost
                        if (currentStageSortOrder == closedLostStageOrder && isPreEntityImage)
                        {
                            if (previousStageSortOrder == closedWonStageOrder)
                            {
                                TraceHelper.Trace(tracingService, " currentStageSortOrder == closedLostStageOrder && previousStageSortOrder == closedWonStageOrder");

                                //dcrease the Sold , Qty Available increased
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                            }
                        }

                        //Moving from previous stage order to Closed lost Stage
                        if (currentStageSortOrder == closedLostStageOrder)
                        {
                            if (previousStageSortOrder != closedWonStageOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "previousStageSortOrder != closedWonStageOrder && currentStageSortOrder == closedLostStageOrder");

                                //remove pitch 
                                if (rateTypeValue == 114300000) // Season
                                {
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                                else //Event
                                {
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300004); // setting up the status at Closed lost stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300004); // setting up the status at Closed lost stage
                            service.Update(opp);
                            TraceHelper.Trace(tracingService, "closed lost updated");
                        }

                        //Eg: Moving from Verbal Agreement to Prospecting but previous stage should not be from Won or lost
                        if (currentStageSortOrder < pitchedStageSortOrder)
                        {
                            if (previousStageSortOrder >= pitchedStageSortOrder && previousStageSortOrder != closedWonStageOrder && previousStageSortOrder != closedLostStageOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "currentStageSortOrder < pitchedStageSortOrder && previousStageSortOrder >= pitchedStageSortOrder && previousStageSortOrder != closedWonStageOrder && previousStageSortOrder!= closedLostStageOrder");
                                //Update the Quantity pitched
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300000); // setting up the status at Pre-Pitched stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300000); // setting up the status at Pre-Pitched stage
                            service.Update(opp);
                            TraceHelper.Trace(tracingService, "Prepitcherd updated");
                        }

                        //Moving from Closed Won to prospecting 
                        if (currentStageSortOrder < pitchedStageSortOrder && isPreEntityImage)
                        {
                            if (previousStageSortOrder == closedWonStageOrder)
                            {
                                TraceHelper.Trace(tracingService, "previousStageSortOrder == closedWonStageOrder  && currentStageSortOrder < pitchedStageSortOrder");

                                //remove sold, add qty available 
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                            }
                        }
                    }
                    else if (stagEndCount == 1) //When Bpf having the single end
                    {
                        TraceHelper.Trace(tracingService, "stagEndCount == 1");
                        if (currentStageSortOrder == pitchedStageSortOrder) //Pitched
                        {
                            if (previousStageSortOrder < pitchedStageSortOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "currentStageSortOrder == pitchedStageSortOrder && previousStageSortOrder < pitchedStageSortOrder");
                                //Updating the Quantity Pitched based on the Rate type
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300001); // setting up the status pitched stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300001); // setting up the status pitched stage
                            service.Update(opp);
                        }

                        if (currentStageSortOrder == soldStageSortOrder) //Sold Stage
                        {
                            TraceHelper.Trace(tracingService, "currentStageSortOrder == soldStageSortOrder");
                            if (isPreEntityImage)
                            {
                                //Updating the Quantity Pitched based on the Rate type
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);

                                    inventoryToUpdate["ats_quantitysold"] = quantitySold + oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty - quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);

                                    inventoryToUpdate["ats_quantitysold"] = quantitySold + (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty - quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300003); // setting up the status closed won stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300003); // setting up the status closed won stage
                            service.Update(opp);
                        }

                        if (currentStageSortOrder < pitchedStageSortOrder) //Sold to pre pitched
                        {
                            if (previousStageSortOrder == soldStageSortOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "previousStageSortOrder == soldStageSortOrder && currentStageSortOrder < pitchedStageSortOrder");
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");

                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");

                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300000); // setting up the status Pre-Pitched Stage  stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300000); // setting up the status Pre-Pitched Stage  
                            service.Update(opp);
                        }

                        if (currentStageSortOrder >= pitchedStageSortOrder && currentStageSortOrder != closedLostStageOrder && isPreEntityImage) // sold stage to verbal agreement but not on the closed lost
                        {
                            if (previousStageSortOrder == soldStageSortOrder)
                            {
                                TraceHelper.Trace(tracingService, "previousStageSortOrder == soldStageSortOrder && currentStageSortOrder >= pitchedStageSortOrder");
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);

                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty - quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched + (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);

                                    inventoryToUpdate["ats_quantitysold"] = quantitySold - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                    inventoryToUpdate["ats_quantityavailable"] = oppQty - quantityPitched;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                                }
                            }
                        }

                        if (currentStageSortOrder < pitchedStageSortOrder && isPreEntityImage) //Pre-pitched stage
                        {
                            if (previousStageSortOrder >= pitchedStageSortOrder && previousStageSortOrder != soldStageSortOrder && previousStageSortOrder != closedLostStageOrder)
                            {
                                TraceHelper.Trace(tracingService, "previousStageSortOrder >= pitchedStageSortOrder && previousStageSortOrder != soldStageSortOrder && previousStageSortOrder != closedLostStageOrder");
                                if (rateTypeValue == 114300000) // Season
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Season");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                                else //Event
                                {
                                    TraceHelper.Trace(tracingService, "Rate Type : Event");
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                            }
                        }





                        //Moving from previous stage order to Closed lost Stage
                        if (currentStageSortOrder == closedLostStageOrder)
                        {
                            if (previousStageSortOrder != closedWonStageOrder && isPreEntityImage)
                            {
                                TraceHelper.Trace(tracingService, "previousStageSortOrder != closedWonStageOrder && currentStageSortOrder == closedLostStageOrder");

                                //remove pitch 
                                if (rateTypeValue == 114300000) // Season
                                {
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - oppQty;
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                                else //Event
                                {
                                    inventoryToUpdate["ats_quantitypitched"] = quantityPitched - (oppQty * oppQtyEvents);
                                    TraceHelper.Trace(tracingService, "Updated ats_quantitypitched: {0}", inventoryToUpdate["ats_quantitypitched"]);
                                }
                            }

                            //Sunny(05-08-25)
                            agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300004); // setting up the status at Closed lost stage
                            service.Update(agreementToUpdate);
                            opp["ats_bpfstatus"] = new OptionSetValue(114300004); // setting up the status at Closed lost stage 
                            service.Update(opp);
                        }

                        //Moving from closed won to close lost
                        if (previousStageSortOrder == closedWonStageOrder && currentStageSortOrder == closedLostStageOrder && isPreEntityImage)
                        {
                            TraceHelper.Trace(tracingService, "previousStageSortOrder == closedWonStageOrder && currentStageSortOrder == closedLostStageOrder");
                            //dcrease the Sold , Qty Available increased
                            if (rateTypeValue == 114300000) // Season
                            {
                                TraceHelper.Trace(tracingService, "Rate Type : Season");
                                inventoryToUpdate["ats_quantitysold"] = quantitySold - oppQty;
                                TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                            }
                            else //Event
                            {
                                TraceHelper.Trace(tracingService, "Rate Type : Event");
                                inventoryToUpdate["ats_quantitysold"] = quantitySold - (oppQty * oppQtyEvents);
                                TraceHelper.Trace(tracingService, "Updated ats_quantitysold: {0}", inventoryToUpdate["ats_quantitysold"]);

                                inventoryToUpdate["ats_quantityavailable"] = oppQty + quantityPitched;
                                TraceHelper.Trace(tracingService, "Updated ats_quantity: {0}", inventoryToUpdate["ats_quantityavailable"]);
                            }
                            //Sunny(05-08-25)
                            //agreementToUpdate["ats_bpfstatus"] = new OptionSetValue(114300004); // setting up the status at Closed lost stage
                            //service.Update(agreementToUpdate);
                        }
                    }

                    service.Update(inventoryToUpdate);
                    TraceHelper.Trace(tracingService, "Inventory record updated successfully.");
                    count++;
                }
                #endregion



            }
            else
            {
                TraceHelper.Trace(tracingService, "Any one of the value is '0': currentStageSortOrder!= 0 && previousStageSortOrder!= 0 && soldStageSortOrder!=0 && pitchedStageSortOrder !=0");
            }



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


    private int RetrieveStageEndCount(ITracingService tracingService, IOrganizationService service)
    {
        string functionName = "RetrieveStageEndCount";
        try
        {
            // Define the schema name of the environment variable
            string schemaName = "ats_BPFEndStageCountType";
            TraceHelper.Trace(tracingService, "[EnvVar] Looking up environment variable with schema name: {0}", schemaName);

            // Build FetchXML (note alias="evv" on the link-entity)
            string fetchXml = $@"
                        <fetch>
                          <entity name='environmentvariabledefinition'>
                            <attribute name='displayname' />
                            <attribute name='defaultvalue' />
                            <attribute name='schemaname' />
                            <filter type='and'>
                              <condition attribute='schemaname' operator='eq' value='{schemaName}' />
                            </filter>
                            <link-entity name='environmentvariablevalue' from='environmentvariabledefinitionid' to='environmentvariabledefinitionid' link-type='outer' alias='evv'>
                              <attribute name='value' />
                            </link-entity>
                          </entity>
                        </fetch>";

            TraceHelper.Trace(tracingService, "[EnvVar] FetchXML being executed:\n{0}", fetchXml);

            // Execute fetch
            var result = service.RetrieveMultiple(new FetchExpression(fetchXml)).Entities.FirstOrDefault();

            if (result == null)
            {
                TraceHelper.Trace(tracingService, "[EnvVar] No environment variable record found.");
                return 0;
            }

            // Parse default value using ternary
            string defaultStr = result.GetAttributeValue<string>("defaultvalue");
            decimal? defaultValue = decimal.TryParse(defaultStr, out var parsedDefault) ? parsedDefault : (decimal?)null;

            TraceHelper.Trace(tracingService, "[EnvVar] Default value string: {0}", defaultStr);
            TraceHelper.Trace(tracingService, "[EnvVar] Parsed default value: {0}", defaultValue);

            // Parse current value using ternary and aliased value
            AliasedValue aliasVal = result.Contains("evv.value") ? result["evv.value"] as AliasedValue : null;
            decimal? currentValue = (aliasVal != null && decimal.TryParse(aliasVal.Value?.ToString(), out var parsedCurrent)) ? parsedCurrent : (decimal?)null;

            TraceHelper.Trace(tracingService, "[EnvVar] Current value (aliased): {0}", aliasVal?.Value);
            TraceHelper.Trace(tracingService, "[EnvVar] Parsed current value: {0}", currentValue);

            // Use current value if available, otherwise default
            decimal? finalValue = currentValue ?? defaultValue;
            TraceHelper.Trace(tracingService, "[EnvVar] Final value being returned: {0}", finalValue);

            return Convert.ToInt32(finalValue);
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


    public string GetEnvironmentVariableValue(IOrganizationService service, string schemaName)
    {
        string functionName = "GetEnvironmentVariableValue";
        try
        {

            // Build FetchXML (note alias="evv" on the link-entity)
            string fetchXml = $@"
                        <fetch>
                          <entity name='environmentvariabledefinition'>
                            <attribute name='displayname' />
                            <attribute name='defaultvalue' />
                            <attribute name='schemaname' />
                            <filter type='and'>
                              <condition attribute='schemaname' operator='eq' value='{schemaName}' />
                            </filter>
                            <link-entity name='environmentvariablevalue' from='environmentvariabledefinitionid' to='environmentvariabledefinitionid' link-type='outer' alias='evv'>
                              <attribute name='value' />
                            </link-entity>
                          </entity>
                        </fetch>";

            // Execute the fetch
            var result = service
                .RetrieveMultiple(new FetchExpression(fetchXml))
                .Entities
                .FirstOrDefault();

            if (result == null)
                return null;

            // 1. Read the Default Value
            string defaultValue = result.GetAttributeValue<string>("defaultvalue");


            // 2. Read the Current Value (from the aliased link-entity)
            string currentValue = null;
            if (result.Contains("evv.value") && result["evv.value"] is AliasedValue aliased)
            {
                currentValue = aliased.Value as string;
            }

            // 3. Decide which to return
            //    If Current Value is set (non-empty), use it; otherwise fallback to Default Value
            return !string.IsNullOrWhiteSpace(currentValue)
                ? currentValue
                : defaultValue;
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


    private EntityCollection RetrieveOpportunityProductsWithInventory(IOrganizationService service, Guid targetId, ITracingService tracingService)
    {
        string functionName = "RetrieveOpportunityProductsWithInventory"; 
        try
        {

            TraceHelper.Trace(tracingService, "functionName: {0}", functionName);

            Entity stageEntity = service.Retrieve("ats_opportunitybpf", targetId, new ColumnSet("bpf_opportunityid"));
            EntityReference oppId = stageEntity.Contains("bpf_opportunityid") ? (EntityReference)stageEntity["bpf_opportunityid"] : null;
            TraceHelper.Trace(tracingService, "OppId: {0}", oppId.Id);

            if (oppId.Id != null)
            {
                string fetchXml = $@"
                        <fetch>
                          <entity name='opportunityproduct'>
                            <attribute name='ats_quantity' />
                            <attribute name='ats_quantityofevents'/>
                            <attribute name='ats_inventorybyseason' />
                            <attribute name='opportunityproductname' />
                            <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='IBS'>
                              <attribute name='ats_quantitypitched' />
                              <attribute name='ats_quantitysold' />
                              <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='outer' alias='Rate'>
                                <attribute name='ats_ratetype' />
                              </link-entity>
                            </link-entity>
                            <link-entity name='opportunity' from='opportunityid' to='opportunityid'>
                              <link-entity name='ats_opportunitybpf' from='bpf_opportunityid' to='opportunityid' alias='OppBPF'>
                                <filter>
                                  <condition attribute='bpf_opportunityid' operator='eq' value='{oppId.Id}' />
                                </filter>
                              </link-entity>
                            </link-entity>
                          </entity>
                        </fetch>";

                TraceHelper.Trace(tracingService, "return fetchxml");
                return service.RetrieveMultiple(new FetchExpression(fetchXml));
            }

            TraceHelper.Trace(tracingService, "return null");
            return null;

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



    private EntityCollection RetrieveOpportunityProductsWithInventoryFromAgreement(IOrganizationService service, Guid agreementId, ITracingService tracingService)
    {
        string functionName = "RetrieveOpportunityProductsWithInventoryFromAgreement";
        try
        {

            TraceHelper.Trace(tracingService, "functionName: {0} | ENTER | agreementId={1}", functionName, agreementId);


            if (agreementId == Guid.Empty)
            {
                TraceHelper.Trace(tracingService, "functionName: {0} | agreementId is empty, returning empty dictionary.", functionName);
                return null;
            }

            string fetchXml = $@"
<fetch>
  <entity name='ats_agreement'>
    <filter>
      <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
    </filter>
    <link-entity name='opportunity' from='ats_agreement' to='ats_agreementid' alias='Opp'>
      <attribute name='name' />
      <attribute name='opportunityid' />
      <attribute name='ats_startseason' />
      <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' link-type='outer' alias='OppProd'>
        <attribute name='opportunityproductname' />
        <attribute name='opportunityproductid' />
        <attribute name='productid' />
        <attribute name='ats_inventorybyseason' />
        <attribute name='ats_quantity' />
        <attribute name='ats_quantityofevents' />
        <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='outer' alias='IBS'>
          <attribute name='ats_quantitypitched' />
          <attribute name='ats_quantitysold' />
          <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='outer' alias='Rate'>
            <attribute name='ats_ratetype' />
          </link-entity>
        </link-entity>
      </link-entity>
      <link-entity name='ats_season' from='ats_seasonid' to='ats_startseason' alias='OppSeason'>
       <attribute name='ats_name' />
        <attribute name='ats_seasonid'/>
        <order attribute='ats_name' />
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            //TraceHelper.Trace(tracingService, "functionName: {0} | FetchXML prepared.", functionName);

            //EntityCollection rows = service.RetrieveMultiple(new FetchExpression(fetchXml));

            //TraceHelper.Trace(tracingService, "functionName: {0} | Fetch returned {1} rows.", functionName, rows.Entities.Count);

            //foreach (var row in rows.Entities)
            //{
            //    // 1. IBS reference from OLI
            //    EntityReference ibsRef =
            //        (row.Contains("OppProd.ats_inventorybyseason") &&
            //         row["OppProd.ats_inventorybyseason"] is AliasedValue invAliased &&
            //         invAliased.Value is EntityReference invEr)
            //            ? invEr
            //            : null;

            //    if (ibsRef == null)
            //    {
            //        TraceHelper.Trace(tracingService, "functionName: {0} | Skipping row: IBS is NULL.", functionName);
            //        continue;
            //    }

            //    // 2. Product
            //    EntityReference productRef =
            //        (row.Contains("OppProd.productid") &&
            //         row["OppProd.productid"] is AliasedValue prodAliased &&
            //         prodAliased.Value is EntityReference prodEr)
            //            ? prodEr
            //            : null;

            //    // 3. SeasonName from OppSeason
            //    string seasonName =
            //        (row.Contains("OppSeason.ats_name") &&
            //         row["OppSeason.ats_name"] is AliasedValue seasonAliased &&
            //         seasonAliased.Value is string sName)
            //            ? sName
            //            : null;

            //    if (productRef == null || string.IsNullOrWhiteSpace(seasonName))
            //    {
            //        TraceHelper.Trace(tracingService, "functionName: {0} | Skipping row: productRef or seasonName missing. IBS={1}", functionName, ibsRef.Id);
            //        continue;
            //    }

            //    string key = seasonName + "|" + productRef.Id.ToString();

            //    if (!ibsDict.ContainsKey(key))
            //    {
            //        ibsDict[key] = ibsRef;
            //        TraceHelper.Trace(tracingService, "functionName: {0} | Added key={1}, IBS={2}", functionName, key, ibsRef.Id);
            //    }
            //    else
            //    {
            //        TraceHelper.Trace(tracingService, "functionName: {0} | Duplicate key={1} skipped. Existing IBS={2}, New IBS={3}", functionName, key, ibsDict[key].Id, ibsRef.Id);
            //    }
            //}

            //TraceHelper.Trace(tracingService, "functionName: {0} | EXIT | CacheCount={1}", functionName, ibsDict.Count);
            ////return ibsDict;

            TraceHelper.Trace(tracingService, "return fetchxml");
            return service.RetrieveMultiple(new FetchExpression(fetchXml));




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

