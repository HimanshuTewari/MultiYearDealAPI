 using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using MultiYearDeal.Workflows;
using System;

namespace MultiYearDeal
{
    public class UpdateRollupFieldPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));


            TraceHelper.Initialize(service);
            TraceHelper.Trace(tracingService, "Tracing initialized");



            TraceHelper.Trace(tracingService, "Plugin execution started: UpdateRollupFieldPlugin (Create/Update/Delete)"); 

            if (context.Depth > 1) // Testing sunny (10-14-25) 
            {
                TraceHelper.Trace(tracingService, "Current Depth: {0}", context.Depth);
                return;
            }

            try
            {
               
                EntityReference opportunityRef = null;

                switch (context.MessageName.ToLower())
                {
                    case "create":
                        //case "update":
                        if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
                        {
                            TraceHelper.Trace(tracingService, "Target entity is missing or not valid.");
                            return;
                        }

                        Entity entity = (Entity)context.InputParameters["Target"];
                        if (entity.LogicalName != "opportunityproduct")
                        {
                            TraceHelper.Trace(tracingService, "Entity is not opportunityproduct.");
                            return;
                        }

                        if (entity.Contains("opportunityid") && entity["opportunityid"] is EntityReference)
                        {
                            opportunityRef = (EntityReference)entity["opportunityid"];
                        }
                        else if (context.PreEntityImages.Contains("PreImage") && context.PreEntityImages["PreImage"].Contains("opportunityid"))
                        {
                            opportunityRef = (EntityReference)context.PreEntityImages["PreImage"]["opportunityid"];
                        }
                        break;

                    case "delete":
                        if (!context.PreEntityImages.Contains("PreImage"))
                        {
                            TraceHelper.Trace(tracingService, "PreImage not available in Delete message.");
                            return;
                        }

                        Entity preImage = context.PreEntityImages["PreImage"];
                        if (preImage.Contains("opportunityid") && preImage["opportunityid"] is EntityReference)
                        {
                            opportunityRef = (EntityReference)preImage["opportunityid"];
                        }
                        break;

                    default:
                        TraceHelper.Trace(tracingService, "Plugin triggered on unsupported message.");
                        return;
                }

                if (opportunityRef == null)
                {
                    TraceHelper.Trace(tracingService, "Opportunity reference could not be retrieved.");
                    return;
                }

                TraceHelper.Trace(tracingService, "Opportunity ID found: {0}", opportunityRef.Id);

                // Perform rollup calculation
                string fieldName = "ats_totalratecard";

                var rollupRequest = new CalculateRollupFieldRequest { Target = opportunityRef, FieldName = fieldName };
                var rollupResponse = (CalculateRollupFieldResponse)service.Execute(rollupRequest);

                TraceHelper.Trace(tracingService, "Rollup field '{0}' recalculated successfully.", fieldName);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Exception in UpdateRollupFieldPlugin: {0}", ex.Message);
                throw new InvalidPluginExecutionException("An error occurred while updating the rollup field.", ex);
            }

            TraceHelper.Trace(tracingService, "Plugin execution completed.");

        }
    }
}
