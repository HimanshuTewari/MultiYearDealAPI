using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Text.Json;
namespace Agilitek_Partnership
{
    public class RecalculateOppurtunityLines : CodeActivity // Workflow Activity
    {
        [Input("OppId")]
        public InArgument<string> OppId { get; set; }

        [Input("OppurtunityEntityReference")]
        [ReferenceTarget("opportunity")]
        public InArgument<EntityReference> OppurtunityEntityReference { get; set; }

        [Input("OppurtunityProductEntityReference")]
        [ReferenceTarget("opportunityproduct")]
        public InArgument<EntityReference> OppurtunityProductEntityReference { get; set; }

        [Input("OppProdId")]
        public InArgument<string> OppProdId { get; set; }

        [Input("Action")]
        public InArgument<string> Action { get; set; }

        [Input("IsFromBatching")]
        public InArgument<bool> IsFromBatching { get; set; }

        [Input("Comment")]
        public InArgument<string> Comment { get; set; }

        [Input("CartSellingRate")]
        public InArgument<string> CartSellingRate { get; set; }

        [Input("CartQty")]
        public InArgument<string> CartQty { get; set; }

        [Input("CartQtyOfEvents")]
        public InArgument<string> CartQtyOfEvents { get; set; }

        [Input("CartHardCost")]
        public InArgument<string> CartHardCost { get; set; }

        [Input("OverwriteLegalDef")]
        public InArgument<string> OverwriteLegalDef { get; set; }

        [Input("LegalDefinition")]
        public InArgument<string> LegalDefinition { get; set; }

        [Output("OutMessage")]
        public OutArgument<string> OutMessage { get; set; }

        [Input("AddProductJsonOpportunity")]
        public InArgument<string> AddProductJsonOpportunity { get; set; }

        [Output("OutAddProductUpdatedOppGuid")]
        public OutArgument<string> OutAddProductUpdatedOppGuid { get; set; }

