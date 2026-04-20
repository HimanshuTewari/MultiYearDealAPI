using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Tooling.Connector;
using System;
using System.Configuration;
using Agilitek_Partnership;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Text;

namespace D365TestApp
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Console Application Started!");
            try
            {
                var test = Guid.Parse("5a040fac4fca4f2eaf5f853f2804709e");
                //Step 1 - Retrieving CRM Essential Information.
                string sEnvironment = ConfigurationManager.AppSettings["Environment"].ToString();
                string sUserKey = System.Configuration.ConfigurationManager.AppSettings["UserKey"].ToString();
                string sUserPassword = System.Configuration.ConfigurationManager.AppSettings["UserPassword"].ToString();
                //Step 2- Creating A Connection String.
                string conn = $@" Url = {sEnvironment};AuthType = OAuth;UserName = {sUserKey}; Password = {sUserPassword};AppId = 51f81489-12ee-4a9e-aaae-a2591f45987d;RedirectUri = app://58145B91-0C36-4500-8554-080854F2AC97;LoginPrompt=Auto; RequireNewInstance = True";
                Console.WriteLine("Operating Environment : " + sEnvironment);
                //Step 3 - Obtaining CRM Service.
                using (var service = new CrmServiceClient(conn))
                {
                    if (service != null)
                    {
                        Guid recordId = Guid.Parse("193c0dff-f1c2-ed11-b597-000d3af4f893"); // Inventory By Season Record // Getting OrganizationService from Context.  
                        CartActionsBusinessLogic oppProductLines = new CartActionsBusinessLogic();
                        //oppProductLines.CreateOrUpdateOppProdLines(null, recordId, service);
                        //oppProductLines.RecalculateOppProdLinesFromCA(null, Guid.Parse("f6a160fa-174d-417f-a43a-d6a8027fd717"), service);


                        Uri uri = new Uri("https://agiliteksolutionssandbox.crm3.dynamics.com/");
                        //string clientId = "edb0f75d-****-****-bcce-259e0d289148";
                        //string appKey = "jVE[Ek_****@w8Ov@OULHs1g7b493?="; //Client Secret
                        //ClientCredential credentials = new ClientCredential(clientId, appKey);
                        //string authority = "https://login.microsoftonline.com/d245e842-b71e-42df-a18e-176555cfb904/oauth2/token";
                        //var authResult = new AuthenticationContext(authority, true).AcquireTokenAsync("https://agiliteksolutionssandbox.crm3.dynamics.com/", credentials).Result;



                        var response = service.ExecuteWorkflowOnEntity("CreateOppotunityLinesFromInventoryBySeason", new Guid("f6a160fa-174d-417f-a43a-d6a8027fd717"));


                        var token = service.CurrentAccessToken;
                        var query = "workflows(58F740F4-16C8-42AD-8B85-D93AEB2CFE4C)/Microsoft.Dynamics.CRM.ExecuteWorkflow";

                        var httpClient = new HttpClient();
                        httpClient.BaseAddress = uri;

                        httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                        httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                        var response1 =  httpClient.PostAsync("https://agiliteksolutionssandbox.crm3.dynamics.com/api/data/v9.2/workflows(58F740F4-16C8-42AD-8B85-D93AEB2CFE4C)/Microsoft.Dynamics.CRM", new StringContent("", Encoding.UTF8, "application/json"));

                        //HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, httpClient.BaseAddress + query)
                        //{
                        //    Content = new StringContent("", Encoding.UTF8, "application/json")
                        //};
                        //service.ExecuteWorkflowOnEntity()
                        #region old code
                        //bool isUpdate = false;


                        //ColumnSet oppProdCols = new ColumnSet(
                        //new String[] { "opportunityproductid", "opportunityproductname", "quantity", "ats_inventorybyseason", "opportunityid", "productid", "priceperunit",
                        //    "uomid", "ats_division", "ats_adjustedhardcost", "ats_productfamily", "ats_productsubfamily"});

                        //ColumnSet oppInventoryBySeasonCols = new ColumnSet(
                        //new String[] { "ats_name", "ats_qtytemp", "ats_inventorybyseasonid", "ats_product", "ats_unitrate",
                        //    "ats_division", "ats_unithardcost", "ats_productfamily", "ats_productsubfamily"});


                        //Entity inventoryBySeasonRecord = service.Retrieve("ats_inventorybyseason", recordId, oppInventoryBySeasonCols);

                        //Console.WriteLine("*****************inventoryBySeasonRecord Retrieve*********************");
                        //if (inventoryBySeasonRecord != null)
                        //{

                        //    Entity OpportunityProduct = new Entity("opportunityproduct");


                        //    Console.WriteLine("*****************BEFORE CREATE*********************");


                        //    string fetchquery = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        //      <entity name='opportunityproduct'>
                        //        <attribute name='productid' />
                        //        <attribute name='productdescription' />
                        //        <attribute name='priceperunit' />
                        //        <attribute name='quantity' />
                        //        <attribute name='extendedamount' />
                        //        <attribute name='opportunityproductid' />
                        //        <attribute name='opportunityproductname' />
                        //        <attribute name='ats_productsubfamily' />
                        //        <attribute name='ats_productfamily' />
                        //        <attribute name='ats_inventorybyseason' />
                        //        <attribute name='ats_adjustedhardcost' />
                        //        <attribute name='ats_division' />
                        //        <filter>
                        //          <condition attribute='opportunityid' operator='eq' value='f6a160fa-174d-417f-a43a-d6a8027fd717' />
                        //        </filter>
                        //        <order attribute='productid' descending='false' />
                        //      </entity>
                        //    </fetch>";

                        //    int qty = 0;
                        //    EntityCollection oppProds = service.RetrieveMultiple(new FetchExpression(fetchquery));
                        //    foreach (var item in oppProds.Entities)
                        //    {
                        //        Console.WriteLine("Name: {0}", item.Attributes["opportunityproductname"]);
                        //        if (Convert.ToString(inventoryBySeasonRecord.FormattedValues["ats_product"]) == Convert.ToString(item.Attributes["opportunityproductname"])
                        //            && Guid.Parse(Convert.ToString(inventoryBySeasonRecord.Attributes["ats_inventorybyseasonid"])) == ((Microsoft.Xrm.Sdk.EntityReference)item.Attributes["ats_inventorybyseason"]).Id
                        //            && ((Microsoft.Xrm.Sdk.EntityReference)inventoryBySeasonRecord.Attributes["ats_product"]).Id == ((Microsoft.Xrm.Sdk.EntityReference)item.Attributes["productid"]).Id
                        //            && Convert.ToString(inventoryBySeasonRecord.Attributes["ats_division"]) == Convert.ToString(item.Attributes["ats_division"])
                        //            && Convert.ToString(inventoryBySeasonRecord.FormattedValues["ats_productfamily"]) == Convert.ToString(item.Attributes["ats_productfamily"])
                        //            && Convert.ToString(inventoryBySeasonRecord.FormattedValues["ats_productsubfamily"]) == Convert.ToString(item.Attributes["ats_productsubfamily"]))
                        //        {
                        //            isUpdate = true;
                        //            OpportunityProduct["opportunityproductid"] = item.Attributes["opportunityproductid"];
                        //            qty = Convert.ToInt32(item["quantity"]) + Convert.ToInt32(inventoryBySeasonRecord.Attributes["ats_qtytemp"]);
                        //        }
                        //    }

                        //    Console.WriteLine("******** " + isUpdate.ToString() + " IS UPDATE*****");

                        //    if (isUpdate)
                        //    {
                        //        OpportunityProduct["quantity"] = qty;
                        //        Console.WriteLine("******** BEFORE UPDATING *****");
                        //        service.Update(OpportunityProduct);
                        //        Console.WriteLine("******** QTY " + qty.ToString() + " *****");
                        //        Console.WriteLine("******* UPDATED *****");
                        //    }
                        //    else
                        //    {

                        //        if (inventoryBySeasonRecord.Attributes["ats_name"] != null)
                        //            OpportunityProduct["opportunityproductname"] = inventoryBySeasonRecord.Attributes["ats_name"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_qtytemp"] != null)
                        //            OpportunityProduct["quantity"] = inventoryBySeasonRecord.Attributes["ats_qtytemp"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_inventorybyseasonid"] != null)
                        //            OpportunityProduct["ats_inventorybyseason"] =
                        //                new EntityReference("ats_inventorybyseason", recordId);

                        //        OpportunityProduct["opportunityid"] = new EntityReference("opportunity", Guid.Parse("f6a160fa-174d-417f-a43a-d6a8027fd717"));

                        //        if (inventoryBySeasonRecord.Attributes["ats_product"] != null)
                        //            OpportunityProduct["productid"] = inventoryBySeasonRecord.Attributes["ats_product"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_unitrate"] != null)
                        //            OpportunityProduct["priceperunit"] = inventoryBySeasonRecord.Attributes["ats_unitrate"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_name"] != null)
                        //            OpportunityProduct["uomid"] = new EntityReference("uom", Guid.Parse("f8175ff2-0f60-45b6-b882-15d1bd249ae4"));

                        //        if (inventoryBySeasonRecord.Attributes["ats_division"] != null)
                        //            OpportunityProduct["ats_division"] = inventoryBySeasonRecord.Attributes["ats_division"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_productfamily"] != null)
                        //            OpportunityProduct["ats_productfamily"] = inventoryBySeasonRecord.FormattedValues["ats_productfamily"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_productsubfamily"] != null)
                        //            OpportunityProduct["ats_productsubfamily"] = inventoryBySeasonRecord.FormattedValues["ats_productsubfamily"];


                        //        Console.WriteLine("******** BEFORE CREATING *****");
                        //        service.Create(OpportunityProduct);
                        //        Console.WriteLine("******** CREATED *****");
                        //    }
                        //    //Console.Write(response);
                        //}




                        //Guid recordId = Guid.Parse("e2ae4831-c4c3-ed11-b597-000d3af4f893"); // Inventory By Season Record

                        //ColumnSet oppProdCols = new ColumnSet(
                        //new String[] { "opportunityproductid", "opportunityproductname", "quantity", "ats_inventorybyseason", "opportunityid", "productid", "priceperunit",
                        //    "uomid", "ats_division", "ats_adjustedhardcost", "ats_productfamily", "ats_productsubfamily"});

                        //ColumnSet oppInventoryBySeasonCols = new ColumnSet(
                        //new String[] { "ats_name", "ats_qtytemp", "ats_inventorybyseasonid", "ats_product", "ats_unitrate",
                        //    "ats_division", "ats_unithardcost", "ats_productfamily", "ats_productsubfamily"});


                        //Entity inventoryBySeasonRecord = service.Retrieve("ats_inventorybyseason", recordId, oppInventoryBySeasonCols);

                        //Console.WriteLine("*****************inventoryBySeasonRecord Retrieve*********************");

                        //if (inventoryBySeasonRecord != null)
                        //{

                        //    Entity OpportunityProduct = new Entity("opportunityproduct");



                        //    Console.WriteLine("*****************BEFORE CREATE*********************");



                        //    string fetchquery = @"<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
                        //      <entity name='opportunityproduct'>
                        //        <attribute name='productid' />
                        //        <attribute name='productdescription' />
                        //        <attribute name='priceperunit' />
                        //        <attribute name='quantity' />
                        //        <attribute name='extendedamount' />
                        //        <attribute name='opportunityproductid' />
                        //        <attribute name='opportunityproductname' />
                        //        <attribute name='ats_productsubfamily' />
                        //        <attribute name='ats_productfamily' />
                        //        <attribute name='ats_inventorybyseason' />
                        //        <attribute name='ats_adjustedhardcost' />
                        //        <attribute name='ats_division' />
                        //        <filter>
                        //          <condition attribute='opportunityid' operator='eq' value='f6a160fa-174d-417f-a43a-d6a8027fd717' />
                        //        </filter>
                        //        <order attribute='productid' descending='false' />
                        //      </entity>
                        //    </fetch>";

                        //    int qty = 0;
                        //    EntityCollection oppProds = service.RetrieveMultiple(new FetchExpression(fetchquery));
                        //    foreach (var item in oppProds.Entities)
                        //    {
                        //        Console.WriteLine("Name: {0}", item.Attributes["opportunityproductname"]);
                        //        if (Convert.ToString(inventoryBySeasonRecord.FormattedValues["ats_product"]) == Convert.ToString(item.Attributes["opportunityproductname"])
                        //            && Guid.Parse(Convert.ToString(inventoryBySeasonRecord.Attributes["ats_inventorybyseasonid"])) == ((Microsoft.Xrm.Sdk.EntityReference)item.Attributes["ats_inventorybyseason"]).Id
                        //            && ((Microsoft.Xrm.Sdk.EntityReference)inventoryBySeasonRecord.Attributes["ats_product"]).Id == ((Microsoft.Xrm.Sdk.EntityReference)item.Attributes["productid"]).Id
                        //            && Convert.ToString(inventoryBySeasonRecord.Attributes["ats_division"]) == Convert.ToString(item.Attributes["ats_division"])
                        //            && Convert.ToString(inventoryBySeasonRecord.FormattedValues["ats_productfamily"]) == Convert.ToString(item.Attributes["ats_productfamily"])
                        //            && Convert.ToString(inventoryBySeasonRecord.FormattedValues["ats_productsubfamily"]) == Convert.ToString(item.Attributes["ats_productsubfamily"]))
                        //        {
                        //            isUpdate = true;
                        //            OpportunityProduct["opportunityproductid"] = item.Attributes["opportunityproductid"];
                        //            qty = Convert.ToInt32(item["quantity"]) + Convert.ToInt32(inventoryBySeasonRecord.Attributes["ats_qtytemp"]);
                        //        }
                        //    }

                        //    if (isUpdate)
                        //    {
                        //        OpportunityProduct["quantity"] = qty;
                        //        service.Update(OpportunityProduct);
                        //    }
                        //    else
                        //    {

                        //        if (inventoryBySeasonRecord.Attributes["ats_name"] != null)
                        //            OpportunityProduct["opportunityproductname"] = inventoryBySeasonRecord.Attributes["ats_name"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_qtytemp"] != null)
                        //            OpportunityProduct["quantity"] = inventoryBySeasonRecord.Attributes["ats_qtytemp"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_inventorybyseasonid"] != null)
                        //            OpportunityProduct["ats_inventorybyseason"] =
                        //                new EntityReference("ats_inventorybyseason", Guid.Parse(Convert.ToString(inventoryBySeasonRecord.Attributes["ats_inventorybyseasonid"])));

                        //        OpportunityProduct["opportunityid"] = new EntityReference("opportunity", Guid.Parse("f6a160fa-174d-417f-a43a-d6a8027fd717"));

                        //        if (inventoryBySeasonRecord.Attributes["ats_product"] != null)
                        //            OpportunityProduct["productid"] = inventoryBySeasonRecord.Attributes["ats_product"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_unitrate"] != null)
                        //            OpportunityProduct["priceperunit"] = inventoryBySeasonRecord.Attributes["ats_unitrate"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_name"] != null)
                        //            OpportunityProduct["uomid"] = new EntityReference("uom", Guid.Parse("f8175ff2-0f60-45b6-b882-15d1bd249ae4"));

                        //        if (inventoryBySeasonRecord.Attributes["ats_division"] != null)
                        //            OpportunityProduct["ats_division"] = inventoryBySeasonRecord.Attributes["ats_division"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_productfamily"] != null)
                        //            OpportunityProduct["ats_productfamily"] = inventoryBySeasonRecord.FormattedValues["ats_productfamily"];

                        //        if (inventoryBySeasonRecord.Attributes["ats_productsubfamily"] != null)
                        //            OpportunityProduct["ats_productsubfamily"] = inventoryBySeasonRecord.FormattedValues["ats_productsubfamily"];

                        //        service.Create(OpportunityProduct);
                        //    }
                        //    //Console.Write(response);
                        //}
                        #endregion
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Log("Error Occured : " + ex.Message, null);
            }
        }
    }
}
