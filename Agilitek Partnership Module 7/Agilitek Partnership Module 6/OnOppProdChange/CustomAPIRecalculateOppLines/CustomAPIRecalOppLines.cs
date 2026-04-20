using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;

namespace Agilitek_Partnership
{
    /// <summary>
    /// Plugin conversion of CodeActivity: RecalculateOppurtunityLines
    /// Register this on a Custom Action / Custom API message where you pass the same input parameters:
    /// OppId, OppProdId, Action, IsFromBatching, Comment, CartSellingRate, CartQty, CartQtyOfEvents, CartHardCost,
    /// OverwriteLegalDef, LegalDefinition, OppurtunityEntityReference (EntityReference), OppurtunityProductEntityReference (EntityReference)
    ///
    /// Outputs:
    /// OutMessage (string), OutAddProductUpdatedOppGuid (string)
    /// </summary>
    public class CustomAPIRecalOppLines : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            // User service (same as workflowContext.UserId in CodeActivity)
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            tracing.Trace("RecalOppLines Plugin Logic begins");
            tracing.Trace("MessageName: {0}, Stage: {1}, Depth: {2}", context.MessageName, context.Stage, context.Depth);

            // Read inputs (equivalent to Action.Get(context), OppId.Get(context), etc.)
            string action = GetString(context, "Action");
            string oppId = GetString(context, "OppId");
            string oppProdId = GetString(context, "OppProdId");

            EntityReference oppRef = GetEntityRef(context, "OppurtunityEntityReference");
            EntityReference oppProdRef = GetEntityRef(context, "OppurtunityProductEntityReference");

            bool isFromBatching = GetBool(context, "IsFromBatching");

            string comment = GetString(context, "Comment");
            string cartSellingRate = GetString(context, "CartSellingRate");
            string cartQty = GetString(context, "CartQty");
            string cartQtyOfEvents = GetString(context, "CartQtyOfEvents");
            string cartHardCost = GetString(context, "CartHardCost");
            string overwriteLegalDef = GetString(context, "OverwriteLegalDef");
            string legalDefinition = GetString(context, "LegalDefinition");

            // If you need these two later, keep them; they were present in workflow but not used in your shown logic
            string addProductJsonOpportunity = GetString(context, "AddProductJsonOpportunity");

            // Admin impersonation service (same method as your CodeActivity)
            IOrganizationService adminService = GetAdminImpersonationService(service, serviceFactory, tracing);

