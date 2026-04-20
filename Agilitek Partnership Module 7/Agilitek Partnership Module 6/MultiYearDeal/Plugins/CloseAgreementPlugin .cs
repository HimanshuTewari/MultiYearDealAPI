using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Workflows;
using System;

public class CloseAgreementPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
        ITracingService tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

        // Tracing helper init (ONLY tracing change)
        TraceHelper.Initialize(service);
        TraceHelper.Trace(tracing, "Tracing initialized");

        TraceHelper.Trace(tracing, "Plugin execution started.");
        string functionName = "Execute";
        try
        {
            if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
            {
                TraceHelper.Trace(tracing, "Target is missing or not an Entity.");
                return;
            }

            Entity target = (Entity)context.InputParameters["Target"];
            if (target.LogicalName != "ats_agreementclose")
            {
                TraceHelper.Trace(tracing, "Incorrect entity triggered the plugin.");
                return;
            }

            if (!target.Attributes.Contains("ats_agreement"))
            {
                TraceHelper.Trace(tracing, "Agreement lookup (ats_agreement) not found on the record.");
                return;
            }

            EntityReference agreementRef = (EntityReference)target["ats_agreement"];
            TraceHelper.Trace(tracing, "Agreement ID: {0}", agreementRef.Id);

            // 🆕 Read Status Reason from Agreement Close
            int agreementCloseStatusCode = -1;
            if (target.Attributes.Contains("ats_agreementstatuscode"))
            {
                OptionSetValue statusValue = (OptionSetValue)target["ats_agreementstatuscode"];
                agreementCloseStatusCode = statusValue.Value;
                TraceHelper.Trace(tracing, "AgreementClose Status Reason value: {0}", agreementCloseStatusCode);
            }
            else
            {
                TraceHelper.Trace(tracing, "Status Reason (ats_agreementstatuscode) not found on AgreementClose.");
            }

            int agreementStatusCode = -1;
            //Validation for setting the Agreement status code
            if (agreementCloseStatusCode == 3) //Won 
            {
                agreementStatusCode = 114300003;
            }
            else if (agreementCloseStatusCode == 114300007) //Lost
            {
                agreementStatusCode = 114300004;
            }
            else if (agreementCloseStatusCode == 1) //In Progress 
            {
                agreementStatusCode = 114300005;
            }
            else if (agreementCloseStatusCode == 2) //On Hold
            {
                agreementStatusCode = 114300006;
            }
            else if (agreementCloseStatusCode == 4) //Canceled
            {
                agreementStatusCode = 114300007;
            }
            else if (agreementCloseStatusCode == 5) //Out-Sold
            {
                agreementStatusCode = 114300008;
            }

            TraceHelper.Trace(tracing, "agreementStatusCode: {0}", agreementStatusCode);

            // Update Agreement record with state and status
            if (agreementStatusCode != -1)
            {
                var setAgreementState = new SetStateRequest
                {
                    EntityMoniker = new EntityReference("ats_agreement", agreementRef.Id),
                    State = new OptionSetValue(1), // 1 = Inactive
                    Status = new OptionSetValue(agreementStatusCode) // 114300001 = Won, 114300002 = Lost
                };

                service.Execute(setAgreementState);
                TraceHelper.Trace(tracing, "SetStateRequest executed for Agreement with StatusCode: {0}", agreementStatusCode);
            }

            bool isLostReason = target.Attributes.Contains("ats_lostreason");
            // If "ats_lostreason" is present, update it on the Agreement record 
            if ((isLostReason && agreementCloseStatusCode == 114300007)) // 114300007 = Lost
            {
                OptionSetValue lostReasonValue = target["ats_lostreason"] as OptionSetValue;
                if (lostReasonValue != null || target.Attributes.Contains("ats_description"))
                {
                    Entity updateAgreement = new Entity("ats_agreement");
                    updateAgreement.Id = agreementRef.Id;

                    if (target.Attributes.Contains("ats_description"))
                    {
                        string descriptionValue = target["ats_description"] as string;
                        if (!string.IsNullOrWhiteSpace(descriptionValue))
                        {
                            updateAgreement["ats_description"] = descriptionValue;
                            TraceHelper.Trace(tracing, "going to update the Agreement with ats_description: {0}", descriptionValue);
                        }
                        else
                        {
                            TraceHelper.Trace(tracing, "ats_description was present but empty.");
                        }
                    }
                    if (target.Attributes.Contains("ats_lostreason"))
                    {
                        updateAgreement["ats_lostreason"] = lostReasonValue;
                    }

                    service.Update(updateAgreement);
                    TraceHelper.Trace(tracing, "Updated Agreement with ats_lostreason: {0}", lostReasonValue.Value);
                }
                else
                {
                    TraceHelper.Trace(tracing, "ats_lostreason value was present but not a valid OptionSetValue.");
                }
            }

            TraceHelper.Trace(tracing, "Proceeeding for fetchxml");

            // 🔁 Continue: Fetch and update related opportunities
            string fetchXml = $@"<fetch>
                              <entity name='opportunity'>
                                <attribute name='opportunityid'/>
                                <attribute name='statecode'/>
                                <attribute name='statuscode'/>
                                <filter type='and'>
                                  <condition attribute='ats_agreement' operator='eq' value='{agreementRef.Id}' />
                                </filter>
                              </entity>
                            </fetch>";

            TraceHelper.Trace(tracing, "Executing FetchXML query:\n{0}", fetchXml);

            EntityCollection opportunities = service.RetrieveMultiple(new FetchExpression(fetchXml));
            TraceHelper.Trace(tracing, "Retrieved {0} opportunities.", opportunities.Entities.Count);

            foreach (Entity opp in opportunities.Entities)
            {
                TraceHelper.Trace(tracing, "Processing Opportunity ID: {0}", opp.Id);

                if (agreementStatusCode == 114300003)//Agreement Won
                {
                    var setStateReq = new SetStateRequest
                    {
                        EntityMoniker = new EntityReference("opportunity", opp.Id),
                        State = new OptionSetValue(1),   // Closed
                        Status = new OptionSetValue(3)   // Won (adjust if needed)
                    };
                    service.Execute(setStateReq);
                    TraceHelper.Trace(tracing, "Opportunity {0} marked as Won.", opp.Id);
                }
                else if (agreementStatusCode == 114300004) //Agreement Lost 
                {
                    var setStateReq = new SetStateRequest
                    {
                        EntityMoniker = new EntityReference("opportunity", opp.Id),
                        State = new OptionSetValue(2),   // Lost
                        Status = new OptionSetValue(114300007)   //Lost 
                    };
                    service.Execute(setStateReq);
                    TraceHelper.Trace(tracing, "Opportunity {0} marked as Lost.", opp.Id);
                }
                else if (agreementStatusCode == 114300005) //Agreement In Progress  
                {
                    var setStateReq = new SetStateRequest
                    {
                        EntityMoniker = new EntityReference("opportunity", opp.Id),
                        State = new OptionSetValue(0),   // In Progress  
                        Status = new OptionSetValue(1)   //In Progress   
                    };
                    service.Execute(setStateReq);
                    TraceHelper.Trace(tracing, "Opportunity {0} marked as In Progress  .", opp.Id);
                }
                else if (agreementStatusCode == 114300006) //Agreement On Hold 
                {
                    var setStateReq = new SetStateRequest
                    {
                        EntityMoniker = new EntityReference("opportunity", opp.Id),
                        State = new OptionSetValue(0),   // On Hold
                        Status = new OptionSetValue(2)   //On Hold 
                    };
                    service.Execute(setStateReq);
                    TraceHelper.Trace(tracing, "Opportunity {0} marked as On Hold.", opp.Id);
                }
                else if (agreementStatusCode == 114300007) //Agreement Canceled 
                {
                    var setStateReq = new SetStateRequest
                    {
                        EntityMoniker = new EntityReference("opportunity", opp.Id),
                        State = new OptionSetValue(2),   // Canceled
                        Status = new OptionSetValue(4)   //Canceled 
                    };
                    service.Execute(setStateReq);
                    TraceHelper.Trace(tracing, "Opportunity {0} marked as Canceled.", opp.Id);
                }
                else if (agreementStatusCode == 114300008) //Agreement Out-Sold 
                {
                    var setStateReq = new SetStateRequest
                    {
                        EntityMoniker = new EntityReference("opportunity", opp.Id),
                        State = new OptionSetValue(2),   // Out-Sold
                        Status = new OptionSetValue(5)   //Out-Sold 
                    };
                    service.Execute(setStateReq);
                    TraceHelper.Trace(tracing, "Opportunity {0} marked as Out-Sold.", opp.Id);
                }

                //Logic for update the lost reason and Description in the opportunity record
                if ((target.Attributes.Contains("ats_lostreason") && agreementCloseStatusCode == 114300007)) // 114300007 = Lost
                {
                    OptionSetValue lostReasonValue = target.Attributes.Contains("ats_lostreason") ? target["ats_lostreason"] as OptionSetValue : null;
                    string descriptionValue = target.Attributes.Contains("ats_description") ? target["ats_description"] as string : null;

                    if (lostReasonValue != null || !string.IsNullOrWhiteSpace(descriptionValue))
                    {
                        Entity updateOpp = new Entity("opportunity");
                        updateOpp.Id = opp.Id;

                        if (lostReasonValue != null)
                        {
                            updateOpp["ats_lostreason"] = lostReasonValue;
                            TraceHelper.Trace(tracing, "Will update Opportunity {0} with ats_lostreason: {1}", opp.Id, lostReasonValue.Value);
                        }

                        if (!string.IsNullOrWhiteSpace(descriptionValue))
                        {
                            updateOpp["description"] = descriptionValue;
                            TraceHelper.Trace(tracing, "Will update Opportunity {0} with ats_description: {1}", opp.Id, descriptionValue);
                        }

                        service.Update(updateOpp);
                        TraceHelper.Trace(tracing, "Updated Opportunity {0} with applicable fields.", opp.Id);
                    }
                    else
                    {
                        TraceHelper.Trace(tracing, "ats_lostreason and ats_description were present but not valid for Opportunity {0}.", opp.Id);
                    }
                }
            }

            TraceHelper.Trace(tracing, "Plugin completed successfully.");
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
