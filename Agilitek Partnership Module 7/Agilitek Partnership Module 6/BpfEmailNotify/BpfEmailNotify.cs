using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using System;

namespace BpfEmailNotify
{
    public class BpfEmailNotify : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var service = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))).CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                Entity preImageEntity = context.PreEntityImages["Image"];
                if (preImageEntity.Attributes.ContainsKey("ats_salesstep") && ((OptionSetValue)preImageEntity["ats_salesstep"]).Value == 114300008) // Approved
                {
                    Entity postImageEntity = context.PostEntityImages["Image"];
                    if (postImageEntity.Attributes.ContainsKey("ats_salesstep") && ((OptionSetValue)postImageEntity["ats_salesstep"]).Value == 114300007 // Contract Built
                        && postImageEntity.Attributes.ContainsKey("name"))
                    {
                        // Send email to Mark Smith
                        Entity email = new Entity("email");
                        Entity fromparty = new Entity("activityparty");
                        Entity toparty = new Entity("activityparty");

                        fromparty["partyid"] = new EntityReference("systemuser", new Guid("b190b6cf-4f76-e911-a81b-000d3a3b5c20")); // Mark Smith
                        toparty["partyid"] = new EntityReference("systemuser", new Guid("b190b6cf-4f76-e911-a81b-000d3a3b5c20")); // Mark Smith

                        email["from"] = new Entity[] { fromparty };
                        email["to"] = new Entity[] { toparty };
                        email["subject"] = "Partnership Deal moved backward from Approved";
                        email["description"] = $"The Partnership Deal \"{postImageEntity["name"]}\" has been moved backward from Approved to Contract Built.";
                        email["directioncode"] = true;

                        //create an email activity record
                        Guid emailId = service.Create(email);

                        //send email to the recipient
                        SendEmailRequest emailRequest = new SendEmailRequest
                        {
                            EmailId = emailId,
                            TrackingToken = "",
                            IssueSend = true
                        };

                        //getting email response
                        SendEmailResponse emailResponse = (SendEmailResponse)service.Execute(emailRequest);
                        tracingService.Trace(emailResponse.ResponseName);
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("BpfEmailNotify: Could not test for email notification conditions. {0}", ex.ToString());
            }
        }
    }
}