            // Prepare args dictionary (same as workflow version)
            var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(action))
            {
                // match safe behavior
                SetOutput(context, "OutMessage", "Action is required.");
                return;
            }

            tracing.Trace("Action: {0}", action);

            // Sunny Testing branch
            if (action == "ReCalcOppLinesAgreementDelete")
            {
                tracing.Trace("Inside ReCalcOppLinesAgreementDelete");

                if (!string.IsNullOrWhiteSpace(oppId))
                {
                    args["OppId"] = oppId;
                    tracing.Trace("Added OppId to args: {0}", oppId);
                }
                else if (oppRef != null && oppRef.Id != Guid.Empty)
                {
                    args["OppId"] = oppRef.Id.ToString();
                    tracing.Trace("Added OppId (from EntityReference) to args: {0}", args["OppId"]);
                }
            }

            // If action is SaveCartLine/DeleteCartItem/AddLine: basic validations + optional ValidateOppProdLines
            if (action == "SaveCartLine" || action == "DeleteCartItem" || action == "AddLine")
            {
                if (!string.IsNullOrWhiteSpace(oppId))
                    args["OppId"] = oppId;

                if (!string.IsNullOrWhiteSpace(oppProdId))
                    args["OppProdId"] = oppProdId;

                if (!string.IsNullOrWhiteSpace(legalDefinition))
                    args["LegalDefinition"] = legalDefinition;

                if (!string.IsNullOrWhiteSpace(overwriteLegalDef))
                    args["OverwriteLegalDef"] = overwriteLegalDef;

                if (action == "SaveCartLine")
                {
                    if (!string.IsNullOrWhiteSpace(cartQty))
                        args["CartQty"] = cartQty;

                    if (!string.IsNullOrWhiteSpace(cartQtyOfEvents))
                        args["CartQtyOfEvents"] = cartQtyOfEvents;

                    // Validate Qty availability
                    var validateQty = new RecalculateOppurtunityLinesBusinessLogic();
                    string msg = validateQty.ValidateOppProdLines(args, service, tracing);

                    tracing.Trace("ValidateOppProdLines Output: {0}", msg);

                    if (!string.Equals(msg, "Success", StringComparison.OrdinalIgnoreCase))
                    {
                        SetOutput(context, "OutMessage", msg);
                        return;
                    }
                }

                SetOutput(context, "OutMessage", "Success");
            }

            // Always add action
            args["Action"] = action;

            // SetIbsSoldAndPitchedQtyFromOpp
            if (action == "SetIbsSoldAndPitchedQtyFromOpp")
            {
                if (oppRef == null || oppRef.Id == Guid.Empty)
                {
                    SetOutput(context, "OutMessage", "OppurtunityEntityReference is required for SetIbsSoldAndPitchedQtyFromOpp.");
                    return;
                }

                args["OppId"] = oppRef.Id.ToString();

                var calculatedFields = new SetCalculatedFields();
                calculatedFields.RecalculateIbsSoldAndPitchedQty(args, adminService ?? service, tracing);

                SetOutput(context, "OutMessage", "Success");
                return;
            }

            // SetIbsSoldAndPitchedFromOppProd
            if (action == "SetIbsSoldAndPitchedFromOppProd")
            {
                if (oppProdRef == null || oppProdRef.Id == Guid.Empty)
                {
                    SetOutput(context, "OutMessage", "OppurtunityProductEntityReference is required for SetIbsSoldAndPitchedFromOppProd.");
                    return;
                }

                args["OppProdId"] = oppProdRef.Id.ToString();

                var calculatedFields = new SetCalculatedFields();
                calculatedFields.RecalculateIbsSoldAndPitchedQty(args, adminService ?? service, tracing);

                SetOutput(context, "OutMessage", "Success");
                return;
            }

            // ReCalcOppLinesAgreement sets batching flag (kept same behavior)
            if (action == "ReCalcOppLinesAgreement")
            {
                tracing.Trace("Inside ReCalcOppLinesAgreement");
                // in workflow you read IsFromBatching here; we already did above.
            }

            // SaveCartLine additional update logic (update OPP Product fields)
            if (action == "SaveCartLine")
            {
                var updateLine = new RecalculateOppurtunityLinesBusinessLogic();

                if (!string.IsNullOrWhiteSpace(cartSellingRate))
                    args["ats_sellingrate"] = cartSellingRate;

                if (!string.IsNullOrWhiteSpace(cartQtyOfEvents))
                    args["ats_quantityofevents"] = cartQtyOfEvents;

                if (!string.IsNullOrWhiteSpace(cartQty))
                    args["ats_quantity"] = cartQty;

                if (!string.IsNullOrWhiteSpace(cartHardCost))
                    args["ats_hardcost"] = cartHardCost;

                if (comment != null) // allow empty comment if you want to clear it
                    args["description"] = comment;

                if (!string.IsNullOrWhiteSpace(legalDefinition))
                    args["ats_legaldefinition"] = legalDefinition;

                // In your workflow code, you always add overwrite flag (even if null)
                // Keep same behavior; store empty string if null.
                args["ats_overwritelegaldefinition"] = overwriteLegalDef ?? string.Empty;

                updateLine.UpdateOppProdLines(args, service, tracing);
            }

            // Determine OppId for recalc
            EntityReference oppRecord = null;
            string oppIdValue = string.Empty;

            if (action == "ReCalcOppLinesD365" || action == "ReCalcOppLinesAgreement")
            {
                if (oppRef == null || oppRef.Id == Guid.Empty)
                {
                    SetOutput(context, "OutMessage", "OppurtunityEntityReference is required for ReCalcOppLinesD365 / ReCalcOppLinesAgreement.");
                    return;
                }

                oppRecord = oppRef;
                oppIdValue = oppRecord.Id.ToString();
                args["OppId"] = oppIdValue;
            }
            else if (action == "ReCalcOppLinesAction")
            {
                if (string.IsNullOrWhiteSpace(oppId))
                {
                    SetOutput(context, "OutMessage", "OppId is required for ReCalcOppLinesAction.");
                    return;
                }

                args["OppId"] = oppId;
            }

            // Execute main recalculation
            var recalLines = new RecalculateOppurtunityLinesBusinessLogic();
            recalLines.RecalculateOppProdLines(args, service, tracing);

            SetOutput(context, "OutMessage", "Success");

            // If later you want to set OutAddProductUpdatedOppGuid, do it here:
            // SetOutput(context, "OutAddProductUpdatedOppGuid", someGuidString);
        }

        private static string GetString(IPluginExecutionContext context, string key)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(key) && context.InputParameters[key] != null)
                return context.InputParameters[key].ToString();

            return null;
        }

        private static bool GetBool(IPluginExecutionContext context, string key)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(key) && context.InputParameters[key] != null)
            {
                if (context.InputParameters[key] is bool b) return b;
                if (bool.TryParse(context.InputParameters[key].ToString(), out bool parsed)) return parsed;
            }
            return false;
        }

        private static EntityReference GetEntityRef(IPluginExecutionContext context, string key)
        {
            if (context.InputParameters != null && context.InputParameters.Contains(key) && context.InputParameters[key] != null)
                return context.InputParameters[key] as EntityReference;

            return null;
        }

        private static void SetOutput(IPluginExecutionContext context, string key, string value)
        {
            if (context.OutputParameters == null)
                return;

            if (context.OutputParameters.Contains(key))
                context.OutputParameters[key] = value;
            else
                context.OutputParameters.Add(key, value);
        }

        private static IOrganizationService GetAdminImpersonationService(
            IOrganizationService service,
            IOrganizationServiceFactory serviceFactory,
            ITracingService tracing)
        {
            try
            {
                var adminSettingQuery = new QueryExpression("ats_agiliteksettings")
                {
                    ColumnSet = new ColumnSet("ats_value", "ats_key")
                };
                adminSettingQuery.Criteria.AddCondition("ats_key", ConditionOperator.Equal, "AdminUserID");

                var adminUserSetting = service.RetrieveMultiple(adminSettingQuery);

                if (adminUserSetting.Entities.Count > 0
                    && adminUserSetting.Entities[0].Attributes.Contains("ats_value")
                    && adminUserSetting.Entities[0]["ats_value"] != null
                    && Guid.TryParse(adminUserSetting.Entities[0]["ats_value"].ToString(), out Guid adminUserId))
                {
                    return serviceFactory.CreateOrganizationService(adminUserId);
                }
            }
            catch (Exception ex)
            {
                tracing.Trace("GetAdminImpersonationService failed: {0}", ex);
            }

            return null;
        }
    }

    //public class AgreementOpportunityData
    //{
    //    public string AgreementId { get; set; }
    //    public System.Collections.Generic.List<string> Opportunities { get; set; }
    //}
}