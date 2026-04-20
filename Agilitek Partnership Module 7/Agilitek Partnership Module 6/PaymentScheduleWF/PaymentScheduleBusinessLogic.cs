using Microsoft.Xrm.Sdk.Workflow;
using Microsoft.Xrm.Sdk;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IdentityModel.Protocols.WSTrust;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk.Query;

namespace PaymentScheduleWF
{
    public class PaymentScheduleBusinessLogic
    {
        string getPaymentSchedulebyId = @"<fetch>
  <entity name='ats_scheduledpayment'>
    <attribute name='ats_amountdue' />
    <attribute name='ats_amountreceived' />
    <attribute name='ats_duedate' />
    <attribute name='ats_name' />
    <attribute name='ats_opportunityid' />
    <attribute name='ats_scheduledpaymentid' />
    <attribute name='statecode' />
    <attribute name='statuscode' />
    <filter>
      <condition attribute='ats_scheduledpaymentid' operator='eq' value='{0}' />
    </filter>
  </entity>
</fetch>";

        public void CreatePaymentSchedule(CodeActivityContext context, Dictionary<string, string> args)
        {
            IWorkflowContext workflowContext = null;
            IOrganizationServiceFactory serviceFactory = null;
            IOrganizationService service = null;
            ITracingService tracingService = null;

            workflowContext = context.GetExtension<IWorkflowContext>();
            serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            tracingService = context.GetExtension<ITracingService>();

            var statusReason = 114300000;
            switch (args["Status"])
            {
                case "Upcoming":
                    statusReason = 1;
                    break;
                case "Partially Paid":
                    statusReason = 114300001;
                    break;
                case "Fully Paid":
                    statusReason = 114300002;
                    break;
                default: // Invoiced
                    statusReason = 114300000;
                    break;
            }

            Entity ScheduledPayment = new Entity("ats_scheduledpayment");

            ScheduledPayment["ats_name"] = args["Name"];

            tracingService.Trace("Before amountDue" + args["AmountDue"]);
            decimal amountDue = string.IsNullOrEmpty(args["AmountDue"]) ? 0 : Convert.ToDecimal(args["AmountDue"]);
            ScheduledPayment["ats_amountdue"] = new Money(amountDue);
            tracingService.Trace("After amountDue" + args["AmountDue"]);

            tracingService.Trace("Before amountReceived" + args["AmountReceived"]);
            decimal amountReceived = string.IsNullOrEmpty(args["AmountReceived"]) ? 0 : Convert.ToDecimal(args["AmountReceived"]);
            ScheduledPayment["ats_amountreceived"] = new Money(amountReceived);
            tracingService.Trace("After amountReceived" + args["AmountReceived"]);

            tracingService.Trace("Before dueDate" + args["DueDate"]);
            ScheduledPayment["ats_duedate"] = Convert.ToDateTime(args["DueDate"]);
            tracingService.Trace("After dueDate" + args["DueDate"]);

            ScheduledPayment["statuscode"] = new OptionSetValue(statusReason);
            ScheduledPayment["ats_opportunityid"] = new EntityReference("opportunity", Guid.Parse(args["OpportunityId"]));

            try
            {
                tracingService.Trace("Before Create");
                var scheduledPaymentId = service.Create(ScheduledPayment);
                tracingService.Trace("After Create");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Before Exception " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
        }

        public void UpdatePaymentSchedule(CodeActivityContext context, Dictionary<string, string> args)
        {
            IWorkflowContext workflowContext = null;
            IOrganizationServiceFactory serviceFactory = null;
            IOrganizationService service = null;
            ITracingService tracingService = null;

            workflowContext = context.GetExtension<IWorkflowContext>();
            serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            tracingService = context.GetExtension<ITracingService>();

            var statusReason = 114300000;
            switch (args["Status"])
            {
                case "Upcoming":
                    statusReason = 1;
                    break;
                case "Partially Paid":
                    statusReason = 114300001;
                    break;
                case "Fully Paid":
                    statusReason = 114300002;
                    break;
                default:
                    statusReason = 114300000;
                    break;
            }

            var query = string.Format(getPaymentSchedulebyId, args["PaymentScheduleId"].ToString());
            EntityCollection retrievedSchedules = service.RetrieveMultiple(new FetchExpression(query));

            foreach(var scheduleDetail in retrievedSchedules.Entities)
            {
                Entity scheduleToUpdate = new Entity("ats_scheduledpayment", new Guid(args["PaymentScheduleId"]));
                scheduleToUpdate["ats_name"] = args["Name"];
                decimal amountDue = string.IsNullOrEmpty(args["AmountDue"]) ? 0 : Convert.ToDecimal(args["AmountDue"]);
                scheduleToUpdate["ats_amountdue"] = new Money(amountDue);
                decimal amountReceived = string.IsNullOrEmpty(args["AmountReceived"]) ? 0 : Convert.ToDecimal(args["AmountReceived"]);
                scheduleToUpdate["ats_amountreceived"] = new Money(amountReceived);
                scheduleToUpdate["ats_duedate"] = Convert.ToDateTime(args["DueDate"]);
                scheduleToUpdate["statuscode"] = new OptionSetValue(statusReason);
                scheduleToUpdate["ats_opportunityid"] = new EntityReference("opportunity", Guid.Parse(args["OpportunityId"]));

                Logging.Log("Performing update...", tracingService);
                service.Update(scheduleToUpdate);
            }
        }

        public void DeletePaymentSchedule(CodeActivityContext context, Dictionary<string, string> args)
        {
            IWorkflowContext workflowContext = null;
            IOrganizationServiceFactory serviceFactory = null;
            IOrganizationService service = null;
            ITracingService tracingService = null;

            workflowContext = context.GetExtension<IWorkflowContext>();
            serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            tracingService = context.GetExtension<ITracingService>();

            Logging.Log("Inside Delete Payment Schedule****** ID: " + args["PaymentScheduleId"].ToString(), tracingService);
            service.Delete("ats_scheduledpayment", new Guid(args["PaymentScheduleId"]));
            Logging.Log("END OF Delete Payment Schedule******", tracingService);
        }
    }
}
