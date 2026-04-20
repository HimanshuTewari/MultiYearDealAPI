using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiYearDeal
{
    public class OpportunityImpact : CodeActivity
    {
        [Input("AgreementEntityReference")]
        [ReferenceTarget("ats_agreement")]
        public InArgument<EntityReference> AgreementEntityReference { get; set; }

        private IOrganizationService GetAdminImpersonationService(IOrganizationService service, IOrganizationServiceFactory serviceFactory)
        {
            QueryExpression adminSettingQuery = new QueryExpression("ats_agiliteksettings");
            adminSettingQuery.ColumnSet = new ColumnSet(new string[] { "ats_value", "ats_key" });
            adminSettingQuery.Criteria.AddCondition("ats_key", ConditionOperator.Equal, "AdminUserID");
            EntityCollection adminUserSetting = service.RetrieveMultiple(adminSettingQuery);
            if (adminUserSetting.Entities.Count > 0)
                return serviceFactory.CreateOrganizationService(new Guid(adminUserSetting.Entities[0].Attributes["ats_value"].ToString()));
            else
                return null;
        }

        protected override void Execute(CodeActivityContext context)
        {
            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            IOrganizationService adminService = GetAdminImpersonationService(service, serviceFactory);
            ITracingService tracingService = context.GetExtension<ITracingService>();

            TraceHelper.Initialize(service);
            TraceHelper.Trace(tracingService, "Tracing initialized");

            var agreementRef = AgreementEntityReference.Get(context);

            Entity agreement = service.Retrieve(agreementRef.LogicalName, agreementRef.Id, new ColumnSet(true));
            Guid agreementId = agreement.GetAttributeValue<Guid>("ats_agreementid");

            int contractLength = agreement.GetAttributeValue<int>("ats_contractlengthyears");
            EntityReference startSeasonRef = agreement.GetAttributeValue<EntityReference>("ats_startseason");
            int startSeasonYear = int.Parse(startSeasonRef.Name);

            //Sunny(09-07-25)
            // Retrieve Agreement Record
            Entity agreementData = service.Retrieve(agreementRef.LogicalName, agreementRef.Id, new ColumnSet("statecode"));
            int stateCodeValue = 0;

            if (agreementData.Attributes.Contains("statecode"))
            {
                OptionSetValue stateCodeOption = agreement.GetAttributeValue<OptionSetValue>("statecode");
                stateCodeValue = stateCodeOption.Value;
            }

            // Calculate new end season year
            int endSeasonYear = startSeasonYear + contractLength - 1;

            TraceHelper.Trace(tracingService, "startSeasonYear={0}, endSeasonYear={1}", startSeasonYear, endSeasonYear);

            // Fetch existing Opportunities
            QueryExpression opportunityQuery = new QueryExpression("opportunity")
            {
                ColumnSet = new ColumnSet("opportunityid", "ats_startseason", "ats_type", "statecode"),
                Criteria = new FilterExpression
                {
                    Conditions =
            {
                new ConditionExpression("ats_agreement", ConditionOperator.Equal, agreementId)
            }
                }
            };

            EntityCollection opportunityCollection = service.RetrieveMultiple(opportunityQuery);

            // Lists to track impacted Opportunities
            List<string> toBeCreated = new List<string>();
            List<string> toBeUpdated = new List<string>();
            List<string> toBeDeleted = new List<string>();

            // Identify Opportunities to delete
            foreach (var opportunity in opportunityCollection.Entities)
            {
                EntityReference seasonRef = opportunity.GetAttributeValue<EntityReference>("ats_startseason");
                int seasonYear = int.Parse(seasonRef.Name);

                if (seasonYear < startSeasonYear || seasonYear > endSeasonYear)
                {
                    toBeDeleted.Add(seasonRef.Name);
                }
                else
                {
                    toBeUpdated.Add(seasonRef.Name);
                }

                if (stateCodeValue == 0)// Agreement status is 'Active'
                {
                    //Sunny(09-07-25)--> Reactivating the Opportunity records associated to the Agreement.
                    OptionSetValue state = opportunity.GetAttributeValue<OptionSetValue>("statecode");

                    if (state != null && (state.Value == 1 || state.Value == 2)) // Inactive or Cancelled
                    {
                        Guid opportunityId = opportunity.Id;

                        Entity updateOpportunity = new Entity("opportunity", opportunityId)
                        {
                            // Set State to Active (0) and Status to default (assumed 1 = In Progress)
                            ["statecode"] = new OptionSetValue(0),
                            ["statuscode"] = new OptionSetValue(1) // Status reason: In Progress (default)
                        };

                        service.Update(updateOpportunity);
                        TraceHelper.Trace(tracingService, "Updated opportunity statecode.");
                    }
                }
            }

            // Identify new Opportunities to create
            for (int year = startSeasonYear; year <= endSeasonYear; year++)
            {
                bool exists = false;
                foreach (var opportunity in opportunityCollection.Entities)
                {
                    EntityReference seasonRef = opportunity.GetAttributeValue<EntityReference>("ats_startseason");
                    int seasonYear = int.Parse(seasonRef.Name);

                    if (seasonYear == year)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    toBeCreated.Add(year.ToString());
                }
            }

            // Log impacted Opportunities
            TraceHelper.Trace(tracingService, "Opportunities to be Created:");
            foreach (var year in toBeCreated)
            {
                TraceHelper.Trace(tracingService, "Season Year: {0}", year);
            }

            TraceHelper.Trace(tracingService, "Opportunities to be Updated:");
            foreach (var oppId in toBeUpdated)
            {
                TraceHelper.Trace(tracingService, "Opportunity ID: {0}", oppId);
            }

            TraceHelper.Trace(tracingService, "Opportunities to be Deleted:");
            foreach (var oppId in toBeDeleted)
            {
                TraceHelper.Trace(tracingService, "Opportunity ID: {0}", oppId);
            }

        }
    }
}
