using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal;
using MultiYearDeal.Workflows;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MultiYearDeal.Plugins
{
    public class CustomAPIAddProductAgreementCartAction : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService =
                (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            IPluginExecutionContext pluginContext =
                (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            IOrganizationServiceFactory serviceFactory =
                (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            IOrganizationService service =
                serviceFactory.CreateOrganizationService(pluginContext.UserId);

            try
            {
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Tracing initialized");
                TraceHelper.Trace(tracingService, "CustomAPIAddProductAgreementCartAction execution started.");

                string inputActionName = GetInputParameter(pluginContext, "inputActionName");
                Guid agreementId = GetGuidInputParameter(pluginContext, "agreementId");
                string packageLineItemIdAddProduct = GetInputParameter(pluginContext, "packageLineItemIdAddProduct");

                TraceHelper.Trace(
                    tracingService,
                    "Input Parameters => inputActionName: {0}, agreementId: {1}, packageLineItemIdAddProduct: {2}",
                    inputActionName,
                    agreementId,
                    packageLineItemIdAddProduct);

                if (string.Equals(inputActionName, "AddProduct", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateAddProductInputs(agreementId, tracingService);

                    ExecuteAddProduct(
                        service,
                        tracingService,
                        pluginContext,
                        agreementId,
                        packageLineItemIdAddProduct
                    );
                    return;
                }

                if (string.Equals(inputActionName, "AddProductBatching", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateAddProductBatchingInputs(pluginContext, tracingService);

                    ExecuteAddProductBatching(
                        service,
                        tracingService,
                        pluginContext
                    );
                    return;
                }

                if (string.Equals(inputActionName, "AddProductEscalateTotalDealAgreement", StringComparison.OrdinalIgnoreCase))
                {
                    AgreementOpportunityData agreementData = ValidateAddProductEscalateTotalDealAgreementInputs(
                        pluginContext,
                        tracingService
                    );

                    ExecuteAddProductEscalateTotalDealAgreement(
                        service,
                        tracingService,
                        pluginContext,
                        agreementData
                    );
                    return;
                }

                TraceHelper.Trace(tracingService, "No matching action found for inputActionName: {0}", inputActionName);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Exception in CustomAPIAddProductAgreementCartAction: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("An error occurred in CustomAPIAddProductAgreementCartAction.", ex);
            }
        }

        /// <summary>
        /// ExecuteAddProductEscalateTotalDealAgreement 
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <param name="pluginContext"></param>
        /// <param name="data"></param>
        private void ExecuteAddProductEscalateTotalDealAgreement(
        IOrganizationService service,
        ITracingService tracingService,
        IPluginExecutionContext pluginContext,
        AgreementOpportunityData data)
        {
            TraceHelper.Trace(tracingService, "Execution of AddProductEscalateTotalDealAgreement started.");

            string retrieveAgreementId = data != null ? data.AgreementId : null;

            TraceHelper.Trace(tracingService, "Proceeding for the total escalate revenue of the opportunities present in the agreement.");

            bool isAddProdEscalationAllYear = true;
            Guid agreementIdd = Guid.TryParse(retrieveAgreementId, out Guid parsedAgreementId)
                ? parsedAgreementId
                : Guid.Empty;

            TraceHelper.Trace(tracingService, "agreementIdd: {0}", agreementIdd);

            string esclateActionNamee = "AddProduct";
            string esclationType = string.Empty;
            decimal esclationValue = 0;

            TotalEsclateRevenue escalateRevenue = new TotalEsclateRevenue();
            escalateRevenue.calTotalEscRevenue(
                isAddProdEscalationAllYear,
                esclateActionNamee,
                agreementIdd,
                esclationType,
                esclationValue,
                service,
                tracingService);

            // if totalDealValueAgreement is inside AgreementCartAction
            AgreementCartAction agreementCartActionObj = new AgreementCartAction();
            agreementCartActionObj.totalDealValueAgreement(retrieveAgreementId, tracingService, service);

            pluginContext.OutputParameters["response"] = "Sucessfull";

            TraceHelper.Trace(tracingService, "Logic executed for the 'AddProductEscalateTotalDealAgreement'");
        }






        #region Validations

        private void ValidateAddProductInputs(Guid agreementId, ITracingService tracingService)
        {
            TraceHelper.Trace(tracingService, "Validating AddProduct inputs.");

            if (agreementId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("agreementId is required for AddProduct.");
            }
        }

        /// <summary>
        /// Validating the inputs of Escalate total deal Agreement
        /// </summary>
        /// <param name="pluginContext"></param>
        /// <param name="tracingService"></param>
        /// <returns></returns>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        private AgreementOpportunityData ValidateAddProductEscalateTotalDealAgreementInputs(
    IPluginExecutionContext pluginContext,
    ITracingService tracingService)
        {
            TraceHelper.Trace(tracingService, "Validating AddProductEscalateTotalDealAgreement inputs.");

            string inputJson = GetInputParameter(pluginContext, "AddProductOpportunityGuid");

            if (string.IsNullOrWhiteSpace(inputJson))
            {
                throw new InvalidPluginExecutionException("AddProductOpportunityGuid input is required for AddProductEscalateTotalDealAgreement.");
            }

            TraceHelper.Trace(tracingService, "inputJson: {0}", inputJson);

            AgreementOpportunityData data;
            try
            {
                data = JsonSerializer.Deserialize<AgreementOpportunityData>(inputJson);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Failed to deserialize AddProductOpportunityGuid JSON. Error: {0}", ex.Message);
                throw new InvalidPluginExecutionException("Invalid AddProductOpportunityGuid JSON.");
            }

            if (data == null)
            {
                throw new InvalidPluginExecutionException("AddProductOpportunityGuid JSON deserialized to null.");
            }

            if (string.IsNullOrWhiteSpace(data.AgreementId))
            {
                throw new InvalidPluginExecutionException("AgreementId is missing in AddProductOpportunityGuid JSON.");
            }

            Guid agreementId;
            if (!Guid.TryParse(data.AgreementId, out agreementId) || agreementId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("AgreementId in AddProductOpportunityGuid JSON is invalid.");
            }

            TraceHelper.Trace(tracingService, "Validation completed for AddProductEscalateTotalDealAgreement.");
            return data;
        }

        /// <summary>
        /// Validating the inputs of the Add Product batching
        /// </summary>
        /// <param name="pluginContext"></param>
        /// <param name="tracingService"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        private void ValidateAddProductBatchingInputs(
            IPluginExecutionContext pluginContext,
            ITracingService tracingService)
        {
            TraceHelper.Trace(tracingService, "Validating AddProductBatching inputs.");

            string inputJson = GetInputParameter(pluginContext, "AddProductOpportunityGuid");

            if (string.IsNullOrWhiteSpace(inputJson))
            {
                throw new InvalidPluginExecutionException("AddProductOpportunityGuid input is required for AddProductBatching.");
            }

            AgreementOpportunityData data;
            try
            {
                data = JsonSerializer.Deserialize<AgreementOpportunityData>(inputJson);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Failed to deserialize AddProductOpportunityGuid JSON. Error: {0}", ex.Message);
                throw new InvalidPluginExecutionException("Invalid AddProductOpportunityGuid JSON.");
            }

            if (data == null)
            {
                throw new InvalidPluginExecutionException("AddProductOpportunityGuid JSON deserialized to null.");
            }

            if (string.IsNullOrWhiteSpace(data.AgreementId))
            {
                throw new InvalidPluginExecutionException("AgreementId is missing in AddProductOpportunityGuid JSON.");
            }

            Guid agreementIdBatch;
            if (!Guid.TryParse(data.AgreementId, out agreementIdBatch) || agreementIdBatch == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("AgreementId in AddProductOpportunityGuid JSON is invalid.");
            }

            if (data.Opportunities == null || data.Opportunities.Count == 0)
            {
                throw new InvalidPluginExecutionException("No opportunity records found in AddProductOpportunityGuid JSON.");
            }

            Guid firstOpportunityId;
            if (!Guid.TryParse(data.Opportunities[0], out firstOpportunityId) || firstOpportunityId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException("First opportunity id in AddProductOpportunityGuid JSON is invalid.");
            }

            string inventoryDataJson = GetInputParameter(pluginContext, "inventoryData");
            if (string.IsNullOrWhiteSpace(inventoryDataJson))
            {
                throw new InvalidPluginExecutionException("inventoryData input is required for AddProductBatching.");
            }

            InventoryData inventoryData;
            try
            {
                inventoryData = JsonSerializer.Deserialize<InventoryData>(inventoryDataJson);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Failed to deserialize inventoryData JSON. Error: {0}", ex.Message);
                throw new InvalidPluginExecutionException("Invalid inventoryData JSON.");
            }

            if (inventoryData == null || string.IsNullOrWhiteSpace(inventoryData.ProductId))
            {
                throw new InvalidPluginExecutionException("inventoryData.ProductId is required for AddProductBatching.");
            }

            TraceHelper.Trace(tracingService, "AddProductBatching validation completed successfully.");
        }

        #endregion

        #region Add Product

        private void ExecuteAddProduct(
            IOrganizationService service,
            ITracingService tracingService,
            IPluginExecutionContext pluginContext,
            Guid agreementId,
            string packageLineItemIdAddProduct)
        {
            TraceHelper.Trace(tracingService, "Function ExecuteAddProduct started.");

            List<string> opportunityGuids = new List<string>();

            string fetchXml = $@"
            <fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='false'>
              <entity name='opportunity'>
                <attribute name='opportunityid' />
                <attribute name='ats_startseason' />
                <attribute name='name' />
                <order attribute='ats_startseason' descending='false' />
                <filter type='and'>
                  <condition attribute='ats_agreement' operator='eq' value='{agreementId}' />
                </filter>
              </entity>
            </fetch>";

            EntityCollection opportunityCollection = service.RetrieveMultiple(new FetchExpression(fetchXml));

            Logging.Log("done query expression", tracingService);

            EntityReference firstOppSeason = null;
            int count = 0;

            Agreement agreementObj = new Agreement();
            string uniqueGuid = agreementObj.UniqueGuidGeneration(service, tracingService);

            pluginContext.OutputParameters["AgreementOpportunityUniqueGuid"] = uniqueGuid;

            TraceHelper.Trace(tracingService, "Unique Guid generated: {0}", uniqueGuid);

            foreach (Entity opportunity in opportunityCollection.Entities)
            {
                Logging.Log("foreach inside", tracingService);

                opportunityGuids.Add(opportunity.Id.ToString());

                TraceHelper.Trace(tracingService, "Opportunity: {0}, is added in the list", opportunity.Id);

                if (count == 0)
                {
                    firstOppSeason = opportunity.GetAttributeValue<EntityReference>("ats_startseason");
                }

                count++;
            }

            TraceHelper.Trace(tracingService, "firstOppSeason: {0}",
                firstOppSeason != null ? firstOppSeason.Id.ToString() : "NULL");

            List<OppProdSeasonInfo> seasonOppProdList = new List<OppProdSeasonInfo>();
            bool isPackageLineId = false;

            if (!string.IsNullOrWhiteSpace(packageLineItemIdAddProduct))
            {
                Guid packageLineItemIdAddProductGuid = Guid.Empty;
                Guid.TryParse(packageLineItemIdAddProduct, out packageLineItemIdAddProductGuid);

                TraceHelper.Trace(tracingService, "Retrieved package Line item id: {0}", packageLineItemIdAddProduct);

                Entity oppProductAgreementOppProductObj = service.Retrieve(
                    "opportunityproduct",
                    packageLineItemIdAddProductGuid,
                    new ColumnSet("ats_agreementopportunityproduct", "productid"));

                string agreementOppProdId = oppProductAgreementOppProductObj.Contains("ats_agreementopportunityproduct")
                    ? oppProductAgreementOppProductObj.GetAttributeValue<string>("ats_agreementopportunityproduct")
                    : string.Empty;

                EntityReference productRef = oppProductAgreementOppProductObj.Contains("productid")
                    ? oppProductAgreementOppProductObj.GetAttributeValue<EntityReference>("productid")
                    : null;

                Guid productIdPackage = productRef != null ? productRef.Id : Guid.Empty;

                TraceHelper.Trace(tracingService, "productIdPackage: {0}", productIdPackage);
                TraceHelper.Trace(tracingService,
                    "For packageLineItemIdAddProductGuid: {0}, agreementOppProdId: {1}",
                    packageLineItemIdAddProductGuid, agreementOppProdId);

                if (!string.IsNullOrWhiteSpace(agreementOppProdId))
                {
                    string fetch = $@"
                    <fetch>
                      <entity name='opportunityproduct'>
                        <attribute name='opportunityproductname' />
                        <attribute name='productname' />
                        <attribute name='opportunityproductid' />

                        <filter>
                          <condition attribute='ats_agreementopportunityproduct' operator='eq' value='{agreementOppProdId}' />
                        </filter>

                        <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' link-type='inner' alias='IBS'>
                          <link-entity name='ats_season' from='ats_seasonid' to='ats_season' link-type='inner' alias='Season'>
                            <attribute name='ats_name' />
                            <order attribute='ats_name' />
                          </link-entity>
                        </link-entity>

                        <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Product'>
                          <filter>
                            <condition attribute='productid' operator='eq' value='{productIdPackage}' />
                          </filter>
                        </link-entity>

                        <link-entity name='opportunity' from='opportunityid' to='opportunityid' link-type='inner' alias='Opp'>
                          <link-entity name='ats_agreement' from='ats_agreementid' to='ats_agreement' link-type='inner' alias='Agreement'>
                            <filter>
                              <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                            </filter>
                          </link-entity>
                        </link-entity>

                      </entity>
                    </fetch>";

                    EntityCollection results = service.RetrieveMultiple(new FetchExpression(fetch));

                    foreach (Entity e in results.Entities)
                    {
                        Guid oppProductId = e.Id;

                        string seasonName = e.Attributes.Contains("Season.ats_name")
                            ? ((AliasedValue)e["Season.ats_name"]).Value?.ToString()
                            : null;

                        TraceHelper.Trace(tracingService, "OppProductId: {0}, SeasonName: {1}",
                            oppProductId, seasonName);

                        seasonOppProdList.Add(new OppProdSeasonInfo
                        {
                            OpportunityProductId = oppProductId,
                            SeasonName = seasonName
                        });
                    }

                    TraceHelper.Trace(tracingService, "Total items added to list: {0}", seasonOppProdList.Count);

                    isPackageLineId = true;
                }
                else
                {
                    TraceHelper.Trace(tracingService,
                        "agreementOppProdId is not present in any other Opp Prod. Value: {0}",
                        agreementOppProdId);
                }
            }

            if (opportunityGuids != null && opportunityGuids.Count > 0 && agreementId != Guid.Empty)
            {
                AgreementOpportunityData data = new AgreementOpportunityData
                {
                    AgreementId = agreementId.ToString(),
                    Opportunities = opportunityGuids,
                    FirstOppSeasonId = firstOppSeason != null ? firstOppSeason.Id.ToString() : string.Empty,
                    UniqueGuid = uniqueGuid,
                    SeasonOppProducts = seasonOppProdList,
                    isPackageLineId = isPackageLineId
                };

                string serializedData = JsonSerializer.Serialize(data);

                pluginContext.OutputParameters["AddProductOpportunityGuid"] = serializedData;

                TraceHelper.Trace(tracingService,
                    "AddProductOpportunityGuid is set as JSON string: {0}", serializedData);
            }
            else
            {
                TraceHelper.Trace(tracingService, "No opportunities found or agreementId is null/empty.");
            }

            pluginContext.OutputParameters["AgreementCartActionbatchingActionName"] = "AddProductBatching";
            TraceHelper.Trace(tracingService, "AgreementCartActionbatchingActionName is set to AddProductBatching");

            pluginContext.OutputParameters["isAddProductBatching"] = true;
            TraceHelper.Trace(tracingService, "Add Product Logic is implemented");
        }

        #endregion

        #region Add Product Batching

        private void ExecuteAddProductBatching(
            IOrganizationService service,
            ITracingService tracingService,
            IPluginExecutionContext pluginContext)
        {
            TraceHelper.Trace(tracingService, "Function ExecuteAddProductBatching started.");

            AgreementCartAction agreementCartActionObj = new AgreementCartAction();
            GetOpportunityProducts getOpportunityProducts = new GetOpportunityProducts();
            string functionName = "ExecuteAddProductBatching";

            string inputJson = GetInputParameter(pluginContext, "AddProductOpportunityGuid");
            string seasonIds = GetInputParameter(pluginContext, "seasonIds");
            string inventoryDataJson = GetInputParameter(pluginContext, "inventoryData");
            string rawComponentJson = GetInputParameter(pluginContext, "PackageComponents");

            AgreementOpportunityData data = JsonSerializer.Deserialize<AgreementOpportunityData>(inputJson);
            InventoryData inventoryData = string.IsNullOrWhiteSpace(inventoryDataJson)
                ? null
                : JsonSerializer.Deserialize<InventoryData>(inventoryDataJson);

            TraceHelper.Trace(tracingService, "Inside AddProductBatching");

            if (string.IsNullOrWhiteSpace(inputJson))
                return;

            if (data == null || data.Opportunities == null || data.Opportunities.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(data.AgreementId))
                throw new InvalidPluginExecutionException("AgreementId missing in input JSON.");

            Guid agreementIdBatch = new Guid(data.AgreementId);

            Entity agreementBatch = service.Retrieve(
                "ats_agreement",
                agreementIdBatch,
                new ColumnSet("ats_startseason", "ats_bpfstatus")
            );

            EntityReference agreementStartSeasonRefBatch =
                agreementBatch.GetAttributeValue<EntityReference>("ats_startseason");

            if (agreementStartSeasonRefBatch == null)
                throw new InvalidPluginExecutionException("Agreement Start Season not found.");

            Guid agreementStartSeasonIdBatch = agreementStartSeasonRefBatch.Id;

            int bpfStatusValueBatch =
                agreementBatch.GetAttributeValue<OptionSetValue>("ats_bpfstatus")?.Value ?? 0;

            Guid opportunityIdBatch = new Guid(data.Opportunities[0]);

            Entity opportunityBatch = service.Retrieve(
                "opportunity",
                opportunityIdBatch,
                new ColumnSet("ats_startseason")
            );

            EntityReference oppSeasonRefBatch =
                opportunityBatch.GetAttributeValue<EntityReference>("ats_startseason");

            if (oppSeasonRefBatch == null)
                throw new InvalidPluginExecutionException("Opportunity Start Season missing.");

            Guid firstOppSeasonIdGuidBatch = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(data.FirstOppSeasonId))
                Guid.TryParse(data.FirstOppSeasonId, out firstOppSeasonIdGuidBatch);

            string uniqueGuidFromDataBatch = string.IsNullOrWhiteSpace(data.UniqueGuid)
                ? null
                : data.UniqueGuid.Trim();

            var seasonSetBatch = new HashSet<Guid>();
            if (!string.IsNullOrWhiteSpace(seasonIds))
            {
                foreach (var s in seasonIds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Guid g;
                    if (Guid.TryParse(s.Trim(), out g))
                        seasonSetBatch.Add(g);
                }
            }

            Guid uomIdBatch = agreementCartActionObj.GetUnitOfMeasure(service, "Unit_of_Measure", tracingService);
            EntityReference uomRefBatch = (uomIdBatch != Guid.Empty) ? new EntityReference("uom", uomIdBatch) : null;

            var ibsCacheBatch = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase);
            TraceHelper.Trace(tracingService, "IBS cached");

            var rateCacheBatch = new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);
            TraceHelper.Trace(tracingService, "Rate cached");

            bool isPackageProductBatch = false;
            Guid primaryPackageTemplateIdBatch = Guid.Empty;

            if (inventoryData == null || string.IsNullOrWhiteSpace(inventoryData.ProductId))
                throw new InvalidPluginExecutionException("inventoryData.ProductId missing.");

            Guid primaryProductIdBatch = new Guid(inventoryData.ProductId);

            Guid packageRateIdBatch = AgreementCartAction.GetPackageRateIdForProductSeason(
                service,
                agreementStartSeasonIdBatch,
                primaryProductIdBatch
            );

            bool isPackageTemplateFromAgreement = false;
            Guid packageTemplateId = Guid.Empty;

            if (packageRateIdBatch != Guid.Empty)
            {
                TraceHelper.Trace(tracingService, "packageRateIdBatch: {0}", packageRateIdBatch);

                primaryPackageTemplateIdBatch = AgreementCartAction.GetPrimaryPackageTemplateId(
                    service,
                    packageRateIdBatch,
                    agreementStartSeasonIdBatch
                );

                TraceHelper.Trace(tracingService, "primaryPackageTemplateIdBatch: {0}", primaryPackageTemplateIdBatch);

                if (primaryPackageTemplateIdBatch != Guid.Empty)
                    isPackageProductBatch = true;

                if (primaryPackageTemplateIdBatch == Guid.Empty)
                {
                    TraceHelper.Trace(tracingService, "Product belongs to package, but package template is missing, Proceeding for Package template creation");

                    Entity packageTemplate = new Entity("ats_packagetemplate");
                    packageTemplate["ats_packagerateid"] = new EntityReference("ats_rate", packageRateIdBatch);

                    try
                    {
                        tracingService.Trace($"functionName: {functionName}");
                        tracingService.Trace("Before Create");
                        packageTemplateId = service.Create(packageTemplate);
                        tracingService.Trace("After Create");
                        tracingService.Trace("Component added sucessfully");
                        isPackageTemplateFromAgreement = true;
                    }
                    catch (Exception ex)
                    {
                        tracingService.Trace("Before Exception " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
                    }

                    TraceHelper.Trace(tracingService, "Package template record created :{0}", packageTemplateId);
                }
            }

            int primaryRateTypeBatch =
                (inventoryData.RateType == "Season") ? 114300000 : 114300001;

            EntityReference primaryIbsRefBatch = agreementCartActionObj.EnsureInventoryForSeasonOptimized(
                oppSeasonRefBatch.Id,
                primaryProductIdBatch,
                firstOppSeasonIdGuidBatch,
                service,
                tracingService,
                ibsCacheBatch,
                null
            );

            Entity primaryRateBatch = AgreementCartAction.GetRateCached(
                service,
                rateCacheBatch,
                primaryIbsRefBatch.Id,
                primaryRateTypeBatch
            );

            if (primaryRateBatch == null)
            {
                throw new InvalidPluginExecutionException(
                    "Rate not found for IBS (primary). IBS=" + primaryIbsRefBatch.Id + ", RateType=" + primaryRateTypeBatch);
            }

            Money primaryPriceBatch = primaryRateBatch.GetAttributeValue<Money>("ats_price") ?? new Money(0m);

            int primaryQtyUnitsBatch = seasonSetBatch.Contains(oppSeasonRefBatch.Id) ? inventoryData.QtyUnits : 0;

            Entity mainOppProd = new Entity("opportunityproduct");
            mainOppProd["opportunityid"] = new EntityReference("opportunity", opportunityIdBatch);
            mainOppProd["productid"] = new EntityReference("product", primaryProductIdBatch);
            mainOppProd["ats_inventorybyseason"] = primaryIbsRefBatch;
            mainOppProd["ats_rate"] = primaryRateBatch.ToEntityReference();

            if (!string.IsNullOrWhiteSpace(uniqueGuidFromDataBatch))
                mainOppProd["ats_agreementopportunityproduct"] = uniqueGuidFromDataBatch;

            if (uomRefBatch != null)
                mainOppProd["uomid"] = uomRefBatch;

            TraceHelper.Trace(tracingService,
                "primaryPriceBatch:{0}, primaryQtyUnitsBatch: {1}, inventoryData.QtyEvents: {2}, inventoryData.HardCost {3}",
                primaryPriceBatch, primaryQtyUnitsBatch, inventoryData.QtyEvents, inventoryData.HardCost);

            mainOppProd["priceperunit"] = primaryPriceBatch;
            mainOppProd["ats_sellingrate"] = primaryPriceBatch;
            mainOppProd["ats_quantity"] = primaryQtyUnitsBatch;
            mainOppProd["ats_quantityofevents"] = inventoryData.QtyEvents;
            mainOppProd["ats_hardcost"] = new Money(inventoryData.HardCost);

            if (isPackageProductBatch && primaryPackageTemplateIdBatch != Guid.Empty)
            {
                mainOppProd["ats_packagetemplate"] = new EntityReference("ats_packagetemplate", primaryPackageTemplateIdBatch);
            }
            else if (isPackageTemplateFromAgreement && !data.isPackageLineId)
            {
                mainOppProd["ats_packagetemplate"] = new EntityReference("ats_packagetemplate", packageTemplateId);
                TraceHelper.Trace(tracingService, "packageTemplateId from isPackageTemplateFromAgreement: {0}", packageTemplateId);
            }

            Guid pkgLineId = Guid.Empty;
            TraceHelper.Trace(tracingService, "data.SeasonOppProducts.Count: {0}", data.SeasonOppProducts != null ? data.SeasonOppProducts.Count : 0);

            if (data.SeasonOppProducts != null && data.SeasonOppProducts.Count > 0)
            {
                pkgLineId = data.SeasonOppProducts[0].OpportunityProductId;

                if (pkgLineId != Guid.Empty &&
                    AgreementCartAction.EntityExists(service, "opportunityproduct", pkgLineId))
                {
                    mainOppProd["ats_packagelineitem"] = new EntityReference("opportunityproduct", pkgLineId);
                }

                TraceHelper.Trace(tracingService, "Package line item reference set to: {0}", pkgLineId);
            }

            TraceHelper.Trace(tracingService, "Creating MAIN OLI...");
            Guid createdMainOppProdId = service.Create(mainOppProd);
            TraceHelper.Trace(tracingService, "Created MAIN OLI: {0}", createdMainOppProdId);

            if (data.SeasonOppProducts != null && data.SeasonOppProducts.Count > 0)
                data.SeasonOppProducts.RemoveAt(0);

            TraceHelper.Trace(tracingService, "Processing package components...");

            string rawJson = string.IsNullOrWhiteSpace(rawComponentJson) ? "[]" : rawComponentJson;
            List<InventoryData> componentsBatch =
                JsonSerializer.Deserialize<List<InventoryData>>(rawJson) ?? new List<InventoryData>();

            TraceHelper.Trace(tracingService, "[Components] Deserialized {0} package components.", componentsBatch.Count);

            var deferredRateCreatesBatch = new List<Entity>();
            var qtyAggBatch = new Dictionary<string, (int qtyUnits, int qtyEvents)>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < componentsBatch.Count; i++)
            {
                var comp = componentsBatch[i];
                if (comp == null) continue;

                comp.ProductId = comp.ProductId ?? string.Empty;
                comp.ProductName = comp.ProductName ?? string.Empty;

                Guid compProductId;
                if (!Guid.TryParse(comp.ProductId, out compProductId))
                    continue;

                int compRateType =
                    (comp.RateType == "Season") ? 114300000 :
                    (comp.RateType == "Individual") ? 114300001 : 0;

                if (compRateType == 0)
                {
                    throw new InvalidPluginExecutionException(
                        "Invalid RateType '" + comp.RateType + "' for product " + comp.ProductName + " (" + comp.ProductId + ").");
                }

                EntityReference compIbsRef = agreementCartActionObj.EnsureInventoryForSeasonOptimized(
                    oppSeasonRefBatch.Id,
                    compProductId,
                    firstOppSeasonIdGuidBatch,
                    service,
                    tracingService,
                    ibsCacheBatch,
                    deferredRateCreatesBatch
                );

                Entity compRate = AgreementCartAction.GetRateCached(
                    service,
                    rateCacheBatch,
                    compIbsRef.Id,
                    compRateType
                );

                if (compRate == null)
                {
                    throw new InvalidPluginExecutionException(
                        "No rate found. IBS=" + compIbsRef.Id + ", RateType=" + compRateType + ", Product=" + comp.ProductName);
                }

                Money compPrice = compRate.GetAttributeValue<Money>("ats_price") ?? new Money(0m);

                int compQtyUnits = seasonSetBatch.Contains(oppSeasonRefBatch.Id) ? comp.QtyUnits : 0;

                Entity compOppProd = new Entity("opportunityproduct");
                compOppProd["opportunityid"] = new EntityReference("opportunity", opportunityIdBatch);
                compOppProd["productid"] = new EntityReference("product", compProductId);
                compOppProd["ats_inventorybyseason"] = compIbsRef;
                compOppProd["ats_rate"] = compRate.ToEntityReference();

                if (!string.IsNullOrWhiteSpace(uniqueGuidFromDataBatch))
                    compOppProd["ats_agreementopportunityproduct"] = uniqueGuidFromDataBatch;

                if (uomRefBatch != null)
                    compOppProd["uomid"] = uomRefBatch;

                compOppProd["priceperunit"] = compPrice;
                compOppProd["ats_sellingrate"] = compPrice;
                compOppProd["ats_quantity"] = compQtyUnits;
                compOppProd["ats_quantityofevents"] = comp.QtyEvents;
                compOppProd["ats_hardcost"] = new Money(comp.HardCost);

                if (createdMainOppProdId != Guid.Empty)
                    compOppProd["ats_packagelineitem"] = new EntityReference("opportunityproduct", createdMainOppProdId);

                compOppProd["ats_manualpriceoverride"] = false;

                if (primaryPackageTemplateIdBatch != Guid.Empty)
                    compOppProd["ats_packagetemplate"] = new EntityReference("ats_packagetemplate", primaryPackageTemplateIdBatch);

                Guid compOppProdGuid = service.Create(compOppProd);
                TraceHelper.Trace(tracingService, "comp Opp prod is created: {0}", compOppProdGuid);

                string aggKey =
                    oppSeasonRefBatch.Id.ToString("N") + "|" +
                    compProductId.ToString("N") + "|" +
                    compRateType.ToString();

                (int qtyUnits, int qtyEvents) agg;
                if (!qtyAggBatch.TryGetValue(aggKey, out agg))
                    agg = (0, 0);

                qtyAggBatch[aggKey] = (agg.qtyUnits + comp.QtyUnits, agg.qtyEvents + comp.QtyEvents);
            }

            if (deferredRateCreatesBatch.Count > 0)
                AgreementCartAction.CreateBulkInChunks(service, deferredRateCreatesBatch, 200);

            TraceHelper.Trace(tracingService, "deferredRateCreatesBatch");

            var ibsPatchesBatch = new List<Entity>();

            foreach (var kvp in qtyAggBatch)
            {
                var parts = kvp.Key.Split('|');

                Guid seasonIdAgg = Guid.ParseExact(parts[0], "N");
                Guid productIdAgg = Guid.ParseExact(parts[1], "N");
                int rateTypeAgg = int.Parse(parts[2]);

                string ibsKey = seasonIdAgg.ToString("N") + "|" + productIdAgg.ToString("N");

                EntityReference cachedIbsRef;
                if (!ibsCacheBatch.TryGetValue(ibsKey, out cachedIbsRef))
                    continue;

                var totals = kvp.Value;

                Entity patch = AgreementCartAction.BuildIbsQtyUpdatePatch(
                    cachedIbsRef.Id,
                    totals.qtyUnits,
                    totals.qtyEvents,
                    rateTypeAgg,
                    bpfStatusValueBatch,
                    service,
                    tracingService
                );

                if (patch != null)
                    ibsPatchesBatch.Add(patch);
            }

            TraceHelper.Trace(tracingService, "ibsPatchesBatch.Count: {0}", ibsPatchesBatch.Count);

            if (ibsPatchesBatch.Count > 0)
            {
                try
                {
                    AgreementCartAction.UpdateBulkInChunks(service, ibsPatchesBatch, 200);
                    TraceHelper.Trace(tracingService, "UpdateMultiple for IBS patches succeeded.");
                }
                catch (Exception ex)
                {
                    tracingService.Trace("UpdateMultiple failed, falling back to per-record Update. Error: {0}", ex.Message);
                    for (int i = 0; i < ibsPatchesBatch.Count; i++)
                        service.Update(ibsPatchesBatch[i]);
                }
            }

            agreementCartActionObj.UpdateQtyPitchedSoldForAddProduct(
                inventoryData.QtyUnits,
                inventoryData.QtyEvents,
                primaryRateTypeBatch,
                bpfStatusValueBatch,
                oppSeasonRefBatch.Id,
                primaryProductIdBatch,
                service,
                tracingService
            );

            TraceHelper.Trace(tracingService, "Updated pitched/sold for primary IBS.");

            OrganizationRequest actionRequest = new OrganizationRequest("ats_CalculateOpportunityLines");
            actionRequest["OppurtunityEntityReference"] = new EntityReference("opportunity", opportunityIdBatch);
            actionRequest["Action"] = "ReCalcOppLinesAgreement";
            service.Execute(actionRequest);

            TraceHelper.Trace(tracingService, "Recalculated opportunity lines.");

            try
            {
                service.Execute(new CalculateRollupFieldRequest
                {
                    Target = new EntityReference("opportunity", opportunityIdBatch),
                    FieldName = "ats_totalratecard"
                });
            }
            catch
            {
            }

            if (pkgLineId != Guid.Empty)
            {
                TraceHelper.Trace(tracingService, "Recalculating total component OLIS rollup for parent OLI(pkgLineId): {0}", pkgLineId);
                AgreementCartAction.RecalculatTotalComponentOLIeRollup(service, tracingService, pkgLineId);
            }
            else if (createdMainOppProdId != Guid.Empty)
            {
                TraceHelper.Trace(tracingService, "Recalculating total component OLIS rollup for parent OLI(createdMainOppProdId): {0}", createdMainOppProdId);
                AgreementCartAction.RecalculatTotalComponentOLIeRollup(service, tracingService, createdMainOppProdId);
            }

            TraceHelper.Trace(tracingService, "Updated total rate card rollup field.");

            data.Opportunities.RemoveAt(0);

            TraceHelper.Trace(tracingService, "Remaining opportunities in batch: {0}", data.Opportunities.Count);

            string updatedJson = JsonSerializer.Serialize(data);

            pluginContext.OutputParameters["AddProductOpportunityGuid"] = updatedJson;
            pluginContext.OutputParameters["NewAddProductOpportunityGuid"] = updatedJson;

            if (data.Opportunities.Count > 0)
            {
                pluginContext.OutputParameters["isAddProductBatching"] = true;
                pluginContext.OutputParameters["AgreementCartActionbatchingActionName"] = "AddProductBatching";
            }
            else
            {
                pluginContext.OutputParameters["isAddProductBatching"] = false;
                pluginContext.OutputParameters["AgreementCartActionbatchingActionName"] = "AddProductEscalateTotalDealAgreement";
                pluginContext.OutputParameters["response"] = "successfull";
            }

            tracingService.Trace("Exiting from the Add Product Batching Logic implementation");
        }

        #endregion

        #region Helpers

        private string GetInputParameter(IPluginExecutionContext pluginContext, string parameterName)
        {
            if (pluginContext.InputParameters.Contains(parameterName) &&
                pluginContext.InputParameters[parameterName] != null)
            {
                return pluginContext.InputParameters[parameterName].ToString();
            }

            return string.Empty;
        }

        private Guid GetGuidInputParameter(IPluginExecutionContext pluginContext, string parameterName)
        {
            if (pluginContext.InputParameters.Contains(parameterName) &&
                pluginContext.InputParameters[parameterName] != null)
            {
                Guid id;
                if (Guid.TryParse(pluginContext.InputParameters[parameterName].ToString(), out id))
                    return id;
            }

            return Guid.Empty;
        }

        #endregion
    }

    public class AgreementOpportunityData
    {
        public string AgreementId { get; set; }
        public List<string> Opportunities { get; set; }
        public string FirstOppSeasonId { get; set; }
        public string UniqueGuid { get; set; }
        public List<OppProdSeasonInfo> SeasonOppProducts { get; set; }
        public bool isPackageLineId { get; set; }
    }

    public class OppProdSeasonInfo
    {
        public Guid OpportunityProductId { get; set; }
        public string SeasonName { get; set; }
    }

    public class InventoryData
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public string RateType { get; set; }
        public int QtyUnits { get; set; }
        public int QtyEvents { get; set; }
        public decimal HardCost { get; set; }
    }

    public static class Logging
    {
        public static void Log(string message, ITracingService tracingService)
        {
            tracingService?.Trace(message);
        }
    }

    public class Agreement
    {
        public string UniqueGuidGeneration(IOrganizationService service, ITracingService tracingService)
        {
            return Guid.NewGuid().ToString();
        }
    }
}