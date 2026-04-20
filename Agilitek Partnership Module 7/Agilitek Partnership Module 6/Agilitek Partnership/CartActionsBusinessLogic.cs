using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Web.Services.Description;

namespace Agilitek_Partnership
{
    public class CartActionsBusinessLogic
    {
        private static String[] units = { "Zero", "One", "Two", "Three",
    "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven",
    "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
    "Seventeen", "Eighteen", "Nineteen" };
        private static String[] tens = { "", "", "Twenty", "Thirty", "Forty",
    "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

        string getAllOppLinesFromOpp = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
  <entity name='opportunityproduct'>
    <attribute name='opportunityproductid' />
    <attribute name='productid' />
    <attribute name='priceperunit' />
    <attribute name='ats_quantityofevents' />
    <attribute name='ats_quantity' />
    <attribute name='opportunityproductid' />
    <attribute name='opportunityproductname' />
    <attribute name='ats_sellingrate' />
    <attribute name='ats_legaldefinition' />
    <attribute name='ats_inventorybyseason' />
    <attribute name='ats_unadjustedtotalprice' />
    <attribute name='ats_adjustedtotalprice' />
    <attribute name='ats_hardcost' />
    <attribute name='ats_discount' />
    <attribute name='description' />
    <filter type='and'>
      <condition attribute='opportunityid' operator='eq' value='{0}' />
    </filter>
    <link-entity name='opportunity' from='opportunityid' to='opportunityid' link-type='inner' alias='Opp'>
      <attribute name='ats_type' />
      <attribute name='ats_startseason' />
      <attribute name='ats_salesgoal' />
      <attribute name='ats_pricingmode' />
      <attribute name='ats_manualamount' />
      <attribute name='ats_tradeamount' />
      <attribute name='ats_isprivate' />
      <attribute name='ats_dealvalue' />
      <attribute name='budgetamount' />
      <attribute name='opportunityid' />
    </link-entity>
    <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Prod'>
      <attribute name='ats_ispassthroughcost' />
      <attribute name='ats_division' />
      <attribute name='ats_productfamily' />
      <attribute name='ats_productsubfamily' />
    </link-entity>
    <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' link-type='inner' alias='Rate'>
      <attribute name='ats_lockhardcost' />
      <attribute name='ats_lockunitrate' />
      <attribute name='ats_ratetype' />
      <attribute name='ats_name' />
      <attribute name='ats_price' />
      <attribute name='ats_hardcost' />
    </link-entity>
  </entity>
</fetch>";

        string invBySeasonLinkedProd = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
  <entity name='ats_inventorybyseason'>
    <attribute name='ats_inventorybyseasonid' />
    <attribute name='ats_name' />
    <attribute name='ats_unlimitedquantity' />
    <attribute name='ats_quantitysold' />
    <attribute name='ats_quantityavailable' />
    <attribute name='ats_quantitypitched' />
    <attribute name='ats_totalquantity' />
    <attribute name='ats_totalquantityperevent' />
    <attribute name='ats_season' />
    <attribute name='ats_recordtype' />
    <attribute name='ats_product' />
    <attribute name='ats_legaldefinition' />
    <attribute name='ats_eventschedule' />
    <attribute name='ats_description' />
    <filter>
      <condition attribute='ats_inventorybyseasonid' operator='eq' value='{0}' />
    </filter>
    <link-entity name='product' from='productid' to='ats_product' link-type='inner' alias='Prod'>
      <attribute name='ats_productsubfamily' />
      <attribute name='ats_productfamily' />
      <attribute name='ats_division' />
    </link-entity>
    <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='inner' alias='Rate'>
      <attribute name='ats_rateid' />
      <attribute name='ats_price' />
      <attribute name='ats_ratetype' />
      <attribute name='ats_hardcost' />
      <attribute name='ats_lockunitrate' />
      <attribute name='ats_lockhardcost' />
      <attribute name='ats_legaldefinition' />
    </link-entity>
    <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventschedule' link-type='outer' alias='ES'>
      <attribute name='ats_name' />
      <attribute name='ats_actualeventquantity' />
      <attribute name='ats_expectedeventquantity' />
      <attribute name='ats_seasoncategory' />
    </link-entity>
  </entity>
</fetch>
";

        string GetUnitId = @"<fetch>
  <entity name='uom'>
    <attribute name='name' />
    <attribute name='uomid' />
    <filter>
      <condition attribute='name' operator='eq' value='Primary Unit' />
    </filter>
  </entity>
</fetch>";

        string getIbsByOppProdId = @"<fetch>
  <entity name='opportunity'>
    <attribute name='opportunityid' />
    <attribute name='statuscode' />
   
    <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' link-type='inner' alias='oppProd'>
      <attribute name='opportunityproductid' />
      <attribute name='ats_quantity' />
      <attribute name='ats_quantityofevents' />
 <filter>
      <condition attribute='opportunityproductid' operator='eq' value='{0}' />
    </filter>
      <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='inner' alias='ibs'>
        <attribute name='ats_inventorybyseasonid' />
        <attribute name='ats_quantitysold' />
        <attribute name='ats_quantitypitched' />
        <attribute name='ats_totalquantity' />
        <attribute name='ats_totalquantityperevent' />
        <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventschedule' link-type='inner' alias='es'>
          <attribute name='ats_actualeventquantity' />
          <attribute name='ats_expectedeventquantity' />
        </link-entity>
      </link-entity>
      <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' link-type='inner' alias='r'>
        <attribute name='ats_ratetype' />
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

        string getIbsById = @"<fetch>
  <entity name='ats_inventorybyseason'>
    <attribute name='ats_allowoverselling' />
    <attribute name='ats_unlimitedquantity' />
    <attribute name='ats_quantityavailable' />
    <filter>
      <condition attribute='ats_inventorybyseasonid' operator='eq' value='{0}' />
    </filter>
    <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventschedule' alias='ES'>
      <attribute name='ats_expectedeventquantity' />
    </link-entity>
    <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' alias='Rate'>
      <attribute name='ats_rateid' />  
      <attribute name='ats_inactive' />
      <attribute name='ats_ratetype' />
    </link-entity>
  </entity>
</fetch>";

        enum OppStatusReason
        {
            Opportunity = 114300008,
            Proposal = 114300009,
            Contract = 114300010
        }

        enum RateType
        {
            Season = 114300000,
            Individual = 114300001
        }

        //Validate Product Line
        public string ValidateProdLines(Dictionary<string, string> args, IOrganizationService service, ITracingService tracingService)
        {
            Logging.Log("Before RetrieveMultiple In Validation******", tracingService);
            EntityCollection oppProductLinesRetrieved = service.RetrieveMultiple(new FetchExpression(string.Format(getIbsById, args["InvBySeasonId"])));
            Logging.Log("After RetrieveMultiple In Validation******", tracingService);

            foreach (var oppProductDetails in oppProductLinesRetrieved.Entities)
            {
                Logging.Log("Inside for loop******", tracingService);
                Logging.Log("Rate String: " + oppProductDetails.Attributes["Rate.ats_rateid"].ToString(), tracingService);
                Logging.Log("Alias Value: " + ((AliasedValue)oppProductDetails.Attributes["Rate.ats_rateid"]).Value.ToString(), tracingService);
                Logging.Log("Arg RateId: " + args["RateId"].ToString(), tracingService);
                Logging.Log("GUID: " + ((Guid)((AliasedValue)oppProductDetails.Attributes["Rate.ats_rateid"]).Value), tracingService);

                string xmlRateId = ((AliasedValue)oppProductDetails.Attributes["Rate.ats_rateid"]).Value.ToString();
                if (args["RateId"].ToString() == xmlRateId)
                {
                    Logging.Log("Before Conversion ibsQuantityAvailable******", tracingService);
                    var ibsQtyAvailable = oppProductDetails.Attributes.ContainsKey("ats_quantityavailable") ?
                                    (int)(oppProductDetails.Attributes["ats_quantityavailable"]) : 0;

                    Logging.Log("Before Conversion ibsOverselling******", tracingService);
                    var ibsOverselling = oppProductDetails.Attributes.ContainsKey("ats_allowoverselling") ?
                                   ((bool)(oppProductDetails.Attributes["ats_allowoverselling"])) : false;
                    Logging.Log("After Conversion ibsOverselling******", tracingService);

                    Logging.Log("Before Conversion ibsUnlimitedQuantity******", tracingService);
                    var ibsUnlimitedQuantity = oppProductDetails.Attributes.ContainsKey("ats_unlimitedquantity") ?
                                   ((bool)(oppProductDetails.Attributes["ats_unlimitedquantity"])) : false;
                    Logging.Log("After Conversion ibsUnlimitedQuantity******", tracingService);

                    Logging.Log("Before Conversion rateInactive******", tracingService);
                    var rateInactive = oppProductDetails.Attributes.ContainsKey("Rate.ats_inactive") ?
                                   ((bool)((AliasedValue)oppProductDetails.Attributes["Rate.ats_inactive"]).Value) : false;
                    Logging.Log("After Conversion rateInactive******", tracingService);

                    var esExpectedEventQty = oppProductDetails.Attributes.ContainsKey("ES.ats_expectedeventquantity") ?
                        (int)((AliasedValue)oppProductDetails.Attributes["ES.ats_expectedeventquantity"]).Value : 1;
                    Logging.Log("After Conversion esExpectedEventQty******", tracingService);


                    var type = (RateType)Convert.ToInt32(((OptionSetValue)((AliasedValue)oppProductDetails.Attributes["Rate.ats_ratetype"]).Value).Value);
                    Logging.Log("Set All Variable Values In Validation******", tracingService);

                    Logging.Log("Before Getting New Cart Qty In Validation******", tracingService);
                    var cartQty = Convert.ToInt32(args["Qty"]);
                    var cartQtyOfEvents = Convert.ToInt32(args["QtyOfEvents"]);
                    Logging.Log("After Getting New Cart Qty In Validation******", tracingService);

                    Int64 lineQty = 0;

                    if (type == RateType.Season)
                    {
                        lineQty = cartQty * esExpectedEventQty;
                    }
                    if (type == RateType.Individual)
                    {
                        lineQty = cartQty * cartQtyOfEvents;
                    }
                    Logging.Log("Type : "+type+" Line Qty : "+lineQty + "rateInactive : "+ rateInactive + " ibsQtyAvailable : "+ ibsQtyAvailable, tracingService);

                    if (rateInactive)  //Check if product is active
                        return "ProductInactive";

                    if (lineQty > ibsQtyAvailable)
                    { //Verify if overselling of product is allowed
                        if (!ibsOverselling && !ibsUnlimitedQuantity) //Stop Overselling
                            return "OSNotAllowed";
                    }
                }
            }

            return "Success";
        }

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

        public void CreateOppProdLines(CodeActivityContext context, Dictionary<string, string> args)
        {
            IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
            IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            IOrganizationService adminService = GetAdminImpersonationService(service, serviceFactory);
            ITracingService tracingService = context.GetExtension<ITracingService>();

            EntityCollection inventoryBySeasonRecordList = service.RetrieveMultiple(new FetchExpression(string.Format(invBySeasonLinkedProd, args["InvBySeasonId"])));
            var inventoryBySeasonRecord = inventoryBySeasonRecordList.Entities.FirstOrDefault();

            var qtyofevts = args["QtyOfEvents"] == "0" ? 1 : Convert.ToInt32(args["QtyOfEvents"]);
            string legalFormatted = string.Empty;

            Logging.Log("*****************InventoryBySeasonRecord Retrieve*********************", tracingService);

            if (inventoryBySeasonRecord != null)
            {
                Entity OpportunityProduct = new Entity("opportunityproduct");
                OpportunityProduct["opportunityid"] = new EntityReference("opportunity", Guid.Parse(args["OppId"]));

                Logging.Log("*****************BEFORE CREATE*********************", tracingService);

                var getuom = service.RetrieveMultiple(new FetchExpression(GetUnitId));
                var uomid = getuom.Entities.FirstOrDefault().Id;
                OpportunityProduct["uomid"] = new EntityReference("uom", uomid);

                if (inventoryBySeasonRecord.Attributes.ContainsKey("ats_name"))
                {
                    OpportunityProduct["opportunityproductname"] = inventoryBySeasonRecord.Attributes["ats_name"];
                }

                if (inventoryBySeasonRecord.Attributes.ContainsKey("Rate.ats_price"))
                {
                    Money priceperunit = new Money(((Money)((AliasedValue)inventoryBySeasonRecord.Attributes["Rate.ats_price"]).Value).Value);
                    OpportunityProduct["priceperunit"] = priceperunit; // Price per unit is RateCard's Rate Price
                    OpportunityProduct["ats_sellingrate"] = priceperunit;
                }

                if (args["QtyOfEvents"] != null)
                {
                    OpportunityProduct["ats_quantityofevents"] = qtyofevts;
                }

                if (args["Qty"] != null)
                {
                    var qtyofevents = decimal.One;
                    var qty = Convert.ToDecimal(args["Qty"]);

                    if (args["QtyOfEvents"] != null && args["QtyOfEvents"] != "")
                    {
                        qtyofevents = Convert.ToDecimal(qtyofevts);
                    }

                    Money priceperunit = new Money(((Money)((AliasedValue)inventoryBySeasonRecord.Attributes["Rate.ats_price"]).Value).Value);
                    OpportunityProduct["ats_quantity"] = qty;

                    Logging.Log("*****************BEFORE ats_unadjustedtotalprice*********************", tracingService);
                    OpportunityProduct["ats_unadjustedtotalprice"] = new Money(qty * qtyofevents * priceperunit.Value);
                    Logging.Log("*****************AFTER ats_unadjustedtotalprice*********************", tracingService);
                    Logging.Log(priceperunit.Value.ToString(), tracingService);
                    Logging.Log((qty * qtyofevents * priceperunit.Value).ToString(), tracingService);
                }

                if (inventoryBySeasonRecord.Attributes.ContainsKey("ats_inventorybyseasonid"))
                {
                    OpportunityProduct["ats_inventorybyseason"] = new EntityReference("ats_inventorybyseason", Guid.Parse(args["InvBySeasonId"]));
                }

                if (inventoryBySeasonRecord.Attributes.ContainsKey("ats_product"))
                {
                    OpportunityProduct["productid"] = inventoryBySeasonRecord.Attributes["ats_product"];
                }

                if (args["RateId"] != null)
                {
                    OpportunityProduct["ats_rate"] = new EntityReference("ats_rate", Guid.Parse(args["RateId"]));
                }

                if (args["Comment"] != null)
                {
                    OpportunityProduct["description"] = args["Comment"];
                }

                if (inventoryBySeasonRecord.Attributes.ContainsKey("Rate.ats_hardcost"))
                {
                    Money hardcost = new Money(((Money)((AliasedValue)inventoryBySeasonRecord.Attributes["Rate.ats_hardcost"]).Value).Value);
                    OpportunityProduct["ats_hardcost"] = hardcost;
                }

                if (args["Comment"] != null)
                {
                    OpportunityProduct["description"] = args["Comment"];
                }

                if (args["LegalDefinition"] != null)
                {
                    string legalDefinition = args["LegalDefinition"];
                    if (!string.IsNullOrEmpty(legalDefinition))
                    {
                        if (args["QtyOfEvents"] != null)
                        {
                            string qtyEventWords = ConvertAmount(Convert.ToInt64(args["QtyOfEvents"]));
                            qtyEventWords = ConvertAmount(Convert.ToInt64(qtyofevts));
                            legalFormatted = legalFormatted.Replace("{#Events}", qtyEventWords + "(" + args["QtyOfEvents"].ToString() + ")");
                            legalFormatted = legalFormatted.Replace("{#events}", qtyEventWords.ToLower() + "(" + args["QtyOfEvents"].ToString() + ")");
                        }
                        
                        if(args["Qty"] != null)
                        {
                            string qtyWords = ConvertAmount(Convert.ToInt64(args["Qty"]));
                            legalFormatted = legalFormatted.Replace("{#units}", qtyWords.ToLower() + "(" + args["Qty"].ToString() + ")");
                            legalFormatted = legalFormatted.Replace("{#Units}", qtyWords + "(" + args["Qty"].ToString() + ")");
                        }
                        Logging.Log("Legal Definition Formatted******" + legalFormatted, tracingService);

                        OpportunityProduct["ats_legaldefinitionformatted"] = legalFormatted;
                        Logging.Log("******** FORMATTED LEGAL DEIFINITION : " + legalFormatted, tracingService);

                        OpportunityProduct["ats_legaldef"] = legalDefinition;
                    }
                }

                Logging.Log("Legal Definition Formatted : " + legalFormatted, tracingService);
                OpportunityProduct["ats_overwritelegaldefinition"] = Convert.ToBoolean(args["OverwriteLegalDef"]);
                Logging.Log("******** END LEGAL DEIFINITATION *****", tracingService);

                Logging.Log("******** BEFORE CREATING *****", tracingService);
                (adminService != null ? adminService : service).Create(OpportunityProduct);
                Logging.Log("******** Opp Product CREATED *****", tracingService);

                var notificationRequest = new OrganizationRequest()
                {
                    RequestName = "SendAppNotification",
                    Parameters = new ParameterCollection
                    {
                        ["Title"] = "A new Opportunity Product is created",
                        ["Recipient"] = new EntityReference("systemuser", workflowContext.InitiatingUserId),
                        ["Body"] = "Opportunity Product created successfully!",
                        ["IconType"] = new OptionSetValue(100000001), //info
                        ["ToastType"] = new OptionSetValue(200000000) //timed
                    }
                };

                Logging.Log("******** BEFORE CREATING Notification Request*****", tracingService);
                OrganizationResponse response = service.Execute(notificationRequest);
                Logging.Log("******** AFTER CREATING Notification Request*****", tracingService);
            }
        }

        public void PerformInventorySubtraction(CodeActivityContext context, Dictionary<string, string> args)
        {
            var workflowContext = context.GetExtension<IWorkflowContext>();
            var serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            var service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            var adminService = GetAdminImpersonationService(service, serviceFactory);
            var tracingService = context.GetExtension<ITracingService>();

            // Subtract from IBS Qty Pitched or Sold
            Logging.Log("Subtract from IBS Qty Pitched or Sold - OppProdID: " + args["OppProdId"].ToString(), tracingService);

            var query = string.Format(getIbsByOppProdId, args["OppProdId"].ToString());
            EntityCollection retrievedIbs = service.RetrieveMultiple(new FetchExpression(query));
            OppStatusReason oppStatusReason = (OppStatusReason)Convert.ToInt32(((OptionSetValue)retrievedIbs.Entities.FirstOrDefault().Attributes["statuscode"]).Value);

            if (oppStatusReason == OppStatusReason.Proposal || oppStatusReason == OppStatusReason.Contract)
            {
                Logging.Log("Status Reason is valid. Retrieved " + retrievedIbs.Entities.Count + " OppProd line to be deleted.", tracingService);

                foreach (var oppProd in retrievedIbs.Entities)
                {
                    var ibsId = ((AliasedValue)oppProd.Attributes["ibs.ats_inventorybyseasonid"]).Value.ToString();
                    var ibsSoldQty = oppProd.Attributes.ContainsKey("ibs.ats_quantitysold") ?
                        Convert.ToInt32(((AliasedValue)oppProd.Attributes["ibs.ats_quantitysold"]).Value) : 0;
                    var ibsPitchedQty = oppProd.Attributes.ContainsKey("ibs.ats_quantitypitched") ?
                         Convert.ToInt32(((AliasedValue)oppProd.Attributes["ibs.ats_quantitypitched"]).Value) : 0;
                    var oppProdQty = oppProd.Attributes.ContainsKey("oppProd.ats_quantity") ?
                        Convert.ToInt32(((AliasedValue)oppProd.Attributes["oppProd.ats_quantity"]).Value) : 1;
                    var oppProdQtyOfEvents = oppProd.Attributes.ContainsKey("oppProd.ats_quantityofevents") ?
                        Convert.ToInt32(((AliasedValue)oppProd.Attributes["oppProd.ats_quantityofevents"]).Value) : 1;
                    var expectedEventQty = oppProd.Attributes.ContainsKey("es.ats_expectedeventquantity") ?
                        Convert.ToInt32(((AliasedValue)oppProd.Attributes["es.ats_expectedeventquantity"]).Value) : 1;
                    var type = (RateType)Convert.ToInt32(((OptionSetValue)((AliasedValue)oppProd.Attributes["r.ats_ratetype"]).Value).Value);
                    var lineQty = oppProdQty * (type == RateType.Season ? expectedEventQty : oppProdQtyOfEvents);

                    var fieldname = oppStatusReason == OppStatusReason.Proposal ? "Pitched" : "Sold";
                    var currentQty = oppStatusReason == OppStatusReason.Proposal ? ibsPitchedQty : ibsSoldQty;

                    Logging.Log("Need to subtract " + lineQty + $" from {fieldname} value which is currently {currentQty}", tracingService);

                    Entity ibsToUpdate = new Entity("ats_inventorybyseason", new Guid(ibsId));
                    if (oppStatusReason == OppStatusReason.Proposal)
                        ibsToUpdate["ats_quantitypitched"] = ibsPitchedQty - lineQty;
                    else if (oppStatusReason == OppStatusReason.Contract)
                        ibsToUpdate["ats_quantitysold"] = ibsSoldQty - lineQty;
                    Logging.Log("Performing update...", tracingService);
                    (adminService != null ? adminService : service).Update(ibsToUpdate);
                }
            }
        }

        public void DeleteOppProdLine(CodeActivityContext context, Dictionary<string, string> args)
        {
            var workflowContext = context.GetExtension<IWorkflowContext>();
            var serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
            var service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
            var adminService = GetAdminImpersonationService(service, serviceFactory);
            var tracingService = context.GetExtension<ITracingService>();

            Logging.Log("Inside DeleteOppProdLines****** ID: " + args["OppProdId"].ToString(), tracingService);
            (adminService != null ? adminService : service).Delete("opportunityproduct", new Guid(args["OppProdId"]));
            Logging.Log("END OF DeleteOppProdLines******", tracingService);
        }

        #region Methods to Convert Numbers to Words
        public static String ConvertAmount(Int64 amount)
        {
            try
            {
                Int64 amount_int = (Int64)amount;
                return ConvertValue(amount_int);
            }
            catch (Exception e)
            {
                // TODO: handle exception  
            }
            return "";
        }

        public static String ConvertValue(Int64 i)
        {
            if (i < 20)
            {
                return units[i];
            }
            if (i < 100)
            {
                return tens[i / 10] + ((i % 10 > 0) ? " " + ConvertValue(i % 10) : "");
            }
            if (i < 1000)
            {
                return units[i / 100] + " Hundred"
                        + ((i % 100 > 0) ? " And " + ConvertValue(i % 100) : "");
            }
            if (i < 100000)
            {
                return ConvertValue(i / 1000) + " Thousand "
                + ((i % 1000 > 0) ? " " + ConvertValue(i % 1000) : "");
            }
            if (i < 10000000)
            {
                return ConvertValue(i / 100000) + " Million "
                        + ((i % 100000 > 0) ? " " + ConvertValue(i % 100000) : "");
            }
            if (i < 1000000000)
            {
                return ConvertValue(i / 10000000) + " Million "
                        + ((i % 10000000 > 0) ? " " + ConvertValue(i % 10000000) : "");
            }
            return ConvertValue(i / 1000000000) + " Billion "
                    + ((i % 1000000000 > 0) ? " " + ConvertValue(i % 1000000000) : "");
        }
        #endregion

    }
}
