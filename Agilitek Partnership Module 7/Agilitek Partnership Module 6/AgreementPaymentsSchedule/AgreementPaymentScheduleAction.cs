using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace AgreementPaymentsSchedule
{
    public class AgreementPaymentScheduleAction : CodeActivity
    {
        [Input("Name")]
        public InArgument<string> Name { get; set; }

        [Input("AmountDue")]
        public InArgument<string> AmountDue { get; set; }

        [Input("AmountReceived")]
        public InArgument<string> AmountReceived { get; set; }

        [Input("DueDate")]
        public InArgument<string> DueDate { get; set; }

        [Input("Status")]
        public InArgument<string> Status { get; set; }

        [Input("OpportunityId")]
        public InArgument<string> OpportunityId { get; set; }

        //[Input("AgreementId")]
        //public InArgument<string> AgreementId { get; set; }

        [Input("PaymentScheduleId")]
        public InArgument<string> PaymentScheduleId { get; set; }

        [Input("Action")]
        public InArgument<string> Action { get; set; }

        [Output("Response")]
        public OutArgument<string> response { get; set; }


        protected override void Execute(CodeActivityContext context)
        {
            var workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            ITracingService tracingService = context.GetExtension<ITracingService>();

            tracingService.Trace($"Context Depth: {workflowContext.Depth.ToString()} ");

            if (workflowContext.Depth > 1)
            {
                return;
            }

            tracingService.Trace("***********Name************" + Name.Get(context));
            tracingService.Trace("***********AmountDue************" + AmountDue.Get(context));
            tracingService.Trace("***********AmountReceived************" + AmountReceived.Get(context));
            tracingService.Trace("***********DueDate************" + DueDate.Get(context));
            tracingService.Trace("***********Status************" + Status.Get(context));
            tracingService.Trace("***********OpportunityId************" + OpportunityId.Get(context));
            tracingService.Trace("***********PaymentScheduleId************" + PaymentScheduleId.Get(context));
            tracingService.Trace("***********Action************" + Action.Get(context));

            Dictionary<string, string> args = new Dictionary<string, string>();
            args["Name"] = Name.Get(context);
            args["AmountDue"] = AmountDue.Get(context);
            args["AmountReceived"] = AmountReceived.Get(context);
            args["DueDate"] = DueDate.Get(context);
            args["Status"] = Status.Get(context);
            args["OpportunityId"] = OpportunityId.Get(context);
            //args["AgreementId"] = AgreementId.Get(context);
            args["PaymentScheduleId"] = PaymentScheduleId.Get(context);
            args["Action"] = Action.Get(context);

            AgreementPaymentScheduleBusinessLogic agreementPaymentScheduleAction = new AgreementPaymentScheduleBusinessLogic();

            if (args["Action"] == "Save")
            {
                agreementPaymentScheduleAction.CreatePaymentSchedule(context, args);
                response.Set(context, "Success");
            }
            else if (args["Action"] == "Update")
            {
                agreementPaymentScheduleAction.UpdatePaymentSchedule(context, args);
                response.Set(context, "Success");
            }
            else if (args["Action"] == "Delete")
            {
                agreementPaymentScheduleAction.DeletePaymentSchedule(context, args);
                response.Set(context, "Success");
            }

        }
    }
}
