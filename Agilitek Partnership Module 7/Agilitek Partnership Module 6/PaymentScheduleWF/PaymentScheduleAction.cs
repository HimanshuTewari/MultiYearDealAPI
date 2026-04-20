using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace PaymentScheduleWF
{
    public class PaymentScheduleAction : CodeActivity
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

            Logging.Log("Context Depth" + workflowContext.Depth.ToString(), tracingService);

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
            args["Status"]= Status.Get(context);
            args["OpportunityId"]= OpportunityId.Get(context);
            args["PaymentScheduleId"]= PaymentScheduleId.Get(context);
            args["Action"] = Action.Get(context);

            PaymentScheduleBusinessLogic paymentScheduleAction=new PaymentScheduleBusinessLogic();

            if (args["Action"] == "Save")
            {
                paymentScheduleAction.CreatePaymentSchedule(context, args);
                response.Set(context, "Success");
            }
            else if (args["Action"] == "Update")
            {
                paymentScheduleAction.UpdatePaymentSchedule(context, args);
                response.Set(context, "Success");
            }
            else if (args["Action"] == "Delete")
            {
                paymentScheduleAction.DeletePaymentSchedule(context, args);
                response.Set(context, "Success");
            }

        }
    }
}
