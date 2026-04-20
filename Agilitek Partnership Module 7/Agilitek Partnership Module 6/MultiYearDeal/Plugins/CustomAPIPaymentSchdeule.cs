using Microsoft.Xrm.Sdk;
using MultiYearDeal.Workflows;
using System;
using System.Collections.Generic;

namespace MultiYearDeal.Plugins
{
    public class CustomAPIPaymentSchdeule : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = GetContext(serviceProvider);
            var service = GetService(serviceProvider, context);
            var tracingService = GetTracing(serviceProvider);

            string functionName = "CustomAPIPaymentSchdeule";

            try
            {
                TraceHelper.Trace(tracingService, "Function:{0}", functionName);
                Logging.Log("Context Depth: " + context.Depth.ToString(), tracingService);

                if (context.Depth > 1) return;

                var args = new Dictionary<string, string>
                {
                    { "Name", GetInput(context, "Name") },
                    { "AmountDue", GetInput(context, "AmountDue") },
                    { "AmountReceived", GetInput(context, "AmountReceived") },
                    { "DueDate", GetInput(context, "DueDate") },
                    { "Status", GetInput(context, "Status") },
                    { "OpportunityId", GetInput(context, "OpportunityId") },
                    { "PaymentScheduleId", GetInput(context, "PaymentScheduleId") }
                };

                string action = GetInput(context, "Action");
                TraceHelper.Trace(tracingService, "Action: {0}", action);

                switch (action)
                {
                    case "Save":
                        Create(service, tracingService, args);
                        break;

                    case "Update":
                        Update(service, tracingService, args);
                        break;

                    case "Delete":
                        Delete(service, tracingService, args);
                        break;

                    default:
                        throw new InvalidPluginExecutionException("Invalid Action parameter.");
                }

                context.OutputParameters["Response"] = "Success";
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Exception in {0}: {1}", functionName, ex.ToString());
                throw new InvalidPluginExecutionException(ex.Message, ex);
            }
        }

        // ================= CREATE =================
        private void Create(IOrganizationService service, ITracingService tracingService, Dictionary<string, string> args)
        {
            TraceHelper.Trace(tracingService, "Function:{0}", "CreatePaymentSchedule");

            var entity = BuildEntity(args);

            Guid id = service.Create(entity);

            TraceHelper.Trace(tracingService, "Created PaymentSchedule Id: {0}", id);
        }

        // ================= UPDATE =================
        private void Update(IOrganizationService service, ITracingService tracingService, Dictionary<string, string> args)
        {
            TraceHelper.Trace(tracingService, "Function:{0}", "UpdatePaymentSchedule");

            var entity = BuildEntity(args, new Guid(args["PaymentScheduleId"]));

            service.Update(entity);

            TraceHelper.Trace(tracingService, "Updated PaymentSchedule Id: {0}", args["PaymentScheduleId"]);
        }

        // ================= DELETE =================
        private void Delete(IOrganizationService service, ITracingService tracingService, Dictionary<string, string> args)
        {
            TraceHelper.Trace(tracingService, "Function:{0}", "DeletePaymentSchedule");

            service.Delete("ats_scheduledpayment", new Guid(args["PaymentScheduleId"]));

            TraceHelper.Trace(tracingService, "Deleted PaymentSchedule Id: {0}", args["PaymentScheduleId"]);
        }

        // ================= COMMON ENTITY BUILDER =================
        private Entity BuildEntity(Dictionary<string, string> args, Guid? id = null)
        {
            Entity entity = id.HasValue
                ? new Entity("ats_scheduledpayment", id.Value)
                : new Entity("ats_scheduledpayment");

            entity["ats_name"] = args["Name"];
            entity["ats_amountdue"] = new Money(ParseDecimal(args["AmountDue"]));
            entity["ats_amountreceived"] = new Money(ParseDecimal(args["AmountReceived"]));
            entity["ats_duedate"] = Convert.ToDateTime(args["DueDate"]);
            entity["statuscode"] = new OptionSetValue(GetStatusReason(args["Status"]));
            entity["ats_opportunityid"] =
                new EntityReference("opportunity", Guid.Parse(args["OpportunityId"]));

            return entity;
        }

        // ================= HELPERS =================
        private IPluginExecutionContext GetContext(IServiceProvider sp) =>
            (IPluginExecutionContext)sp.GetService(typeof(IPluginExecutionContext));

        private IOrganizationService GetService(IServiceProvider sp, IPluginExecutionContext ctx) =>
            ((IOrganizationServiceFactory)sp.GetService(typeof(IOrganizationServiceFactory)))
            .CreateOrganizationService(ctx.UserId);

        private ITracingService GetTracing(IServiceProvider sp) =>
            (ITracingService)sp.GetService(typeof(ITracingService));

        private string GetInput(IPluginExecutionContext context, string name) =>
            context.InputParameters.Contains(name) && context.InputParameters[name] != null
                ? context.InputParameters[name].ToString()
                : string.Empty;

        private decimal ParseDecimal(string value) =>
            string.IsNullOrEmpty(value) ? 0 : Convert.ToDecimal(value);

        private int GetStatusReason(string status)
        {
            switch (status)
            {
                case "Upcoming": return 1;
                case "Partially Paid": return 114300001;
                case "Fully Paid": return 114300002;
                default: return 114300000;
            }
        }
    }
}