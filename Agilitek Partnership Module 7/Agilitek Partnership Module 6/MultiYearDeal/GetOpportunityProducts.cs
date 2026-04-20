using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Activities.DurableInstancing;
using Microsoft.Xrm.Sdk.Workflow.Activities;
using System.IdentityModel.Metadata;
using System.Web.Services.Description;

namespace MultiYearDeal
{
    public class GetOpportunityProducts
    {
        // Cache rate headers (last season rate -> ratetype)
        private static readonly Dictionary<Guid, Entity> _rateHeaderCache = new Dictionary<Guid, Entity>();

        // Cache rates per inventory (inventoryId -> list of rates)
        private static readonly Dictionary<Guid, List<Entity>> _ratesByInventoryCache = new Dictionary<Guid, List<Entity>>();

        public string fetchLastYearInventorySeason = @"<fetch>
  <entity name='ats_inventorybyseason'>
    <attribute name='ats_allowoverselling' />
    <attribute name='ats_inventorybyseasonid' />
    <attribute name='ats_product' />
    <attribute name='ats_season' />
    <attribute name='ats_totalquantity' />
    <attribute name='ats_unlimitedquantity' />
    <attribute name='statecode' />
    <attribute name='ats_description' />
    <attribute name='ats_eventschedule' />
    <attribute name='ats_name' />
    <attribute name='ats_recordtype' />
    <attribute name='ats_totalquantityperevent' />
    <attribute name='statuscode' />
    <filter type='and'>
      <condition attribute='ats_product' operator='eq' value='{productId}' />
      <condition attribute='ats_season' operator='eq' value='{seasonId}' />
      <condition attribute='statecode' operator='eq' value='0' />
    </filter>
    <link-entity name='product' from='productid' to='ats_product' alias='Product'>
      <attribute name='ats_division' />
      <attribute name='ats_productfamily' />
      <attribute name='ats_productsubfamily' />
      <attribute name='name' />
      <attribute name='statecode' />
      <link-entity name='ats_division' from='ats_divisionid' to='ats_division' alias='Division'>
        <attribute name='ats_name' />
      </link-entity>
      <link-entity name='ats_productfamily' from='ats_productfamilyid' to='ats_productfamily' alias='ProductFamily'>
        <attribute name='ats_name' />
      </link-entity>
      <link-entity name='ats_productsubfamily' from='ats_productsubfamilyid' to='ats_productsubfamily' alias='ProductSubFamily'>
        <attribute name='ats_name' />
      </link-entity>
    </link-entity>
<link-entity name='ats_season' from='ats_seasonid' to='ats_season' alias='Season'>
      <attribute name='ats_name' />
      <attribute name='ats_seasonid' />
    </link-entity>
  </entity>
</fetch>";

        private IOrganizationService GetAdminImpersonationService(IOrganizationService service, IOrganizationServiceFactory serviceFactory)
        {
            string functionName = "GetAdminImpersonationService";
            try
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
        /// Sunny(Optimized code)--> 28-12-25
        /// </summary>
        public List<Entity> RetrieveOpportunityProducts(Guid opportunityId, IOrganizationService service, bool newOpp, ITracingService tracingService)
        {
            string functionName = "RetrieveOpportunityProducts";
            try
            {
                if (opportunityId == Guid.Empty)
                    return new List<Entity>();

                // NOTE: Keeping ColumnSet(true) to avoid functional impact.
                QueryExpression productQuery = new QueryExpression("opportunityproduct")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("opportunityid", ConditionOperator.Equal, opportunityId)
                        }
                    }
                };

                EntityCollection products = service.RetrieveMultiple(productQuery);

                // Do NOT copy these fields to new OLI
                string[] excludeFields =
                {
                    "opportunityid",
                    //"opportunityproductid",
                    "ats_unadjustedtotalprice",
                    "createdon",
                    "modifiedon",
                    "versionnumber",
                    "overriddencreatedon",
                    "timezoneruleversionnumber",
                    "utcconversiontimezonecode",
                    "statecode",
                    "statuscode"
                };

                var exclude = new HashSet<string>(excludeFields, StringComparer.OrdinalIgnoreCase);

                List<Entity> productCopies = new List<Entity>(products.Entities.Count);

                foreach (Entity product in products.Entities)
                {
                    Entity newProduct = new Entity("opportunityproduct");

                    foreach (var attribute in product.Attributes)
                    {
                        var key = attribute.Key;

                        if (exclude.Contains(key))
                            continue;

                        // Your original behavior:
                        // if newOpp and key == ats_sellingrate -> copy Money from priceperunit
                        if (newOpp && key.Equals("ats_sellingrate", StringComparison.OrdinalIgnoreCase))
                        {
                            Money ppu = product.GetAttributeValue<Money>("priceperunit");
                            if (ppu != null)
                                newProduct[key] = ppu;
                            else
                                newProduct[key] = attribute.Value;

                            continue;
                        }

                        newProduct[key] = attribute.Value;
                    }
                    // IMPORTANT: preserve source OLI id for batching
                    newProduct.Id = product.Id;

                    productCopies.Add(newProduct);
                }

