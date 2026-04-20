using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using System.Linq;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;

namespace MultiYearDeal
{
    public class TotalEsclateRevenue
    {
        /// <summary>
        /// function is reponsible for Updating the escalation across all year
        /// </summary>
        public void calTotalEscRevenue(
            bool isAddProdDeleteEscalationAllYear,
            string esclateActionName,
            Guid agreementId,
            string esclationType,
            decimal esclationValue,
            IOrganizationService service,
            ITracingService tracingService)
        {
            string functionName = "calTotalEscRevenue";
            try
            {
                //Calculate the Esclate Revenue acorss all years 
                if (esclateActionName == "RevenueEscalate" || esclateActionName == "AddProduct" || esclateActionName == "Delete" || esclateActionName == "updateOpportunityLineItem")
                {
                    tracingService.Trace("escalate Revenue across all years");

                    QueryExpression opportunityQuery = new QueryExpression("opportunity")
                    {
                        ColumnSet = new ColumnSet("opportunityid", "ats_startseason", "name", "ats_dealvalue", "ats_escalationvalue", "ats_escalationtype", "ats_pricingmode"),
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

                    Logging.Log($"done query expression", tracingService);

                    EntityCollection opportunityCollection = service.RetrieveMultiple(opportunityQuery);
                    Logging.Log($"opportunityCollection.Entities.Count: {opportunityCollection.Entities.Count}", tracingService);

                    int count = 0;
                    Money currOppDealValue = new Money(0);
                    decimal currOppValue = 0;

                    foreach (Entity opportunity in opportunityCollection.Entities)
                    {
                        Logging.Log("Entered opportunity processing", tracingService);

                        if (isAddProdDeleteEscalationAllYear == false)
                        {
                            if (string.IsNullOrWhiteSpace(esclationType))
                            {
                                opportunity["ats_escalationvalue"] = new Money(0);
                                opportunity["ats_escalationtype"] = null;
                                service.Update(opportunity);
                                tracingService.Trace("None Logic Executed");
                                continue;
                            }
                        }

                        if (count == 0)
                        {
                            Money oppDealValue = opportunity.GetAttributeValue<Money>("ats_dealvalue") ?? new Money(0);
                            tracingService.Trace("Processing first opportunity");
                            currOppDealValue = oppDealValue;
                        }

                        decimal addProductEscalationValue = opportunity.GetAttributeValue<Money>("ats_escalationvalue")?.Value ?? 0;
                        tracingService.Trace($"addProductEscalationValue: {addProductEscalationValue}");

                        var retrievedEscalationType = opportunity.Contains("ats_escalationtype")
                            ? ((OptionSetValue)opportunity["ats_escalationtype"]).Value
                            : 0;

                        if (isAddProdDeleteEscalationAllYear == false)
                        {
                            if (count > 0)
                            {
                                tracingService.Trace($"Escalation Type: {esclationType}");

                                if (esclationType == "Fixed")
                                {
                                    opportunity["ats_escalationtype"] = new OptionSetValue(114300000);  //Fixed
                                    currOppValue = count == 1 ? currOppDealValue.Value + esclationValue : currOppValue + esclationValue;
                                }
                                else if (esclationType == "Percent")
                                {
                                    opportunity["ats_escalationtype"] = new OptionSetValue(114300001);  //Percent
                                    currOppValue = count == 1
                                        ? currOppDealValue.Value * (1 + (esclationValue / 100))
                                        : currOppValue * (1 + (esclationValue / 100));
                                }
                            }
                        }
                        else
                        {
                            tracingService.Trace("isAddProdDeleteEscalationAllYear == true");
                            if (count > 0)
                            {
                                tracingService.Trace($"Escalation Type: {esclationType}");

                                if (retrievedEscalationType == 114300000) //Fixed
                                {
                                    opportunity["ats_escalationtype"] = new OptionSetValue(114300000);  //Fixed
                                    currOppValue = count == 1 ? currOppDealValue.Value + addProductEscalationValue : currOppValue + addProductEscalationValue;
                                }
                                else if (retrievedEscalationType == 114300001) //Percent
                                {
                                    opportunity["ats_escalationtype"] = new OptionSetValue(114300001);  //Percent
                                    currOppValue = count == 1
                                        ? currOppDealValue.Value * (1 + (addProductEscalationValue / 100))
                                        : currOppValue * (1 + (addProductEscalationValue / 100));
                                }
                            }
                        }

                        if (currOppValue > 0)
                        {
                            tracingService.Trace($"Updating opportunity with new deal value: {currOppValue}");

                            if (isAddProdDeleteEscalationAllYear == false)
                                opportunity["ats_escalationvalue"] = new Money(esclationValue);
                            else
                                opportunity["ats_escalationvalue"] = new Money(addProductEscalationValue);

                            opportunity["ats_dealvalue"] = new Money(currOppValue);

                            opportunity["ats_manualamount"] = new Money(currOppValue);
                            opportunity["ats_pricingmode"] = new OptionSetValue(559240001); //Manual

                            tracingService.Trace("Escalation across all year option set value of Escalation is set.");
                            service.Update(opportunity);
                            tracingService.Trace("Opportunity updated successfully");

                            // Call Recalculate Action
                            RecalculateOppLines(opportunity.Id, service, tracingService);

                            // Rollup calc (wrapped with same manner try/catch)
                            UpdateOppTotalRateCardRollup(opportunity.Id, service, tracingService);
                        }

                        count++;
                    }

                    AgreementCartAction agcartObj = new AgreementCartAction();
                    agcartObj.updateTotalDealValAgree(agreementId, service, tracingService);
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

        public void individualEscalateRevenue(
            string oppId,
            bool isManualOppId,
            string pricingMode,
            string esclateActionName,
            string esclatedOpp,
            string esclationType,
            decimal esclationValue,
            IOrganizationService service,
            ITracingService tracingService)
        {
            string functionName = "individualEscalateRevenue";
            TraceHelper.Trace(tracingService, "functionName: {0}", functionName);

            try
            {
                if (esclateActionName == "UpdateOpportunity" && esclatedOpp != null && esclatedOpp != string.Empty)
                {
                    Guid esclateOppId = Guid.TryParse(esclatedOpp, out Guid parsedAgreementId)
                        ? parsedAgreementId
                        : Guid.Empty;

                    if (esclateOppId != Guid.Empty)
                    {
                        ColumnSet columns = new ColumnSet("ats_dealvalue", "ats_agreement", "ats_startseason");
                        Entity retrievedOpportunity = service.Retrieve("opportunity", esclateOppId, columns);

                        EntityReference agreementRef = retrievedOpportunity.Contains("ats_agreement")
                            ? (EntityReference)retrievedOpportunity["ats_agreement"]
                            : null;

                        Guid agreeRefId = agreementRef.Id;

                        string retrieveOppFetchXml = $@"
                    <fetch version='1.0' top='50'>
                      <entity name='opportunity'>
                        <attribute name='statecode' />
                        <attribute name='name' />
                        <attribute name='opportunityid' />
                        <attribute name='ats_dealvalue' />
                        <attribute name='ats_pricingmode' />
                        <attribute name='ats_escalationtype' />
                        <attribute name='ats_escalationvalue' />
                        <filter type='and'>
                          <condition attribute='ats_agreement' operator='eq' value='{agreeRefId}' uitype='ats_agreement' />
                        </filter>
                        <link-entity name='ats_season' from='ats_seasonid' to='ats_startseason'>
                          <order attribute='ats_name' />
                        </link-entity>
                      </entity>
                    </fetch>
                    ";

                        EntityCollection oppCollection = service.RetrieveMultiple(new FetchExpression(retrieveOppFetchXml));
                        var prevOppDealValue = new Money(0);

                        TraceHelper.Trace(tracingService, "oppCollection Count: {0}", oppCollection.Entities.Count);

                        if (retrievedOpportunity != null)
                        {
                            bool isOppUpdated = false;

                            foreach (Entity opp in oppCollection.Entities)
                            {
                                TraceHelper.Trace(tracingService, "Entered in the for loop");

                                var retrievedEscalationValue = opp.Contains("ats_escalationvalue")
                                    ? ((Money)opp["ats_escalationvalue"]).Value
                                    : 0;

                                int currOppPricingMode = opp.Contains("ats_pricingmode")
                                    ? ((OptionSetValue)opp["ats_pricingmode"]).Value
                                    : 0;

                                TraceHelper.Trace(tracingService, "currOppPricingMode: {0}", currOppPricingMode);

                                var retrievedEscalationType = opp.Contains("ats_escalationtype")
                                    ? ((OptionSetValue)opp["ats_escalationtype"]).Value
                                    : 0;

                                if (currOppPricingMode == 559240001) // Manual
                                {
                                    if (isManualOppId)
                                    {
                                        TraceHelper.Trace(tracingService, "isManualOppId: {0}", isManualOppId);

                                        string currOppId = opp.Contains("opportunityid")
                                            ? opp["opportunityid"].ToString()
                                            : string.Empty;

                                        TraceHelper.Trace(tracingService, "currOppId: {0}", currOppId);
                                        TraceHelper.Trace(tracingService, "oppId: {0}", oppId);

                                        if (currOppId == oppId)
                                        {
                                            decimal updatedDealValue = opp.Contains("ats_dealvalue") && opp["ats_dealvalue"] != null
                                                ? ((Money)opp["ats_dealvalue"]).Value
                                                : 0m;

                                            TraceHelper.Trace(tracingService, "updatedDealValue: {0}", updatedDealValue);

                                            isOppUpdated = true;
                                            prevOppDealValue = new Money(updatedDealValue);
                                            continue;
                                        }
                                    }

                                    if (retrievedEscalationType == 114300000) // Fixed
                                    {
                                        TraceHelper.Trace(tracingService, "esclation type is fixed");

                                        opp["ats_dealvalue"] = new Money(prevOppDealValue.Value + retrievedEscalationValue);
                                        opp["ats_manualamount"] = new Money(prevOppDealValue.Value + retrievedEscalationValue);

                                        service.Update(opp);
                                        TraceHelper.Trace(tracingService, "retrievedOpportunity updated sucessfully");

                                        isOppUpdated = true;

                                        UpdateOppTotalRateCardRollup(opp.Id, service, tracingService);
                                    }
                                    else if (retrievedEscalationType == 114300001) // Percent
                                    {
                                        TraceHelper.Trace(tracingService, "Esclation type is percent");

                                        opp["ats_dealvalue"] = new Money(
                                            prevOppDealValue.Value + ((prevOppDealValue.Value) * retrievedEscalationValue / 100)
                                        );

                                        opp["ats_manualamount"] = new Money(
                                            prevOppDealValue.Value + ((prevOppDealValue.Value) * retrievedEscalationValue / 100)
                                        );

                                        service.Update(opp);
                                        TraceHelper.Trace(tracingService, "retrievedOpportunity updated sucessfully");

                                        isOppUpdated = true;

                                        UpdateOppTotalRateCardRollup(opp.Id, service, tracingService);
                                    }
                                }

                                if (isOppUpdated && currOppPricingMode == 559240000) // Automatic
                                {
                                    isOppUpdated = false;
                                    TraceHelper.Trace(tracingService, "isOppUpdated = false");
                                }

                                if (isOppUpdated && currOppPricingMode == 559240001) // Manual
                                {
                                    if (retrievedEscalationType == 114300000) // Fixed
                                    {
                                        prevOppDealValue = new Money(prevOppDealValue.Value + retrievedEscalationValue);
                                    }
                                    else if (retrievedEscalationType == 114300001) // Percent
                                    {
                                        prevOppDealValue = new Money(prevOppDealValue.Value + ((prevOppDealValue.Value) * retrievedEscalationValue / 100));
                                    }

                                    TraceHelper.Trace(tracingService, "prevOppDealValue: {0}", prevOppDealValue.Value);
                                }
                                else
                                {
                                    prevOppDealValue = opp.Contains("ats_dealvalue")
                                        ? (Money)opp["ats_dealvalue"]
                                        : new Money(0);

                                    TraceHelper.Trace(tracingService, "prevOppDealValue: {0}", prevOppDealValue.Value);
                                }

                                RecalculateOppLines(opp.Id, service, tracingService);
                            }

                            Guid agreementId = Guid.Empty;

                            if (agreementRef == null)
                            {
                                TraceHelper.Trace(tracingService, "agreementRef found null");
                                return;
                            }
                            else
                            {
                                agreementId = agreementRef.Id;
                                TraceHelper.Trace(tracingService, "agreementId: {0}", agreementId);

                                AgreementCartAction agcartObj = new AgreementCartAction();
                                agcartObj.updateTotalDealValAgree(agreementId, service, tracingService);
                            }
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, "retrievedOpportunity == null");
                        }
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "esclateOppId == Empty");
                    }
                }
            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message));
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException(string.Format("functionName: {0},Exception: {1}", functionName, ex.Message));
            }
        }

