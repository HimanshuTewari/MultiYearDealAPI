 
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Remoting.Contexts;
using System.Text.Json; // or use Newtonsoft.Json

namespace AgreementProductBundling
{
    public class GetAgreementProductBundlingData : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            #region function Level variables
            string functionName = "Execute";
            string productId = string.Empty;
            Guid productGuid = Guid.Empty;
            string seasonId = string.Empty;
            Guid seasonGuid = Guid.Empty;
            string packageRateId = string.Empty;
            Guid packageRateGuid = Guid.Empty;
            #endregion

            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);

            tracingService.Trace($"functionName: {functionName}");

            string actionName = context.InputParameters.Contains("ActionName") && context.InputParameters["ActionName"] is string
                ? (string)context.InputParameters["ActionName"]
                : string.Empty;
            tracingService.Trace($"actionName: {actionName}");
            if (actionName == string.Empty)
            {
                throw new InvalidPluginExecutionException("Invalid or missing ActionName.");
            }

            //retrieving season and product data
            #region Getting ordered seasons and 'Individual' rate per season by product 
            if (actionName == "GetSeasonData")
            {
                //retrieving the product id from input parameter
                productId = context.InputParameters.Contains("ProductId") && context.InputParameters["ProductId"] is string
                    ? (string)context.InputParameters["ProductId"]
                    : string.Empty;

                tracingService.Trace($"productId: {productId}");
                if (!Guid.TryParse(productId, out productGuid))
                {
                    throw new InvalidPluginExecutionException("Invalid or missing ProductId.");
                }
                GetSeasonJsonData(service, tracingService, productId, context);
                tracingService.Trace($"SeasonDataJson sent sucessfully");
            }
            #endregion

            //retrieving package component data
            #region Getting ordered seasons and 'Individual' rate per season by product 
            if (actionName == "GetComponentData")
            {
                //retrieving the package rate id from input parameter
                packageRateId = context.InputParameters.Contains("PackageRateId") && context.InputParameters["PackageRateId"] is string
                    ? (string)context.InputParameters["PackageRateId"]
                    : string.Empty;

                tracingService.Trace($"packageRateId: {packageRateId}");
                if (!Guid.TryParse(packageRateId, out packageRateGuid))
                {
                    throw new InvalidPluginExecutionException("Invalid or missing Package RateId.");
                }
                GetComponentJsonData(service, tracingService, packageRateId, context);
                tracingService.Trace($"ComponentDataJson sent sucessfully");
            }
            #endregion

            //retrieving inventory data
            #region Getting inventory with IBS/rate for selected season 
            if (actionName == "GetInventoryData")
            {
                //retrieving the season id from input parameter
                seasonId = context.InputParameters.Contains("SeasonId") && context.InputParameters["SeasonId"] is string
                    ? (string)context.InputParameters["SeasonId"]
                    : string.Empty;

                tracingService.Trace($"seasonId: {seasonId}");
                if (!Guid.TryParse(seasonId, out seasonGuid))
                {
                    throw new InvalidPluginExecutionException("Invalid or missing selected season id.");
                }
                GetInventoryJsonData(service, tracingService, seasonId, context);
                tracingService.Trace($"InventoryDataJson sent sucessfully");
            }
            #endregion

            //adding a new component to a package
            #region 
            if (actionName == "AddComponent")
            {
                // retrieving the component data from input parameters
                var packageTemplateRecord = new PackageTemplateRecord
                {
                    PackageRateId = context.InputParameters.Contains("PackageRateId") && context.InputParameters["PackageRateId"] is string
                                        ? (string)context.InputParameters["PackageRateId"]
                                        : string.Empty,

                    QtyUnits = context.InputParameters.Contains("QtyUnits")
                                        ? Convert.ToInt32(context.InputParameters["QtyUnits"])
                                        : 0,

                    QtyEvents = context.InputParameters.Contains("QtyEvents")
                                        ? Convert.ToInt32(context.InputParameters["QtyEvents"])
                                        : 0,

                    ComponentRateId = context.InputParameters.Contains("ComponentRateId")
                                        ? (string)context.InputParameters["ComponentRateId"]
                                        : string.Empty,
                };

                tracingService.Trace($"PackageRateId: {packageTemplateRecord.PackageRateId}");
                tracingService.Trace($"ComponentRateId: {packageTemplateRecord.ComponentRateId}");
                tracingService.Trace($"QtyUnits: {packageTemplateRecord.QtyUnits}");
                tracingService.Trace($"QtyEvents: {packageTemplateRecord.QtyEvents}");
                AddComponent(service, tracingService, packageTemplateRecord, context);
            }
            #endregion

            //update an existing component in a package
            #region 
            if (actionName == "UpdateComponent")
            {
                // retrieving the component data from input parameters
                var packageTemplateRecord = new PackageTemplateRecord
                {
                    PackageRateId = context.InputParameters.Contains("PackageRateId") && context.InputParameters["PackageRateId"] is string
                                        ? (string)context.InputParameters["PackageRateId"]
                                        : string.Empty,

                    QtyUnits = context.InputParameters.Contains("QtyUnits")
                                        ? Convert.ToInt32(context.InputParameters["QtyUnits"])
                                        : 0,

                    QtyEvents = context.InputParameters.Contains("QtyEvents")
                                        ? Convert.ToInt32(context.InputParameters["QtyEvents"])
                                        : 0,

                    ComponentRateId = context.InputParameters.Contains("ComponentRateId")
                                        ? (string)context.InputParameters["ComponentRateId"]
                                        : string.Empty,
                    PackageTemplateId = context.InputParameters.Contains("PackageTemplateId")
                                        ? (string)context.InputParameters["PackageTemplateId"]
                                        : string.Empty
                };
                tracingService.Trace($"PackageRateId: {packageTemplateRecord.PackageRateId}");
                tracingService.Trace($"ComponentRateId: {packageTemplateRecord.ComponentRateId}");
                tracingService.Trace($"QtyUnits: {packageTemplateRecord.QtyUnits}");
                tracingService.Trace($"QtyEvents: {packageTemplateRecord.QtyEvents}");
                UpdateComponent(service, tracingService, packageTemplateRecord, context);
            }
            #endregion

            //deleting a component from a package
            #region 
            if (actionName == "DeleteComponent")
            {
                string packageTemplateId = context.InputParameters.Contains("PackageTemplateId") && context.InputParameters["PackageTemplateId"] is string
                                                ? (string)context.InputParameters["PackageTemplateId"]
                                                : string.Empty;
                tracingService.Trace($"PackageTemplateId: {packageTemplateId}");
                DeleteComponent(service, tracingService, packageTemplateId, context);
            }
            #endregion

            //validate seasons and package templates for bulk copy
            #region 
            if (actionName == "CheckConflicts")
            {
                productId = context.InputParameters.Contains("ProductId") && context.InputParameters["ProductId"] is string
                    ? (string)context.InputParameters["ProductId"]
                    : string.Empty;
                string seasonIdsParam = context.InputParameters.Contains("SeasonIds") && context.InputParameters["SeasonIds"] is string
                                            ? (string)context.InputParameters["SeasonIds"]
                                            : string.Empty;
                string[] seasonIds = seasonIdsParam.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string packageTemplateIdsParam = context.InputParameters.Contains("PackageTemplateIds") && context.InputParameters["PackageTemplateIds"] is string
                                                    ? (string)context.InputParameters["PackageTemplateIds"]
                                                    : string.Empty;
                string[] packageTemplateIds = packageTemplateIdsParam.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                tracingService.Trace($"SeasonIds: {seasonIds}");
                tracingService.Trace($"PackageTemplateIds: {packageTemplateIds}");
                ValidateConflicts(service, tracingService, productId, seasonIds, packageTemplateIds, context);
            }
            #endregion
            
            //copying selected components for this package from one season to one or more other seasons
            #region 
            if (actionName == "CopyComponents")
            {
                productId = context.InputParameters.Contains("ProductId") && context.InputParameters["ProductId"] is string
                    ? (string)context.InputParameters["ProductId"]
                    : string.Empty;
                string seasonIdsParam = context.InputParameters.Contains("SeasonIds") && context.InputParameters["SeasonIds"] is string
                                            ? (string)context.InputParameters["SeasonIds"]
                                            : string.Empty;
                string[] seasonIds = seasonIdsParam.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string packageTemplateIdsParam = context.InputParameters.Contains("PackageTemplateIds") && context.InputParameters["PackageTemplateIds"] is string
                                                    ? (string)context.InputParameters["PackageTemplateIds"]
                                                    : string.Empty;
                string[] packageTemplateIds = packageTemplateIdsParam.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                tracingService.Trace($"SeasonIds: {seasonIds}");
                tracingService.Trace($"PackageTemplateIds: {packageTemplateIds}");
                CopyComponents(service, tracingService, productId, seasonIds, packageTemplateIds, context);
            }
            #endregion
        }

        /// <summary>
        /// retrieving the product and season data sending into the output parameter
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <param name="productId"></param>
        /// <param name="context"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public static void GetSeasonJsonData(
            IOrganizationService service,
            ITracingService tracingService,
            string productId,
            IPluginExecutionContext context)
        {
            #region function level variables 
            string functionName = "GetSeasonJsonData";
            List<SeasonData> seasonList = new List<SeasonData>();

            #endregion

            try
            {
                tracingService.Trace($"functionName: {functionName}");

                string fetchXml = @"
                    <fetch>
                        <entity name='ats_season'>
                        <attribute name='ats_name' />
                        <attribute name='ats_startdate' />
                        <filter>
                            <condition attribute='statuscode' operator='eq' value='1' />
                        </filter>
                        <order attribute='ats_startdate' />
                        <order attribute='ats_name' />
                        </entity>
                    </fetch>";

                EntityCollection seasonData = service.RetrieveMultiple(new FetchExpression(fetchXml));

                foreach (var entity in seasonData.Entities)
                {
                    tracingService.Trace($"season Id: {entity.Id}");

                    #region
                    // Check package parent for existance of IBS and 'Individual' rate per season

                    string packageParentFetchXml = $@"
                                                   <fetch>
                                                  <entity name='product'>
                                                    <filter>
                                                      <condition attribute='ats_ispackage' operator='eq' value='1' />
                                                      <condition attribute='productid' operator='eq' value='{productId}' />
                                                    </filter>

                                                    <link-entity name='ats_inventorybyseason' from='ats_product' to='productid' link-type='inner' alias='IBS'>
                                                      <filter>
                                                        <condition attribute='ats_season' operator='eq' value='{entity.Id}' />
                                                      </filter>

                                                      <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='inner' alias='Rate'>
                                                        <attribute name='ats_rateid' />
                                                        <attribute name='ats_ratetype' />
                                                        <!-- Removed ratetype filter: now it only checks that a Rate record exists -->
                                                      </link-entity>

                                                    </link-entity>
                                                  </entity>
                                                </fetch>";

                    EntityCollection results = service.RetrieveMultiple(new FetchExpression(packageParentFetchXml));
                    tracingService.Trace($"Fetched {results.Entities.Count} rate records.");

                    bool isIBSAndRatePresent = results.Entities.Count > 0;
                    tracingService.Trace($"isIBSAndRatePresent: {isIBSAndRatePresent}");

                    #endregion

                    string startDate = string.Empty;
                    if (entity.Contains("ats_startdate") && entity["ats_startdate"] is DateTime)
                    {
                        DateTime startDateValue = (DateTime)entity["ats_startdate"];
                        startDate = startDateValue.ToString("yyyy-MM-dd"); // Or any format you prefer
                    }

                    string rateId = string.Empty;
                    if (isIBSAndRatePresent)
                    {
                        rateId = results.Entities[0].GetAttributeValue<AliasedValue>("Rate.ats_rateid")?.Value.ToString() ?? string.Empty;
                    }

                    seasonList.Add(new SeasonData
                    {
                        SeasonId = entity.Id.ToString(),
                        Name = entity.GetAttributeValue<string>("ats_name") ?? string.Empty,
                        IsIBSAndRatePresent = isIBSAndRatePresent,
                        StartSeason = startDate,
                        RateId = rateId
                    });

                }
                //Sending the seasonjsondata output
                context.OutputParameters["SeasonDataJson"] = JsonSerializer.Serialize(seasonList) == null ? "" : JsonSerializer.Serialize(seasonList);
                tracingService.Trace($"Season data json sent sucessfully");
            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// retrieving the product and season data sending into the output parameter
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <param name="productId"></param>
        /// <param name="context"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public static void GetComponentJsonData(
            IOrganizationService service,
            ITracingService tracingService,
            string packageRateId,
            IPluginExecutionContext context)
        {
            #region function level variables 
            string functionName = "GetComponentJsonData";
            List<ComponentData> componentList = new List<ComponentData>();
            #endregion

            try
            {
                tracingService.Trace($"functionName: {functionName}");
                #region retrieving the components for this package rate Id from the package template table.
                string componentFetchXml = $@"
                    <fetch>
                        <entity name='ats_packagetemplate'>
                            <filter> 
                                <condition attribute='ats_packagerateid' operator='eq' value='{packageRateId}' />
                            </filter>
                            <attribute name='ats_packagerateid' />
                            <attribute name='ats_componentrateid' />
                            <attribute name='ats_quantity' />
                            <attribute name='ats_quantityofevents' />

                            <link-entity name='ats_rate' from='ats_rateid' to='ats_componentrateid' alias='rate'>
                                <attribute name='ats_ratetype' />
                                <attribute name='ats_price' />
                                
                                <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='ibs'>
                                    <attribute name='ats_inventorybyseasonid' />

                                    <link-entity name='product' from='productid' to='ats_product' alias='prod'>
                                        <attribute name='productid' />
                                        <attribute name='name' />
                                    </link-entity>

                                </link-entity>
                            </link-entity>
                        </entity>
                    </fetch>";
                
                #endregion

                EntityCollection componentData = service.RetrieveMultiple(new FetchExpression(componentFetchXml));

                foreach (var entity in componentData.Entities)
                {
                    tracingService.Trace($"package rate Id: {entity.Id}");

                    OptionSetValue rateTypeSetValue = (OptionSetValue)((AliasedValue)entity.Attributes["rate.ats_ratetype"]).Value;
                    string rateType = GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateTypeSetValue.Value);

                    Money rateMoney = entity.GetAttributeValue<AliasedValue>("rate.ats_price")?.Value as Money;
                    decimal rate = rateMoney?.Value ?? 0m;
                        
                    #region
                    componentList.Add(new ComponentData
                    {
                        PackageRateId = entity.GetAttributeValue<EntityReference>("ats_packagerateid")?.Id.ToString() ?? string.Empty,
                        ComponentId = (entity.GetAttributeValue<AliasedValue>("prod.productid")?.Value as Guid?)?.ToString() ?? string.Empty,
                        ComponentName = (entity.GetAttributeValue<AliasedValue>("prod.name")?.Value?.ToString()) ?? string.Empty,
                        RateType = rateType,
                        ComponentRateId = entity.GetAttributeValue<EntityReference>("ats_componentrateid")?.Id.ToString() ??string.Empty,
                        Rate = rate,
                        QtyUnits = entity.GetAttributeValue<int?>("ats_quantity") ?? 0,
                        QtyEvents = entity.GetAttributeValue<int?>("ats_quantityofevents") ?? 0,
                        PackageTemplateId = entity.Id.ToString()
                    });
                    #endregion
                }

                //sending the componentJson data output
                context.OutputParameters["ComponentDataJson"] = JsonSerializer.Serialize(componentList) == null ? "" : JsonSerializer.Serialize(componentList);
                tracingService.Trace($"Component data json sent sucessfully");

            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// retrieving the product and season data sending into the output parameter
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <param name="productId"></param>
        /// <param name="context"></param>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        public static void GetInventoryJsonData(
            IOrganizationService service,
            ITracingService tracingService,
            string seasonId,
            IPluginExecutionContext context)
        {
            #region function level variables 
            string functionName = "GetInventoryJsonData";
            List<InventoryData> inventoryList = new List<InventoryData>();
            #endregion

            try
            {
                tracingService.Trace($"functionName: {functionName}");
                #region
                //retrieving all inventory with IBS/rate for selected season id
                string inventoryFetchXml = $@"
                    <fetch  output-format='xml-platform' mapping='logical'>
                        <entity name='ats_season'>
                            <filter>
                                <condition attribute='ats_seasonid' operator='eq' value='{seasonId}' />
                            </filter>
                            <link-entity name='ats_inventorybyseason' from='ats_season' to='ats_seasonid' alias='IBS'>
                                <attribute name='ats_quantityavailable' />
                                <attribute name='ats_eventschedule' />
                                <attribute name='ats_totalquantity' />
                                <attribute name='ats_quantitysold' />
                                <attribute name='ats_quantitypitched' />
                                <attribute name='ats_totalquantityperevent' />
                                <attribute name='ats_notavailable' />
                                <filter>
                                <condition attribute='statecode' operator='eq' value='0' />
                                </filter>
                                <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='inner' alias='Rate'>
                                    <attribute name='ats_hardcost' />
                                    <attribute name='ats_hardcost2' />
                                    <attribute name='ats_lockhardcost' />
                                    <attribute name='ats_price' />
                                    <attribute name='ats_rateid' />
                                    <attribute name='ats_ratetype' />
                                    <attribute name='ats_lockunitrate' />
                                    <filter>
                                        <condition attribute='statecode' operator='eq' value='0' />
                                        <condition attribute='ats_inactive' operator='eq' value='0' />
                                    </filter>
                                </link-entity>
                                <link-entity name='product' from='productid' to='ats_product' alias='Product'>
                                    <attribute name='ats_division' />
                                    <attribute name='ats_productfamily' />
                                    <attribute name='ats_productsubfamily' />
                                    <attribute name='name' />
                                    <attribute name='productid' />
                                    <attribute name='ats_ispassthroughcost' />
                                    <filter>
                                        <condition attribute='statecode' operator='eq' value='0' />
                                        <condition attribute='ats_ispackage' operator='ne' value='1' />
                                    </filter>
                                </link-entity>
                                <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventschedule' link-type='outer' alias='EventSched'>
                                    <attribute name='ats_expectedeventquantity' />
                                    <filter>
                                        <condition attribute='statecode' operator='eq' value='0' />
                                    </filter>
                                </link-entity>
                            </link-entity>
                        </entity>
                    </fetch>";

                #endregion
                EntityCollection inventoryData = service.RetrieveMultiple(new FetchExpression(inventoryFetchXml));
                tracingService.Trace($"Fetched {inventoryData.Entities.Count} inventory records.");


                foreach (var entity in inventoryData.Entities)
                {
                    #region 
                    InventoryData inventoryProduct = new InventoryData();

                    inventoryProduct.ProductId = ((AliasedValue)entity.Attributes["Product.productid"]).Value.ToString();
                    inventoryProduct.ProductName = ((AliasedValue)entity.Attributes["Product.name"]).Value.ToString();

                    EntityReference productFamilyRef = (EntityReference)((AliasedValue)entity.Attributes["Product.ats_productfamily"]).Value;
                    inventoryProduct.ProductFamily = productFamilyRef.Name;

                    EntityReference productSubFamilyRef = (EntityReference)((AliasedValue)entity.Attributes["Product.ats_productsubfamily"]).Value;
                    inventoryProduct.ProductSubFamily = productSubFamilyRef.Name;

                    EntityReference divisonRef = (EntityReference)((AliasedValue)entity.Attributes["Product.ats_division"]).Value;
                    inventoryProduct.Division = divisonRef.Name;

                    inventoryProduct.IsPassthroughCost = (bool)((AliasedValue)entity.Attributes["Product.ats_ispassthroughcost"]).Value;

                    if (entity.Attributes.Contains("Rate.ats_ratetype"))
                    {
                        OptionSetValue rateTypeSetValue = (OptionSetValue)((AliasedValue)entity.Attributes["Rate.ats_ratetype"]).Value;
                        if (rateTypeSetValue != null)
                        {
                            inventoryProduct.RateType = GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateTypeSetValue.Value);

                        }
                    }

                    if (entity.Attributes.Contains("Rate.ats_price"))
                    {
                        Money rateValue = (Money)((AliasedValue)entity.Attributes["Rate.ats_price"]).Value;
                        inventoryProduct.Rate = rateValue.Value;
                    }

                    if (entity.Attributes.Contains("Rate.ats_rateid"))
                    {
                        inventoryProduct.RateId = ((AliasedValue)entity.Attributes["Rate.ats_rateid"]).Value.ToString();
                    }

                    if (entity.Attributes.Contains("Rate.ats_lockunitrate"))
                    {
                        inventoryProduct.LockRate = (bool)((AliasedValue)entity.Attributes["Rate.ats_lockunitrate"]).Value;
                    }

                    if (entity.Attributes.Contains("Rate.ats_hardcost"))
                    {
                        Money hardCostValue = (Money)((AliasedValue)entity.Attributes["Rate.ats_hardcost"]).Value;
                        inventoryProduct.HardCost = hardCostValue.Value;
                    }

                    if (entity.Attributes.Contains("Rate.ats_lockhardcost"))
                    {
                        inventoryProduct.LockHardCost = (bool)((AliasedValue)entity.Attributes["Rate.ats_lockhardcost"]).Value;
                    }

                    if (entity.Attributes.Contains("Rate.ats_hardcost2"))
                    {
                        Money productionCostValue = (Money)((AliasedValue)entity.Attributes["Rate.ats_hardcost2"]).Value;
                        inventoryProduct.ProductionCost = productionCostValue.Value;
                    }

                    if (entity.Attributes.Contains("Rate.ats_lockhardcost"))
                    {
                        inventoryProduct.LockProductionCost = (bool)((AliasedValue)entity.Attributes["Rate.ats_lockhardcost"]).Value;
                    }

                    if (entity.Attributes.Contains("IBS.ats_quantityavailable"))
                    {
                        inventoryProduct.QuantityAvailable = (int)((AliasedValue)entity.Attributes["IBS.ats_quantityavailable"]).Value;
                    }

                    if (entity.Attributes.Contains("IBS.ats_totalquantity"))
                    {
                        inventoryProduct.QtyUnits = 1;
                    }

                    if (entity.Attributes.Contains("IBS.ats_totalquantityperevent"))
                    {
                        // if the Rate type is other than season, the show the QtyEvents as '1'; 
                        tracingService.Trace($"inventoryProduct.RateType: {inventoryProduct.RateType}");
                        if (inventoryProduct.RateType != "Season")
                        {
                            inventoryProduct.QtyEvents = 1;
                        }

                        //if the rate type is season, and the Eventt schedule is null, then also the QtyEvents= 1 ; 
                        if (inventoryProduct.RateType == "Season")
                        {
                            //validating the Event Schedule
                            if (entity.Attributes.Contains("IBS.ats_eventschedule"))
                            {
                                var aliasedEventSchedule = entity["IBS.ats_eventschedule"] as AliasedValue;
                                if (aliasedEventSchedule == null && aliasedEventSchedule.Value == null)
                                {
                                    tracingService.Trace("ats_eventschedule is null inside AliasedValue.");
                                    inventoryProduct.QtyEvents = 1;
                                }
                                else //If event schedule exist then the value of the QtyEvents would be the expected event quantity
                                {
                                    tracingService.Trace("ats_eventschedule is not null inside AliasedValue.");
                                    inventoryProduct.QtyEvents = (int)((AliasedValue)entity.Attributes["EventSched.ats_expectedeventquantity"]).Value;
                                }
                            }
                            else
                            {
                                tracingService.Trace("IBS.ats_eventschedule not found in entity attributes.");
                            }
                        }
                    }
                    inventoryList.Add(inventoryProduct);
                    #endregion
                    tracingService.Trace($"product id is getting added in the Inventory json: {inventoryProduct.ProductId}");

                }
                //sending the inventoryJson data output
                context.OutputParameters["InventoryDataJson"] = JsonSerializer.Serialize(inventoryList) == null ? "" : JsonSerializer.Serialize(inventoryList);
                tracingService.Trace($"Inventory data json sent sucessfully");
            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
        }

        public static void AddComponent(
            IOrganizationService service,
            ITracingService tracingService,
            PackageTemplateRecord packageTemplateRecord,
            IPluginExecutionContext context)
        {
            #region function level variables 
            string functionName = "AddComponent";

            Entity PackageTemplate = new Entity("ats_packagetemplate");

            Guid packageRateId = Guid.Parse(packageTemplateRecord.PackageRateId);
            Guid componentRateId = Guid.Parse(packageTemplateRecord.ComponentRateId);

            PackageTemplate["ats_packagerateid"] = new EntityReference("ats_rate", packageRateId);
            PackageTemplate["ats_componentrateid"] = new EntityReference("ats_rate", componentRateId);
            PackageTemplate["ats_quantity"] = packageTemplateRecord.QtyUnits;
            PackageTemplate["ats_quantityofevents"] = packageTemplateRecord.QtyEvents;
            try
            {
                tracingService.Trace($"functionName: {functionName}");
                tracingService.Trace("Before Create");
                Guid packageTemplateId = service.Create(PackageTemplate);
                tracingService.Trace("After Create");
                tracingService.Trace($"Component added sucessfully");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Before Exception " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
            #endregion
        }

        public static void DeleteComponent(
            IOrganizationService service,
            ITracingService tracingService,
            string packageTemplateId,
            IPluginExecutionContext context)
        {
            #region function level variables 
            string functionName = "DeleteComponent";
            try
            {
                tracingService.Trace($"functionName: {functionName}");
                tracingService.Trace("Before Delete");
                service.Delete("ats_packagetemplate", new Guid(packageTemplateId));
                tracingService.Trace("After Delete");
                tracingService.Trace($"Component deleted sucessfully");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Before Exception " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
            #endregion
        }

        public static void UpdateComponent(
            IOrganizationService service,
            ITracingService tracingService,
            PackageTemplateRecord packageTemplateRecord,
            IPluginExecutionContext context)
        {
            #region function level variables 
            string functionName = "UpdateComponent";

            string getPackageTemplatebyId = @"
                <fetch>
                    <entity name='ats_packagetemplate'>
                        <attribute name='ats_packagerateid' />
                        <attribute name='ats_componentrateid' />
                        <attribute name='ats_quantity' />
                        <attribute name='ats_quantityofevents' />
                        <filter>
                        <condition attribute='ats_packagetemplateid' operator='eq' value='{0}' />
                        </filter>
                    </entity>
                </fetch>";

            var query = string.Format(getPackageTemplatebyId, packageTemplateRecord.PackageTemplateId);
            EntityCollection retrievedPackageTemplates = service.RetrieveMultiple(new FetchExpression(query));

            foreach (var packageTemplate in retrievedPackageTemplates.Entities)
            {
                Entity templateToUpdate = new Entity("ats_packagetemplate", new Guid(packageTemplateRecord.PackageTemplateId));
                Guid packageRateId = Guid.Parse(packageTemplateRecord.PackageRateId);
                Guid componentRateId = Guid.Parse(packageTemplateRecord.ComponentRateId);

                templateToUpdate["ats_packagerateid"] = new EntityReference("ats_rate", packageRateId);
                templateToUpdate["ats_componentrateid"] = new EntityReference("ats_rate", componentRateId);
                templateToUpdate["ats_quantity"] = packageTemplateRecord.QtyUnits;
                templateToUpdate["ats_quantityofevents"] = packageTemplateRecord.QtyEvents;

                try
                {
                    tracingService.Trace($"functionName: {functionName}");
                    tracingService.Trace("Before Update");
                    service.Update(templateToUpdate);
                    tracingService.Trace("After Update");
                    tracingService.Trace($"Component updated sucessfully");
                }
                catch (Exception ex)
                {
                    tracingService.Trace("Before Exception " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
                }
            }
            #endregion
        }

        public static void ValidateConflicts(
            IOrganizationService service,
            ITracingService tracingService,
            string productId,
            string[] seasonIds,
            string[] packageTemplateIds,
            IPluginExecutionContext context)
        {
            List<SeasonConflictSummary> conflictsList = new List<SeasonConflictSummary>();

            // ---- Load Seasons ----
            var seasonsById = new Dictionary<Guid, Entity>();
            try
            {
                seasonsById = LoadSeasons(seasonIds, service, tracingService);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Validate conflicts: failed to load seasons: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            // ---- Load Package Templates ----
            List<PackageConflictTemplateRecord> templates = new List<PackageConflictTemplateRecord>();
            var componentProductIds = new HashSet<Guid>();
            var componentKeySet = new HashSet<string>();
            var packageProductName = string.Empty;
            try
            {
                templates = LoadTemplates(productId, packageTemplateIds, service, tracingService);

                foreach (var template in templates)
                {
                    if (template.ComponentProductId != null)
                    {
                        var componentProductGuid = Guid.Parse(template.ComponentProductId);
                        componentProductIds.Add(componentProductGuid);
                        componentKeySet.Add($"{template.ComponentProductId}|{template.ComponentRateType}");
                    }
                    packageProductName = template.PackageProductName;
                }
                tracingService.Trace($"Collected {componentProductIds.Count} componentProductIds.");
                tracingService.Trace($"Collected {componentKeySet.Count} componentKeySet entries.");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Validate conflicts: failed to load package templates: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            // Get all productids, package + components
            var allProductIds = new HashSet<Guid>();
            if (!Guid.TryParse(productId, out Guid productGuid))
            {
                throw new InvalidPluginExecutionException("Invalid or missing product id.");
            }
            else
            {
                allProductIds.Add(productGuid);
            }

            foreach (var id in componentProductIds)
            {
                allProductIds.Add(id);
            }

            // ---- Load Inventory by Season ----
            var inventoryMap = new Dictionary<string, Entity>();
            try
            {
                inventoryMap = LoadInventoryBySeason(allProductIds, seasonIds, service, tracingService);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Validate conflicts: failed to load inventory by season records: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            // ---- Load Rates ----
            var rateMap = new Dictionary<string, Entity>();

            var inventoryIds = inventoryMap.Values
                .Select(inv => inv.Id)
                .ToList();

            if (!inventoryIds.Any())
            {
                tracingService.Trace("No inventory records found — skipping rate lookup.");
            }
            else
            {
                try
                {
                    rateMap = LoadRates(inventoryIds, service, tracingService);
                }
                catch (Exception ex)
                {
                    tracingService.Trace("Validate conflicts: failed to load rate records: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
                }                   
            }

            // ---- Check for conflicts across each season ----
            try
            {
                SeasonConflictSummary seasonConflict = null;
                foreach (var seasonId in seasonsById)
                {
                    Guid seasonGuid = seasonId.Key;
                    var season = seasonId.Value;

                    seasonConflict = new SeasonConflictSummary
                    {
                        SeasonId = season.Id.ToString(),
                        SeasonName = season.GetAttributeValue<string>("ats_name"),
                        Conflicts = new List<ProductConflict>()
                    };

                    // ---- Check package product ----
                    string packageInventoryKey = $"{productId}|{season.Id}";
                    inventoryMap.TryGetValue(packageInventoryKey, out var packageInventory);
                    tracingService.Trace($"packageInventory: {packageInventory.Id}");

                    Entity packageRate = null;


                    //Sunny(22-1-26)--> Making the rate type eligible for Individual as well as Season type
                    #region rate type dynamic logic 
                    int rateTypeValue = -1; 
                    if (packageInventory != null)
                    {
                        // ats_rate where related ats_inventorybyseason = <your IBS id>
                        Guid ibsId = packageInventory.Id;

                        var qe = new QueryExpression("ats_rate")
                        {
                            ColumnSet = new ColumnSet("ats_rateid", "ats_ratetype")
                        };

                        var linkIbs = qe.AddLink(
                            "ats_inventorybyseason",
                            "ats_inventorybyseason",          // ats_rate lookup to IBS (on ats_rate)
                            "ats_inventorybyseasonid",        // IBS primary id
                            JoinOperator.Inner
                        );

                        linkIbs.EntityAlias = "IBS";
                        linkIbs.Columns = new ColumnSet(false);
                        linkIbs.LinkCriteria.AddCondition("ats_inventorybyseasonid", ConditionOperator.Equal, ibsId);

                        // Execute
                        EntityCollection rates = service.RetrieveMultiple(qe);

                        Entity rate = rates.Entities[0];
                        rateTypeValue = rate.Contains("ats_ratetype")
                              ? rate.GetAttributeValue<OptionSetValue>("ats_ratetype").Value
                              : -1;

                        tracingService.Trace($"RateId: {rate.Id}, RateTypeValue: {rateTypeValue}");
                       
                    }
                   
                    if(rateTypeValue == -1)
                    {
                        throw new InvalidPluginExecutionException("Incorrect Rate type");
                    }
                    #endregion


                    if (packageInventory != null)
                    {
                        string packageRateKey = String.Empty;
                        if (rateTypeValue == 114300000)
                        {
                            packageRateKey = $"{packageInventory.Id}|{RateType.Season}";
                        }
                        else
                        {
                            packageRateKey = $"{packageInventory.Id}|{RateType.Individual}";
                        }
                        //packageRateKey = $"{packageInventory.Id}|{RateType.Individual}";
                        tracingService.Trace($"packageRateKey:{packageRateKey}"); 
                        rateMap.TryGetValue(packageRateKey, out packageRate);
                    }
                    tracingService.Trace($"packageRate: {packageRate.Id}");
                    if (packageInventory == null && packageRate == null)
                    {
                        var conflict = new ProductConflict
                        {
                            ProductId = productId,
                            ProductName = packageProductName.ToString(),
                            Message = "Missing Individual rate record and inventory record for this season"
                        };
                        seasonConflict.Conflicts.Add(conflict);
                    }
                    else if (packageInventory == null)
                    {
                        var conflict = new ProductConflict
                        {
                            ProductId = productId,
                            ProductName = packageProductName.ToString(),
                            Message = "Missing inventory record for this season"
                        };
                        seasonConflict.Conflicts.Add(conflict);
                    }
                    else if (packageRate == null)
                    {
                        var conflict = new ProductConflict
                        {
                            ProductId = productId,
                            ProductName = packageProductName.ToString(),
                            Message = "Missing Individual rate record for this season"
                        };
                        seasonConflict.Conflicts.Add(conflict);
                    }

                    // ---- Check component products ----
                    foreach (var template in templates)
                    {
                        if (template.PackageProductId != productId)
                            continue;

                        var componentProductId = template.ComponentProductId;
                        var rateType = template.ComponentRateType;

                        // Build inventory key
                        string componentInventoryKey = $"{componentProductId}|{seasonGuid}";
                        inventoryMap.TryGetValue(componentInventoryKey, out var componentInventory);

                        Entity componentRate = null;
                        if (componentInventory != null)
                        {
                            string componentRateKey = $"{componentInventory.Id}|{rateType}";
                            rateMap.TryGetValue(componentRateKey, out componentRate);
                        }

                        if (componentInventory == null && componentRate == null)
                        {
                            var conflict = new ProductConflict
                            {
                                ProductId = componentProductId,
                                ProductName = template.ComponentProductName,
                                Message = $"Missing {rateType} rate record and inventory record for this season"
                            };
                            seasonConflict.Conflicts.Add(conflict);
                        }
                        else if (componentInventory == null)
                        {
                            var conflict = new ProductConflict
                            {
                                ProductId = componentProductId,
                                ProductName = template.ComponentProductName,
                                Message = "Missing inventory record for this season"
                            };
                            seasonConflict.Conflicts.Add(conflict);
                        }
                        else if (componentRate == null)
                        {
                            var conflict = new ProductConflict
                            {
                                ProductId = componentProductId,
                                ProductName = template.ComponentProductName,
                                Message = $"Missing {rateType} rate record for this season"
                            };
                            seasonConflict.Conflicts.Add(conflict);
                        }
                    }

                    if (seasonConflict.Conflicts.Any())
                        conflictsList.Add(seasonConflict);
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Failed while validating rates and inventories: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            //sending the conflictsJson data output
            context.OutputParameters["ConflictsDataJson"] = JsonSerializer.Serialize(conflictsList) == null ? "" : JsonSerializer.Serialize(conflictsList);
            tracingService.Trace($"Conflicts data json sent sucessfully");
        }

        public static void CopyComponents(
            IOrganizationService service,
            ITracingService tracingService,
            string productId,
            string[] seasonIds,
            string[] packageTemplateIds,
            IPluginExecutionContext context)
        {
            #region function level variables 
            string functionName = "CopyComponents";

            List<PackageTemplateRecord> packageTemplateRecords = new List<PackageTemplateRecord>();

            // ---- Load Seasons ----
            var seasonsById = new Dictionary<Guid, Entity>();
            try
            {
                seasonsById = LoadSeasons(seasonIds, service, tracingService);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Copy components: failed to load seasons: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            // ---- Load Package Templates ----
            List<PackageConflictTemplateRecord> templates = new List<PackageConflictTemplateRecord>();
            var componentProductIds = new HashSet<Guid>();
            var componentKeySet = new HashSet<string>();
            try
            {
                templates = LoadTemplates(productId, packageTemplateIds, service, tracingService);
                
                foreach (var template in templates)
                {
                    if (template.ComponentProductId != null)
                    {
                        var componentProductGuid = Guid.Parse(template.ComponentProductId);
                        componentProductIds.Add(componentProductGuid);
                        componentKeySet.Add($"{template.ComponentProductId}|{template.ComponentRateType}");
                    }
                }
                tracingService.Trace($"Collected {componentProductIds.Count} componentProductIds.");
                tracingService.Trace($"Collected {componentKeySet.Count} componentKeySet entries.");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Copy components: failed to load package templates: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            // Get all productids, package + components
            var allProductIds = new HashSet<Guid>();
            if (!Guid.TryParse(productId, out Guid productGuid))
            {
                throw new InvalidPluginExecutionException("Invalid or missing product id.");
            }
            else
            {
                allProductIds.Add(productGuid);
            }

            foreach (var id in componentProductIds)
            {
                allProductIds.Add(id);
            }

            // ---- Load Inventory by Season ----
            var inventoryMap = new Dictionary<string, Entity>();
            try
            {
                inventoryMap = LoadInventoryBySeason(allProductIds, seasonIds, service, tracingService);
            }
            catch (Exception ex)
            {
                tracingService.Trace("Copy components: failed to load inventory by season records: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            // ---- Load Rates ----
            var rateMap = new Dictionary<string, Entity>();

            var inventoryIds = inventoryMap.Values
                .Select(inv => inv.Id)
                .ToList();

            if (!inventoryIds.Any())
            {
                tracingService.Trace("No inventory records found — skipping rate lookup.");
            }
            else
            {
                try
                {
                    rateMap = LoadRates(inventoryIds, service, tracingService);
                }
                catch (Exception ex)
                {
                    tracingService.Trace("Copy components: failed to load rate records: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
                }
            }
            
            // ---- Create template records to create for each season ----
            try
            {
                foreach (var seasonId in seasonsById)
                {
                    Guid seasonGuid = seasonId.Key;
                    var season = seasonId.Value;

                    string packageInventoryKey = $"{productId}|{season.Id}";
                    inventoryMap.TryGetValue(packageInventoryKey, out var packageInventory);
                    if (packageInventory == null) continue;

                    //Sunny(22-1-26)--> Making the copy template creation rate type dynamic
                    #region rate type dynamic logic 
                    int rateTypeValue = -1;
                    if (packageInventory != null)
                    {
                        // ats_rate where related ats_inventorybyseason = <your IBS id>
                        Guid ibsId = packageInventory.Id;

                        var qe = new QueryExpression("ats_rate")
                        {
                            ColumnSet = new ColumnSet("ats_rateid", "ats_ratetype")
                        };

                        var linkIbs = qe.AddLink(
                            "ats_inventorybyseason",
                            "ats_inventorybyseason",          // ats_rate lookup to IBS (on ats_rate)
                            "ats_inventorybyseasonid",        // IBS primary id
                            JoinOperator.Inner
                        );

                        linkIbs.EntityAlias = "IBS";
                        linkIbs.Columns = new ColumnSet(false);
                        linkIbs.LinkCriteria.AddCondition("ats_inventorybyseasonid", ConditionOperator.Equal, ibsId);

                        // Execute
                        EntityCollection rates = service.RetrieveMultiple(qe);

                        Entity rate = rates.Entities[0];
                        rateTypeValue = rate.Contains("ats_ratetype")
                              ? rate.GetAttributeValue<OptionSetValue>("ats_ratetype").Value
                              : -1;

                        tracingService.Trace($"RateId: {rate.Id}, RateTypeValue: {rateTypeValue}");

                    }

                    if (rateTypeValue == -1)
                    {
                        throw new InvalidPluginExecutionException("Incorrect Rate type");
                    }
                    #endregion
                    string packageRateKey = string.Empty;
                    if (rateTypeValue == 114300000)
                    {
                        packageRateKey = $"{packageInventory.Id}|Season";
                    }
                    else
                    {
                        packageRateKey = $"{packageInventory.Id}|Individual"; 
                    }





                        //string packageRateKey = $"{packageInventory.Id}|Individual";
                        rateMap.TryGetValue(packageRateKey, out var packageRate);
                    if (packageRate == null) continue;

                    foreach (var template in templates)
                    {
                        if (template.PackageProductId != productId)
                            continue;

                        var componentProductId = template.ComponentProductId;
                        var rateType = template.ComponentRateType;
                        string componentInventoryKey = $"{componentProductId}|{seasonGuid}";
                        inventoryMap.TryGetValue(componentInventoryKey, out var componentInventory);
                        if (componentInventory == null) continue;

                        string componentRateKey = $"{componentInventory.Id}|{rateType}";
                        rateMap.TryGetValue(componentRateKey, out var componentRate);
                        if (componentRate == null) continue;

                        PackageTemplateRecord newTemplate = new PackageTemplateRecord();
                        newTemplate.PackageRateId = packageRate.Id.ToString();
                        newTemplate.ComponentRateId = componentRate.Id.ToString();
                        newTemplate.QtyUnits = template.QtyUnits != 0 ? template.QtyUnits : 1;
                        newTemplate.QtyEvents = template.QtyEvents != 0 ? template.QtyEvents : 1;
                        packageTemplateRecords.Add(newTemplate);
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Failed while building new package templates: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }

            var multipleRequest = new ExecuteMultipleRequest()
            {
                Requests = new OrganizationRequestCollection(),
                Settings = new ExecuteMultipleSettings()
                {
                    ContinueOnError = false,
                    ReturnResponses = true
                }
            };
            foreach (var record in packageTemplateRecords)
            {
                Entity PackageTemplate = new Entity("ats_packagetemplate")
                {
                    ["ats_packagerateid"] = new EntityReference("ats_rate", Guid.Parse(record.PackageRateId)),
                    ["ats_componentrateid"] = new EntityReference("ats_rate", Guid.Parse(record.ComponentRateId)),
                    ["ats_quantity"] = record.QtyUnits,
                    ["ats_quantityofevents"] = record.QtyEvents
                };
                multipleRequest.Requests.Add(new CreateRequest { Target = PackageTemplate });
            }
            try
            {
                tracingService.Trace($"functionName: {functionName}");
                tracingService.Trace("Before Create Multiple");
                var response = (ExecuteMultipleResponse)service.Execute(multipleRequest);
                tracingService.Trace("After Create Multiple");
                tracingService.Trace($"Components copied sucessfully");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Failed to insert new package templates: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
            #endregion
        }

        public static Dictionary<Guid, Entity> LoadSeasons(string[] seasonIds, IOrganizationService service, ITracingService tracingService)
        {
            var result = new Dictionary<Guid, Entity>();
            string seasonValuesXml = string.Join("", seasonIds.Select(id => $"<value>{id}</value>"));
            try
            {
                string fetchSeasonsXml = $@"
                        <fetch>
                            <entity name='ats_season'>
                                <attribute name='ats_seasonid' />
                                <attribute name='ats_name' />
                                <order attribute='ats_startdate' />
                                <order attribute='ats_name' />
                                <filter>
                                    <condition attribute='ats_seasonid' operator='in'>
                                        {seasonValuesXml}
                                    </condition>
                                </filter>
                            </entity>
                        </fetch>";
                tracingService.Trace($"FetchXML: {fetchSeasonsXml}");

                var results = service.RetrieveMultiple(new FetchExpression(fetchSeasonsXml));
                result = results.Entities.ToDictionary(
                    e => e.Id,
                    e => e
                );
            }
            catch (Exception ex)
            {
                tracingService.Trace("Failed to load seasons: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
            return result;
        }

        public static List<PackageConflictTemplateRecord> LoadTemplates(string productId, string[] packageTemplateIds, IOrganizationService service, ITracingService tracingService)
        {
            List<PackageConflictTemplateRecord> templates = new List<PackageConflictTemplateRecord>();
            string templateValuesXml = string.Join("", packageTemplateIds.Select(id => $"<value>{id}</value>"));
            var packageProductFromTemplate = new EntityReference();
            try
            {
                string fetchTemplatesXml = $@"
                        <fetch>
                        <entity name='ats_packagetemplate'>
                            <attribute name='ats_packagetemplateid' />
                            <attribute name='ats_componentrateid' />
                            <attribute name='ats_packagerateid' />
                            <attribute name='ats_quantity' />
                            <attribute name='ats_quantityofevents' />
                            <link-entity name='ats_rate' from='ats_rateid' to='ats_componentrateid' alias='componentrate'>
                                <attribute name='ats_ratetype' />
                            <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='componentibs'>
                                <attribute name='ats_product' />
                                <link-entity name='product' from='productid' to='ats_product' alias='componentproduct'>
                                <attribute name='name' />
                                </link-entity>
                            </link-entity>
                            </link-entity>
                            <link-entity name='ats_rate' from='ats_rateid' to='ats_packagerateid' alias='packagerate'>
                            <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='packageibs'>
                                <attribute name='ats_product' />
                                <link-entity name='product' from='productid' to='ats_product' alias='packageproduct'>
                                <attribute name='name' />
                                </link-entity>
                            </link-entity>
                            </link-entity>
                            <filter>
                                <condition attribute='ats_packagetemplateid' operator='in'>
                                    {templateValuesXml}
                                </condition>
                            </filter>
                        </entity>
                        </fetch>";
                tracingService.Trace($"FetchXML: {fetchTemplatesXml}");

                var results = service.RetrieveMultiple(new FetchExpression(fetchTemplatesXml));
                foreach (var template in results.Entities)
                {
                    packageProductFromTemplate = (EntityReference)((AliasedValue)template.Attributes["packageibs.ats_product"]).Value;
                    if (packageProductFromTemplate == null || packageProductFromTemplate.Id.ToString() != productId)
                        continue;

                    var componentProductRef = (EntityReference)((AliasedValue)template.Attributes["componentibs.ats_product"]).Value;
                    var aliasedRateType = (AliasedValue)template.Attributes["componentrate.ats_ratetype"];
                    var optionSet = (OptionSetValue)aliasedRateType.Value;
                    var rateTypeEnum = (RateType)optionSet.Value;
                    var rateType = rateTypeEnum.ToString();

                    var record = new PackageConflictTemplateRecord
                    {
                        PackageRateId = template.GetAttributeValue<EntityReference>("ats_packagerateid")?.Id.ToString() ?? string.Empty,
                        ComponentRateId = template.GetAttributeValue<EntityReference>("ats_componentrateid")?.Id.ToString() ?? string.Empty,
                        PackageTemplateId = template.Id.ToString(),
                        PackageProductId = ((EntityReference)((AliasedValue)template["packageibs.ats_product"]).Value).Id.ToString(),
                        PackageProductName = ((AliasedValue)template["packageproduct.name"]).Value.ToString(),
                        ComponentProductId = ((EntityReference)((AliasedValue)template["componentibs.ats_product"]).Value).Id.ToString(),
                        ComponentProductName = ((AliasedValue)template["componentproduct.name"]).Value.ToString(),
                        ComponentRateType = rateType,
                        QtyUnits = template.GetAttributeValue<int>("ats_quantity"),
                        QtyEvents = template.GetAttributeValue<int>("ats_quantityofevents")
                    };
                    templates.Add(record);
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Failed to load templates: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
            return templates;
        }

        public static Dictionary<string, Entity> LoadInventoryBySeason(HashSet<Guid> productIds, string[] seasonIds, IOrganizationService service, ITracingService tracingService)
        {
            string productValuesXml = string.Join("", productIds.Select(id => $"<value>{id}</value>"));
            string seasonValuesXml = string.Join("", seasonIds.Select(id => $"<value>{id}</value>"));
            var inventoryMap = new Dictionary<string, Entity>();
            try
            {
                string fetchInventoryXml = $@"
                        <fetch  output-format='xml-platform' mapping='logical'>
                            <entity name='ats_inventorybyseason'>
                                <attribute name='ats_product' />
                                <attribute name='ats_season' />
                                <filter>
                                    <condition attribute='ats_product' operator='in'>
                                        {productValuesXml}
                                    </condition>
                                    <condition attribute='ats_season' operator='in'>
                                        {seasonValuesXml}
                                    </condition>
                                </filter>
                            </entity>
                        </fetch>";
                tracingService.Trace($"FetchXML: {fetchInventoryXml}");

                var results = service.RetrieveMultiple(new FetchExpression(fetchInventoryXml));
                foreach (var ibs in results.Entities)
                {
                    var ibsProductRef = ibs.GetAttributeValue<EntityReference>("ats_product");
                    var ibsSeasonRef = ibs.GetAttributeValue<EntityReference>("ats_season");
                    var ibsProduct = ibsProductRef.Id;
                    var ibsSeason = ibsSeasonRef.Id;
                    string inventoryKey = $"{ibsProduct}|{ibsSeason}";
                    inventoryMap[inventoryKey] = ibs;
                }
                tracingService.Trace($"Collected {inventoryMap.Count} inventory by season records.");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Failed to load inventory by season records: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
            return inventoryMap;
        }

        public static Dictionary<string, Entity> LoadRates(List<Guid> inventoryIds, IOrganizationService service, ITracingService tracingService)
        {
            string inventoryValuesXml = string.Join("", inventoryIds.Select(id => $"<value>{id}</value>"));
            var rateMap = new Dictionary<string, Entity>();
            try
            {
                inventoryValuesXml = string.Join("", inventoryIds.Select(id => $"<value>{id}</value>"));
                
                string fetchRatesXml = $@"
                        <fetch  output-format='xml-platform' mapping='logical'>
                            <entity name='ats_rate'>
                                <attribute name='ats_inventorybyseason' />
                                <attribute name='ats_ratetype' />
                                <filter>
                                    <condition attribute='ats_inventorybyseason' operator='in'>
                                        {inventoryValuesXml}
                                    </condition>
                                </filter>
                            </entity>
                        </fetch>";
                tracingService.Trace($"FetchXML: {fetchRatesXml}");

                var results = service.RetrieveMultiple(new FetchExpression(fetchRatesXml));
                foreach (var rate in results.Entities)
                {
                    var inventoryBySeasonId = rate.GetAttributeValue<EntityReference>("ats_inventorybyseason")?.Id;
                    var rateTypeValue = rate.GetAttributeValue<OptionSetValue>("ats_ratetype")?.Value;
                    string rateType = GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateTypeValue.Value);

                    tracingService.Trace($"Rate record found: Id={rate.Id}, InventoryBySeason={inventoryBySeasonId}, RateType={rateType}");

                    if (inventoryBySeasonId != null && rateType != null)
                    {
                        string rateKey = $"{inventoryBySeasonId}|{rateType}";
                        rateMap[rateKey] = rate;
                    }
                }
                tracingService.Trace($"Collected {rateMap.Count} rate records.");
            }
            catch (Exception ex)
            {
                tracingService.Trace("Failed to load rate records: " + ex.Data + ex.Message + ex.Source + ex.StackTrace);
            }
            return rateMap;
        }    
        
        // -----------------------------------------------------------------
        // Phase C.3 — option-set label metadata cache.
        // Caches the full value→label map per (entity, field) the first time
        // it's requested. Subsequent calls (for the same picklist OR for any
        // sibling value within it) are dictionary lookups instead of
        // RetrieveAttributeRequest round-trips.
        //
        // Plugin-sandbox workers reuse the loaded assembly across many
        // executions, so this cache survives most invocations. Recycle
        // (assembly reload) clears it, which is fine for slowly-changing
        // metadata.
        // -----------------------------------------------------------------
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.Dictionary<int, string>> _optionSetLabelCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        public static string GetOptionSetLabel(IOrganizationService service, string entityLogicalName, string fieldName, int optionSetValue)
        {
            var cacheKey = entityLogicalName + "|" + fieldName;

            var labelMap = _optionSetLabelCache.GetOrAdd(cacheKey, _ =>
            {
                var retrieveAttributeRequest = new RetrieveAttributeRequest
                {
                    EntityLogicalName = entityLogicalName,
                    LogicalName = fieldName,
                    RetrieveAsIfPublished = true
                };

                var response = (RetrieveAttributeResponse)service.Execute(retrieveAttributeRequest);
                var metadata = (PicklistAttributeMetadata)response.AttributeMetadata;

                var map = new System.Collections.Generic.Dictionary<int, string>();
                foreach (var option in metadata.OptionSet.Options)
                {
                    if (option.Value.HasValue && option.Label?.UserLocalizedLabel != null)
                    {
                        map[option.Value.Value] = option.Label.UserLocalizedLabel.Label;
                    }
                }
                return map;
            });

            return labelMap.TryGetValue(optionSetValue, out var label) ? label : null;
        }
    }

    public enum RateType
    {
        Individual = 114300001,
        Season = 114300000
    }

    public class SeasonData
    {
        public string SeasonId { get; set; }
        public string StartSeason { get; set; }
        public string Name { get; set; }
        public bool IsIBSAndRatePresent { get; set; }
        public string RateId { get; set; }
    }

    public class ComponentData
    {
        public string PackageRateId { get; set; }
        public string ComponentId { get; set; }
        public string ComponentName { get; set; }
        public string RateType { get; set; }
        public string ComponentRateId { get; set; }
        public decimal Rate { get; set; }
        public int QtyUnits { get; set; }
        public int QtyEvents { get; set; }
        public string PackageTemplateId { get; set; }
    }

    public class InventoryData
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public string ProductFamily { get; set; }
        public string ProductSubFamily { get; set; }
        public string Division { get; set; }
        public bool IsPassthroughCost { get; set; }
        public string RateType { get; set; }
        public decimal Rate { get; set; }
        public string RateId { get; set; }
        public bool LockRate { get; set; }
        public decimal HardCost { get; set; }
        public bool LockHardCost { get; set; }
        public decimal ProductionCost { get; set; }
        public bool LockProductionCost { get; set; }
        public int QuantityAvailable { get; set; }
        public int QtyUnits { get; set; }
        public int QtyEvents { get; set; }
    }

    public class PackageTemplateRecord
    {
        public string PackageRateId { get; set; }
        public string ComponentRateId { get; set; }
        public int QtyUnits { get; set; }
        public int QtyEvents { get; set; }
        public string PackageTemplateId { get; set; }
    }

    public class PackageConflictTemplateRecord
    {
        public string PackageRateId { get; set; }
        public string PackageProductId { get; set; }
        public string PackageProductName { get; set; }
        public string ComponentRateId { get; set; }
        public string ComponentProductId { get; set; }
        public string ComponentProductName { get; set; }
        public string ComponentRateType { get; set; }
        public string PackageTemplateId { get; set; }
        public int QtyUnits { get; set; }
        public int QtyEvents { get; set; }
    }
    
    public class ProductConflict
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public string Message { get; set; }
    }

    public class SeasonConflictSummary
    {
        public string SeasonId { get; set; }
        public string SeasonName { get; set; }
        public List<ProductConflict> Conflicts { get; set; } = new List<ProductConflict>();
    }
}