                return productCopies;
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







        //Sunny(code optimization) 28-12-25
        public EntityReference EnsureInventoryForSeason(Guid seasonId, Guid productId, Guid lastSeasonId, int QtyUnits, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "EnsureInventoryForSeason";
            try
            {
                Logging.Log("---------------------------------------------------------------", tracingService);
                Logging.Log("Inside EnsureInventoryForSeason", tracingService);

                if (seasonId == Guid.Empty || productId == Guid.Empty)
                    return null;

                // 1) Check if IBS already exists (minimal columns)
                QueryExpression inventoryQuery = new QueryExpression("ats_inventorybyseason")
                {
                    ColumnSet = new ColumnSet("ats_inventorybyseasonid", "ats_quantitypitched"), // minimal
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("ats_season", ConditionOperator.Equal, seasonId),
                            new ConditionExpression("ats_product", ConditionOperator.Equal, productId)
                        }
                    }
                };

                EntityCollection inventoryCollection = service.RetrieveMultiple(inventoryQuery);

                if (inventoryCollection.Entities.Count > 0)
                {
                    return inventoryCollection.Entities[0].ToEntityReference();
                }

                // 2) No IBS found -> find previous season IBS (your existing fetch)
                if (lastSeasonId == Guid.Empty)
                    return null;

                string fetchXml = fetchLastYearInventorySeason
                    .Replace("{productId}", productId.ToString())
                    .Replace("{seasonId}", lastSeasonId.ToString());

                EntityCollection prevSeasonInventoryCollection = service.RetrieveMultiple(new FetchExpression(fetchXml));

                if (prevSeasonInventoryCollection.Entities.Count == 0)
                    return null;

                Entity lastInventoryBySeason = prevSeasonInventoryCollection.Entities.Last();

