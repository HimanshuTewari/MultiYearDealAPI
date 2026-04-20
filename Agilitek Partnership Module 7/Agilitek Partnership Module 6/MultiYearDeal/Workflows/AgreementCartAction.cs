using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Model;

using System;
using System.Activities;
using System.Collections.Generic;

using System.Linq;

//using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Text.Json;



namespace MultiYearDeal.Workflows
{



    /// <summary>
    /// Helper classes to avoid the tracingservices 
    /// </summary>
    public static class TraceHelper
    {
        // Cached per plugin execution
        private static bool? _isTracingEnabled;

        public static void Initialize(IOrganizationService service)
        {
            if (_isTracingEnabled.HasValue)
                return;

            _isTracingEnabled = GetTracingFlag(service);
        }

        //ex: Trace(tracing, "Simple message");
        public static void Trace(ITracingService tracing, string message)
        {
            if (!_isTracingEnabled.GetValueOrDefault() || tracing == null)
                return;

            tracing.Trace(message);
        }

        //ex: TraceHelper.Trace(tracing, "ProductId: {0}, Qty: {1}", productId, qty);

        public static void Trace(ITracingService tracing, string format, params object[] args)
        {
            if (!_isTracingEnabled.GetValueOrDefault() || tracing == null)
                return;

            tracing.Trace(format, args);
        }

        private static bool GetTracingFlag(IOrganizationService service)
        {
            // Safe default: tracing OFF
            try
            {
                var qe = new QueryExpression("environmentvariabledefinition")
                {
                    ColumnSet = new ColumnSet("schemaname"),
                    Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("schemaname", ConditionOperator.Equal, "ats_EnablePluginTracing")
                    }
                },
                    LinkEntities =
                {
                    new LinkEntity(
                        "environmentvariabledefinition",
                        "environmentvariablevalue",
                        "environmentvariabledefinitionid",
                        "environmentvariabledefinitionid",
                        JoinOperator.LeftOuter)
                    {
                        Columns = new ColumnSet("value"),
                        EntityAlias = "val"
                    }
                }
                };

                var result = service.RetrieveMultiple(qe);

                if (result.Entities.Count == 0)
                    return false;

                var def = result.Entities[0];

                // Prefer Current Value
                if (def.Contains("val.value"))
                {
                    var value = ((AliasedValue)def["val.value"]).Value?.ToString();

                    if (string.IsNullOrWhiteSpace(value))
                        return false;

                    value = value.Trim().ToLowerInvariant();

                    return value == "true" || value == "yes" || value == "1";

                }

                return false;
            }
            catch
            {
                // Never break plugin because of tracing
                return false;
            }
        }
    }




    /// <summary>
    /// class to store the Opportunity product details needed in the Add Product package component functionality
    /// </summary>
    public class OppProdSeasonInfo
    {
        public Guid OpportunityProductId { get; set; }
        public string SeasonName { get; set; }
    }

    public class AgreementOpportunityData
{
        public string AgreementId { get; set; }
    
        public string FirstOppSeasonId { get; set; }  
        
        public string UniqueGuid { get; set; }
        public List<string> Opportunities { get; set; }
        public List<OppProdSeasonInfo> SeasonOppProducts { get; set; }

