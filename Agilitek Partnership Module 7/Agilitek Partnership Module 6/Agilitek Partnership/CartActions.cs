using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using System.Activities;
using System.Collections.Generic;

namespace Agilitek_Partnership
{
    public class CartActions : CodeActivity // Workflow Activity
    {
        [Input("InvBySeasonId")]
        public InArgument<string> InvBySeasonId { get; set; }

        [Input("OppId")]
        public InArgument<string> OppId { get; set; }

        [Input("RateId")]
        public InArgument<string> RateId { get; set; }

        [Input("OppProdId")]
        public InArgument<string> OppProdId { get; set; }

        [Input("Qty")]
        public InArgument<string> Qty { get; set; }

        [Input("QtyOfEvents")]
        public InArgument<string> QtyOfEvents { get; set; }

        [Input("Action")]
        public InArgument<string> Action { get; set; }

        [Input("SellingRate")]
        public InArgument<string> SellingRate { get; set; }

        [Input("HardCost")]
        public InArgument<string> HardCost { get; set; }

        [Input("Comment")]
        public InArgument<string> Comment { get; set; }

        [Input("OverwriteLegalDef")]
        public InArgument<string> OverwriteLegalDef { get; set; }

        [Input("LegalDefinition")]
        public InArgument<string> LegalDefinition { get; set; }

        [Output("OutMessage")]
        public OutArgument<string> OutMessage { get; set; }

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

            Dictionary<string, string> args = new Dictionary<string, string>();

            tracingService.Trace("***********Action************" + Action.Get(context));
            tracingService.Trace("***********InvBySeasonId************" + InvBySeasonId.Get(context));
            tracingService.Trace("***********OppId************" + OppId.Get(context));
            tracingService.Trace("***********OppProdId************" + OppProdId.Get(context));
            tracingService.Trace("***********RateId************" + RateId.Get(context));
            tracingService.Trace("***********Qty************" + Qty.Get(context));
            tracingService.Trace("***********QtyOfEvents************" + QtyOfEvents.Get(context));
            tracingService.Trace("***********Comment************" + Comment.Get(context));
            tracingService.Trace("***********LegalDefinition************" + LegalDefinition.Get(context));
            tracingService.Trace("***********OverwriteLegalDef************" + OverwriteLegalDef.Get(context));

            args.Add("Action", Action.Get(context));
            args.Add("InvBySeasonId", InvBySeasonId.Get(context));
            args.Add("OppId", OppId.Get(context));
            args.Add("OppProdId", OppProdId.Get(context));
            args.Add("RateId", RateId.Get(context));
            args.Add("Qty", Qty.Get(context));
            args.Add("QtyOfEvents", QtyOfEvents.Get(context));
            args.Add("Comment", Comment.Get(context));
            args.Add("LegalDefinition", LegalDefinition.Get(context));
            args.Add("OverwriteLegalDef", OverwriteLegalDef.Get(context).ToString());

            CartActionsBusinessLogic cartAction = new CartActionsBusinessLogic();

            if (args["Action"] == "AddLine")
            {
                //Check Product Details and Validate

                string msg = cartAction.ValidateProdLines(args, service, tracingService);
                if (msg != "Success")
                {
                    OutMessage.Set(context, msg);
                    return;
                }

                cartAction.CreateOppProdLines(context, args);
                OutMessage.Set(context, "Success");
            }
            else if (args["Action"] == "SaveCartLine")
            {
                cartAction.PerformInventorySubtraction(context, args);
                OutMessage.Set(context, "Success");
            }
            //else if (args["Action"] == "ReCalcLines")
            //{
            //    cartAction.RecalculateOppProdLinesFromWF(context, args);
            //}
            else if (args["Action"] == "DeleteCartItem")
            {
                cartAction.PerformInventorySubtraction(context, args);
                cartAction.DeleteOppProdLine(context, args);
                OutMessage.Set(context, "Success");
            }
        }
    }
}