                // 3) Retrieve rates for last season IBS
                QueryExpression rateQuery = new QueryExpression("ats_rate")
                {
                    ColumnSet = new ColumnSet(true),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("ats_inventorybyseason", ConditionOperator.Equal, lastInventoryBySeason.Id)
                        }
                    }
                };
                EntityCollection rateCollection = service.RetrieveMultiple(rateQuery);

                // 4) Retrieve season name only
                EntityReference newSeasonRef = new EntityReference("ats_season", seasonId);
                Entity newSeason = service.Retrieve(newSeasonRef.LogicalName, newSeasonRef.Id, new ColumnSet("ats_name"));

                // 5) Create new IBS
                Entity newInventory = new Entity("ats_inventorybyseason");

                string inventoryBySeasonName =
                    ((AliasedValue)lastInventoryBySeason.Attributes["Division.ats_name"]).Value.ToString() + " " +
                    ((AliasedValue)lastInventoryBySeason.Attributes["ProductFamily.ats_name"]).Value.ToString() + " " +
                    ((AliasedValue)lastInventoryBySeason.Attributes["ProductSubFamily.ats_name"]).Value.ToString() + " " +
                    ((AliasedValue)lastInventoryBySeason.Attributes["Product.name"]).Value.ToString() + " " +
                    newSeason.GetAttributeValue<string>("ats_name");

                newInventory["ats_product"] = (EntityReference)lastInventoryBySeason.Attributes["ats_product"];
                newInventory["ats_name"] = inventoryBySeasonName;

                if (lastInventoryBySeason.Attributes.Contains("ats_description"))
                    newInventory["ats_description"] = lastInventoryBySeason["ats_description"];

                newInventory["ats_season"] = newSeasonRef;

                if (lastInventoryBySeason.Attributes.Contains("ats_allowoverselling"))
                    newInventory["ats_allowoverselling"] = lastInventoryBySeason["ats_allowoverselling"];

                if (lastInventoryBySeason.Attributes.Contains("ats_unlimitedquantity"))
                    newInventory["ats_unlimitedquantity"] = lastInventoryBySeason["ats_unlimitedquantity"];

                if (lastInventoryBySeason.Attributes.Contains("ats_recordtype"))
                    newInventory["ats_recordtype"] = lastInventoryBySeason["ats_recordtype"];

                if (lastInventoryBySeason.Attributes.Contains("ats_eventschedule") && lastInventoryBySeason["ats_eventschedule"] != null)
                {
                    EntityReference oldEventScheduleRef = (EntityReference)lastInventoryBySeason["ats_eventschedule"];
                    newInventory["ats_eventschedule"] = GetEventSchedule(oldEventScheduleRef, newSeasonRef, service, tracingService);
                }

                if (lastInventoryBySeason.Attributes.Contains("ats_totalquantityperevent"))
                    newInventory["ats_totalquantityperevent"] = lastInventoryBySeason["ats_totalquantityperevent"];

                if (lastInventoryBySeason.Attributes.Contains("ats_totalquantity"))
                    newInventory["ats_totalquantity"] = lastInventoryBySeason["ats_totalquantity"];

                newInventory["ats_autogenerated"] = true;

                Guid newInventoryId = service.Create(newInventory);

                // 6) Clone rates
                string[] excludeFields =
                {
                    "ats_name", "ats_rateid", "ats_inventorybyseason",
                    "createdon", "owningbusinessunit", "ownerid", "modifiedon",
                    "modifiedby", "createdby", "owninguser", "ats_price_base",
                    "versionnumber", "overriddencreatedon", "timezoneruleversionnumber",
                    "utcconversiontimezonecode", "statecode", "statuscode"
                };
                var exclude = new HashSet<string>(excludeFields, StringComparer.OrdinalIgnoreCase);

                foreach (var rate in rateCollection.Entities)
                {
                    string rateTypeValue = string.Empty;
                    Entity newRate = new Entity("ats_rate");

                    foreach (var attribute in rate.Attributes)
                    {
                        if (exclude.Contains(attribute.Key))
                            continue;

                        if (attribute.Key.Equals("ats_ratetype", StringComparison.OrdinalIgnoreCase))
                        {
                            rateTypeValue = rate.FormattedValues.Contains("ats_ratetype") ? rate.FormattedValues["ats_ratetype"] : string.Empty;
                        }

                        newRate[attribute.Key] = attribute.Value;
                    }

                    newRate["ats_inventorybyseason"] = new EntityReference("ats_inventorybyseason", newInventoryId);
                    newRate["ats_name"] = inventoryBySeasonName + (string.IsNullOrWhiteSpace(rateTypeValue) ? "" : " " + rateTypeValue);
                    newRate["ats_autogenerated"] = true;

                    service.Create(newRate);
                }

                return new EntityReference("ats_inventorybyseason", newInventoryId);
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
        /// Sunny(Optimizing the code)--> 28-12-25)
        /// </summary>
        public EntityReference GetRateForSeason(EntityReference lastSeasonRateRef, IOrganizationService service, EntityReference inventoryRef, ref decimal hardcost)
        {
            string functionName = "GetRateForSeason";
            try
            {
                hardcost = 0m;

                if (lastSeasonRateRef == null || lastSeasonRateRef.Id == Guid.Empty)
                    return lastSeasonRateRef;

                if (inventoryRef == null || inventoryRef.Id == Guid.Empty)
                    return lastSeasonRateRef;

                // Get last season rate (only ratetype) - CACHED
                Entity lastRate;
                if (!_rateHeaderCache.TryGetValue(lastSeasonRateRef.Id, out lastRate))
                {
                    lastRate = service.Retrieve(lastSeasonRateRef.LogicalName, lastSeasonRateRef.Id, new ColumnSet("ats_ratetype"));
                    _rateHeaderCache[lastSeasonRateRef.Id] = lastRate;
                }

                OptionSetValue lastRateType = lastRate.GetAttributeValue<OptionSetValue>("ats_ratetype");
                if (lastRateType == null)
                    return lastSeasonRateRef;

                // Get current season rates for inventory - CACHED
                List<Entity> currentSeasonRates;
                if (!_ratesByInventoryCache.TryGetValue(inventoryRef.Id, out currentSeasonRates))
                {
                    QueryExpression rateQuery = new QueryExpression("ats_rate")
                    {
                        ColumnSet = new ColumnSet("ats_rateid", "ats_ratetype", "ats_hardcost"),
                        Criteria = new FilterExpression
                        {
                            Conditions =
                            {
                                new ConditionExpression("ats_inventorybyseason", ConditionOperator.Equal, inventoryRef.Id)
                            }
                        }
                    };

                    currentSeasonRates = service.RetrieveMultiple(rateQuery).Entities.ToList();
                    _ratesByInventoryCache[inventoryRef.Id] = currentSeasonRates;
                }

                // Match rate by ratetype
                foreach (var rate in currentSeasonRates)
                {
                    var rateType = rate.GetAttributeValue<OptionSetValue>("ats_ratetype");
                    if (rateType != null && rateType.Value == lastRateType.Value)
                    {
                        hardcost = rate.GetAttributeValue<Money>("ats_hardcost")?.Value ?? 0m;
                        return rate.ToEntityReference();
                    }
                }

                // Fallback
                return lastSeasonRateRef;
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

        public EntityReference GetEventSchedule(EntityReference oldEventScheduleRef, EntityReference newSeason, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "GetEventSchedule";
            try
            {
                Entity eventSchedule = new Entity();

                Entity oldEventSchedule = service.Retrieve(oldEventScheduleRef.LogicalName, oldEventScheduleRef.Id, new ColumnSet(true));

                string eventScheduleName = oldEventSchedule.Contains("ats_name") ? oldEventSchedule.GetAttributeValue<string>("ats_name") : string.Empty;
                Logging.Log($"Existing Event Schedule Name : {eventScheduleName}", tracingService);

                EntityReference divisionReference = oldEventSchedule.Contains("ats_division") ? oldEventSchedule.GetAttributeValue<EntityReference>("ats_division") : null;
                Logging.Log($"Existing Event Division Name : {divisionReference.Name}", tracingService);

                Guid divisionReferenceId = divisionReference?.Id ?? Guid.Empty;

                int? seasonCategoryValue = oldEventSchedule.GetAttributeValue<OptionSetValue>("ats_seasoncategory")?.Value;

                QueryExpression query = new QueryExpression("ats_eventschedule")
                {
                    ColumnSet = new ColumnSet("ats_name"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("ats_name", ConditionOperator.Equal, eventScheduleName),
                            new ConditionExpression("ats_division", ConditionOperator.Equal, divisionReferenceId),
                            new ConditionExpression("ats_season", ConditionOperator.Equal, newSeason.Id)
                        }
                    }
                };

                if (seasonCategoryValue.HasValue)
                {
                    query.Criteria.Conditions.Add(new ConditionExpression("ats_seasoncategory", ConditionOperator.Equal, seasonCategoryValue.Value));
                }

                EntityCollection existingRecords = service.RetrieveMultiple(query);
                if (existingRecords.Entities.Count > 0)
                {
                    Logging.Log($"Inside Existing Event Schedule", tracingService);
                    foreach (var record in existingRecords.Entities)
                    {
                        Logging.Log($"Existing Event Schedule Id : {record.Id}", tracingService);
                        return record.ToEntityReference();
                    }
                }
                else
                {
                    Logging.Log($"Inside Create Event Schedule", tracingService);
                    return CreateEventSchedules(oldEventScheduleRef, newSeason, service, tracingService);
                }

                return eventSchedule.ToEntityReference();
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

        public EntityReference CreateEventSchedules(EntityReference eventScheduleCopy, EntityReference newSeason, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "CreateEventSchedules";
            try
            {
                Entity schedule = service.Retrieve(eventScheduleCopy.LogicalName, eventScheduleCopy.Id, new ColumnSet(true));

                Logging.Log($"Inside Event Schedule Create Loop", tracingService);
                Entity eventSchedule = new Entity("ats_eventschedule");

                eventSchedule["ats_name"] = (string)schedule.Attributes["ats_name"];
                Logging.Log($"After ats_name", tracingService);

                eventSchedule["ats_season"] = newSeason;
                Logging.Log($"After ats_season", tracingService);

                eventSchedule["ats_autogenerated"] = true;
                Logging.Log($"After ats_autogenerated", tracingService);

                eventSchedule["statecode"] = 0;
                Logging.Log($"After statecode", tracingService);

                if (schedule.Attributes.ContainsKey("ats_division"))
                {
                    eventSchedule["ats_division"] = ((EntityReference)schedule.Attributes["ats_division"]);
                    Logging.Log($"After ats_division", tracingService);
                }

                if (schedule.Attributes.ContainsKey("ats_seasoncategory"))
                {
                    eventSchedule["ats_seasoncategory"] = ((OptionSetValue)schedule.Attributes["ats_seasoncategory"]);
                    Logging.Log($"After ats_seasoncategory", tracingService);
                }

                if (schedule.Attributes.ContainsKey("ats_expectedeventquantity"))
                {
                    eventSchedule["ats_expectedeventquantity"] = ((int)schedule.Attributes["ats_expectedeventquantity"]);
                    Logging.Log($"After ats_expectedeventquantity", tracingService);
                }

                if (schedule.Attributes.ContainsKey("ats_actualeventquantity"))
                {
                    eventSchedule["ats_actualeventquantity"] = ((int)schedule.Attributes["ats_actualeventquantity"]);
                    Logging.Log($"After ats_actualeventquantity", tracingService);
                }

                Guid newEventScheduleGuid = service.Create(eventSchedule);

                Logging.Log($"Event Schedule Created {newEventScheduleGuid}", tracingService);

                return new EntityReference("ats_eventschedule", newEventScheduleGuid);
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
    }
}