        /// <summary>
        /// Recalculating the Opp Lines based on the Opp. 
        /// </summary>
        public void RecalculateOppLines(Guid OppId, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "RecalculateOppLines";
            try
            {
                tracingService.Trace("Calling Recalculate Opportunity Lines action");
                OrganizationRequest actionRequest = new OrganizationRequest("ats_CalculateOpportunityLines")
                {
                    ["OppurtunityEntityReference"] = new EntityReference("opportunity", OppId),
                    ["Action"] = "ReCalcOppLinesAgreement"
                };

                service.Execute(actionRequest);
                tracingService.Trace($"Action executed for Opportunity ID: {OppId}");
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
        /// Sunny(19-06-25) -> Updating the totalratecard Roll Up value
        /// Added in SAME try/catch style (functionName + InvalidPluginExecutionException + Exception)
        /// </summary>
        private void UpdateOppTotalRateCardRollup(Guid opportunityId, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "UpdateOppTotalRateCardRollup";
            try
            {
                tracingService.Trace("Proceeding for updating the total rate card roll up field.");

                string fieldName = "ats_totalratecard";
                string entityLogicalName = "opportunity";
                tracingService.Trace($"opportunityId: {opportunityId}");

                var calculateRollup = new CalculateRollupFieldRequest
                {
                    Target = new EntityReference(entityLogicalName, opportunityId),
                    FieldName = fieldName
                };

                var calculateRollupResult = (CalculateRollupFieldResponse)service.Execute(calculateRollup);

                tracingService.Trace($"Total Rate Card rollup field updated successfully: {calculateRollupResult}");
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

public enum AtsPricingMode
{
    Manual = 559240001,
    Automatic = 559240000,
}

public enum EscalationMode
{
    Individual = 114300010,
    AllYear = 114300011,
}
