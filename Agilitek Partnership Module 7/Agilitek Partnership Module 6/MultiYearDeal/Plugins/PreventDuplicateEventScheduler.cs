using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Workflows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace MultiYearDeal
{
    public class PreventDuplicateEventScheduler : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            // Initialize variables
            #region Initialize Variables
            string functionName = "Execute";
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            #endregion

            try
            {
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Tracing initialized");

                // Validate context, target, and targetEntity
                if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
                {
                    Entity targetEntity = (Entity)context.InputParameters["Target"];
                    // Retrieve the form data
                    string eventScheduleName = targetEntity.Contains("ats_name") ? targetEntity.GetAttributeValue<string>("ats_name") : string.Empty;
                    EntityReference divisionReference = targetEntity.Contains("ats_division") ? targetEntity.GetAttributeValue<EntityReference>("ats_division") : null;
                    Guid divisionReferenceId = divisionReference?.Id ?? Guid.Empty;
                    EntityReference seasonReference = targetEntity.Contains("ats_season") ? targetEntity.GetAttributeValue<EntityReference>("ats_season") : null;
                    Guid seasonReferenceId = seasonReference?.Id ?? Guid.Empty;
                    int? seasonCategoryValue = targetEntity.GetAttributeValue<OptionSetValue>("ats_seasoncategory")?.Value;

                    if (!string.IsNullOrEmpty(eventScheduleName) && divisionReferenceId != Guid.Empty && seasonReferenceId != Guid.Empty)
                    {
                        #region Checking the Duplicate Record
                        QueryExpression query = new QueryExpression("ats_eventschedule")
                        {
                            ColumnSet = new ColumnSet("ats_name"), // Include fields you need
                            Criteria = new FilterExpression
                            {
                                Conditions =
                {
                    new ConditionExpression("ats_name", ConditionOperator.Equal, eventScheduleName),
                    new ConditionExpression("ats_division", ConditionOperator.Equal, divisionReferenceId),
                    new ConditionExpression("ats_season", ConditionOperator.Equal, seasonReferenceId)
                }
                            }
                        };

                        // Adding the season category condition only if it has a value
                        if (seasonCategoryValue.HasValue)
                        {
                            query.Criteria.Conditions.Add(new ConditionExpression("ats_seasoncategory", ConditionOperator.Equal, seasonCategoryValue.Value));
                        }

                        // Execute query and check for duplicates
                        EntityCollection existingRecords = service.RetrieveMultiple(query);
                        if (existingRecords.Entities.Count > 0)
                        {
                            TraceHelper.Trace(tracingService, "Duplicate record found.");
                            throw new InvalidPluginExecutionException("Duplicate record found.");
                        }
                        #endregion
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "Validation failed: Name - {0}, Division - {1}, Season - {2}", string.IsNullOrEmpty(eventScheduleName) ? "Empty" : "Valid", divisionReferenceId == Guid.Empty ? "Empty" : "Valid", seasonReferenceId == Guid.Empty ? "Empty" : "Valid");
                    }
                }
                else
                {
                    TraceHelper.Trace(tracingService, "Input parameter doesn't contain the Target.");
                }


            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException(functionName + ": " + ex.Message);
            }
            catch (Exception ex)
            {
                tracingService.Trace($"Exception occurred: {ex.Message}");
                throw new InvalidPluginExecutionException($"{functionName}: An error occurred in PreventDuplicateEventScheduler plugin: {ex.Message}");
            }
        }
    }
}
