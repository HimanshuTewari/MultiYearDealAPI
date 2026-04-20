using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Web.Services.Description;

namespace MultiYearDeal.Workflows
{
    public class BulkOppLineUpdateAgreementCartAction : CodeActivity
    {
        [Input("InputOppLines")]
        public InArgument<string> InputOppLines { get; set; }

        [Input("InputAction")]
        public InArgument<string> InputAction { get; set; }

        [Output("MergedOutputJSON")] 
        public OutArgument<string> MergedOutputJson { get; set; }

        [Output("OutputActionName")]
        public OutArgument<string> OutputActionName { get; set; }

        protected override void Execute(CodeActivityContext context)
        {
            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            ITracingService tracingService = context.GetExtension<ITracingService>();

            string functionName = "Execute";
            try
            {
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Tracing found Enabled");
                TraceHelper.Trace(tracingService, "functionName: {0}", functionName);

                string inputActionName = InputAction.Get(context);

                List<OppLineUpdateModel> mergedLines = null;
                if (inputActionName == "BulkUpdateOppLine")
                {
                    string rawJson = InputOppLines.Get(context);

                    TraceHelper.Trace(tracingService, "Input action name: {0}", inputActionName);

                    if (string.IsNullOrWhiteSpace(rawJson))
                    {
                        MergedOutputJson.Set(context, "[]");
                        return;
                    }

                    // Step 1: Deserialize incoming JSON into a list of typed models
                    List<OppLineUpdateModel> incomingLines = ReadInputPayload(rawJson, tracingService);

                    // Step 2: Merge records by id 
                    mergedLines = MergeOppLines(incomingLines, tracingService);


                    if (mergedLines == null || mergedLines.Count == 0)
                    {
                        TraceHelper.Trace(tracingService, "No records found in mergedLines.");
                        return;
                    }

                    // Get first record only
                    var line = mergedLines[0];

                    if (!line.id.HasValue)
                    {
                        TraceHelper.Trace(tracingService, "Skipping first record because id is missing.");
                        return;
                    }

                    // Map fields
                    string oppProduct = line.id.Value.ToString();
                    string opp = line.opportunity.HasValue ? line.opportunity.Value.ToString() : string.Empty;

                    string description = line.Description ?? string.Empty;
                    decimal totalValueOppProd = line.totalValue ?? 0m;
                    decimal hardCost = line.HardCost ?? 0m;
                    decimal productionCost = line.ProductionCost ?? 0m;
                    int qtyUnits = line.QtyUnits ?? 0;
                    int qtyEvents = line.QtyEvents ?? 0;
                    decimal rate = line.Rate ?? 0m;
                    string rateType = line.RateType ?? string.Empty;
                    bool isResetOverride = line.ResetOverride ?? false;
                    string legalDefinition = line.LegalDefinition ?? string.Empty;
                    bool isOverwriteLegalDefinition = line.OverwriteLegalDefinition ?? false;

                    // LOG
                    TraceHelper.Trace(tracingService, "Calling UpdateOppLineItems for FIRST OppProduct: {0}, Opp: {1}", oppProduct, opp);

                    // SINGLE-LINE CALL
                    UpdateOppLineItems(oppProduct, opp, description, totalValueOppProd, hardCost, productionCost, qtyUnits, qtyEvents, rate, rateType, isResetOverride, legalDefinition, isOverwriteLegalDefinition, tracingService, service);

                    TraceHelper.Trace(tracingService, "Remaining records count before removing first: {0}", mergedLines.Count);

                    TraceHelper.Trace(tracingService, "Removed the 1st opp Line data, and passing the data to the next iteration.");
                    mergedLines.RemoveAt(0);

                    TraceHelper.Trace(tracingService, "Remaining records count after removing first: {0}", mergedLines.Count);

                    //return if all the opp lines are updated.
                    if (mergedLines.Count == 0)
                    {
                        OutputActionName.Set(context, "Success");
                        return;
                    }


                    // Step 3: Serialize merged result back to JSON string (optional, still sending out)
                    string finalJson = JsonSerializer.Serialize(mergedLines);
                    TraceHelper.Trace(tracingService, "Merged JSON: " + finalJson);

                    MergedOutputJson.Set(context, finalJson);
                    TraceHelper.Trace(tracingService, "Action name is set");
                
                }
            

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

        private List<OppLineUpdateModel> ReadInputPayload(string jsonText, ITracingService trace)
        {
            List<OppLineUpdateModel> items;
            string trimmed = jsonText.TrimStart();

            if (trimmed.StartsWith("["))
            {
                items = JsonSerializer.Deserialize<List<OppLineUpdateModel>>(jsonText);
            }
            else
            {
                var single = JsonSerializer.Deserialize<OppLineUpdateModel>(jsonText);
                items = new List<OppLineUpdateModel>();
                if (single != null)
                {
                    items.Add(single);
                }
            }

            if (items == null)
            {
                items = new List<OppLineUpdateModel>();
            }

            TraceHelper.Trace(trace, "ReadInputPayload: Deserialized {0} items.", items.Count);
            return items;
        }

        /// <summary>
        /// Merges multiple records by their "id" field. 
        /// </summary>
        private List<OppLineUpdateModel> MergeOppLines(List<OppLineUpdateModel> input, ITracingService trace)
        {
            var result = new List<OppLineUpdateModel>();

            int index = 0;
            foreach (var item in input)
            {
                index++;

                if (item == null || !item.id.HasValue || item.id.Value == Guid.Empty)
                {
                    TraceHelper.Trace(trace, "MergeOppLines: Item #{0} skipped due to missing id.", index);
                    continue;
                }

                Guid id = item.id.Value;

                // Always treat each item as a new record, even if the id is repeated
                var copy = CloneOppLine(item);
                result.Add(copy);

                TraceHelper.Trace(trace, "MergeOppLines: New record added for id={0} at index {1}", id, result.Count - 1);
            }

            TraceHelper.Trace(trace, "MergeOppLines: Final count (no merging) = {0}", result.Count);
            return result;
        }

        /// <summary>
        /// Deep clone using JsonSerializer.Serialize / Deserialize (as you requested).
        /// </summary>
        private OppLineUpdateModel CloneOppLine(OppLineUpdateModel source)
        {
            string temp = JsonSerializer.Serialize(source);
            return JsonSerializer.Deserialize<OppLineUpdateModel>(temp);
        }

        // Your existing UpdateOppLineItems method goes here (as you pasted)
        public void UpdateOppLineItems(string oppProduct, string opp, string description, decimal totalValueOppProd, decimal hardCost, decimal productionCost, int qtyUnits, int qtyEvents, decimal rate, string rateType, bool isResetOverride, string legalDefinition, bool isOverwriteLegalDefinition, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "updateOppLineItems";
            try
            {
                TraceHelper.Trace(tracingService, "function Name: {0}", functionName);

                AgreementCartAction agreementActionObj = new AgreementCartAction();


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

                        agreementActionObj.updateTotalDealValAgree(agreementId, service, tracingService);
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

                            agreementActionObj.updateTotalDealValAgree(agreementId, service, tracingService);
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
                                  <filter>
                                    <condition attribute='opportunityid' operator='eq' value='{0}' />
                                  </filter>
                                </entity>
                              </fetch>", oppId);

                    TraceHelper.Trace(tracingService, "[SumHardCost] FetchXML:\n{0}", fetchXml);

                    var result = service.RetrieveMultiple(new FetchExpression(fetchXml));

                    foreach (var row in result.Entities)
                    {
                        var retreivedHardCost = row.GetAttributeValue<Money>("ats_hardcost");
                        if (retreivedHardCost != null)
                        {
                            totalHardCost += retreivedHardCost.Value;
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
                    agreementActionObj.updateTotalDealValAgree(aggrementRef.Id, service, tracingService);
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



    }

    /// <summary>
    /// Strongly typed model for Opp Line update payload.
    /// Property names are aligned with JSON fields coming from the front-end.
    /// </summary>
    public class OppLineUpdateModel
    {
        public Guid? id { get; set; }
        public string Description { get; set; }
        public decimal? HardCost { get; set; }
        public string LegalDefinition { get; set; }
        public bool? LockHardCost { get; set; }
        public bool? LockProductionCost { get; set; }
        public bool? LockRate { get; set; }
        public bool? OverwriteLegalDefinition { get; set; }
        public decimal? ProductionCost { get; set; }
        public int? QtyEvents { get; set; }
        public int? QtyUnits { get; set; }
        public decimal? QuantityAvailable { get; set; }
        public decimal? Rate { get; set; }
        public string RateType { get; set; }
        public string actionName { get; set; }
        public bool? isActive { get; set; }
        public Guid? opportunity { get; set; }
        public Guid? product2 { get; set; }
        public decimal? totalHardCost { get; set; }
        public decimal? totalProductionCost { get; set; }
        public decimal? totalValue { get; set; }

        // New: to map resetOverride from JSON
        public bool? ResetOverride { get; set; }
    }
}