        private IOrganizationService GetAdminImpersonationService(IOrganizationService service, IOrganizationServiceFactory serviceFactory)
        {
            QueryExpression adminSettingQuery = new QueryExpression("ats_agiliteksettings");
            adminSettingQuery.ColumnSet = new ColumnSet(new string[] { "ats_value", "ats_key" });
            adminSettingQuery.Criteria.AddCondition("ats_key", ConditionOperator.Equal, "AdminUserID");
            EntityCollection adminUserSetting = service.RetrieveMultiple(adminSettingQuery);
            if (adminUserSetting.Entities.Count > 0 )
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
            tracingService.Trace("RecalOppLines Logic begins");
            Logging.Log("Before Action (Action.Get(context).ToString()  " + Action.Get(context).ToString(), tracingService);

            Dictionary<string, string> args = new Dictionary<string, string>();
            bool isFromBatching = false;


            ////Sunny Testing 
            if (Action.Get(context).ToString() == "ReCalcOppLinesAgreementDelete")
            {
                tracingService.Trace("Inside ReCalcOppLinesAgreementTest");
                args.Add("OppId", OppId.Get(context).ToString());
                tracingService.Trace("Added OppId to args: " + OppId.Get(context).ToString());
            }
            //============================================================================================================






            if (Action.Get(context).ToString() == "SaveCartLine" || Action.Get(context).ToString() == "DeleteCartItem" || Action.Get(context).ToString() == "AddLine")
            {
                if (OppId != null)
                {
                    args.Add("OppId", OppId.Get(context).ToString());
                }

                Logging.Log("Before OppProdId args add  ", tracingService);
                if (OppProdId != null)
                {
                    args.Add("OppProdId", OppProdId.Get(context).ToString());
                }

                Logging.Log("Before Legal Definition " + LegalDefinition.Get(context), tracingService);
                if (LegalDefinition.Get(context) != null)
                    args.Add("LegalDefinition", LegalDefinition.Get(context).ToString());

                Logging.Log("After Legal Definition  ", tracingService);
                if (OverwriteLegalDef.Get(context) != null)
                    args.Add("OverwriteLegalDef", OverwriteLegalDef.Get(context).ToString());

                Logging.Log("After OverwriteLegalDef", tracingService);

                if (Action.Get(context).ToString() == "SaveCartLine")
                {
                    args.Add("CartQty", CartQty.Get(context).ToString());
                    args.Add("CartQtyOfEvents", CartQtyOfEvents.Get(context).ToString()); 

                    //If Qty changes then Validate the avability of the Product else proced with current logic
                    RecalculateOppurtunityLinesBusinessLogic validateQty = new RecalculateOppurtunityLinesBusinessLogic();
                    string msg = validateQty.ValidateOppProdLines(args, service, tracingService);
                    Logging.Log("Output Message" + msg, tracingService);
                    if (msg != "Success")
                    {
                        OutMessage.Set(context, msg);
                        return;
                    }
                }

                OutMessage.Set(context, "Success");
            }

            args.Add("Action", Action.Get(context).ToString());
            Logging.Log("After EditJson args add  ", tracingService);

            if (args["Action"] == "SetIbsSoldAndPitchedQtyFromOpp")
            {
                var opp = OppurtunityEntityReference.Get(context);
                var oppId = opp.Id.ToString();

                args.Add("OppId", oppId);

                if (oppId != null && oppId != string.Empty) // Triggered when Contract lookup changes, gets added or removed from Opporutunity
                {
                    // Get Opp record, Opp Prod record, and get Event schedule record from OPPID --> Calc and update IBS
                    SetCalculatedFields calculatedFields = new SetCalculatedFields();

                    calculatedFields.RecalculateIbsSoldAndPitchedQty(args, adminService ?? service, tracingService);
                }

                return;
            }

            if (args["Action"] == "SetIbsSoldAndPitchedFromOppProd")
            {
                var oppProd = OppurtunityProductEntityReference.Get(context);
                var oppProdId = oppProd.Id.ToString();
                args.Add("OppProdId", oppProdId);

                if (oppProdId != null && oppProdId != string.Empty) // Triggered when Contract lookup changes, gets added or removed from Opporutunity
                {
                    // Get Opp record, Opp Prod record, and get Event schedule record from OPPID --> Calc and update IBS
                    SetCalculatedFields calculatedFields = new SetCalculatedFields();

                    calculatedFields.RecalculateIbsSoldAndPitchedQty(args, adminService ?? service, tracingService);
                }

                return;
            }

            //string isFromBatching = IsFromBatching.Get(context).ToString();

            if (args["Action"] == "ReCalcOppLinesAgreement")
            {
                tracingService.Trace("Inside ReCalcOppLinesAgreement");
                isFromBatching = IsFromBatching.Get(context);
            }


            Logging.Log("Context Depth" + workflowContext.Depth.ToString(), tracingService);



            if (args["Action"] == "SaveCartLine")
            {
                Logging.Log("Inside EditJson IF args add Before deserialize", tracingService);
                var cartSellingRate = CartSellingRate.Get(context);
                var cartQty = CartQty.Get(context);
                var cartQtyOfEvents = CartQtyOfEvents.Get(context);
                var cartHardCost = CartHardCost.Get(context);
                var cartComment = Comment.Get(context);
                var legalDefinition = LegalDefinition.Get(context);
                var overWriteLegalDef = OverwriteLegalDef.Get(context);

                RecalculateOppurtunityLinesBusinessLogic updateLine = new RecalculateOppurtunityLinesBusinessLogic();

                Logging.Log("***ARGS VALUE*********" + cartSellingRate + " : " + cartQty + ":" + cartQtyOfEvents + ":" + cartHardCost + ":" + cartComment + "************", tracingService);

                if (cartSellingRate != null && cartSellingRate != string.Empty)
                {
                    args.Add("ats_sellingrate", cartSellingRate);
                }
                if (cartQtyOfEvents != null && cartQtyOfEvents != string.Empty)
                {
                    args.Add("ats_quantityofevents", cartQtyOfEvents);
                }
                if (cartQty != null && cartQty != string.Empty)
                {
                    args.Add("ats_quantity", cartQty);
                }
                if (cartHardCost != null && cartHardCost != string.Empty)
                {
                    args.Add("ats_hardcost", cartHardCost);
                }
                if (cartComment != null)
                {
                    args.Add("description", cartComment);
                }
                if (legalDefinition != null && legalDefinition != string.Empty)
                {
                    args.Add("ats_legaldefinition", legalDefinition);
                }

                args.Add("ats_overwritelegaldefinition", overWriteLegalDef);
                updateLine.UpdateOppProdLines(args, service, tracingService);

                Logging.Log("Inside EditJson IF args add After deserialize", tracingService);
            }

            RecalculateOppurtunityLinesBusinessLogic recalLines = new RecalculateOppurtunityLinesBusinessLogic();
            Logging.Log("Before RecalculateOppProdLines", tracingService);

            EntityReference oppRecord = null;
            var oppIdValue = string.Empty;
            Logging.Log("args[Action]" + args["Action"].ToString(), tracingService);
            if (args["Action"] == "ReCalcOppLinesD365" || args["Action"] == "ReCalcOppLinesAgreement")
            {
                Logging.Log("OppurtunityEntityReference.Get(context);" + OppurtunityEntityReference.Get(context).ToString(), tracingService);
                oppRecord = OppurtunityEntityReference.Get(context);
                oppIdValue = oppRecord.Id.ToString();
                args.Add("OppId", oppIdValue);
                Logging.Log("args.Add(OppId, oppIdValue);" + oppIdValue, tracingService);
            }
            else if (args["Action"] == "ReCalcOppLinesAction")
            {
                args.Add("OppId", OppId.Get(context).ToString());
            }



            recalLines.RecalculateOppProdLines(args, service, tracingService);

           


            Logging.Log("After RecalculateOppProdLines", tracingService);
        }
    }
    public class AgreementOpportunityData
    {
        public string AgreementId { get; set; }
        public List<string> Opportunities { get; set; }
    }
}