        public bool isPackageLineId { get; set; }

    }
    public class AgreementCartAction : CodeActivity
    {
       
        [Input("actionName")]
        public InArgument<string> actionName { get; set; }



        // get selected seasons from the input parameters
        [Input("seasonIds")]
        public InArgument<string> SeasonIds { get; set; }

        [Input("opportunity")]
        public InArgument<string> opportunity { get; set; }

        //Sunny(19-Nov-2025)
        [Input("packageLineId")]
        public InArgument<string> PackageLineId { get; set; }

        [Input("agreementId")]
        public InArgument<string> AgreementId { get; set; }

        [Input("ResetOverride")]
        public InArgument<bool> ResetOverride { get; set; }

        [Input("ProductId")]
        public InArgument<string> ProductId { get; set; }

        [Input("ProductName")]
        public InArgument<string> ProductName { get; set; }

        [Input("ProductFamily")]
        public InArgument<string> ProductFamily { get; set; }

        [Input("ProductSubFamily")]
        public InArgument<string> ProductSubFamily { get; set; }

        [Input("pricingMode")]
        public InArgument<string> PricingMode { get; set; }

        [Input("Division")]
        public InArgument<string> Division { get; set; }

        [Input("IsPassthroughCost")]
        public InArgument<bool> IsPassthroughCost { get; set; }

        [Input("RateType")]
        public InArgument<string> RateType { get; set; }

        [Input("Rate")]
        public InArgument<decimal> Rate { get; set; }

        [Input("RateId")]
        public InArgument<string> RateId { get; set; }

        [Input("LockRate")]
        public InArgument<bool> LockRate { get; set; }

        [Input("HardCost")]
        public InArgument<decimal> HardCost { get; set; }

        [Input("LockHardCost")]
        public InArgument<bool> LockHardCost { get; set; }

        [Input("ProductionCost")]
        public InArgument<decimal> ProductionCost { get; set; }

        [Input("LockProductionCost")]
        public InArgument<bool> LockProductionCost { get; set; }

        [Input("QuantityAvailable")]
        public InArgument<int> QuantityAvailable { get; set; }

        [Input("QtyUnits")]
        public InArgument<int> QtyUnits { get; set; }

        [Input("QtyEvents")]
        public InArgument<int> QtyEvents { get; set; }

        [Output("AgreementCartActionbatchingActionName")]
        public OutArgument<string> AgreementCartActionbatchingActionName { get; set; }  

        [Output("isAddProductBatching")]
        public OutArgument<bool> isAddProductBatching { get; set; }

        [Output("isTotalDealvalueFromAddProduct")]
        public OutArgument<bool> isTotalDealvalueFromAddProduct { get; set; }

        [Output("AddProductOpportunityGuid")]
        public OutArgument<string> AddProductOpportunityGuid { get; set; }

        [Output("DeleteRecalOppLines")]
        public OutArgument<string> DeleteRecalOppLines { get; set; }

        [Output("NewAddProductOpportunityGuid")]
        public OutArgument<string> NewAddProductOpportunityGuid { get; set; }

        [Input("AddProductRecalOppLinesActionName")]
        public InArgument<string> AddProductRecalOppLinesActionName { get; set; }

        [Input("InputAddProductOpportunityGuid")]
        public InArgument<string> InputAddProductOpportunityGuid { get; set; }

        [Input("NewInputAddProductOpportunityGuid")]
        public InArgument<string> NewInputAddProductOpportunityGuid { get; set; }

        [Output("response")]
        public OutArgument<string> response { get; set; }

        [Output("TestDelete")]
        public OutArgument<bool> TestDelete { get; set; }



        [Output("FunctionalityActionName")]
        public OutArgument<string> FunctionalityActionName { get; set; }

        [Output("AddProductFirstOppSeason")]
        public OutArgument<string> AddProductFirstOppSeason { get; set; }

        [Output("AgreementOpportunityUniqueGuid")]
        public OutArgument<string> AgreementOpportunityUniqueGuid { get; set; }

        [Input("AgreementOpportunityUniqueGuid")]
        public InArgument<string> InputAgreementOpportunityUniqueGuid { get; set; }

        [Input("InputAddProductFirstOppSeason")]
        public InArgument<string> InputAddProductFirstOppSeason { get; set; }

        [Output("DeleteopportunityGuids")]
        public OutArgument<string> DeleteopportunityGuids { get; set; }
        
        [Output("DeleteopportunityFirstGuids")]
        public OutArgument<string> DeleteopportunityFirstGuids { get; set; }
        
        [Output("DeleteRecalActionName")]
        public OutArgument<string> DeleteRecalActionName { get; set; }

        [Input("InputDeleteopportunityGuids")]
        public InArgument<string> InputDeleteopportunityGuids { get; set; }

        [Input("OppProdId")]
        public InArgument<string> OppProdId { get; set; }

        [Input("id")]
        public InArgument<string> id { get; set; }

        [Input("OverwriteLegalDefinition")]
        public InArgument<bool> OverwriteLegalDefinition { get; set; }

        [Input("LegalDefinition")]
        public InArgument<string> LegalDefinition { get; set; }

        [Input("EscalateActionName")]
        public InArgument<string> EscalateActionName { get; set; }


        [Input("Description")]
        public InArgument<string> Description { get; set; }

        [Input("escalationType")]
        public InArgument<string> EscalationType { get; set; }

        [Input("totalValue")]
        public InArgument<decimal> totalValue { get; set; }

        [Input("automaticAmount")]
        public InArgument<decimal> AutomaticAmount { get; set; }

        [Input("escalationValue")]
        public InArgument<decimal> EscalationValue { get; set; }


        [Input("manualAmount")]
        public InArgument<decimal> ManualAmount { get; set; }

        [Input("BarterAmount")]
        public InArgument<decimal> BarterAmount { get; set; }


        //[Input("IsNewAddProductCreateOLI")]
        //public InArgument<bool> IsNewAddProductCreateOLI { get; set; }


        [Output("AddProductEscalateTotalDealAgreement")]
        public OutArgument<string> AddProductEscalateTotalDealAgreement { get; set; }


        [Output("isDeleteRecalOppLines")]
        public OutArgument<bool> isDeleteRecalOppLines { get; set; }

       

        [Input("PackageComponents")]
        public InArgument<string> PackageComponents { get; set; }



        protected override void Execute(CodeActivityContext context)
        {
            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            ITracingService tracingService = context.GetExtension<ITracingService>();

            string functionName = "Execute";
            string inputActionName = actionName.Get(context);
            //bool isNewAddProductCreateOLI = bool.TryParse(IsNewAddProductCreateOLI.Get(context).ToString(), out bool parsedValue) && parsedValue;
            //tracingService.Trace($"isNewAddProductCreateOLI: {isNewAddProductCreateOLI}");
            //bool fromActionAddProduct = false;
            try
            {
                // Initialize tracing ONCE
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Tracing initialized");

                TraceHelper.Trace( tracingService,"functionName: {0}", functionName);

                string inpuAgreementActionName = actionName.Get(context) != null ? actionName.Get(context).ToString() : string.Empty;
                string inputRecalActionName = AddProductRecalOppLinesActionName.Get(context) != null ? AddProductRecalOppLinesActionName.Get(context).ToString() : string.Empty;

                //TraceHelper th = new TraceHelper();



                if (inpuAgreementActionName == null || inputRecalActionName == null)
                {
                    TraceHelper.Trace(tracingService, "Either inputActionName or inputRecalActionName is null, exiting the Execute method.");
                    return;
                }


                //Agilitek-SHolmes(23-07-25)
                #region Getting available seasons by product 
                if (inputActionName == "GetAvailableSeasons")
                {
                    TraceHelper.Trace(tracingService, "Entered in Get Available Seasons.");
                    Logging.Log("Action name is GetAvailableSeasons.", tracingService);

                    // Safely get input values
                    string inputProductId = ProductId.Get(context)?.ToString() ?? string.Empty;
                    string inputSeasonIds = SeasonIds.Get(context)?.ToString() ?? string.Empty;

                    Logging.Log($"productId: {inputProductId}", tracingService);
                    Logging.Log($"seasonIds: {inputSeasonIds}", tracingService);

                    string availableSeasons = GetAvailableSeasons(inputProductId, inputSeasonIds, tracingService, service);

                    response.Set(context, availableSeasons);
                    TestDelete.Set(context, true);

                    TraceHelper.Trace(tracingService, "test delete set.");
                    TraceHelper.Trace(tracingService, "response : {0}", response.Get(context));
                    TraceHelper.Trace(tracingService, "Exit from Get Available Seasons.");

                    isAddProductBatching.Set(context, false);
                    return;
                }

                #endregion


                #region getting the variables from input parameters and assigning to the model class 
                #region Retrieve Input Parameters Safely

                #region Safe reads from workflow InArguments (non-nullable value types)

                string seasonIds = SeasonIds.Get(context) ?? string.Empty;
                string productId = ProductId.Get(context) ?? string.Empty;
                string productName = ProductName.Get(context) ?? string.Empty;
                string productFamily = ProductFamily.Get(context) ?? string.Empty;
                string productSubFamily = ProductSubFamily.Get(context) ?? string.Empty;
                string division = Division.Get(context) ?? string.Empty;
                // bool isPassthroughCost = IsPassthroughCost.Get(context);

                string inputRateType = RateType.Get(context) ?? string.Empty;
                decimal inputRate = Rate.Get(context);                 // default 0m if not set

                string rateId = RateId.Get(context) ?? string.Empty;
                bool lockRate = LockRate.Get(context);              // default false

                decimal hardCost = HardCost.Get(context);              // default 0m
                bool lockHardCost = LockHardCost.Get(context);          // default false

                decimal productionCost = ProductionCost.Get(context);        // default 0m
                bool lockProductionCost = LockProductionCost.Get(context);    // default false

                int quantityAvailable = QuantityAvailable.Get(context);     // default 0
                int qtyUnits = QtyUnits.Get(context);              // default 0
                int qtyEvents = QtyEvents.Get(context);             // default 0

                string oppProduct = id.Get(context) ?? string.Empty;
                string agreementId = AgreementId.Get(context) ?? string.Empty;
                string opportuniity = opportunity.Get(context) ?? string.Empty;
                string description = Description.Get(context) ?? string.Empty;

                decimal totalValueOppProd = totalValue.Get(context);            // default 0m

                bool resetOverride = ResetOverride.Get(context);         // default false
                string legalDefinition = LegalDefinition.Get(context) ?? string.Empty;
                bool overwriteLegalDefinition = OverwriteLegalDefinition.Get(context); // default false
                string pricingMode = PricingMode.Get(context) ?? string.Empty;
                string escalationType = EscalationType.Get(context) ?? string.Empty;
                decimal escalationValue = EscalationValue.Get(context); // default 0m
                decimal manualAmount = ManualAmount.Get(context); // default 0m
                decimal barterAmount = BarterAmount.Get(context); // default 0m
                string esclateActionName = inputActionName;
                decimal automaticAmount = AutomaticAmount.Get(context); // default 0m
                string packageLineItemIdAddProduct = PackageLineId.Get(context) ?? string.Empty;

                #endregion


                #endregion

                TraceHelper.Trace(tracingService, "Input Parameters:SeasonIds:{0},ProductId:{1},ProductName:{2},ProductFamily:{3},ProductSubFamily:{4},Division:{5},Rate:{6},RateId:{7},LockRate:{8},HardCost:{9},LockHardCost:{10},ProductionCost:{11},LockProductionCost:{12},QuantityAvailable:{13},QtyUnits:{14},QtyEvents:{15},AgreementId:{16},ActionName:{17}", seasonIds, productId, productName, productFamily, productSubFamily, division, inputRate, rateId, lockRate, hardCost, lockHardCost, productionCost, lockProductionCost, quantityAvailable, qtyUnits, qtyEvents, agreementId, inputActionName);

                #endregion

                InventoryData inventoryData = new InventoryData();
                Logging.Log($"inventoryData instance creation", tracingService);

                inventoryData.ProductId = productId;
                inventoryData.ProductName = productName;
                inventoryData.ProductFamily = productFamily;
                inventoryData.ProductSubFamily = productSubFamily;
                inventoryData.Division = division;
                //inventoryData.IsPassthroughCost = isPassthroughCost;
                inventoryData.RateType = inputRateType;
                inventoryData.Rate = inputRate;
                inventoryData.RateId = rateId;
                inventoryData.LockRate = lockRate;
                inventoryData.HardCost = hardCost;
                inventoryData.LockHardCost = lockHardCost;
                inventoryData.ProductionCost = productionCost;
                inventoryData.LockProductionCost = lockProductionCost;
                inventoryData.QuantityAvailable = quantityAvailable;
                inventoryData.QtyUnits = qtyUnits;
                inventoryData.QtyEvents = qtyEvents;

                tracingService.Trace($"inventoryData values assigned");


                List<string> opportunityGuids = new List<string>();

                #region Add Product functionality Initial 
                if (inputActionName == "AddProduct")
                {
                    tracingService.Trace("function Add Product");

                    string fetchXml = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                                          <entity name='opportunity'>
                                            <attribute name='opportunityid' />
                                            <attribute name='ats_startseason' />
                                            <attribute name='name' />
                                            <order attribute='ats_startseason' descending='false' />
                                            <filter type='and'>
                                              <condition attribute='ats_agreement' operator='eq' value='" + agreementId + @"' />
                                            </filter>
                                          </entity>
                                        </fetch>";

                    EntityCollection opportunityCollection = service.RetrieveMultiple(new FetchExpression(fetchXml));

                    Logging.Log($"done query expression", tracingService);
                    // Guid firstOppSeasonId = new Guid();
                    EntityReference firstOppSeason = null;
                    int count = 0;
                    //decimal totalAgreementDealValue = 0;

                    //calling the function to create the new GUID.
                    string uniqueGuid = UniqueGuidGeneration(service, tracingService);
                    AgreementOpportunityUniqueGuid.Set(context, uniqueGuid);

                    TraceHelper.Trace(tracingService, "Unique Guid generated: {0}", uniqueGuid);


                    //tracingService.Trace($"Unique Guid generated: {uniqueGuid}");

                    foreach (Entity opportunity in opportunityCollection.Entities)
                    {
                        Logging.Log($"foreach inside", tracingService);

                        opportunityGuids.Add(opportunity.Id.ToString());

                        TraceHelper.Trace(tracingService, "Opportunity: {0}, is added in the list", opportunity.Id);

                        if (count == 0)
                        {
                            firstOppSeason = opportunity.Attributes["ats_startseason"] as EntityReference;
                        }
                        //AddProductFirstOppSeason.Set(context, firstOppSeason.Id.ToString());
                        count++;
                    }
                    TraceHelper.Trace(tracingService, "firstOppSeason: {0}", firstOppSeason.Id);


                    List<OppProdSeasonInfo> seasonOppProdList = new List<OppProdSeasonInfo>();
                    //Sunny(23-Nov-2025)
                    bool isPackageLineId = false; 
                    #region HAndle the package Line item id, when the "Add New Package Component" button is clicked
                    if (packageLineItemIdAddProduct != string.Empty)
                    {
                        Guid packageLineItemIdAddProductGuid = Guid.Empty;
                        Guid.TryParse(packageLineItemIdAddProduct, out packageLineItemIdAddProductGuid);
                        TraceHelper.Trace(tracingService, "Retrieved package Line item id: {0}", packageLineItemIdAddProduct);

                        //retreving the AgreementOpportunity Product Id
                        Entity oppProductAgreementOppProductObj = service.Retrieve("opportunityproduct", packageLineItemIdAddProductGuid, new ColumnSet("ats_agreementopportunityproduct", "productid", "ats_agreementopportunityproduct"));
                        string AgreementOppProdId = oppProductAgreementOppProductObj.Contains("ats_agreementopportunityproduct") ? oppProductAgreementOppProductObj.GetAttributeValue<string>("ats_agreementopportunityproduct") : string.Empty;
                        EntityReference productRef = oppProductAgreementOppProductObj.Contains("productid") ? (EntityReference)oppProductAgreementOppProductObj["productid"] : null;
                        Guid productIdPackage = productRef!=null ? productRef.Id : Guid.Empty;

                        tracingService.Trace($"productIdPackage: {productIdPackage}");

                        TraceHelper.Trace( tracingService,"For packageLineItemIdAddProductGuid: {0}, AgreementOppProdId: {1}",packageLineItemIdAddProductGuid,AgreementOppProdId);

                        if (AgreementOppProdId != string.Empty)
                        {
                            var fetch = $@"
                                        <fetch>
                                          <entity name='opportunityproduct'>
                                            <attribute name='opportunityproductname' />
                                            <attribute name='productname' />
                                            <attribute name='opportunityproductid' />

                                            <filter>
                                              <condition attribute='ats_agreementopportunityproduct' operator='eq' value='{AgreementOppProdId}' />
                                            </filter>

                                            <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='inner' alias='IBS'>
                                              <link-entity name='ats_season' from='ats_seasonid' to='ats_season' link-type='inner' alias='Season'>
                                                <attribute name='ats_name' />
                                                <order attribute='ats_name' />
                                              </link-entity>
                                            </link-entity>

                                            <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Product'>
                                              <filter>
                                                <condition attribute='productid' operator='eq' value='{productIdPackage}' />
                                              </filter>
                                            </link-entity>

                                            <link-entity name='opportunity' from='opportunityid' to='opportunityid' link-type='inner' alias='Opp'>
                                              <link-entity name='ats_agreement' from='ats_agreementid' to='ats_agreement' link-type='inner' alias='Agreement'>
                                                <filter>
                                                  <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                                                </filter>
                                              </link-entity>
                                            </link-entity>

                                          </entity>
                                        </fetch>";
                            var results = service.RetrieveMultiple(new FetchExpression(fetch));
                            foreach (var e in results.Entities)
                            {
                                Guid oppProductId = e.Id;

                                string seasonName = e.Attributes.Contains("Season.ats_name")
                                    ? (string)((AliasedValue)e["Season.ats_name"]).Value
                                    : null;

                                TraceHelper.Trace( tracingService,"OppProductId: {0}, SeasonName: {1}", oppProductId,seasonName);


                                // --- Add into list ---
                                seasonOppProdList.Add(new OppProdSeasonInfo
                                {
                                    OpportunityProductId = oppProductId,
                                    SeasonName = seasonName
                                });

                                TraceHelper.Trace(tracingService, "OppProductId: {0}, SeasonName: {1}", oppProductId, seasonName);
                            }

                            // Optional: trace the count
                            TraceHelper.Trace( tracingService,"Total items added to list: {0}", seasonOppProdList.Count);

                            isPackageLineId = true; 

                        }
                        else
                        {
                            TraceHelper.Trace( tracingService, "AgreementOppProdId: {0} is not present in any other Opp Prod",AgreementOppProdId);

                        }

                    }
                    #endregion


                    #region binding the opportunity Guid and the agreement Id to the output parameter
                    if (opportunityGuids != null && opportunityGuids.Count > 0 && agreementId != null)
                    {
                        AgreementOpportunityData data = new AgreementOpportunityData()
                        {
                            AgreementId = agreementId.ToString(),
                            Opportunities = opportunityGuids,
                            FirstOppSeasonId = firstOppSeason.Id.ToString(),
                            UniqueGuid = uniqueGuid,
                            SeasonOppProducts = seasonOppProdList, 
                            isPackageLineId = isPackageLineId
                        };

                        // Serialize using System.Text.Json
                        string serializedData = JsonSerializer.Serialize(data);

                        AddProductOpportunityGuid.Set(context, serializedData);
                        TraceHelper.Trace(tracingService, "AddProductOpportunityGuids is set as JSON string: {0}", serializedData);


                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "No opportunities found or agreementId is null.");

                    }
                    #endregion

                    AgreementCartActionbatchingActionName.Set(context, "AddProductBatching");
                    TraceHelper.Trace(tracingService, "AgreementCartActionbatchingActionName is set to AddProductRecalOppLines");

                    isAddProductBatching.Set(context, true);
                    TraceHelper.Trace(tracingService, "Add Product Logic is implemented");

                    return;
                }
                #endregion

                #region Add Product -  calculating the total Escalate revenue and total deal value of the Agreement
                //this validation is for calculating the total Escalate revenue and total deal value of the Agreement
                if (inputActionName == "AddProductEscalateTotalDealAgreement")
                {
                    TraceHelper.Trace(tracingService, "Execution of  AddProductEscalateTotalDealAgreement");
                    string inputJson = InputAddProductOpportunityGuid.Get(context) ?? string.Empty;

                    //string uniqueGuid = InputAgreementOpportunityUniqueGuid.Get(context);
                    TraceHelper.Trace(tracingService, "inputJson: {0}", inputJson);
                    AgreementOpportunityData data = null;
                    //string firstOpportunityId = string.Empty;
                    TraceHelper.Trace(tracingService, "Inside AddProductEscalateTotalDealAgreement");
                    if (inputJson != null)
                    {
                        TraceHelper.Trace(tracingService, "inputjson:{0}", inputJson);
                        //Validating and extracting the Guid
                        data = JsonSerializer.Deserialize<AgreementOpportunityData>(inputJson);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "inputjson is null");
                        //Validating and extracting the Guid
                    }
                    //retrieving the agreement Id from the input json
                    string retrieveAgreementId = data != null ? data.AgreementId : null;

                    //proceeding for the total escalate revenue of the opportunities present in the agreement.
                    TraceHelper.Trace(tracingService, "proceeding for the total escalate revenue of the opportunities present in the agreement.");


                    #region declaring the necessary fields passed to call the escalation across all year functionality
                    //declaring flag to know, that the Escalation across all year is called from add product functionality
                    bool isAddProdEscalationAllYear = true;
                    //string agreementId = context.InputParameters.Contains("agreementId") ? context.InputParameters["agreementId"].ToString() : string.Empty;
                    Guid agreementIdd = Guid.TryParse(retrieveAgreementId, out Guid parsedAgreementId) ? parsedAgreementId : Guid.Empty;
                    TraceHelper.Trace(tracingService, "agreementIdd: {0}", agreementIdd);
                    string esclateActionNamee = "AddProduct";
                    string esclationType = string.Empty;
                    decimal esclationValue = 0;
                    #endregion

                    TotalEsclateRevenue escalateRevenue = new TotalEsclateRevenue();
                    //escalateRevenue.calTotalEscRevenue();
                    escalateRevenue.calTotalEscRevenue(isAddProdEscalationAllYear, esclateActionNamee, agreementIdd, esclationType, esclationValue, service, tracingService);
                    #region updating the total deal value of Agreement

                    totalDealValueAgreement(retrieveAgreementId, tracingService, service);
                    response.Set(context, "Sucessfull"); 
                    #endregion

                    TraceHelper.Trace(tracingService, "Logic executed for the 'AddProductEscalateTotalDealAgreement'");
                    return;
                }

                #endregion

                #region deleting the opportunity products from the opportunities
                if (inputActionName == "Delete" || inputActionName == "DeleteRecalOppLines")
                {
                    Logging.Log($"Action name is delete.", tracingService);

                    Guid deletProductAgreementId = Guid.Empty;

                    //retreving the prod json from the input parameter
                    //retreving the prod json from the input parameter
                    string oppProdIDJsonInput = OppProdId.Get(context) ?? string.Empty;
                    TraceHelper.Trace(tracingService, "oppProdIDJsonInput: {0}", oppProdIDJsonInput);

                    string deleteAgreementId = string.Empty;

                    int depth = workflowContext.Depth;
                    //calling the function to delete the Oppportunity Product
                    DeleteOpportunityProducts(depth, context, ref deleteAgreementId, oppProdIDJsonInput, service, tracingService);

                    TraceHelper.Trace(tracingService, "return from Delete product fucntionaltiy");
                    return;

                }

                #endregion

                #region Delete functionality - Calculate Total Escalate 

                if (inputActionName == "DeleteCalculateTotalEscalate")
                {
                    TraceHelper.Trace(tracingService, "Entered in DeleteCalculateTotalEscalate");

                    // retrieving the agreement Id from the input json
                    string inputJson = InputDeleteopportunityGuids.Get(context) ?? string.Empty;
                    TraceHelper.Trace(tracingService, "inputJson: {0}", inputJson);

                    // deserialize the input json
                    List<Guid> data = JsonSerializer.Deserialize<List<Guid>>(inputJson);

                    // retrieving the opportunity Id from the input json
                    Guid retrieveOpportunityId = data[0];
                    TraceHelper.Trace(tracingService, "retrieveAgreementId: {0}", retrieveOpportunityId);

                    // retrieving the AgreementId
                    Entity agreement = service.Retrieve("opportunity", retrieveOpportunityId, new ColumnSet("ats_agreement"));
                    EntityReference agreementObj = agreement.Contains("ats_agreement")
                        ? agreement.GetAttributeValue<EntityReference>("ats_agreement")
                        : null;

                    Guid agreementIdDelete = Guid.Empty;
                    if (agreementObj != null)
                    {
                        TraceHelper.Trace(tracingService, "agreementObj is not null");
                        agreementIdDelete = agreementObj.Id;
                    }

                    #region calling the Escalation Across all year
                    #region declaring the necessary fields passed to call the escalation across all year functionality

                    // declaring flag to know, that the Escalation across all year is called from add product functionality
                    bool isDeleteProdEscalationAllYear = true;

                    TraceHelper.Trace(tracingService, "agreementIdd: {0}", agreementIdDelete);

                    string esclateActionNamee = "Delete";
                    string esclationType = string.Empty;
                    decimal esclationValue = 0;

                    #endregion

                    // Calling the Escalation Across all year functionality
                    TotalEsclateRevenue escalateRevenue = new TotalEsclateRevenue();
                    escalateRevenue.calTotalEscRevenue(
                        isDeleteProdEscalationAllYear,
                        esclateActionNamee,
                        agreementIdDelete,
                        esclationType,
                        esclationValue,
                        service,
                        tracingService
                    );
                    #endregion

                    TraceHelper.Trace(tracingService, "returning from the delete calculate total escalate condition");
                    return;
                }


                #endregion

                #region Add Product Batching Logic implementation
                if (inputActionName == "AddProductBatching")
                {
                    var getOpportunityProducts = new GetOpportunityProducts();

                    string inputJson = InputAddProductOpportunityGuid.Get(context);
                    TraceHelper.Trace(tracingService, "Inside AddProductBatching");

                    if (string.IsNullOrWhiteSpace(inputJson))
                        return;

                    AgreementOpportunityData data = JsonSerializer.Deserialize<AgreementOpportunityData>(inputJson);

                    if (data == null || data.Opportunities == null || data.Opportunities.Count == 0)
                        return;

                    // ------------------------------------------------------------------
                    // ONE-TIME PREP
                    // ------------------------------------------------------------------

                    if (string.IsNullOrWhiteSpace(data.AgreementId))
                        throw new InvalidPluginExecutionException("AgreementId missing in input JSON.");

                    Guid agreementIdBatch = new Guid(data.AgreementId);

                    Entity agreementBatch = service.Retrieve(
                        "ats_agreement",
                        agreementIdBatch,
                        new ColumnSet("ats_startseason", "ats_bpfstatus")
                    );

                    EntityReference agreementStartSeasonRefBatch =
                        agreementBatch.GetAttributeValue<EntityReference>("ats_startseason");

                    if (agreementStartSeasonRefBatch == null)
                        throw new InvalidPluginExecutionException("Agreement Start Season not found.");

                    Guid agreementStartSeasonIdBatch = agreementStartSeasonRefBatch.Id;

                    int bpfStatusValueBatch =
                        agreementBatch.GetAttributeValue<OptionSetValue>("ats_bpfstatus")?.Value ?? 0;

                    // First opportunity in batch
                    Guid opportunityIdBatch = new Guid(data.Opportunities[0]);

                    Entity opportunityBatch = service.Retrieve(
                        "opportunity",
                        opportunityIdBatch,
                        new ColumnSet("ats_startseason")
                    );

                    EntityReference oppSeasonRefBatch =
                        opportunityBatch.GetAttributeValue<EntityReference>("ats_startseason");

                    if (oppSeasonRefBatch == null)
                        throw new InvalidPluginExecutionException("Opportunity Start Season missing.");

                    // First Opp Season (used for IBS cloning)
                    Guid firstOppSeasonIdGuidBatch = Guid.Empty;
                    if (!string.IsNullOrWhiteSpace(data.FirstOppSeasonId))
                        Guid.TryParse(data.FirstOppSeasonId, out firstOppSeasonIdGuidBatch);

                    // Unique Guid (TEXT field)
                    string uniqueGuidFromDataBatch = string.IsNullOrWhiteSpace(data.UniqueGuid) ? null : data.UniqueGuid.Trim();

                    // Parse seasonIds ONCE
                    var seasonSetBatch = new HashSet<Guid>();
                    if (!string.IsNullOrWhiteSpace(seasonIds))
                    {
                        foreach (var s in seasonIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            Guid g;
                            if (Guid.TryParse(s.Trim(), out g))
                                seasonSetBatch.Add(g);
                        }
                    }


                    // UOM ONCE
                    Guid uomIdBatch = GetUnitOfMeasure(service, "Unit_of_Measure", tracingService);
                    EntityReference uomRefBatch = (uomIdBatch != Guid.Empty) ? new EntityReference("uom", uomIdBatch) : null;

                    // Caches ONCE
                    var ibsCacheBatch = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase); // season|product -> IBS
                    TraceHelper.Trace(tracingService, "IBS cached");

                    var rateCacheBatch = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);        // ibs|ratetype -> rate
                    TraceHelper.Trace(tracingService, "Rate cached");

                    // ------------------------------------------------------------------
                    // PACKAGE TEMPLATE CHECK (primary product)
                    // ------------------------------------------------------------------
                    bool isPackageProductBatch = false;
                    Guid primaryPackageTemplateIdBatch = Guid.Empty;

                    if (inventoryData == null || string.IsNullOrWhiteSpace(inventoryData.ProductId))
                        throw new InvalidPluginExecutionException("inventoryData.ProductId missing.");

                    Guid primaryProductIdBatch = new Guid(inventoryData.ProductId);

                    Guid packageRateIdBatch = GetPackageRateIdForProductSeason(
                        service,
                        agreementStartSeasonIdBatch,
                        primaryProductIdBatch
                    );

                    bool isPackageTemplateFromAgreement = false;
                    Guid packageTemplateId = Guid.Empty; 
                    if (packageRateIdBatch != Guid.Empty)
                    {
                         TraceHelper.Trace(tracingService, "packageRateIdBatch: {0}", packageRateIdBatch);
                        primaryPackageTemplateIdBatch = GetPrimaryPackageTemplateId(
                            service,
                            packageRateIdBatch,
                            agreementStartSeasonIdBatch
                        );
                        TraceHelper.Trace(tracingService, "primaryPackageTemplateIdBatch: {0}", primaryPackageTemplateIdBatch);

                        if (primaryPackageTemplateIdBatch != Guid.Empty)
                            isPackageProductBatch = true;
                        
                        if (primaryPackageTemplateIdBatch == Guid.Empty) // this case can be occur when the package doesnt have any component product, and added in Agreement cart. 
                        {
                            TraceHelper.Trace(tracingService, "Product belongs to package, but package template is missing, Proceeding for Package template creation");

                            Entity PackageTemplate = new Entity("ats_packagetemplate");

                            Guid packageRateId = packageRateIdBatch;
                            //Guid componentRateId = Guid.Parse(packageTemplateRecord.ComponentRateId);

                            PackageTemplate["ats_packagerateid"] = new EntityReference("ats_rate", packageRateId);
                            //PackageTemplate["ats_componentrateid"] = new EntityReference("ats_rate", componentRateId);
                            //PackageTemplate["ats_quantity"] = packageTemplateRecord.QtyUnits;
                            //PackageTemplate["ats_quantityofevents"] = packageTemplateRecord.QtyEvents;
                            try
                            {
                                tracingService.Trace($"functionName: {functionName}");
                                tracingService.Trace("Before Create");
                                packageTemplateId = service.Create(PackageTemplate);
                                tracingService.Trace("After Create");
                                tracingService.Trace($"Component added sucessfully");
                                isPackageTemplateFromAgreement = true; 
                            }
                            catch (Exception ex)
                            {
                                tracingService.Trace("Before Exception " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
                            }

                            TraceHelper.Trace(tracingService, "Package template record created :{0}", packageTemplateId); 
                        }

                    }

                   



                        // ------------------------------------------------------------------
                        // MAIN OLI: Ensure IBS + Rate (cached) + Create (service.Create)
                        // ------------------------------------------------------------------
                        int primaryRateTypeBatch =
                        (inventoryData.RateType == "Season") ? 114300000 : 114300001;

                    EntityReference primaryIbsRefBatch =EnsureInventoryForSeasonOptimized(
                            oppSeasonRefBatch.Id,
                            primaryProductIdBatch,
                            firstOppSeasonIdGuidBatch,
                            service,
                            tracingService,
                            ibsCacheBatch,
                            null
                        );

                    Entity primaryRateBatch = GetRateCached(service, rateCacheBatch, primaryIbsRefBatch.Id, primaryRateTypeBatch);
                    if (primaryRateBatch == null)
                    {
                        throw new InvalidPluginExecutionException(
                            "Rate not found for IBS (primary). IBS=" + primaryIbsRefBatch.Id + ", RateType=" + primaryRateTypeBatch);
                    }

                    Money primaryPriceBatch = primaryRateBatch.GetAttributeValue<Money>("ats_price") ?? new Money(0m);

                    int primaryQtyUnitsBatch = seasonSetBatch.Contains(oppSeasonRefBatch.Id) ? inventoryData.QtyUnits : 0;

                    Entity mainOppProd = new Entity("opportunityproduct");
                    mainOppProd["opportunityid"] = new EntityReference("opportunity", opportunityIdBatch);
                    mainOppProd["productid"] = new EntityReference("product", primaryProductIdBatch);
                    mainOppProd["ats_inventorybyseason"] = primaryIbsRefBatch;
                    mainOppProd["ats_rate"] = primaryRateBatch.ToEntityReference();

                    if (!string.IsNullOrWhiteSpace(uniqueGuidFromDataBatch))
                        mainOppProd["ats_agreementopportunityproduct"] = uniqueGuidFromDataBatch;

                    if (uomRefBatch != null)
                        mainOppProd["uomid"] = uomRefBatch;
                    TraceHelper.Trace(tracingService, $"primaryPriceBatch:{primaryPriceBatch}, primaryQtyUnitsBatch: {primaryQtyUnitsBatch}, inventoryData.QtyEvents: {inventoryData.QtyEvents}, inventoryData.HardCost {inventoryData.HardCost}");

                    mainOppProd["priceperunit"] = primaryPriceBatch;
                    mainOppProd["ats_sellingrate"] = primaryPriceBatch;
                    mainOppProd["ats_quantity"] = primaryQtyUnitsBatch;
                    mainOppProd["ats_quantityofevents"] = inventoryData.QtyEvents;
                    mainOppProd["ats_hardcost"] = new Money(inventoryData.HardCost);

                    if (isPackageProductBatch && primaryPackageTemplateIdBatch != Guid.Empty)
                    {
                        mainOppProd["ats_packagetemplate"] = new EntityReference("ats_packagetemplate", primaryPackageTemplateIdBatch);

                    }
                    else if (isPackageTemplateFromAgreement && !data.isPackageLineId)
                    {
                        mainOppProd["ats_packagetemplate"] = new EntityReference("ats_packagetemplate", packageTemplateId);
                        TraceHelper.Trace(tracingService, "packageTemplateId from isPackageTemplateFromAgreement: {0}", packageTemplateId);
                    }

                    Guid pkgLineId = Guid.Empty;
                    TraceHelper.Trace(tracingService, "data.SeasonOppProducts.Count: {0}", data.SeasonOppProducts.Count); 
                    // Optional package line item from data.SeasonOppProducts[0]
                    if (data.SeasonOppProducts != null && data.SeasonOppProducts.Count > 0)
                    {
                        
                        pkgLineId = data.SeasonOppProducts[0].OpportunityProductId;
                        if (pkgLineId != Guid.Empty && EntityExists(service, "opportunityproduct", pkgLineId))
                            mainOppProd["ats_packagelineitem"] = new EntityReference("opportunityproduct", pkgLineId);
                    
                        TraceHelper.Trace(tracingService, "Package line item reference set to: {0}", pkgLineId);

                    }

                    TraceHelper.Trace(tracingService, "Creating MAIN OLI...");
                    Guid createdMainOppProdId = service.Create(mainOppProd);
                    TraceHelper.Trace(tracingService, "Created MAIN OLI: {0}", createdMainOppProdId);

                    // Remove first package line item from list
                    if (data.SeasonOppProducts != null && data.SeasonOppProducts.Count > 0)
                        data.SeasonOppProducts.RemoveAt(0);

                    // ------------------------------------------------------------------
                    // COMPONENTS: Build + Create (service.Create) + IBS qty patches
                    // ------------------------------------------------------------------
                    TraceHelper.Trace(tracingService, "Processing package components...");
                    string rawJson = PackageComponents.Get(context) ?? "[]";
                    List<InventoryData> componentsBatch =
                        JsonSerializer.Deserialize<List<InventoryData>>(rawJson) ?? new List<InventoryData>();

                    TraceHelper.Trace(tracingService, "[Components] Deserialized {0} package components.", componentsBatch.Count);

                    // Deferred Rate clones (CreateMultiple works for ats_rate)
                    var deferredRateCreatesBatch = new List<Entity>();

                    // Aggregate IBS qty update: season|product|ratetype -> totals
                    var qtyAggBatch = new Dictionary<string, (int qtyUnits, int qtyEvents)>(StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < componentsBatch.Count; i++)
                    {
                        var comp = componentsBatch[i];
                        if (comp == null) continue;

                        comp.ProductId = comp.ProductId ?? string.Empty;
                        comp.ProductName = comp.ProductName ?? string.Empty;

                        Guid compProductId;
                        if (!Guid.TryParse(comp.ProductId, out compProductId))
                            continue;

                        int compRateType =
                            (comp.RateType == "Season") ? 114300000 :
                            (comp.RateType == "Individual") ? 114300001 : 0;

                        if (compRateType == 0)
                            throw new InvalidPluginExecutionException("Invalid RateType '" + comp.RateType + "' for product " + comp.ProductName + " (" + comp.ProductId + ").");

                        // Ensure IBS (cached)
                        EntityReference compIbsRef = EnsureInventoryForSeasonOptimized(
                            oppSeasonRefBatch.Id,
                            compProductId,
                            firstOppSeasonIdGuidBatch,
                            service,
                            tracingService,
                            ibsCacheBatch,
                            deferredRateCreatesBatch
                        );

                        // Rate (cached)
                        Entity compRate = GetRateCached(service, rateCacheBatch, compIbsRef.Id, compRateType);
                        if (compRate == null)
                        {
                            throw new InvalidPluginExecutionException(
                                "No rate found. IBS=" + compIbsRef.Id + ", RateType=" + compRateType + ", Product=" + comp.ProductName);
                        }

                        Money compPrice = compRate.GetAttributeValue<Money>("ats_price") ?? new Money(0m);

                        int compQtyUnits = seasonSetBatch.Contains(oppSeasonRefBatch.Id) ? comp.QtyUnits : 0;

                        // Build component OLI
                        Entity compOppProd = new Entity("opportunityproduct");
                        compOppProd["opportunityid"] = new EntityReference("opportunity", opportunityIdBatch);
                        compOppProd["productid"] = new EntityReference("product", compProductId);
                        compOppProd["ats_inventorybyseason"] = compIbsRef;
                        compOppProd["ats_rate"] = compRate.ToEntityReference();

                        if (!string.IsNullOrWhiteSpace(uniqueGuidFromDataBatch))
                            compOppProd["ats_agreementopportunityproduct"] = uniqueGuidFromDataBatch;

                        if (uomRefBatch != null)
                            compOppProd["uomid"] = uomRefBatch;

                        compOppProd["priceperunit"] = compPrice;
                        compOppProd["ats_sellingrate"] = compPrice;
                        compOppProd["ats_quantity"] = compQtyUnits;
                        compOppProd["ats_quantityofevents"] = comp.QtyEvents;
                        compOppProd["ats_hardcost"] = new Money(comp.HardCost);

                        // IMPORTANT: packagelineitem should reference MAIN OLI
                        if (createdMainOppProdId != Guid.Empty)
                            compOppProd["ats_packagelineitem"] = new EntityReference("opportunityproduct", createdMainOppProdId);

                        compOppProd["ats_manualpriceoverride"] = false;

                        if (primaryPackageTemplateIdBatch != Guid.Empty)
                            compOppProd["ats_packagetemplate"] = new EntityReference("ats_packagetemplate", primaryPackageTemplateIdBatch);

                        // Create component OLI immediately (per your preference)
                        Guid compOppProdGuid = service.Create(compOppProd);

                        TraceHelper.Trace(tracingService, $"comp Opp prod is created: {compOppProdGuid}"); 
                        // Aggregate qty for IBS update
                        string aggKey =
                            oppSeasonRefBatch.Id.ToString("N") + "|" +
                            compProductId.ToString("N") + "|" +
                            compRateType.ToString();

                        (int qtyUnits, int qtyEvents) agg;
                        if (!qtyAggBatch.TryGetValue(aggKey, out agg))
                            agg = (0, 0);

                        qtyAggBatch[aggKey] = (agg.qtyUnits + comp.QtyUnits, agg.qtyEvents + comp.QtyEvents);
                    }

                    // Create deferred cloned Rates (CreateMultiple)
                    if (deferredRateCreatesBatch.Count > 0)
                        CreateBulkInChunks(service, deferredRateCreatesBatch, 200);

                    TraceHelper.Trace(tracingService, "deferredRateCreatesBatch");
                    // ------------------------------------------------------------------
                    // IBS Qty update patches (UpdateMultiple, fallback to Update)
                    // ------------------------------------------------------------------
                    var ibsPatchesBatch = new List<Entity>();

                    foreach (var kvp in qtyAggBatch)
                    {
                        var parts = kvp.Key.Split('|');

                        Guid seasonIdAgg = Guid.ParseExact(parts[0], "N");
                        Guid productIdAgg = Guid.ParseExact(parts[1], "N");
                        int rateTypeAgg = int.Parse(parts[2]);

                        string ibsKey = seasonIdAgg.ToString("N") + "|" + productIdAgg.ToString("N");

                        EntityReference cachedIbsRef;
                        if (!ibsCacheBatch.TryGetValue(ibsKey, out cachedIbsRef))
                            continue;

                        var totals = kvp.Value;

                        Entity patch = BuildIbsQtyUpdatePatch(
                            cachedIbsRef.Id,
                            totals.qtyUnits,
                            totals.qtyEvents,
                            rateTypeAgg,
                            bpfStatusValueBatch,
                            service,
                            tracingService
                        );

                        if (patch != null)
                            ibsPatchesBatch.Add(patch);
                    }
                    TraceHelper.Trace(tracingService, "ibsPatchesBatch.Count: {0}", ibsPatchesBatch.Count);

                    if (ibsPatchesBatch.Count > 0)
                    {
                        try
                        {
                            UpdateBulkInChunks(service, ibsPatchesBatch, 200);
                            TraceHelper.Trace(tracingService, "UpdateMultiple for IBS patches succeeded.");
                        }
                        catch (Exception ex)
                        {
                            tracingService.Trace("UpdateMultiple failed, falling back to per-record Update. Error: {0}", ex.Message);
                            for (int i = 0; i < ibsPatchesBatch.Count; i++)
                                service.Update(ibsPatchesBatch[i]);
                        }
                    }

                    // ------------------------------------------------------------------
                    // Primary IBS pitched/sold update (your existing behavior)
                    // ------------------------------------------------------------------
                    UpdateQtyPitchedSoldForAddProduct(
                        inventoryData.QtyUnits,
                        inventoryData.QtyEvents,
                        primaryRateTypeBatch,
                        bpfStatusValueBatch,
                        oppSeasonRefBatch.Id,
                        primaryProductIdBatch,
                        service,
                        tracingService
                    );

                    TraceHelper.Trace(tracingService, "Updated pitched/sold for primary IBS.");
                    // ------------------------------------------------------------------
                    // Recalc opportunity lines (once)
                    // ------------------------------------------------------------------
                    OrganizationRequest actionRequest = new OrganizationRequest("ats_CalculateOpportunityLines");
                    actionRequest["OppurtunityEntityReference"] = new EntityReference("opportunity", opportunityIdBatch);
                    actionRequest["Action"] = "ReCalcOppLinesAgreement";
                    service.Execute(actionRequest);

                    TraceHelper.Trace(tracingService, "Recalculated opportunity lines.");
                    //Ttoal rate card Rollup (once)
                    try
                    {
                        service.Execute(new CalculateRollupFieldRequest
                        {
                            Target = new EntityReference("opportunity", opportunityIdBatch),
                            FieldName = "ats_totalratecard"
                        });
                    }
                    catch { }


                    #region Updating the total Components olis count (Sunny --> 20-02-26)
                    if (pkgLineId != Guid.Empty)
                    {
                        TraceHelper.Trace(tracingService, "Recalculating total component OLIS rollup for parent OLI(pkgLineId): {0}", pkgLineId);
                        RecalculatTotalComponentOLIeRollup(service, tracingService, pkgLineId);
                    }
                    else if (createdMainOppProdId !=Guid.Empty)
                    {
                        TraceHelper.Trace(tracingService, "Recalculating total component OLIS rollup for parent OLI(createdMainOppProdId): {0}", createdMainOppProdId);
                        RecalculatTotalComponentOLIeRollup(service, tracingService, createdMainOppProdId);
                    }
                    #endregion

                        TraceHelper.Trace(tracingService, "Updated total rate card rollup field.");
                    // ------------------------------------------------------------------
                    // Batching state update
                    // ------------------------------------------------------------------
                    data.Opportunities.RemoveAt(0);

                    TraceHelper.Trace(tracingService, "Remaining opportunities in batch: {0}", data.Opportunities.Count);
                    string updatedJson = JsonSerializer.Serialize(data);
                    AddProductOpportunityGuid.Set(context, updatedJson);
                    NewAddProductOpportunityGuid.Set(context, updatedJson);

                    if (data.Opportunities.Count > 0)
                    {
                        isAddProductBatching.Set(context, true);
                        AgreementCartActionbatchingActionName.Set(context, "AddProductBatching");
                    }
                    else
                    {
                        isAddProductBatching.Set(context, false);
                        AgreementCartActionbatchingActionName.Set(context, "AddProductEscalateTotalDealAgreement");
                        response.Set(context, "successfull");
                    }
                    tracingService.Trace("Exiting from the Add Product Batching Logic implementation");

                    return;
                }
                #endregion





                #region Proceeding for the Opportunity Product Update
                if (inputActionName == "updateOpportunityLineItem")
                {
                    //calling the function responsible for the Opportunity update
                    TraceHelper.Trace(tracingService, "Proceeding for the Opportunity Product Update");

                    UpdateOppLineItems(oppProduct, opportuniity, description, totalValueOppProd, hardCost, productionCost, qtyUnits, qtyEvents, inputRate, inputRateType, resetOverride, legalDefinition, overwriteLegalDefinition, tracingService, service
                    );

                    TraceHelper.Trace(tracingService, "Exiting from the validation of the update Opp Line item");
                    return;
                }
                #endregion



                #region Logic for Across all year Opportunities and Individual year Escalate Revenue
                //if (context.InputParameters.Contains("actionName"))
                //{
                //string esclateActionName = context.InputParameters.Contains("actionName") ? context.InputParameters["actionName"].ToString() : "";
                if (inputActionName == "RevenueEscalate") // Escalation across all year opp.
                {
                    string strAgreementId = agreementId;
                    TraceHelper.Trace(tracingService, "strAgreementId: {0}", strAgreementId);

                    Guid agreementIdd = Guid.TryParse(strAgreementId, out Guid parsedAgreementId) ? parsedAgreementId : Guid.Empty;
                    TraceHelper.Trace(tracingService, "agreementIdd: {0}", agreementIdd);

                    string esclationType = escalationType;
                    TraceHelper.Trace(tracingService, "esclationType: {0}", esclationType);

                    decimal esclationValue = escalationValue;
                    TraceHelper.Trace(tracingService, "esclationValue: {0}", esclationValue);

                    Logging.Log("Action name is RevenueEscalate.", tracingService);
                    Logging.Log("Action name is present", tracingService);

                    // creating the instance of the calTotalEscRevenue
                    TotalEsclateRevenue calEsclObj = new TotalEsclateRevenue();
                    Logging.Log("testing g ", tracingService);

                    bool isAddProdEscalationAllYear = false;
                    calEsclObj.calTotalEscRevenue(isAddProdEscalationAllYear, esclateActionName, agreementIdd, esclationType, esclationValue, service, tracingService);
                    return;
                }





                //this logic is for individual esclate revenue
                if (inputActionName == "UpdateOpportunity")
                {
                    string esclateOpp = oppProduct;
                    TotalEsclateRevenue totEscalateObj = new TotalEsclateRevenue();

                    // calling the function to update the total Escalate Revenue
                    TraceHelper.Trace(tracingService, "esclateOpp: {0}", esclateOpp);

                    // creating the instance of the calTotalEscRevenue
                    // TotalEsclateRevenue calEsclObj = new TotalEsclateRevenue();

                    TraceHelper.Trace(tracingService, "esclationType: {0}", escalationType);

                    var escalationTypeObj = escalationType;
                    if (escalationTypeObj != null)
                    {
                        escalationType = escalationTypeObj.ToString();
                        TraceHelper.Trace(tracingService, "esclationType: {0}", escalationType);
                    }


                    //updating the manual coost of Individual Opp.
                    if (pricingMode == "Manual" && esclateOpp != string.Empty)
                    {
                        TraceHelper.Trace(tracingService, "Update of Individual opp Manual amount");

                        //updating the Opportuntiy
                        Entity opp = new Entity("opportunity");
                        Guid esclateOppId = Guid.TryParse(esclateOpp, out Guid parsedAgreementId) ? parsedAgreementId : Guid.Empty;
                        opp.Id = esclateOppId;
                        opp["ats_pricingmode"] = new OptionSetValue((int)AtsPricingMode.Manual);

                        decimal esclationValue = escalationValue;
                        if (esclationValue != 0)// Escalation value is there
                        {
                            opp["ats_escalationvalue"] = new Money(esclationValue);
                            TraceHelper.Trace(tracingService, "esclationValue: {0}", esclationValue);
                            //opp["ats_escalation"] = new OptionSetValue((int)EscalationMode.Individual);
                            TraceHelper.Trace(tracingService, "Escalation Value is set from the Manual Pricing Mode.");
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, "Escalation value not found proceeding with the manual amount provided");
                            //decimal manualAmount = context.InputParameters.Contains("manualAmount") ? (decimal)context.InputParameters["manualAmount"] : 0;
                            TraceHelper.Trace(tracingService, "manualAmount: {0}", manualAmount);
                            opp["ats_manualamount"] = new Money(manualAmount);
                            opp["ats_dealvalue"] = new Money(manualAmount);
                            opp["ats_escalationvalue"] = new Money(0);
                            opp["ats_escalationtype"] = null;

                            string oppIdd = oppProduct;
                            Guid oppGuidd = Guid.TryParse(oppIdd, out Guid parsedGuidd) ? parsedGuidd : Guid.Empty;
                            bool isManualOppId = true;

                            //Sunny(30-05-025)
                            decimal barterAmounnt = BarterAmount.Get(context);
                            TraceHelper.Trace(tracingService, "barterAmount: {0}", barterAmounnt);
                            opp["ats_barteramount"] = new Money(barterAmounnt);

                            //Sunny(13-08-25)
                            #region the below logic handeled the Barter Amount, Cash Amount and Total Deal Value for the Opportunity
                            Entity oppDataa = service.Retrieve("opportunity", oppGuidd, new ColumnSet("ats_dealvalue"));
                            if (oppDataa.Attributes.Contains("ats_dealvalue") && oppDataa["ats_dealvalue"] != null)
                            {
                                TraceHelper.Trace(tracingService, "deal value is retrieved from the opportunity");

                                Money dealMoney = oppDataa["ats_dealvalue"] as Money;
                                if (dealMoney != null)
                                {
                                    //decimal dealValue = dealMoney.Value;
                                    decimal dealValue = manualAmount;
                                    TraceHelper.Trace(tracingService, "Deal Value: {0}", dealValue);

                                    //calculation for getting the cash Amount
                                    decimal cashAmount = dealValue - barterAmounnt;
                                    opp["ats_cashamount"] = new Money(cashAmount);
                                    TraceHelper.Trace(tracingService, "cashAmount: {0}", cashAmount);
                                    TraceHelper.Trace(tracingService, "Cash Amount is initlized, based on the changes in the Barter Amount value");
                                }
                                else
                                {
                                    TraceHelper.Trace(tracingService, "deal value is not initialized");
                                }
                            }
                            #endregion

                            service.Update(opp);
                            TraceHelper.Trace(tracingService, "Manual cost overridden");

                            //Sunny(19-06-25)
                            #region  Updating the totalratecard Roll Up value
                            try
                            {
                                TraceHelper.Trace(tracingService, "Proceeding for updating the total rate card roll up field.");

                                string fieldName = "ats_totalratecard";
                                string entityLogicalName = "opportunity";
                                TraceHelper.Trace(tracingService, "newOpportunityId: {0}", opp.Id);

                                Guid oppEntityID = opp.Id;

                                var calculateRollup = new CalculateRollupFieldRequest
                                {
                                    Target = new EntityReference(entityLogicalName, oppEntityID),
                                    FieldName = fieldName
                                };

                                var calculateRollupResult = (CalculateRollupFieldResponse)service.Execute(calculateRollup);

                                TraceHelper.Trace(tracingService, "Total Rate Card rollup field updated successfully: {0}", calculateRollupResult);
                            }
                            catch (Exception e)
                            {
                                TraceHelper.Trace(tracingService, "Error Occurred while updating rollup field: {0}", e.Message);
                            }
                            #endregion

                            //calling the recalculate opportunity lines
                            totEscalateObj.RecalculateOppLines(esclateOppId, service, tracingService);

                            //proceeding for the Individual Escalation
                            #region proceeding for the Individual Escalation

                            //validating for the indivisual escalation
                            if (escalationType != null && esclationValue != 0)
                            {
                                totEscalateObj.individualEscalateRevenue(oppIdd, isManualOppId, pricingMode, esclateActionName, esclateOpp, escalationType, esclationValue, service, tracingService);
                            }

                            TraceHelper.Trace(tracingService, "Returning from the manual amount Escalation Individual Opp");

                            //Sunny(13-10-2025) --> Updating the total deal value of the agreement

                            //retrieving the AgreementId from the opportunity 
                            Entity agreementObjj = service.Retrieve("opportunity", opp.Id, new ColumnSet("ats_agreement"));

                            EntityReference agreeementReff = agreementObjj.Contains("ats_agreement") ? (EntityReference)agreementObjj["ats_agreement"] : null;
                            updateTotalDealValAgree(agreeementReff.Id, service, tracingService);
                            TraceHelper.Trace(tracingService, "total deal value of the AgreementId: {0} updated.", agreeementReff.Id);

                            #endregion
                            return;
                        }

                        if (escalationType == "Fixed" && escalationType != string.Empty)
                        {
                            opp["ats_escalationtype"] = new OptionSetValue((int)114300000);//Fixed
                            TraceHelper.Trace(tracingService, "Escalation Type is fixed");
                        }
                        else if (escalationType == "Percent" && escalationType != string.Empty)
                        {
                            opp["ats_escalationtype"] = new OptionSetValue((int)114300001);//Percent
                            TraceHelper.Trace(tracingService, "Escalation Type is Percent");
                        }

                        //decimal barterAmount = BarterAmount.Get(context);
                        TraceHelper.Trace(tracingService, "barterAmount: {0}", barterAmount);
                        opp["ats_barteramount"] = new Money(barterAmount);

                        //Sunny(13-08-25)
                        #region the below logic handeled the Barter Amount, Cash Amount and Total Deal Value for the Opportunity

                        string oppId = oppProduct;
                        Guid oppGuid = Guid.TryParse(oppId, out Guid parsedGuid) ? parsedGuid : Guid.Empty;

                        Entity oppData = service.Retrieve("opportunity", oppGuid, new ColumnSet("ats_dealvalue"));
                        if (oppData.Attributes.Contains("ats_dealvalue") && oppData["ats_dealvalue"] != null)
                        {
                            TraceHelper.Trace(tracingService, "deal value is retrieved from the opportunity");

                            Money dealMoney = oppData["ats_dealvalue"] as Money;
                            if (dealMoney != null)
                            {
                                decimal dealValue = dealMoney.Value;
                                TraceHelper.Trace(tracingService, "Deal Value: {0}", dealValue);

                                //calculation for getting the cash Amount
                                decimal cashAmount = dealValue - barterAmount;
                                opp["ats_cashamount"] = new Money(cashAmount);
                                TraceHelper.Trace(tracingService, "cashAmount: {0}", cashAmount);
                                TraceHelper.Trace(tracingService, "Cash Amount is initlized, based on the changes in the Barter Amount value");
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "deal value is not initialized");
                            }
                        }

                        #endregion

                        service.Update(opp);
                        TraceHelper.Trace(tracingService, "Manual cost overridden");

                        //calling the recalculate opportunity lines
                        totEscalateObj.RecalculateOppLines(esclateOppId, service, tracingService);
                    }

                    //Returning from the manual amount Escalation Individual Opp


                    if (pricingMode == "Automatic" && esclateOpp != string.Empty)
                    {
                        TraceHelper.Trace(tracingService, "Automatic amount is present");

                        //retireving the automatic amount 
                        //decimal automaticAmount = context.InputParameters.Contains("automaticAmount") ? (decimal)context.InputParameters["automaticAmount"] : 0;
                        decimal manualAmt = manualAmount;

                        Entity opp = new Entity("opportunity");
                        Guid esclateOppId = Guid.TryParse(esclateOpp, out Guid parsedAgreementId) ? parsedAgreementId : Guid.Empty;
                        opp.Id = esclateOppId;

                        //handling when the Pricing mode is Automatic, but user also goes for escalation
                        decimal esclationValue = escalationValue;
                        if (esclationValue != 0)
                        {
                            opp["ats_escalationvalue"] = new Money(esclationValue);
                            TraceHelper.Trace(tracingService, "esclationValue: {0}", esclationValue);
                        }
                        else
                        {
                            opp["ats_escalationvalue"] = new Money(0);
                            TraceHelper.Trace(tracingService, "escalation value: 0");
                        }

                        //Handling the Escalation Type
                        if (escalationTypeObj != null)
                            escalationType = escalationTypeObj.ToString();

                        TraceHelper.Trace(tracingService, "esclationType: {0}", escalationType);

                        if (escalationType == "Fixed" && escalationType != string.Empty)
                        {
                            opp["ats_escalationtype"] = new OptionSetValue(114300000); // Fixed
                            TraceHelper.Trace(tracingService, "Escalation Type is fixed");
                        }
                        else if (escalationType == "Percent" && escalationType != string.Empty)
                        {
                            opp["ats_escalationtype"] = new OptionSetValue(114300001); // Percent
                            TraceHelper.Trace(tracingService, "Escalation Type is Percent");
                        }
                        else
                        {
                            opp["ats_escalationtype"] = null;
                        }

                        //Handling the pricing Mode & deal value 
                        if (escalationType != string.Empty && esclationValue != 0 && manualAmt != 0)
                        {
                            opp["ats_pricingmode"] = new OptionSetValue((int)AtsPricingMode.Manual);
                            opp["ats_dealvalue"] = new Money(manualAmt);
                        }
                        else
                        {
                            opp["ats_pricingmode"] = new OptionSetValue((int)AtsPricingMode.Automatic);
                            opp["ats_dealvalue"] = new Money(automaticAmount);
                        }

                        //Sunny(20-05-25)
                        if (pricingMode == "Automatic" && escalationType != string.Empty && esclationValue != 0)
                        {
                            TraceHelper.Trace(tracingService, "pricingMode=Automatic and individual escalation applied");
                            opp["ats_pricingmode"] = new OptionSetValue((int)AtsPricingMode.Manual);
                            TraceHelper.Trace(tracingService, "Escalation Mode individual has been set.");
                        }

                        //Sunny(30-05-25)
                        TraceHelper.Trace(tracingService, "barterAmount: {0}", barterAmount);
                        opp["ats_barteramount"] = new Money(barterAmount);

                        //Sunny(13-08-25)
                        #region the below logic handeled the Barter Amount, Cash Amount and Total Deal Value for the Opportunity

                        string oppId = oppProduct;
                        Guid oppGuid = string.IsNullOrEmpty(oppId) ? Guid.Empty : new Guid(oppId);

                        Entity oppData = service.Retrieve("opportunity", oppGuid, new ColumnSet("ats_dealvalue"));
                        if (oppData.Attributes.Contains("ats_dealvalue") && oppData["ats_dealvalue"] != null)
                        {
                            TraceHelper.Trace(tracingService, "deal value is retrieved from the opportunity");

                            Money dealMoney = oppData["ats_dealvalue"] as Money;
                            if (dealMoney != null)
                            {
                                decimal dealValue = dealMoney.Value;
                                TraceHelper.Trace(tracingService, "Deal Value: {0}", dealValue);

                                decimal cashAmount = dealValue - barterAmount;
                                opp["ats_cashamount"] = new Money(cashAmount);
                                TraceHelper.Trace(tracingService, "cashAmount: {0}", cashAmount);
                                TraceHelper.Trace(tracingService, "Cash Amount initialized based on Barter Amount change");
                            }
                            else
                            {
                                TraceHelper.Trace(tracingService, "deal value is not initialized");
                            }
                        }

                        #endregion

                        service.Update(opp);
                        TraceHelper.Trace(tracingService, "Automatic cost overridden.");

                        //calling the recalculate opportunity lines
                        totEscalateObj.RecalculateOppLines(esclateOppId, service, tracingService);
                    }





                    //escalationType
                    // escalationType
                    if (escalationType != string.Empty)
                    {
                        TraceHelper.Trace(tracingService, "Individual escalation is proceed");

                        // string esclationType = context.InputParameters.Contains("escalationType") ? context.InputParameters["escalationType"].ToString() : "";

                        if (escalationTypeObj != null)
                        {
                            escalationType = escalationTypeObj.ToString();
                        }

                        TraceHelper.Trace(tracingService, "esclationType: {0}", escalationType);

                        decimal esclationValue = escalationValue;
                        TraceHelper.Trace(tracingService, "esclationValue: {0}", esclationValue);

                        Logging.Log("testing g", tracingService);

                        TraceHelper.Trace(tracingService, "esclateOpp: {0}", esclateOpp);

                        string oppId = string.Empty;
                        bool isManualOppId = false;

                        totEscalateObj.individualEscalateRevenue(
                            oppId,
                            isManualOppId,
                            pricingMode,
                            esclateActionName,
                            esclateOpp,
                            escalationType,
                            esclationValue,
                            service,
                            tracingService
                        );
                    }

                    TraceHelper.Trace(tracingService, "Returning from Individual Escalate Revenue");












                    //Sunny(13-10-2025) --> Updating the total deal value of the agreement
                    Guid oppGuidRetrieve = Guid.Empty;
                    if (!string.IsNullOrEmpty(esclateOpp) && Guid.TryParse(esclateOpp, out Guid parsedOppGuid))
                    {
                        oppGuidRetrieve = parsedOppGuid;
                    }

                    Entity agreementObj = service.Retrieve("opportunity", oppGuidRetrieve, new ColumnSet("ats_agreement"));
                    EntityReference agreeementRef = agreementObj.Contains("ats_agreement")
                        ? (EntityReference)agreementObj["ats_agreement"]
                        : null;

                    updateTotalDealValAgree(agreeementRef.Id, service, tracingService);

                    TraceHelper.Trace(tracingService, "total deal value of the AgreementId: {0} updated.", agreeementRef.Id);
                    return;


                }

                #endregion


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
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="seasonId"></param>
        /// <param name="productId"></param>
        /// <param name="rateType"></param>
        /// <returns></returns>
        public static Guid GetPackageRateIdForProductSeason(IOrganizationService service,Guid seasonId,Guid productId)
        {
            string functionName = "GetPackageRateIdForProductSeason"; 
            try
            {
                var qe = new QueryExpression("product")
                {
                    ColumnSet = new ColumnSet(false),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
            {
                new ConditionExpression("productid", ConditionOperator.Equal, productId)
            }
                    },
                    TopCount = 1
                };

                var linkIbs = qe.AddLink("ats_inventorybyseason", "productid", "ats_product", JoinOperator.Inner);
                linkIbs.EntityAlias = "IBS";
                linkIbs.Columns = new ColumnSet(false);
                linkIbs.LinkCriteria.AddCondition("ats_season", ConditionOperator.Equal, seasonId);

                var linkRate = linkIbs.AddLink("ats_rate", "ats_inventorybyseasonid", "ats_inventorybyseason", JoinOperator.Inner);
                linkRate.EntityAlias = "Rate";
                linkRate.Columns = new ColumnSet("ats_rateid");
                //linkRate.LinkCriteria.AddCondition("ats_ratetype", ConditionOperator.Equal, rateType);

                var ec = service.RetrieveMultiple(qe);
                if (ec.Entities.Count == 0)
                    return Guid.Empty;

                var record = ec.Entities[0];
                if (!record.Contains("Rate.ats_rateid"))
                    return Guid.Empty;

                return (Guid)((AliasedValue)record["Rate.ats_rateid"]).Value;
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
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="packageRateId"></param>
        /// <param name="seasonId"></param>
        /// <returns></returns>
        public static Guid GetPrimaryPackageTemplateId(
     IOrganizationService service,
     Guid packageRateId,
     Guid seasonId)
        {
            //if (packageRateId != null)
            //{
                
            //}
            var qe = new QueryExpression("ats_packagetemplate")
            {
                ColumnSet = new ColumnSet("ats_packagetemplateid"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
            {
                new ConditionExpression("ats_packagerateid", ConditionOperator.Equal, packageRateId)
            }
                },
                TopCount = 1
            };

            var linkRate = qe.AddLink("ats_rate", "ats_componentrateid", "ats_rateid", JoinOperator.Inner);
            linkRate.Columns = new ColumnSet(false);

            var linkIbs = linkRate.AddLink("ats_inventorybyseason", "ats_inventorybyseason", "ats_inventorybyseasonid", JoinOperator.Inner);
            linkIbs.Columns = new ColumnSet(false);
            linkIbs.LinkCriteria.AddCondition("ats_season", ConditionOperator.Equal, seasonId);

            var ec = service.RetrieveMultiple(qe);
            return (ec.Entities.Count > 0) ? ec.Entities[0].Id : Guid.Empty;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="logicalName"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static bool EntityExists(IOrganizationService service, string logicalName, Guid id)
        {
            try
            {
                service.Retrieve(logicalName, id, new ColumnSet(false));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="cache"></param>
        /// <param name="ibsId"></param>
        /// <param name="rateType"></param>
        /// <returns></returns>
        public static Entity GetRateCached(
     IOrganizationService service,
     Dictionary<string, Entity> rateCache,
     Guid ibsId,
     int rateType)
        {
            string key = ibsId.ToString("N") + "|" + rateType.ToString();

            Entity cached;
            if (rateCache.TryGetValue(key, out cached))
                return cached;

            var qe = new QueryExpression("ats_rate")
            {
                ColumnSet = new ColumnSet("ats_rateid", "ats_price", "ats_ratetype"),
                Criteria = new FilterExpression(LogicalOperator.And)
                {
                    Conditions =
            {
                new ConditionExpression("ats_inventorybyseason", ConditionOperator.Equal, ibsId),
                new ConditionExpression("ats_ratetype", ConditionOperator.Equal, rateType)
            }
                },
                TopCount = 1
            };

            var ec = service.RetrieveMultiple(qe);
            if (ec.Entities.Count == 0)
                return null;

            var rate = ec.Entities[0];
            rateCache[key] = rate;
            return rate;
        }





        public void CreateOppProdComponent(IOrganizationService service, List<Entity> entities, ITracingService tracingService)
        {
            string functionName = "CreateOppProdComponent";

            try
            {
                TraceHelper.Trace(tracingService, "functionName: {0}", functionName);

                for (int i = 0; i <= entities.Count - 1; i++)
                {
                    Entity oppProdCreate = entities[i];
                    service.Create(oppProdCreate);
                }

                TraceHelper.Trace(tracingService, "Exit functionName: {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException(
                    string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message),ex);
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException(
                    string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message),ex);
            }
        }




        /// <summary>
        /// Sunny(21-12-25)
        /// </summary>
        /// <param name="seasonId"></param>
        /// <param name="productId"></param>
        /// <param name="lastSeasonId"></param>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <param name="ibsCache"></param>
        /// <param name="deferredRateCreates"></param>
        /// <returns></returns>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public EntityReference EnsureInventoryForSeasonOptimized(Guid seasonId, Guid productId, Guid lastSeasonId, IOrganizationService service, ITracingService tracingService, Dictionary<string, EntityReference> ibsCache, List<Entity> deferredRateCreates)
        {
            string functionName = "EnsureInventoryForSeasonOptimized";
            TraceHelper.Trace(tracingService, "functionName: {0}", functionName);
            string ibsKey = seasonId.ToString("N") + "|" + productId.ToString("N");

            try
            {
                TraceHelper.Trace(tracingService, "EnsureInventoryForSeasonOptimized function begins");
                TraceHelper.Trace(tracingService, "seasonId: {0}", seasonId);
                TraceHelper.Trace(tracingService, "productId: {0}", productId);
                TraceHelper.Trace(tracingService, "lastSeasonId: {0}", lastSeasonId);


                if (service == null) { TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName); return null; }
                if (seasonId == Guid.Empty || productId == Guid.Empty) { TraceHelper.Trace(tracingService, "Invalid inputs in {0}: seasonId/productId is empty", functionName); return null; }

                EntityReference cachedIbs;
                if (ibsCache != null && ibsCache.TryGetValue(ibsKey, out cachedIbs) && cachedIbs != null)
                {
                    TraceHelper.Trace(tracingService, "IBS cache HIT key={0} ibsId={1}", ibsKey, cachedIbs.Id);
                    return cachedIbs;
                }
                TraceHelper.Trace(tracingService, "IBS cache MISS key={0}", ibsKey);

                // 1) Fast IBS existence check (minimal cols)
                var inventoryQuery = new QueryExpression("ats_inventorybyseason")
                {
                    ColumnSet = new ColumnSet("ats_inventorybyseasonid", "ats_name"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                {
                    new ConditionExpression("ats_season", ConditionOperator.Equal, seasonId),
                    new ConditionExpression("ats_product", ConditionOperator.Equal, productId)
                }
                    },
                    TopCount = 1
                };

                TraceHelper.Trace(tracingService, "Checking IBS existence via QueryExpression. seasonId={0} productId={1}", seasonId, productId);

                var inventoryCollection = service.RetrieveMultiple(inventoryQuery);
                TraceHelper.Trace(tracingService, "IBS existence check returned count={0}", inventoryCollection.Entities.Count);

                if (inventoryCollection.Entities.Count > 0)
                {
                    var existingRef = inventoryCollection.Entities[0].ToEntityReference();
                    if (ibsCache != null) ibsCache[ibsKey] = existingRef;
                    TraceHelper.Trace(tracingService, "Existing IBS found ibsId={0} name={1}", existingRef.Id, existingRef.Name);
                    return existingRef;
                }

                GetOpportunityProducts getOppProd = new GetOpportunityProducts();

                // 2) Fetch last season IBS (your template)
                string fetchXml = getOppProd.fetchLastYearInventorySeason
                    .Replace("{productId}", productId.ToString())
                    .Replace("{seasonId}", lastSeasonId.ToString());

                TraceHelper.Trace(tracingService, "Fetching previous season IBS via FetchXML. lastSeasonId={0} productId={1}", lastSeasonId, productId);

                var prevSeasonInventoryCollection = service.RetrieveMultiple(new FetchExpression(fetchXml));
                TraceHelper.Trace(tracingService, "Previous season IBS fetch returned count={0}", prevSeasonInventoryCollection.Entities.Count);

                if (prevSeasonInventoryCollection.Entities.Count == 0)
                {
                    TraceHelper.Trace(tracingService, "No previous season IBS found. ProductId={0} LastSeasonId={1} TargetSeasonId={2}", productId, lastSeasonId, seasonId);
                    throw new InvalidPluginExecutionException("No previous season Inventory By Season found to clone. Product: " + productId + ", LastSeason: " + lastSeasonId + ", TargetSeason: " + seasonId);
                }

                var lastInventoryBySeason = prevSeasonInventoryCollection.Entities[prevSeasonInventoryCollection.Entities.Count - 1];
                TraceHelper.Trace(tracingService, "Using last previous IBS record. prevIbsId={0}", lastInventoryBySeason.Id);

                // 3) Get new season name (minimal)
                var newSeason = service.Retrieve("ats_season", seasonId, new ColumnSet("ats_name"));
                string newSeasonName = newSeason.GetAttributeValue<string>("ats_name") ?? string.Empty;
                TraceHelper.Trace(tracingService, "Target season name retrieved: {0}", newSeasonName);

                // 4) Build IBS name using aliased values
                string division = (lastInventoryBySeason.Contains("Division.ats_name") ? ((AliasedValue)lastInventoryBySeason["Division.ats_name"]).Value : null) as string ?? "";
                string pf = (lastInventoryBySeason.Contains("ProductFamily.ats_name") ? ((AliasedValue)lastInventoryBySeason["ProductFamily.ats_name"]).Value : null) as string ?? "";
                string psf = (lastInventoryBySeason.Contains("ProductSubFamily.ats_name") ? ((AliasedValue)lastInventoryBySeason["ProductSubFamily.ats_name"]).Value : null) as string ?? "";
                string prodName = (lastInventoryBySeason.Contains("Product.name") ? ((AliasedValue)lastInventoryBySeason["Product.name"]).Value : null) as string ?? "";

                string inventoryBySeasonName = (division + " " + pf + " " + psf + " " + prodName + " " + newSeasonName).Trim();
                TraceHelper.Trace(tracingService, "New IBS name built: {0}", inventoryBySeasonName);

                // 5) Create IBS (copy only needed fields)
                var newInventory = new Entity("ats_inventorybyseason");
                newInventory["ats_product"] = (EntityReference)lastInventoryBySeason["ats_product"];
                newInventory["ats_season"] = new EntityReference("ats_season", seasonId);
                newInventory["ats_name"] = inventoryBySeasonName;
                newInventory["ats_autogenerated"] = true;

                CopyIfPresent(lastInventoryBySeason, newInventory, "ats_description");
                CopyIfPresent(lastInventoryBySeason, newInventory, "ats_allowoverselling");
                CopyIfPresent(lastInventoryBySeason, newInventory, "ats_unlimitedquantity");
                CopyIfPresent(lastInventoryBySeason, newInventory, "ats_recordtype");
                CopyIfPresent(lastInventoryBySeason, newInventory, "ats_totalquantityperevent");
                CopyIfPresent(lastInventoryBySeason, newInventory, "ats_totalquantity");

                if (lastInventoryBySeason.Attributes.ContainsKey("ats_eventschedule"))
                {
                    TraceHelper.Trace(tracingService, "Copying EventSchedule to new IBS (GetEventSchedule call)");
                    var oldEventScheduleRef = (EntityReference)lastInventoryBySeason["ats_eventschedule"];
                    newInventory["ats_eventschedule"] = getOppProd.GetEventSchedule(oldEventScheduleRef, new EntityReference("ats_season", seasonId), service, tracingService);
                }
                else
                {
                    TraceHelper.Trace(tracingService, "No EventSchedule on previous IBS, skipping");
                }

                TraceHelper.Trace(tracingService, "Creating new IBS record...");
                Guid newInventoryId = service.Create(newInventory);
                TraceHelper.Trace(tracingService, "New IBS created. newInventoryId={0}", newInventoryId);

                var newIbsRef = new EntityReference("ats_inventorybyseason", newInventoryId) { Name = inventoryBySeasonName };

                // 6) Fetch rates from last IBS (minimal columns), defer clone creates
                var rateQuery = new QueryExpression("ats_rate")
                {
                    ColumnSet = new ColumnSet("ats_ratetype", "ats_price", "ats_hardcost"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                {
                    new ConditionExpression("ats_inventorybyseason", ConditionOperator.Equal, lastInventoryBySeason.Id)
                }
                    }
                };

                TraceHelper.Trace(tracingService, "Fetching rates from previous IBS. prevIbsId={0}", lastInventoryBySeason.Id);

                var rateCollection = service.RetrieveMultiple(rateQuery);
                TraceHelper.Trace(tracingService, "Previous IBS rate count={0}", rateCollection.Entities.Count);

                for (int i = 0; i < rateCollection.Entities.Count; i++)
                {
                    var rate = rateCollection.Entities[i];

                    var newRate = new Entity("ats_rate");
                    if (rate.Contains("ats_ratetype")) newRate["ats_ratetype"] = rate["ats_ratetype"];
                    if (rate.Contains("ats_price")) newRate["ats_price"] = rate["ats_price"];
                    if (rate.Contains("ats_hardcost")) newRate["ats_hardcost"] = rate["ats_hardcost"];

                    newRate["ats_inventorybyseason"] = new EntityReference("ats_inventorybyseason", newInventoryId);
                    newRate["ats_autogenerated"] = true;

                    string rateTypeLabel = rate.FormattedValues.ContainsKey("ats_ratetype") ? rate.FormattedValues["ats_ratetype"] : "";
                    newRate["ats_name"] = string.IsNullOrWhiteSpace(rateTypeLabel) ? inventoryBySeasonName : (inventoryBySeasonName + " " + rateTypeLabel);

                    //if (deferredRateCreates != null)
                    //{
                    //    deferredRateCreates.Add(newRate);
                    //    TraceHelper.Trace(tracingService, "Deferred rate create added index={0} rateName={1}", i, newRate.GetAttributeValue<string>("ats_name"));
                    //}
                    //else
                    //{
                        Guid newRateId = service.Create(newRate);
                        TraceHelper.Trace(tracingService, "Rate created index={0} newRateId={1}", i, newRateId);
                    //}
                }

                if (ibsCache != null) ibsCache[ibsKey] = newIbsRef;
                TraceHelper.Trace(tracingService, "End {0} returning newIbsId={1}", functionName, newIbsRef.Id);

                return newIbsRef;
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message), ex);
            }
        }


        private static void CopyIfPresent(Entity source, Entity target, string attributeName)
        {
            if (source.Attributes.ContainsKey(attributeName))
                target[attributeName] = source[attributeName];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="ibsId"></param>
        /// <param name="qtyUnits"></param>
        /// <param name="qtyEvents"></param>
        /// <param name="rateType"></param>
        /// <param name="bpfStatusValue"></param>
        /// <param name="service"></param>
        /// <returns></returns>
        public static Entity BuildIbsQtyUpdatePatch(Guid ibsId, int qtyUnits, int qtyEvents, int rateType, int bpfStatusValue, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "BuildIbsQtyUpdatePatch";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} ibsId={1} qtyUnits={2} qtyEvents={3} rateType={4} bpfStatusValue={5}",
                    functionName, ibsId, qtyUnits, qtyEvents, rateType, bpfStatusValue);

                if (service == null)
                {
                    TraceHelper.Trace(tracingService, "Service is null in {0}, exiting", functionName);
                    return null;
                }

                if (ibsId == Guid.Empty)
                {
                    TraceHelper.Trace(tracingService, "Invalid ibsId in {0}, exiting", functionName);
                    return null;
                }

                // Only fields we need
                var ibs = service.Retrieve("ats_inventorybyseason", ibsId, new ColumnSet("ats_quantitypitched", "ats_quantitysold"));

                decimal quantityPitched = 0m;
                decimal quantitySold = 0m;

                if (ibs.Contains("ats_quantitypitched") && ibs["ats_quantitypitched"] != null)
                    quantityPitched = Convert.ToDecimal(ibs["ats_quantitypitched"]);

                if (ibs.Contains("ats_quantitysold") && ibs["ats_quantitysold"] != null)
                    quantitySold = Convert.ToDecimal(ibs["ats_quantitysold"]);

                TraceHelper.Trace(tracingService, "Current IBS values pitched={0} sold={1}", quantityPitched, quantitySold);

                // Stage: Pitched (114300001)
                if (bpfStatusValue == 114300001)
                {
                    decimal add = (rateType == 114300000) ? qtyUnits : (qtyUnits * qtyEvents);
                    decimal newVal = quantityPitched + add;

                    TraceHelper.Trace(tracingService, "Pitched stage calc add={0} newVal={1}", add, newVal);

                    if (newVal != quantityPitched)
                    {
                        var patch = new Entity("ats_inventorybyseason", ibsId);
                        patch["ats_quantitypitched"] = newVal;
                        TraceHelper.Trace(tracingService, "Pitched quantity updated to {0}", newVal);
                        return patch;
                    }

                    TraceHelper.Trace(tracingService, "No pitched quantity change detected");
                    return null;
                }

                // Stage: Closed Won (114300003)
                if (bpfStatusValue == 114300003)
                {
                    decimal add = (rateType == 114300000) ? qtyUnits : (qtyUnits * qtyEvents);
                    decimal newVal = quantitySold + add;

                    TraceHelper.Trace(tracingService, "Sold stage calc add={0} newVal={1}", add, newVal);

                    if (newVal != quantitySold)
                    {
                        var patch = new Entity("ats_inventorybyseason", ibsId);
                        patch["ats_quantitysold"] = newVal;
                        TraceHelper.Trace(tracingService, "Sold quantity updated to {0}", newVal);
                        return patch;
                    }

                    TraceHelper.Trace(tracingService, "No sold quantity change detected");
                    return null;
                }

                TraceHelper.Trace(tracingService, "No IBS quantity update required for bpfStatusValue={0}", bpfStatusValue);
                return null;
            }
            catch (InvalidPluginExecutionException)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException thrown in {0}", functionName);
                throw;
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message),
                    ex
                );
            }
        }





        //Sunny(26-02-25)
        /// <summary>
        /// updating the Opp Line Items 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="tracingService"></param>
        /// <param name="service"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public void UpdateOppLineItems(string oppProduct, string opp, string description, decimal totalValueOppProd, decimal hardCost, decimal productionCost, int qtyUnits, int qtyEvents, decimal rate, string rateType, bool isResetOverride, string legalDefinition, bool isOverwriteLegalDefinition, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "updateOppLineItems";
            try
            {
                TraceHelper.Trace(tracingService, "function Name: {0}", functionName);

                //Retrieving the Opp Product Id
                Guid oppProductId = Guid.TryParse(oppProduct, out Guid parsedOppProdId) ? parsedOppProdId : Guid.Empty;
                TraceHelper.Trace(tracingService, "oppProductId: {0}", oppProductId);

                TraceHelper.Trace(tracingService, "opp: {0}", opp);

                Guid oppId = Guid.TryParse(opp, out Guid parsedOppId) ? parsedOppId : Guid.Empty;
                TraceHelper.Trace(tracingService, "oppId: {0}", oppId);

                //Updating the Opprtunity Product
                Entity oppProd = new Entity("opportunityproduct");
                oppProd.Id = oppProductId;

                TraceHelper.Trace(tracingService, "before Update the Opp OLI description ");

                #region Update the Opp OLI description 

                TraceHelper.Trace(tracingService, "Description value: {0}", description);

                if (description != string.Empty)
                {
                    oppProd["description"] = description;
                    service.Update(oppProd);
                    TraceHelper.Trace(tracingService, "decription updated sucessfully in the Opp Product, return...");
                }

                #endregion

                #region Updating the Opp Product 

                //this retrieval is for knowing the Opp pricing mode is Automatic or Manual. 
                Entity oppObj = service.Retrieve("opportunity", oppId, new ColumnSet("ats_pricingmode", "ats_agreement"));
                OptionSetValue pricingMode = oppObj.Contains("ats_pricingmode") ? (OptionSetValue)oppObj["ats_pricingmode"] : null;

                EntityReference aggrementRef = oppObj.Contains("ats_agreement") ? (EntityReference)oppObj["ats_agreement"] : null;

                #region Reset Override functionaltiy

                TraceHelper.Trace(tracingService, "before isResetOverride");
                TraceHelper.Trace(tracingService, "isResetOverride: {0}", isResetOverride);

                TraceHelper.Trace(tracingService, "legalDefinition");
                TraceHelper.Trace(tracingService, "isOverwriteLegalDefinition");

                #region  Legal Definition Override functionalities.

                if (isOverwriteLegalDefinition) // true 
                {
                    TraceHelper.Trace(tracingService, "Overwrite legal definition functionality triggered.");
                    oppProd["ats_overwritelegaldefinition"] = isOverwriteLegalDefinition;
                    oppProd["ats_legaldefinition"] = legalDefinition;
                    service.Update(oppProd);
                    TraceHelper.Trace(tracingService, "Overwrite legal definition functionality is updated on the Opp product.");
                }

                #endregion

                TotalEsclateRevenue totEscalateObj = new TotalEsclateRevenue();

                if (isResetOverride)
                {
                    oppProd["ats_manualpriceoverride"] = false;
                    service.Update(oppProd);
                    TraceHelper.Trace(tracingService, "Opp prod Updated sucessfully.");

                    if (pricingMode != null && pricingMode.Value == 559240000) // Automatic
                    {
                        TraceHelper.Trace(tracingService, "Pricing Mode is Automatic");

                        #region declaring the necessary fields passed to call the escalation across all year functionality

                        bool isManualOverridenEscalationAllYear = true;
                        TraceHelper.Trace(tracingService, "agreementIdd: {0}", aggrementRef != null ? aggrementRef.Id : Guid.Empty);
                        string esclateActionName = "updateOpportunityLineItem";
                        string esclationType = string.Empty;
                        decimal esclationValue = 0;

                        #endregion

                        totEscalateObj.RecalculateOppLines(oppId, service, tracingService);

                        TotalEsclateRevenue totalEscalaRevenueObj = new TotalEsclateRevenue();
                        totalEscalaRevenueObj.calTotalEscRevenue(isManualOverridenEscalationAllYear, esclateActionName, aggrementRef.Id, esclationType, esclationValue, service, tracingService);
                    }
                    else
                    {
                        totEscalateObj.RecalculateOppLines(oppId, service, tracingService);
                    }

                    TraceHelper.Trace(tracingService, "returning isResetOverride functionality");
                    return;
                }

                #endregion

                TraceHelper.Trace(tracingService, " total value: {0}", totalValueOppProd);

                //Sunny(16-04-25)
                //validation when qty is 0
                if (qtyUnits == 0)
                {
                    TraceHelper.Trace(tracingService, "qtyUnits == 0");
                    oppProd["ats_quantity"] = 0;
                    oppProd["ats_sellingrate"] = new Money(0);

                    TraceHelper.Trace(tracingService, "set ats_sellingrate= 0");
                    service.Update(oppProd);
                    TraceHelper.Trace(tracingService, "Updating the Opp product early, selling rate: 0");
                    totEscalateObj.RecalculateOppLines(oppId, service, tracingService);

                    #region updating the Agreement total deal value based on the deal value is getting updated on the opportunity

                    TraceHelper.Trace(tracingService, "Logic for updating the Agreement total deal value");
                    Entity agreementData = service.Retrieve("opportunity", oppId, new ColumnSet("ats_agreement"));
                    if (agreementData.Attributes.Contains("ats_agreement") && agreementData["ats_agreement"] != null)
                    {
                        TraceHelper.Trace(tracingService, "Agreement field is present");
                        EntityReference agreement = (EntityReference)agreementData["ats_agreement"];
                        Guid agreementId = agreement.Id;
                        TraceHelper.Trace(tracingService, "Agreement Id: {0}", agreementId);

                        updateTotalDealValAgree(agreementId, service, tracingService);
                        TraceHelper.Trace(tracingService, "updateTotalDealValAgree executed sucessfully.");
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Agreement attribute is not present in the opportunity record: {0}", oppId);
                    }

                    #endregion

                    return;
                }

                //Sunny(05-07-25)
                //Adding the validation for the rate type Events.
                if (rateType != "Season")
                {
                    TraceHelper.Trace(tracingService, "rate type is not season.");
                    if (qtyEvents == 0)
                    {
                        oppProd["ats_quantity"] = qtyUnits; //Sunny(16-07-25)
                        TraceHelper.Trace(tracingService, "qtyEvents == 0");
                        oppProd["ats_quantityofevents"] = 0;
                        oppProd["ats_sellingrate"] = new Money(0);

                        TraceHelper.Trace(tracingService, "set ats_sellingrate= 0");
                        service.Update(oppProd);
                        TraceHelper.Trace(tracingService, "Updating the Opp product early, selling rate: 0");

                        totEscalateObj.RecalculateOppLines(oppId, service, tracingService);

                        #region updating the Agreement total deal value based on the deal value is getting updated on the opportunity

                        TraceHelper.Trace(tracingService, "Logic for updating the Agreement total deal value");
                        Entity agreementData = service.Retrieve("opportunity", oppId, new ColumnSet("ats_agreement"));
                        if (agreementData.Attributes.Contains("ats_agreement") && agreementData["ats_agreement"] != null)
                        {
                            TraceHelper.Trace(tracingService, "Agreement field is present");
                            EntityReference agreement = (EntityReference)agreementData["ats_agreement"];
                            Guid agreementId = agreement.Id;
                            TraceHelper.Trace(tracingService, "Agreement Id: {0}", agreementId);

                            updateTotalDealValAgree(agreementId, service, tracingService);
                            TraceHelper.Trace(tracingService, "updateTotalDealValAgree executed sucessfully.");
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, "Agreement attribute is not present in the opportunity record: {0}", oppId);
                        }

                        #endregion

                        return;
                    }
                }

                #region  Manually Total value Overriden Functionality 

                bool setManualOverride = false;

                Entity oppProdObj = service.Retrieve("opportunityproduct", oppProductId, new ColumnSet("ats_adjustedtotalprice"));
                Money adjustedtotalPrice = oppProdObj.Contains("ats_adjustedtotalprice") ? (Money)oppProdObj["ats_adjustedtotalprice"] : new Money(0);

                if (adjustedtotalPrice.Value != totalValueOppProd)
                {
                    setManualOverride = true;
                    TraceHelper.Trace(tracingService, "total value does not match, setManualOverride is kept true");
                }

                oppProd["ats_adjustedtotalprice"] = new Money(totalValueOppProd);
                oppProd["ats_quantity"] = qtyUnits;

                if (rateType != "Season")
                {
                    TraceHelper.Trace(tracingService, "rate type is not season.");
                    oppProd["ats_quantityofevents"] = qtyEvents;
                }

                oppProd["ats_hardcost"] = new Money(hardCost);
                oppProd["ats_productioncost"] = new Money(productionCost);

                if (setManualOverride)
                {
                    oppProd["ats_manualpriceoverride"] = true;
                    TraceHelper.Trace(tracingService, "is manual Override value: {0}", oppProd["ats_manualpriceoverride"]);
                }

                service.Update(oppProd);
                TraceHelper.Trace(tracingService, "Opp prod Updated sucessfully.");

                #region  updating the total hard cost

                if (hardCost != 0)
                {
                    TraceHelper.Trace(tracingService, "Updating the total hardcost for the opportunity");
                    decimal totalHardCost = 0m;

                    var fetchXml = string.Format(@"
                                    <fetch version='1.0' no-lock='true'>
                                      <entity name='opportunityproduct'>
                                        <attribute name='ats_hardcost' />
                                        <attribute name='ats_quantity' />
                                        <attribute name='ats_quantityofevents' />
                                        <filter>
                                          <condition attribute='opportunityid' operator='eq' value='{0}' />
                                        </filter>

                                        <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' link-type='outer' alias='Rate'>
                                          <attribute name='ats_ratetype' />
                                        </link-entity>
                                      </entity>
                                    </fetch>", oppId);

                    TraceHelper.Trace(tracingService, "[SumHardCost] FetchXML:\n{0}", fetchXml);

                    var result = service.RetrieveMultiple(new FetchExpression(fetchXml));

                    foreach (var row in result.Entities)
                    {
                        var retrievedHardCost = row.GetAttributeValue<Money>("ats_hardcost");
                        var quantity = row.GetAttributeValue<decimal?>("ats_quantity") ?? 0;
                        var quantityOfEvents = row.GetAttributeValue<decimal?>("ats_quantityofevents") ?? 0;

                        var rateTypeAliased = row.GetAttributeValue<AliasedValue>("Rate.ats_ratetype");
                        var rateTypeForHardCost = rateTypeAliased != null ? (int)rateTypeAliased.Value : -1;

                        if (retrievedHardCost != null)
                        {
                            decimal lineHardCost = 0;

                            if (rateTypeForHardCost == 114300000) //Season
                            {
                                lineHardCost = quantity * retrievedHardCost.Value;
                            }
                            else //Individual
                            {
                                lineHardCost = quantity * quantityOfEvents * retrievedHardCost.Value;
                            }

                            totalHardCost += lineHardCost;

                            TraceHelper.Trace(
                                tracingService,
                                "[SumHardCost] OLI Id: {0}, RateType: {1}, Qty: {2}, QtyEvents: {3}, UnitHardCost: {4}, LineHardCost: {5}",
                                row.Id,
                                rateType,
                                quantity,
                                quantityOfEvents,
                                retrievedHardCost.Value,
                                lineHardCost
                            );
                        }
                    }

                    TraceHelper.Trace(tracingService, "total hard cost : {0}", totalHardCost);

                    Entity oppTotalHardCostUpdate = new Entity("opportunity");
                    oppTotalHardCostUpdate.Id = oppId;
                    oppTotalHardCostUpdate["ats_totalhardcost"] = new Money(totalHardCost);
                    TraceHelper.Trace(tracingService, "total hard cost updated");
                }

                #endregion

                if (pricingMode != null && pricingMode.Value == 559240000) // Automatic
                {
                    TraceHelper.Trace(tracingService, "Pricing Mode is Automatic");

                    #region declaring the necessary fields passed to call the escalation across all year functionality

                    bool isManualOverridenEscalationAllYear = true;
                    TraceHelper.Trace(tracingService, "agreementIdd: {0}", aggrementRef != null ? aggrementRef.Id : Guid.Empty);
                    string esclateActionName = "updateOpportunityLineItem";
                    string esclationType = string.Empty;
                    decimal esclationValue = 0;

                    #endregion

                    totEscalateObj.RecalculateOppLines(oppId, service, tracingService);

                    TotalEsclateRevenue totalEscalaRevenueObj = new TotalEsclateRevenue();
                    totalEscalaRevenueObj.calTotalEscRevenue(isManualOverridenEscalationAllYear, esclateActionName, aggrementRef.Id, esclationType, esclationValue, service, tracingService);
                }
                else
                {
                    totEscalateObj.RecalculateOppLines(oppId, service, tracingService);
                }

                #endregion

                if (aggrementRef != null)
                {
                    TraceHelper.Trace(tracingService, "updating the total deal value agreement.");
                    updateTotalDealValAgree(aggrementRef.Id, service, tracingService);
                    TraceHelper.Trace(tracingService, "deal value updated sucessfully.");
                }
                else
                {
                    TraceHelper.Trace(tracingService, "Opportunity does not contain ats_agreement field.");
                }

                TraceHelper.Trace(tracingService, "Exit function Name: {0}", functionName);

                #endregion
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
        }





        /// <summary>
        /// updating the total deal value of the Agreement
        /// </summary>
        /// <param name="agreementId"></param>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public void updateTotalDealValAgree(Guid agreementId, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "updateTotalDealValAgree";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} agreementId={1}", functionName, agreementId);

                if (service == null) { TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName); return; }
                if (agreementId == Guid.Empty) { TraceHelper.Trace(tracingService, "agreementId is empty, exiting {0}", functionName); return; }

                QueryExpression opportunityQueryObj = new QueryExpression("opportunity")
                {
                    ColumnSet = new ColumnSet("opportunityid", "ats_startseason", "name", "ats_dealvalue"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                {
                    new ConditionExpression("ats_agreement", ConditionOperator.Equal, agreementId)
                }
                    },
                    Orders =
            {
                new OrderExpression("ats_startseason", OrderType.Ascending)
            }
                };

                Logging.Log("done query expression", tracingService);

                EntityCollection opportunityCollectionObj = service.RetrieveMultiple(opportunityQueryObj);

                decimal totalAgreementDealValue = 0m;
                Logging.Log(string.Format("opportunityCollectionObj.Entities.Count: {0}", opportunityCollectionObj.Entities.Count), tracingService);
                TraceHelper.Trace(tracingService, "Opportunity count for agreement {0}: {1}", agreementId, opportunityCollectionObj.Entities.Count);

                foreach (Entity opportunity in opportunityCollectionObj.Entities)
                {
                    Logging.Log("inside in for each loop", tracingService);

                    Money oppRevenue = opportunity.Attributes.Contains("ats_dealvalue") && opportunity["ats_dealvalue"] != null ? (Money)opportunity["ats_dealvalue"] : new Money(0);
                    Logging.Log(string.Format("oppRevenue: {0}", oppRevenue.Value), tracingService);

                    totalAgreementDealValue = totalAgreementDealValue + oppRevenue.Value;
                    Logging.Log(string.Format("totalAgreementDealValue: {0}", totalAgreementDealValue), tracingService);
                }

                Logging.Log(string.Format("totalAgreementDealValue: {0}", totalAgreementDealValue), tracingService);
                TraceHelper.Trace(tracingService, "Calculated totalAgreementDealValue={0} for agreementId={1}", totalAgreementDealValue, agreementId);

                #region updating the total deal value of Agreement

                string entityLogicalName = "ats_agreement";
                Entity agreementToUpdate = new Entity(entityLogicalName) { Id = agreementId };
                agreementToUpdate["ats_totaldealvalue"] = totalAgreementDealValue;

                Logging.Log(string.Format("ats_totaldealvalue: {0}", agreementToUpdate["ats_totaldealvalue"]), tracingService);
                TraceHelper.Trace(tracingService, "Updating Agreement total deal value. agreementId={0} ats_totaldealvalue={1}", agreementId, totalAgreementDealValue);

                service.Update(agreementToUpdate);

                Logging.Log("ats_totaldealvalue updated successfully", tracingService);
                TraceHelper.Trace(tracingService, "End {0} success agreementId={1}", functionName, agreementId);

                #endregion
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
        }




        /// <summary>
        /// Recalculating the Opp Lines based on the Opp. 
        /// </summary>
        /// <param name="OppId"></param>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public void RecalculateOppLines(Guid oppId, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "RecalculateOppLines";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} oppId={1}", functionName, oppId);

                if (service == null)
                {
                    TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName);
                    return;
                }

                if (oppId == Guid.Empty)
                {
                    TraceHelper.Trace(tracingService, "oppId is empty, exiting {0}", functionName);
                    return;
                }

                // Call Recalculate Action
                TraceHelper.Trace(tracingService, "Calling Recalculate Opportunity Lines action");

                OrganizationRequest actionRequest = new OrganizationRequest("ats_CalculateOpportunityLines")
                {
                    ["OppurtunityEntityReference"] = new EntityReference("opportunity", oppId),
                    ["Action"] = "ReCalcOppLinesAgreement"
                };

                service.Execute(actionRequest);

                TraceHelper.Trace(tracingService, "Recalculate action executed successfully for oppId={0}", oppId);
                TraceHelper.Trace(tracingService, "End {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message),
                    ex
                );
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message),
                    ex
                );
            }
        }



        //Sunny(20-Nov-2025)
        /// <summary>
        /// Adding the Component Opp Prod in the GuidList based on the package OLI id
        /// </summary>
        /// <param name="guidList"></param>
        /// <param name="firstOppProd"></param>
        /// <param name="tracingService"></param>
        /// <param name="service"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public void IsProductPackageOLIAndDeleteCompOppProd(ref List<Guid> guidList, Guid firstOppProd, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "IsProductPackageOLI";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} firstOppProd={1} guidListCount={2}", functionName, firstOppProd, guidList != null ? guidList.Count : 0);

                if (service == null) { TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName); return; }
                if (firstOppProd == Guid.Empty) { TraceHelper.Trace(tracingService, "firstOppProd is empty, exiting {0}", functionName); return; }
                if (guidList == null || guidList.Count == 0) { TraceHelper.Trace(tracingService, "guidList is null/empty, exiting {0}", functionName); return; }

               

                var fetch = string.Format(@"
                        <fetch>
                          <entity name='opportunityproduct'>
                            <filter type='and'>
                              <condition attribute='opportunityproductid' operator='eq' value='{0}' />
                            </filter>
                            <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Prod'>
                              <attribute name='ats_ispackage' />
                            </link-entity>
                          </entity>
                        </fetch>", firstOppProd);

                TraceHelper.Trace(tracingService, "Fetching opp product to check package flag. firstOppProd={0}", firstOppProd);

                var results = service.RetrieveMultiple(new FetchExpression(fetch));
                TraceHelper.Trace(tracingService, "Opp product fetch count={0}", results != null ? results.Entities.Count : 0);

                var oppProd = results.Entities.FirstOrDefault();
                if (oppProd == null)
                {
                    TraceHelper.Trace(tracingService, "No opportunityproduct record found for ID: {0}", firstOppProd);
                    return;
                }

                Guid oppProdId = oppProd.Id;
                TraceHelper.Trace(tracingService, "Retrieved OppProd ID: {0}", oppProdId);

                bool isPackage = false;
                if (oppProd.Attributes.Contains("Prod.ats_ispackage") && oppProd["Prod.ats_ispackage"] != null)
                    isPackage = (bool)((AliasedValue)oppProd["Prod.ats_ispackage"]).Value;

                TraceHelper.Trace(tracingService, "isPackage={0} for firstOppProd={1}", isPackage, firstOppProd);

                int totalPackageOppProd = guidList.Count;
                TraceHelper.Trace(tracingService, "totalPackageOppProd={0}", totalPackageOppProd);

                for (int i = 0; i < totalPackageOppProd; i++)
                {
                    Guid packageOppProdGuid = guidList[i];
                    TraceHelper.Trace(tracingService, "Loop i={0} packageOppProdGuid={1}", i, packageOppProdGuid);

                    if (isPackage)
                    {
                        TraceHelper.Trace(tracingService, "Product is package for firstOppProd={0}, retrieving component OLIs linked to packageOppProdGuid={1}", firstOppProd, packageOppProdGuid);

                        var fetchComponentOppProd = string.Format(@"
                            <fetch>
                              <entity name='opportunityproduct'>
                                <attribute name='opportunityproductname' />
                                <filter type='and'>
                                  <condition attribute='ats_packagelineitem' operator='eq' value='{0}' />
                                </filter>
                              </entity>
                            </fetch>", packageOppProdGuid);

                        var oppProdResults = service.RetrieveMultiple(new FetchExpression(fetchComponentOppProd));
                        TraceHelper.Trace(tracingService, "Component OLI fetch count={0} for packageOppProdGuid={1}", oppProdResults != null ? oppProdResults.Entities.Count : 0, packageOppProdGuid);

                        Guid OpportunityId = Guid.Empty;

                        foreach (var componentOppProd in oppProdResults.Entities)
                        {
                            Guid oppProductId = componentOppProd.Id;
                            TraceHelper.Trace(tracingService, "Processing componentOppProdId={0}", oppProductId);

                            string fetchXml = string.Format(@"
                                    <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                                      <entity name='opportunity'>
                                        <attribute name='name' />
                                        <attribute name='ats_agreement' />
                                        <attribute name='customerid' />
                                        <attribute name='estimatedvalue' />
                                        <attribute name='statuscode' />
                                        <attribute name='opportunityid' />
                                        <order attribute='name' descending='false' />
                                        <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' link-type='inner' alias='OP'>
                                          <filter type='and'>
                                            <condition attribute='opportunityproductid' operator='eq' uitype='opportunityproduct' value='{0}' />
                                          </filter>
                                          <attribute name='ats_quantity' />
                                          <attribute name='ats_quantityofevents' />
                                          <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='outer' alias='IBS'>
                                            <attribute name='ats_inventorybyseasonid' />
                                            <attribute name='ats_quantitypitched' />
                                            <attribute name='ats_quantitysold' />
                                            <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='outer' alias='Rate'>
                                              <attribute name='ats_ratetype' />
                                            </link-entity>
                                          </link-entity>
                                        </link-entity>
                                        <link-entity name='ats_agreement' from='ats_agreementid' to='ats_agreement' link-type='inner' alias='Agreement'>
                                          <attribute name='ats_bpfstatus' />
                                        </link-entity>
                                      </entity>
                                    </fetch>", componentOppProd.Id);

                            EntityCollection oppAssociatedProduct = service.RetrieveMultiple(new FetchExpression(fetchXml));
                            TraceHelper.Trace(tracingService, "Number of opportunities found for componentOppProdId={0}: {1}", componentOppProd.Id, oppAssociatedProduct != null ? oppAssociatedProduct.Entities.Count : 0);

                            Guid oppId = Guid.Empty;

                            foreach (Entity oppObj in oppAssociatedProduct.Entities)
                            {
                                OpportunityId = oppObj.Id;
                                oppId = oppObj.Id;

                                TraceHelper.Trace(tracingService, "Updating inventory for delete. oppId={0} componentOppProdId={1}", oppId, componentOppProd.Id);

                                UpdateInventoryOfDeleteOppProd(oppObj, tracingService, service);

                                TraceHelper.Trace(tracingService, "Inventory update complete. oppId={0} componentOppProdId={1}", oppId, componentOppProd.Id);
                            }

                            service.Delete("opportunityproduct", componentOppProd.Id);
                            TraceHelper.Trace(tracingService, "Successfully deleted Opportunity Product: {0}", componentOppProd.Id);
                        }
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Product is not a package for the OPP Prod: {0}", firstOppProd);
                    }

                    break;
                }

               

                TraceHelper.Trace(tracingService, "End {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
        }

        /// <summary>
        /// Update the Total Component OLIS rolup
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracing"></param>
        /// <param name="parentOliId"></param>
        /// <param name="rollupFieldLogicalName"></param>
        public static void RecalculatTotalComponentOLIeRollup(IOrganizationService service, ITracingService tracing, Guid parentOliId)
        {
            var request = new CalculateRollupFieldRequest
            {
                Target = new EntityReference("opportunityproduct", parentOliId),
                FieldName = "ats_totalcomponentolis" 
            };

            service.Execute(request);

            tracing.Trace($"Rollup recalculated: ats_totalcomponentolis for Parent OLI {parentOliId}");
        }


        //Sunny(31-01-25)
        /// <summary>
        /// delete the Opportunity Product record
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tempData"></param>
        public void DeleteOpportunityProducts(int depth, CodeActivityContext context, ref string deleteAgreementId, string oppProdIDJsonInput, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "DeleteOpportunityProducts";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} depth={1}", functionName, depth);
                TraceHelper.Trace(tracingService, "Input JSON: {0}", oppProdIDJsonInput);

                if (service == null)
                {
                    TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName);
                    return;
                }

                if (string.IsNullOrWhiteSpace(oppProdIDJsonInput))
                {
                    TraceHelper.Trace(tracingService, "oppProdIDJsonInput is empty, exiting {0}", functionName);
                    return;
                }

                // Deserialize JSON
                List<Guid> guidList = JsonSerializer.Deserialize<List<Guid>>(oppProdIDJsonInput);
                TraceHelper.Trace(tracingService, "Number of GUIDs received: {0}", guidList != null ? guidList.Count : 0);

                if (guidList == null || guidList.Count == 0)
                {
                    TraceHelper.Trace(tracingService, "GUID list empty after deserialize, exiting {0}", functionName);
                    return;
                }


                // First OLI
                Guid firstOppProd = guidList[0];
                TraceHelper.Trace(tracingService, "firstOppProd: {0}", firstOppProd);


                #region getting the packageoli Detaiils (Sunny-->20-2-26)
                Guid parentOliId = Guid.Empty;
                Entity packageOLI = service.Retrieve("opportunityproduct", firstOppProd, new ColumnSet("ats_packagelineitem"));
                if (packageOLI.Contains("ats_packagelineitem") && packageOLI.GetAttributeValue<EntityReference>("ats_packagelineitem") != null)
                {
                    EntityReference parentOliRef =
                        packageOLI.GetAttributeValue<EntityReference>("ats_packagelineitem");

                    parentOliId = parentOliRef.Id;
                    TraceHelper.Trace(tracingService, "parentOliId from the opp Prod: {0}", parentOliId);
                }
                #endregion


                // Handle package OLI + component deletes
                IsProductPackageOLIAndDeleteCompOppProd(ref guidList, firstOppProd, tracingService, service);

                Guid agreementId = Guid.Empty;
                Guid OpportunityId = Guid.Empty;

                foreach (Guid guid in guidList)
                {
                    TraceHelper.Trace(tracingService, "Processing OLI Guid: {0}", guid);

                    string fetchXml = string.Format(@"
                                <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
                                  <entity name='opportunity'>
                                    <attribute name='name' />
                                    <attribute name='ats_agreement' />
                                    <attribute name='customerid' />
                                    <attribute name='estimatedvalue' />
                                    <attribute name='statuscode' />
                                    <attribute name='opportunityid' />
                                    <order attribute='name' descending='false' />
                                    <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' link-type='inner' alias='OP'>
                                      <filter type='and'>
                                        <condition attribute='opportunityproductid' operator='eq' value='{0}' />
                                      </filter>
                                      <attribute name='ats_quantity' />
                                      <attribute name='ats_quantityofevents' />
                                      <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='outer' alias='IBS'>
                                        <attribute name='ats_inventorybyseasonid' />
                                        <attribute name='ats_quantitypitched' />
                                        <attribute name='ats_quantitysold' />
                                        <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='outer' alias='Rate'>
                                          <attribute name='ats_ratetype' />
                                        </link-entity>
                                      </link-entity>
                                    </link-entity>
                                    <link-entity name='ats_agreement' from='ats_agreementid' to='ats_agreement' link-type='inner' alias='Agreement'>
                                      <attribute name='ats_bpfstatus' />
                                    </link-entity>
                                  </entity>
                                </fetch>", guid);

                    EntityCollection oppAssociatedProduct = service.RetrieveMultiple(new FetchExpression(fetchXml));
                    TraceHelper.Trace(tracingService, "Associated opportunity count: {0}", oppAssociatedProduct.Entities.Count);

                    Guid oppId = Guid.Empty;

                    foreach (Entity oppObj in oppAssociatedProduct.Entities)
                    {
                        OpportunityId = oppObj.Id;
                        oppId = oppObj.Id;

                        TraceHelper.Trace(tracingService, "Processing oppId={0} for delete inventory update", oppId);

                        UpdateInventoryOfDeleteOppProd(oppObj, tracingService, service);

                        if (oppObj.Contains("ats_agreement") && oppObj["ats_agreement"] is EntityReference agreementRef)
                        {
                            agreementId = agreementRef.Id;
                        }
                        else
                        {
                            agreementId = Guid.Empty;
                        }

                        deleteAgreementId = agreementId.ToString();
                        TraceHelper.Trace(tracingService, "agreementId resolved: {0}", agreementId);
                    }

                    // Delete OLI
                    service.Delete("opportunityproduct", guid);
                    TraceHelper.Trace(tracingService, "Successfully deleted Opportunity Product: {0}", guid);

                    // Update total rate card rollup
                    try
                    {
                        if (oppId != Guid.Empty) 
                        {
                            TraceHelper.Trace(tracingService, "Updating total rate card rollup. oppId={0}", oppId);

                            var calculateRollup = new CalculateRollupFieldRequest
                            {
                                Target = new EntityReference("opportunity", oppId),
                                FieldName = "ats_totalratecard"
                            };

                            service.Execute(calculateRollup);
                            TraceHelper.Trace(tracingService, "Total rate card rollup updated successfully for oppId={0}", oppId);
                        }

                       
                    }
                    catch (Exception e)
                    {
                        TraceHelper.Trace(tracingService, "Rollup update failed (non-blocking): {0}", e.Message);
                    }

                    break;
                }


                if (parentOliId != Guid.Empty)
                {
                    TraceHelper.Trace(tracingService, "Recalculating total component OLIS rollup for parent OLI: {0}", parentOliId);
                    RecalculatTotalComponentOLIeRollup(service, tracingService, parentOliId);
                }



                // Remove processed GUID
                guidList.RemoveAt(0);

                string updatedJsonOutput = string.Empty;

                if (guidList.Count > 0)
                {
                    TraceHelper.Trace(tracingService, "Remaining GUID count: {0}", guidList.Count);

                    updatedJsonOutput = JsonSerializer.Serialize(guidList);
                    DeleteopportunityGuids.Set(context, updatedJsonOutput);
                    isDeleteRecalOppLines.Set(context, true);
                    FunctionalityActionName.Set(context, "DeleteRecalOppLines");
                    DeleteRecalOppLines.Set(context, OpportunityId.ToString());
                }
                else
                {
                    TraceHelper.Trace(tracingService, "No more GUIDs remaining, final iteration");

                    isDeleteRecalOppLines.Set(context, false);
                    guidList.Add(OpportunityId);
                    DeleteRecalOppLines.Set(context, OpportunityId.ToString());

                    updatedJsonOutput = JsonSerializer.Serialize(guidList);
                    DeleteopportunityGuids.Set(context, updatedJsonOutput);
                    FunctionalityActionName.Set(context, "DeleteCalculateTotalEscalate");
                }

                TraceHelper.Trace(tracingService, "Output JSON: {0}", updatedJsonOutput);
                TraceHelper.Trace(tracingService, "End {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message), ex);
            }
        }


        public void UpdateInventoryOfDeleteOppProd(Entity oppObj, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "UpdateInventoryOfDeleteOppProd";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0}", functionName);

                if (oppObj == null)
                {
                    TraceHelper.Trace(tracingService, "oppObj is null, exiting {0}", functionName);
                    return;
                }

                // ---------------------------
                // Read aliased IBS quantities
                // ---------------------------
                decimal quantityPitched = oppObj.Contains("IBS.ats_quantitypitched")
                    ? Convert.ToDecimal((oppObj["IBS.ats_quantitypitched"] as AliasedValue)?.Value ?? 0)
                    : 0;

                decimal quantitySold = oppObj.Contains("IBS.ats_quantitysold")
                    ? Convert.ToDecimal((oppObj["IBS.ats_quantitysold"] as AliasedValue)?.Value ?? 0)
                    : 0;

                // ---------------------------
                // Rate Type
                // ---------------------------
                OptionSetValue rateType = oppObj.Contains("Rate.ats_ratetype")
                    ? (oppObj["Rate.ats_ratetype"] as AliasedValue)?.Value as OptionSetValue
                    : null;

                // ---------------------------
                // BPF Status
                // ---------------------------
                OptionSetValue bpfStatus = oppObj.Contains("Agreement.ats_bpfstatus")
                    ? (oppObj["Agreement.ats_bpfstatus"] as AliasedValue)?.Value as OptionSetValue
                    : null;

                // ---------------------------
                // Inventory By Season Id
                // ---------------------------
                Guid? inventoryBySeasonId = oppObj.Contains("IBS.ats_inventorybyseasonid")
                    ? (oppObj["IBS.ats_inventorybyseasonid"] as AliasedValue)?.Value as Guid?
                    : null;

                // ---------------------------
                // OLI quantities
                // ---------------------------
                decimal quantity = oppObj.Contains("OP.ats_quantity")
                    ? Convert.ToDecimal((oppObj["OP.ats_quantity"] as AliasedValue)?.Value ?? 0)
                    : 0;

                decimal quantityOfEvents = oppObj.Contains("OP.ats_quantityofevents")
                    ? Convert.ToDecimal((oppObj["OP.ats_quantityofevents"] as AliasedValue)?.Value ?? 0)
                    : 0;

                // ---------------------------
                // Tracing snapshot
                // ---------------------------
                TraceHelper.Trace(tracingService,
                    "InventoryUpdate Snapshot | IBS={0}, Qty={1}, QtyEvents={2}, Pitched={3}, Sold={4}, RateType={5}, BpfStatus={6}",
                    inventoryBySeasonId,
                    quantity,
                    quantityOfEvents,
                    quantityPitched,
                    quantitySold,
                    rateType?.Value,
                    bpfStatus?.Value);

                if (!inventoryBySeasonId.HasValue || inventoryBySeasonId.Value == Guid.Empty)
                {
                    TraceHelper.Trace(tracingService, "InventoryBySeasonId missing, skipping update");
                    return;
                }

                if (rateType == null || bpfStatus == null)
                {
                    TraceHelper.Trace(tracingService, "RateType or BpfStatus missing, skipping inventory update");
                    return;
                }

                // ---------------------------
                // Build update entity
                // ---------------------------
                Entity updateInventory = new Entity("ats_inventorybyseason", inventoryBySeasonId.Value);

                // Pitched
                if (bpfStatus.Value == 114300001)
                {
                    decimal delta = rateType.Value == 114300000
                        ? quantity
                        : (quantity * quantityOfEvents);

                    updateInventory["ats_quantitypitched"] = quantityPitched - delta;
                    TraceHelper.Trace(tracingService, "Pitched stage adjustment: -{0}", delta);
                }
                // Closed Won
                else if (bpfStatus.Value == 114300003)
                {
                    decimal delta = rateType.Value == 114300000
                        ? quantity
                        : (quantity * quantityOfEvents);

                    updateInventory["ats_quantitysold"] = quantitySold - delta;
                    TraceHelper.Trace(tracingService, "ClosedWon stage adjustment: -{0}", delta);
                }
                else
                {
                    TraceHelper.Trace(tracingService, "BPF status not eligible for inventory update");
                    return;
                }

                service.Update(updateInventory);
                TraceHelper.Trace(tracingService, "Inventory updated successfully for IBS={0}", inventoryBySeasonId.Value);

                TraceHelper.Trace(tracingService, "End {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
        }




        /// <param name="productId"></param>
        /// <param name="seasonIds"></param>
        /// <param name="tracingService"></param>
        /// /// <param name="service"></param>
        /// <returns></returns>
        private string GetAvailableSeasons(string productId, string seasonIds, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "GetAvailableSeasons";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} | productId={1} | seasonIds={2}", functionName, productId, seasonIds);

                if (string.IsNullOrWhiteSpace(seasonIds))
                {
                    TraceHelper.Trace(tracingService, "No seasonIds provided, exiting {0}", functionName);
                    return string.Empty;
                }

                var allSeasonIds = seasonIds
                    .Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                TraceHelper.Trace(tracingService, "Total input seasons count={0}", allSeasonIds.Count);

                var excludedSeasonIds = new HashSet<string>();

                // ---------------------------
                // Build exclusion filter
                // ---------------------------
                var seasonFilter = new StringBuilder("<condition attribute='ats_season' operator='in'>");
                foreach (var id in allSeasonIds)
                    seasonFilter.AppendFormat("<value>{0}</value>", id);
                seasonFilter.Append("</condition>");

                TraceHelper.Trace(tracingService, "Season IN filter built");

                string exclusionFetch = $@"
            <fetch>
              <entity name='ats_inventorybyseason'>
                <attribute name='ats_season' />
                <filter type='and'>
                  <condition attribute='ats_product' operator='eq' value='{productId}' />
                  {seasonFilter}
                  <filter type='or'>
                    <condition attribute='statecode' operator='ne' value='0' />
                    <condition attribute='ats_notavailable' operator='eq' value='1' />
                  </filter>
                </filter>
              </entity>
            </fetch>";

                var exclusions = service.RetrieveMultiple(new FetchExpression(exclusionFetch));
                TraceHelper.Trace(tracingService, "Excluded IBS count={0}", exclusions.Entities.Count);

                foreach (var e in exclusions.Entities)
                {
                    var seasonRef = e.GetAttributeValue<EntityReference>("ats_season");
                    if (seasonRef != null)
                        excludedSeasonIds.Add(seasonRef.Id.ToString());
                }

                // ---------------------------
                // Remaining seasons
                // ---------------------------
                var remainingSeasonIds = allSeasonIds
                    .Where(id => !excludedSeasonIds.Contains(id))
                    .ToList();

                if (!remainingSeasonIds.Any())
                {
                    TraceHelper.Trace(tracingService, "No remaining valid seasons after exclusion");
                    return string.Empty;
                }

                TraceHelper.Trace(tracingService, "Remaining valid seasons count={0}", remainingSeasonIds.Count);

                // ---------------------------
                // Fetch valid seasons
                // ---------------------------
                var remainingFilter = new StringBuilder("<condition attribute='ats_seasonid' operator='in'>");
                foreach (var id in remainingSeasonIds)
                    remainingFilter.AppendFormat("<value>{0}</value>", id);
                remainingFilter.Append("</condition>");

                string validSeasonFetch = $@"
            <fetch>
              <entity name='ats_season'>
                <attribute name='ats_seasonid' />
                <attribute name='ats_name' />
                <attribute name='ats_startdate' />
                <filter>{remainingFilter}</filter>
                <order attribute='ats_startdate' />
              </entity>
            </fetch>";

                var seasonResults = service.RetrieveMultiple(new FetchExpression(validSeasonFetch));
                TraceHelper.Trace(tracingService, "Valid season records retrieved={0}", seasonResults.Entities.Count);

                // ---------------------------
                // Build response
                // ---------------------------
                var availableSeasons = new List<Dictionary<string, string>>();

                foreach (var s in seasonResults.Entities)
                {
                    var name = s.GetAttributeValue<string>("ats_name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        availableSeasons.Add(new Dictionary<string, string>
                {
                    { "value", s.Id.ToString() },
                    { "label", name }
                });
                    }
                }

                #region Making the response in the input order of season Ids
                // ---------------------------
                // Build response (PRESERVE INPUT ORDER)
                // ---------------------------
                var seasonMap = seasonResults.Entities
                    .Where(s => s.Contains("ats_name"))
                    .ToDictionary(
                        s => s.Id.ToString(),
                        s => s.GetAttributeValue<string>("ats_name")
                    );

                var availableSeasonsUpdated = new List<string>();

                foreach (var seasonId in remainingSeasonIds) // SAME ORDER AS INPUT
                {
                    if (seasonMap.TryGetValue(seasonId, out var seasonName))
                    {
                        availableSeasonsUpdated.Add($"{seasonName}|{seasonId}");
                    }
                }

                string response = string.Join(";", availableSeasonsUpdated);
                #endregion

                //string response = string.Join(";", availableSeasons.Select(x => $"{x["label"]}|{x["value"]}"));

                TraceHelper.Trace(tracingService, "Response built | count={0}", availableSeasonsUpdated.Count);
                TraceHelper.Trace(tracingService, "End {0}", functionName);

                return response;
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
        }

        //Sunny(5-02-25)

        /// <summary>
        /// updating the total deal value of the Agreement
        /// </summary>
        /// <param name="agreementId"></param>
        /// <param name="tracingService"></param>
        /// <param name="service"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public void totalDealValueAgreement(string agreementId, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "totalDealValueAgreement";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} | agreementId={1}", functionName, agreementId);

                if (string.IsNullOrWhiteSpace(agreementId) || !Guid.TryParse(agreementId, out Guid agreementGuid))
                {
                    TraceHelper.Trace(tracingService, "Invalid agreementId supplied, exiting {0}", functionName);
                    return;
                }

                decimal totalAgreementDealValue = 0m;

                QueryExpression opportunityQuery = new QueryExpression("opportunity")
                {
                    ColumnSet = new ColumnSet("opportunityid", "ats_startseason", "ats_dealvalue", "ats_pricingmode"),
                    Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("ats_agreement", ConditionOperator.Equal, agreementGuid)
                }
            },
                    Orders =
            {
                new OrderExpression("ats_startseason", OrderType.Ascending)
            }
                };

                TraceHelper.Trace(tracingService, "Retrieving opportunities for agreement {0}", agreementGuid);

                EntityCollection opportunities = service.RetrieveMultiple(opportunityQuery);
                TraceHelper.Trace(tracingService, "Opportunity count={0}", opportunities.Entities.Count);

                foreach (Entity opp in opportunities.Entities)
                {
                    Money oppRevenue = opp.GetAttributeValue<Money>("ats_dealvalue") ?? new Money(0);
                    totalAgreementDealValue += oppRevenue.Value;

                    TraceHelper.Trace(
                        tracingService,
                        "OppId={0} | DealValue={1}",
                        opp.Id,
                        oppRevenue.Value);

                    // ---------------------------
                    // Ensure Pricing Mode retained
                    // ---------------------------
                    OptionSetValue pricingMode = opp.GetAttributeValue<OptionSetValue>("ats_pricingmode")
                        ?? new OptionSetValue(559240000); // Automatic (default)

                    Entity oppUpdate = new Entity("opportunity", opp.Id)
                    {
                        ["ats_pricingmode"] = pricingMode
                    };

                    service.Update(oppUpdate);

                    TraceHelper.Trace(
                        tracingService,
                        "PricingMode ensured for OppId={0} | Value={1}",
                        opp.Id,
                        pricingMode.Value);
                }

                // ---------------------------
                // Update Agreement total deal
                // ---------------------------
                Entity agreementUpdate = new Entity("ats_agreement", agreementGuid)
                {
                    ["ats_totaldealvalue"] = totalAgreementDealValue
                };

                service.Update(agreementUpdate);

                TraceHelper.Trace(
                    tracingService,
                    "Agreement total deal value updated | AgreementId={0} | Total={1}",
                    agreementGuid,
                    totalAgreementDealValue);

                TraceHelper.Trace(tracingService, "End {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
        }


        #region Helper Methods for bulk record creation and Bulk record Updation 
        //Sunny(21-12-2025)
        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="entities"></param>
        /// <param name="chunkSize"></param>
        public static void CreateBulkInChunks(IOrganizationService service, List<Entity> entities, int chunkSize)
        {
            if (entities == null || entities.Count == 0)
                return;

            // Validate entities (prevents "type none")
            for (int i = entities.Count - 1; i >= 0; i--)
            {
                var e = entities[i];

                if (e == null)
                {
                    entities.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(e.LogicalName))
                {
                    throw new InvalidPluginExecutionException(
                        "CreateMultiple failed because an entity in the list has no LogicalName (type 'none'). " +
                        "Index: " + i);
                }
            }

            if (entities.Count == 0)
                return;

            for (int i = 0; i < entities.Count; i += chunkSize)
            {
                int take = Math.Min(chunkSize, entities.Count - i);

                var chunk = new List<Entity>(take);
                for (int j = 0; j < take; j++)
                    chunk.Add(entities[i + j]);

                var req = new CreateMultipleRequest
                {
                    Targets = new EntityCollection(chunk)
                };

                service.Execute(req);
            }
        }


        //Sunny(21-12-2025)
        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="entities"></param>
        /// <param name="chunkSize"></param>
        public static void UpdateBulkInChunks(IOrganizationService service, List<Entity> entities, int chunkSize)
        {
            if (entities == null || entities.Count == 0)
                return;

            // Validate + cleanup nulls
            for (int i = entities.Count - 1; i >= 0; i--)
            {
                var e = entities[i];

                if (e == null)
                {
                    entities.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(e.LogicalName))
                {
                    throw new InvalidPluginExecutionException(
                        "UpdateMultiple failed because an entity in the list has no LogicalName (type 'none'). " +
                        "Index: " + i);
                }

                if (e.Id == Guid.Empty)
                {
                    throw new InvalidPluginExecutionException(
                        "UpdateMultiple failed because an entity in the list has an empty Id. " +
                        "LogicalName: " + e.LogicalName + ", Index: " + i);
                }
            }

            if (entities.Count == 0)
                return;

            // Chunk update
            for (int i = 0; i < entities.Count; i += chunkSize)
            {
                int take = Math.Min(chunkSize, entities.Count - i);

                var chunk = new List<Entity>(take);
                for (int j = 0; j < take; j++)
                    chunk.Add(entities[i + j]);

                //UpdateMultiple should NOT mix entity logical names in one request
                string logicalName = chunk[0].LogicalName;
                for (int k = 1; k < chunk.Count; k++)
                {
                    if (!string.Equals(chunk[k].LogicalName, logicalName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidPluginExecutionException(
                            "UpdateMultiple failed because the chunk contains mixed entity types. " +
                            "Expected: " + logicalName + ", Found: " + chunk[k].LogicalName + ", ChunkIndex: " + i);
                    }
                }

                var req = new UpdateMultipleRequest
                {
                    Targets = new EntityCollection(chunk)
                };

                service.Execute(req);
            }
        }


        #endregion




        //Sunny(06-08-25)
        /// <summary>
        /// to update the quantity pitched and sold in the IBS when new product is getting added 
        /// </summary>
        /// <param name="seasonId"></param>
        /// <param name="productId"></param>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public void UpdateQtyPitchedSoldForAddProduct(int qtyUnits, int qtyEvents, int rateType, int bpfStatusValue, Guid seasonId, Guid productId, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "UpdateQtyPitchedSoldForAddProduct";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} | qtyUnits={1} qtyEvents={2} rateType={3} bpfStatusValue={4} seasonId={5} productId={6}", functionName, qtyUnits, qtyEvents, rateType, bpfStatusValue, seasonId, productId);

                if (service == null) { TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName); return; }
                if (seasonId == Guid.Empty || productId == Guid.Empty) { TraceHelper.Trace(tracingService, "seasonId/productId empty, exiting {0}", functionName); return; }

                string fetchXml = string.Format(@"
            <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false' top='1'>
              <entity name='ats_inventorybyseason'>
                <attribute name='ats_inventorybyseasonid' />
                <attribute name='ats_quantitypitched' />
                <attribute name='ats_quantitysold' />
                <attribute name='ats_quantityavailable' />
                <attribute name='ats_totalquantity' />
                <filter type='and'>
                  <condition attribute='ats_season' operator='eq' value='{0}' />
                  <condition attribute='ats_product' operator='eq' value='{1}' />
                </filter>
              </entity>
            </fetch>", seasonId, productId);

                EntityCollection inventoryResults = service.RetrieveMultiple(new FetchExpression(fetchXml));
                TraceHelper.Trace(tracingService, "IBS fetch count={0}", inventoryResults != null ? inventoryResults.Entities.Count : 0);

                if (inventoryResults == null || inventoryResults.Entities.Count == 0)
                {
                    TraceHelper.Trace(tracingService, "No IBS found for seasonId={0} productId={1}", seasonId, productId);
                    return;
                }

                Entity inventory = inventoryResults.Entities[0];
                Guid ibsId = inventory.Id;
                TraceHelper.Trace(tracingService, "IBS found ibsId={0}", ibsId);

                decimal quantityPitched = inventory.Contains("ats_quantitypitched") && inventory["ats_quantitypitched"] != null ? Convert.ToDecimal(inventory["ats_quantitypitched"]) : 0m;
                decimal quantitySold = inventory.Contains("ats_quantitysold") && inventory["ats_quantitysold"] != null ? Convert.ToDecimal(inventory["ats_quantitysold"]) : 0m;
                decimal quantityAvailable = inventory.Contains("ats_quantityavailable") && inventory["ats_quantityavailable"] != null ? Convert.ToDecimal(inventory["ats_quantityavailable"]) : 0m;
                decimal totalQuantity = inventory.Contains("ats_totalquantity") && inventory["ats_totalquantity"] != null ? Convert.ToDecimal(inventory["ats_totalquantity"]) : 0m;

                TraceHelper.Trace(tracingService, "IBS snapshot | pitched={0} sold={1} available={2} total={3}", quantityPitched, quantitySold, quantityAvailable, totalQuantity);

                // Compute delta based on rate type
                decimal delta = (rateType == 114300000) ? qtyUnits : (qtyUnits * qtyEvents);
                TraceHelper.Trace(tracingService, "Computed delta={0} (rateType={1})", delta, rateType);

                // Apply based on BPF stage
                if (bpfStatusValue == 114300001) // Pitched
                {
                    inventory["ats_quantitypitched"] = quantityPitched + delta;
                    TraceHelper.Trace(tracingService, "Pitched stage update | newPitched={0}", (quantityPitched + delta));
                }
                else if (bpfStatusValue == 114300003) // Closed Won
                {
                    inventory["ats_quantitysold"] = quantitySold + delta;
                    TraceHelper.Trace(tracingService, "ClosedWon stage update | newSold={0}", (quantitySold + delta));
                }
                else
                {
                    TraceHelper.Trace(tracingService, "BPF status not eligible for update bpfStatusValue={0}, exiting", bpfStatusValue);
                    return;
                }

                service.Update(inventory);
                TraceHelper.Trace(tracingService, "IBS updated successfully ibsId={0}", ibsId);
                TraceHelper.Trace(tracingService, "End {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message), ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(string.Format("functionName: {0}, Exception: {1}", functionName, ex.Message), ex);
            }
        }



        //Sunny(28-01-25)
        /// <summary>
        /// To Create the unique Guid 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <returns></returns>
        public string UniqueGuidGeneration(IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "UniqueGuidGeneration";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0}", functionName);

                // Generate high-precision UTC timestamp
                string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

                TraceHelper.Trace(tracingService, "Generated timestamp={0}", timestamp);

                // Timestamp itself is the unique value
                string uniqueValue = timestamp;

                TraceHelper.Trace(tracingService, "Generated unique value={0}", uniqueValue);
                TraceHelper.Trace(tracingService, "End {0}", functionName);

                return uniqueValue;
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="settingKey"></param>
        /// <param name="tracingService"></param>
        /// <returns></returns>
        internal Guid GetUnitOfMeasure(IOrganizationService service, string settingKey, ITracingService tracingService)
        {
            string functionName = "GetUnitOfMeasure";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} | settingKey={1}", functionName, settingKey);

                if (service == null)
                {
                    TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName);
                    return Guid.Empty;
                }

                if (string.IsNullOrWhiteSpace(settingKey))
                {
                    TraceHelper.Trace(tracingService, "settingKey is null or empty, exiting {0}", functionName);
                    return Guid.Empty;
                }

                string fetchXml = string.Format(@"
            <fetch top='1'>
              <entity name='ats_agiliteksettings'>
                <attribute name='ats_value' />
                <filter>
                  <condition attribute='ats_key' operator='eq' value='{0}' />
                </filter>
              </entity>
            </fetch>", settingKey);

                EntityCollection result = service.RetrieveMultiple(new FetchExpression(fetchXml));
                TraceHelper.Trace(tracingService, "Settings fetch count={0}", result.Entities.Count);

                if (result.Entities.Count == 0)
                {
                    TraceHelper.Trace(tracingService, "No setting found for key={0}", settingKey);
                    return Guid.Empty;
                }

                Entity setting = result.Entities[0];
                string value = setting.GetAttributeValue<string>("ats_value");

                TraceHelper.Trace(tracingService, "ats_value={0}", value);

                if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out Guid parsedGuid))
                {
                    TraceHelper.Trace(tracingService, "Valid GUID parsed for key={0}", settingKey);
                    TraceHelper.Trace(tracingService, "End {0}", functionName);
                    return parsedGuid;
                }

                TraceHelper.Trace(tracingService, "ats_value is missing or not a valid GUID for key={0}", settingKey);
                TraceHelper.Trace(tracingService, "End {0}", functionName);

                return Guid.Empty;
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
        }


    }
}
