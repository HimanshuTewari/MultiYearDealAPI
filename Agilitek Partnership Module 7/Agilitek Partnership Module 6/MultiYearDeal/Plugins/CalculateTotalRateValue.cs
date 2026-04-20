using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Workflows;
using System;
using System.Activities.Statements;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;

namespace MultiYearDeal
{
    public class CalculateTotalRateValue : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Tracing helper init (ONLY tracing change)
            TraceHelper.Initialize(service);
            TraceHelper.Trace(tracingService, "Tracing initialized");

            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity entity)
            {
                string functionName = "Execute";
                try
                {
                    

                    if (entity.LogicalName != "opportunityproduct") return;

                    TraceHelper.Trace(tracingService, "Opportunity Product Plugin triggered");

                    Guid opportunityProductId = entity.Id;

                    ColumnSet cols = new ColumnSet("priceperunit", "ats_quantity", "ats_quantityofevents", "ats_packagelineitem"); 
                    Entity oppProduct = service.Retrieve("opportunityproduct", opportunityProductId, cols);
                    
                    TraceHelper.Trace(tracingService, "Opportunity Product Id: {0}", opportunityProductId);
                    // Safely extract priceperunit
                    decimal pricePerUnit = 0;
                    if (oppProduct.Contains("priceperunit") && oppProduct["priceperunit"] is Money moneyVal)
                    {
                        pricePerUnit = moneyVal.Value != 0 ? moneyVal.Value : 1;
                    }

                    TraceHelper.Trace(tracingService, "pricePerUnit: {0}", pricePerUnit);

                    // ---------------------
                    // Get Quantity
                    // ---------------------
                    decimal quantity = 0;

                    bool isQuantityInEntity = entity.Attributes.Contains("ats_quantity");
                    TraceHelper.Trace(tracingService, "isQuantityInEntity: {0}", isQuantityInEntity);

                    if (isQuantityInEntity)
                    {
                        TraceHelper.Trace(tracingService, "Quanitty is received from the target entity");

                        if (entity["ats_quantity"] != null)
                        {
                            object raw = entity["ats_quantity"];
                            if (raw is int intVal)
                                quantity = intVal != 0 ? intVal : 0; //Sunny(14-08-25) from 0 to 1 
                            else if (raw is decimal decVal)
                                quantity = decVal != 0 ? decVal : 0;
                            else if (raw is double dblVal)
                                quantity = dblVal != 0 ? Convert.ToDecimal(dblVal) : 0;

                            TraceHelper.Trace(tracingService, "quantity: {0}", quantity);
                        }
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Quantity is received from the opp Product");
                        if (oppProduct.Attributes.Contains("ats_quantity") && oppProduct["ats_quantity"] != null)
                        {
                            object raw = oppProduct["ats_quantity"];
                            if (raw is int intVal)
                                quantity = intVal != 0 ? intVal : 0;
                            else if (raw is decimal decVal)
                                quantity = decVal != 0 ? decVal : 0;
                            else if (raw is double dblVal)
                                quantity = dblVal != 0 ? Convert.ToDecimal(dblVal) : 0;

                            TraceHelper.Trace(tracingService, "quantity: {0}", quantity);
                        }
                    }

                    // ---------------------
                    // Get Quantity Of Events 
                    // ---------------------
                    decimal quantityOfEvents = 1;
                    if (entity.Attributes.Contains("ats_quantityofevents") && entity["ats_quantityofevents"] != null)
                    {
                        object raw = entity["ats_quantityofevents"];
                        if (raw is int intVal)
                            quantityOfEvents = intVal != 0 ? intVal : 0;
                        else if (raw is decimal decVal)
                            quantityOfEvents = decVal != 0 ? decVal : 0;
                        else if (raw is double dblVal)
                            quantityOfEvents = dblVal != 0 ? Convert.ToDecimal(dblVal) : 0;
                    }

                    TraceHelper.Trace(tracingService, "quantityOfEvents: {0}", quantityOfEvents);

                    //Sunny(15-08-25)
                    //Qty events should be considered or not, depends on the rate type
                    string fetchXml = $@"
                                    <fetch top='1'>
                                      <entity name='opportunityproduct'>
                                        <filter>
                                          <condition attribute='opportunityproductid' operator='eq' value='{entity.Id}' />
                                        </filter>
                                        <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' link-type='outer' alias='Rate'>
                                          <attribute name='ats_ratetype' />
                                        </link-entity>
                                      </entity>
                                    </fetch>";

                    EntityCollection result = service.RetrieveMultiple(new FetchExpression(fetchXml));

                    int rateTypeValue = 0; // default

                    var record = result.Entities.FirstOrDefault();
                    rateTypeValue = record != null && record.Contains("Rate.ats_ratetype") && record["Rate.ats_ratetype"] != null
                        ? ((OptionSetValue)((AliasedValue)record["Rate.ats_ratetype"]).Value).Value
                        : 0;

                    TraceHelper.Trace(tracingService, "rateTypeValue: {0}", rateTypeValue); 

                    decimal finalValue = 0;
                    if (rateTypeValue == 114300000)// Season 
                    {
                        TraceHelper.Trace(tracingService, "Rate type is season");
                        finalValue = pricePerUnit * quantity;
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Rate type is other than season");
                        finalValue = pricePerUnit * quantity * quantityOfEvents;
                    }

                    TraceHelper.Trace(tracingService, "Final Value Calculated: {0}", finalValue);

                    // ---------------------
                    // Update Opportunity Product 
                    // ---------------------
                    Entity updateEntity = new Entity("opportunityproduct", entity.Id);
                    updateEntity["ats_totalratevalue"] = new Money(finalValue);

                    //Avoid the oppProd calculation which belongs to the component oli.
                    if (oppProduct.Contains("ats_packagelineitem"))
                    {
                        bool isPackageLineItem = oppProduct["ats_packagelineitem"] == null; 
                        if(!isPackageLineItem)
                        {
                            updateEntity["ats_totalratevalue"] = new Money(0);
                            TraceHelper.Trace(tracingService, "The opportunity product is a compoonent line item. Total rate value is set to 0."); 
                        }
                    }

                    service.Update(updateEntity);

                    TraceHelper.Trace(tracingService, "ats_totalratevalue updated successfully.");

                    EntityReference opportunityRef = null;

                    if (entity.Contains("opportunityid") && entity["opportunityid"] is EntityReference)
                    {
                        opportunityRef = (EntityReference)entity["opportunityid"];
                    }
                    else if (context.PreEntityImages.Contains("PreImage") &&
                             context.PreEntityImages["PreImage"].Contains("opportunityid"))
                    {
                        opportunityRef = (EntityReference)context.PreEntityImages["PreImage"]["opportunityid"];
                    }

                    TraceHelper.Trace(tracingService, "Opportunity ID found: {0}", opportunityRef.Id);

                    #region Updating the all opportunity products total rate value before rollup calculation 

                    string fetchXmlForOppProd = $@"
                                                <fetch>
                                                  <entity name='opportunityproduct'>
                                                    <attribute name='ats_packagelineitem' />
                                                    <filter>
                                                      <condition attribute='opportunityid' operator='eq' value='{opportunityRef.Id}' />
                                                    </filter>
                                                  </entity>
                                                </fetch>";

                    FetchExpression fetchExpression = new FetchExpression(fetchXmlForOppProd);
                    EntityCollection oppProducts = service.RetrieveMultiple(fetchExpression);

                    TraceHelper.Trace(tracingService, " oppProducts.Entities.Count: {0}", oppProducts.Entities.Count);

                    foreach (Entity oppProd in oppProducts.Entities)
                    {
                        EntityReference packageLineItem = oppProd.GetAttributeValue<EntityReference>("ats_packagelineitem");

                        bool isPackageLineItem = packageLineItem != null;
                        TraceHelper.Trace(tracingService, "isPackageLineItem: {0}", isPackageLineItem);


                        if (packageLineItem != null) //component OLI 
                        {
                            Entity oppProdToUpdate = new Entity("opportunityproduct", oppProd.Id);
                            oppProdToUpdate["ats_totalratevalue"] = new Money(0);
                            service.Update(oppProdToUpdate);

                            TraceHelper.Trace(
                                tracingService,
                                "Updated opportunity product {0} total rate value to 0 before rollup calculation.",
                                oppProd.Id
                            );
                        }
                    }

                    #endregion

                    // Perform rollup calculation
                    string fieldName = "ats_totalratecard";

                    var rollupRequest = new CalculateRollupFieldRequest
                    {
                        Target = opportunityRef,
                        FieldName = fieldName
                    };

                    var rollupResponse = (CalculateRollupFieldResponse)service.Execute(rollupRequest);

                    TraceHelper.Trace(tracingService, "Rollup field '{0}' recalculated successfully.", fieldName);

                    
                }
                catch (InvalidPluginExecutionException ex)
                {
                    throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}");
                }
                catch (Exception ex)
                {
                    TraceHelper.Trace(tracingService, "Exception: {0} - {1}", ex.Message, ex.StackTrace);
                    throw new InvalidPluginExecutionException("An error occurred in CalculateTotalRateValue plugin.", ex);
                }
            }
        }
    }
}
