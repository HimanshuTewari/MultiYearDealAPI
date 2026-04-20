using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IdentityModel.Metadata;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Security.Cryptography.X509Certificates;
using static OpportunityStagePlugin;

namespace MultiYearDeal
{
    public class GetAgreementData : IPlugin
    {

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

        public void Execute(IServiceProvider serviceProvider)
        {
            // Obtain the execution context
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            // Obtain the tracing service
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            // Obtain the organization service factory
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            // Create the organization service
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            string AgreementId = context.InputParameters["AgreementId"].ToString();

            Logging.Log($"Retrieved InputName: {AgreementId}", tracingService);

            List<InventoryData> inventoryData = null;
            //Sunny(01-07-25)
            //Authorize the user is having the System-Administrator or Agilitek - Partnership Admin
            bool isAuthorized = IsUserAuthorizedUsingFetchXml(context.InitiatingUserId, service, tracingService);



            // Step 1: Get both datasets
            List<OpportunityData> opportunitiesData = GetOpportunityData(AgreementId, service, tracingService);
            string hiddenFieldsJson = GetHiddenFieldsJson(service, tracingService); // from your hidden field fetchXML


            List<LineItemData> lineItems = GetLineItemData(AgreementId, service, tracingService);

            tracingService.Trace($"lineItems data retreieved");

            // Step 2: Deserialize hidden fields
            var hiddenFieldDict = JsonConvert.DeserializeObject<Dictionary<string, bool>>(hiddenFieldsJson ?? "{}");
            Guid agreementId;

            if (context.InputParameters.Contains("AgreementId") && Guid.TryParse(context.InputParameters["AgreementId"].ToString(), out agreementId))
            {
                tracingService.Trace("Agreement Id is converted in Guid");
            }
            else
            {
                throw new InvalidPluginExecutionException("Invalid or missing AgreementId in input parameters.");
            }


            var userhasAccess = false;
            //Sunny(02-07-25)
            #region Retrieving the Agreement Bpf stage name and quantity sold
            bool hasOppProductSold = HasAnyQuantitySoldGreaterThanZero(service, tracingService, agreementId);
            if (!isAuthorized)
            {
                if (!hasOppProductSold)
                {
                    userhasAccess = true;
                    inventoryData = GetInventoryData(context, AgreementId, service, tracingService);
                }
                else
                {
                    userhasAccess = false;
                }
            }
            else if (isAuthorized)
            {
                userhasAccess = true;
                inventoryData = GetInventoryData(context, AgreementId, service, tracingService);
                //                Logging.Log($"inventoryData: {JsonConvert.SerializeObject(inventoryData)}", tracingService);
            }
            #endregion

            var isAuthorizedjson = userhasAccess;

            // Step 3: Combine into wrapper
            var wrappedData = new OpportunityDataResponse
            {
                Opportunities = opportunitiesData,
                HiddenFields = hiddenFieldDict,
                isAuthorized = isAuthorizedjson
            };

            // Logging.Log($"Opp Data Count: {JsonConvert.SerializeObject(opportunitiesData)}", tracingService);

            //context.OutputParameters["InventoryDataOutput"] = JsonConvert.SerializeObject(inventoryData);
            context.OutputParameters["InventoryDataOutput"] = inventoryData != null ? JsonConvert.SerializeObject(inventoryData) : "[]";

            //context.OutputParameters["OpportunityDataOutput"] = JsonConvert.SerializeObject(opportunitiesData);

            // Step 4: Output wrapped JSON
            context.OutputParameters["OpportunityDataOutput"] = JsonConvert.SerializeObject(wrappedData);
            context.OutputParameters["LineItemDataOutput"] = JsonConvert.SerializeObject(lineItems);

        }

        #region Authorize the user is having the System-Administrator or Agilitek - Partnership Admin
        //Sunny(01-07-25)

        private bool IsUserAuthorizedUsingFetchXml(Guid userId, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "IsUserAuthorizedUsingFetchXml";
            //var allowedRoles = new List<string> { "Agilitek - Partnership Admin", "System Administrator" };
            var allowedRoles = new List<string> { "Agilitek - Partnership Admin" };

            //var allowedRoles = new List<string> { "Activity Feeds" };

            var foundRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                tracingService.Trace($"{functionName} - Started");
                tracingService.Trace($"{functionName} - Checking roles for UserId: {userId}");

                // 🔷 Fetch user roles directly assigned
                string userRolesFetch = $@"
                                        <fetch>
                                          <entity name='role'>
                                            <attribute name='name' />
                                            <link-entity name='systemuserroles' from='roleid' to='roleid' intersect='true'>
                                              <filter>
                                                <condition attribute='systemuserid' operator='eq' value='{userId}' />
                                              </filter>
                                            </link-entity>
                                          </entity>
                                        </fetch>";

                tracingService.Trace($"{functionName} - Executing fetch for direct user roles.");
                var userRoles = service.RetrieveMultiple(new FetchExpression(userRolesFetch));
                tracingService.Trace($"{functionName} - Found {userRoles.Entities.Count} direct user roles.");

                foreach (var role in userRoles.Entities)
                {
                    string roleName = role.GetAttributeValue<string>("name");
                    if (!string.IsNullOrEmpty(roleName))
                    {
                        foundRoles.Add(roleName);
                        tracingService.Trace($"{functionName} - Direct role found: {roleName}");
                    }
                }

                // 🔷 Fetch user's teams
                string userTeamsFetch = $@"
                                        <fetch>
                                          <entity name='teammembership'>
                                            <attribute name='teamid' />
                                            <filter>
                                              <condition attribute='systemuserid' operator='eq' value='{userId}' />
                                            </filter>
                                          </entity>
                                        </fetch>";

                tracingService.Trace($"{functionName} - Executing fetch for user team memberships.");
                var userTeams = service.RetrieveMultiple(new FetchExpression(userTeamsFetch));
                var teamIds = userTeams.Entities
                    .Select(e => e.GetAttributeValue<Guid>("teamid"))
                    .Distinct()
                    .ToList();

                tracingService.Trace($"{functionName} - Found {teamIds.Count} teams for user.");

                if (teamIds.Any())
                {
                    // 🔷 Fetch team roles
                    string teamRolesFetch = $@"
                                            <fetch>
                                              <entity name='role'>
                                                <attribute name='name' />
                                                <link-entity name='teamroles' from='roleid' to='roleid' intersect='true'>
                                                  <filter>
                                                    <condition attribute='teamid' operator='in'>
                                                      {string.Join("", teamIds.Select(id => $"<value>{id}</value>"))}
                                                    </condition>
                                                  </filter>
                                                </link-entity>
                                              </entity>
                                            </fetch>";

                    tracingService.Trace($"{functionName} - Executing fetch for team roles.");
                    var teamRoles = service.RetrieveMultiple(new FetchExpression(teamRolesFetch));
                    tracingService.Trace($"{functionName} - Found {teamRoles.Entities.Count} team roles.");

                    foreach (var role in teamRoles.Entities)
                    {
                        string roleName = role.GetAttributeValue<string>("name");
                        if (!string.IsNullOrEmpty(roleName))
                        {
                            foundRoles.Add(roleName);
                            tracingService.Trace($"{functionName} - Team role found: {roleName}");
                        }
                    }
                }

                // 🔷 Check if user has any allowed role
                bool isAuthorized = foundRoles.Intersect(allowedRoles, StringComparer.OrdinalIgnoreCase).Any();
                tracingService.Trace($"{functionName} - User authorization result: {isAuthorized}");

                return isAuthorized;
            }
            catch (InvalidPluginExecutionException ex)
            {
                tracingService.Trace($"{functionName} - Plugin exception: {ex.Message}");
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                tracingService.Trace($"{functionName} - General exception: {ex.Message}");
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// this function checks and return if any opportunity product quantity is sold or not. (Opportunity Product --> Opportunity --> Agreement)
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <param name="agreementId"></param>
        /// <returns></returns>
        /// <exception cref="InvalidPluginExecutionException"></exception>
        private bool HasAnyQuantitySoldGreaterThanZero(IOrganizationService service, ITracingService tracingService, Guid agreementId)
        {
            string functionName = "HasAnyQuantitySoldGreaterThanZero";
            try
            {


                #region Handling the stages before and after contract sent
                string bpfAgreementUniqueName = "ats_agreementbusinessprocessflow";

                // Step 1: Prepare FetchXML for custom configuration table
                string fetchAgreementBPFXml = $@"
                                        <fetch>
                                          <entity name='ats_agreementopportunitybpfstageconfiguration'>
                                            <attribute name='ats_bpfname' />
                                            <attribute name='ats_sortorder' />
                                            <attribute name='ats_stagename' />
                                            <filter>
                                              <condition attribute='ats_bpfname' operator='eq' value='{bpfAgreementUniqueName}' />
                                            </filter>
                                            <order attribute='ats_sortorder' />
                                          </entity>
                                        </fetch>";

                // Step 2: Execute Fetch
                EntityCollection configResults = service.RetrieveMultiple(new FetchExpression(fetchAgreementBPFXml));

                // Step 3: Process Results
                List<ConfiguredStageInfo> configuredStageList = new List<ConfiguredStageInfo>();
                int indexes = 0;
                int index = 0;
                foreach (Entity config in configResults.Entities)
                {
                    string stageNameAgreement = config.GetAttributeValue<string>("ats_stagename");
                    int? sortOrder = config.GetAttributeValue<int?>("ats_sortorder");

                    tracingService.Trace("Config record {0}: Stage = {1}, SortOrder = {2}", index, stageNameAgreement, sortOrder);

                    ConfiguredStageInfo configInfo = new ConfiguredStageInfo
                    {
                        StageName = stageNameAgreement,
                        SortOrder = sortOrder ?? 0 // default to 0 if null
                    };

                    configuredStageList.Add(configInfo);
                    indexes++;
                }

                tracingService.Trace("Configured stage list created. Total: {0}", configuredStageList.Count);

                //Getting the Sort order of the contract sent Stage 




                //Sunny(04-07-25)
                #region Testing for making it dynamic.
                //Testing for making it dynamic.

                OpportunityStagePlugin oppStageObj = new OpportunityStagePlugin();

                string evSoldStage = oppStageObj.GetEnvironmentVariableValue(service, "ats_SoldStage");

                #endregion


                int? soldStageSortOrder = configuredStageList
                    .Where(c => string.Equals(c.StageName, evSoldStage, StringComparison.OrdinalIgnoreCase))
                    .Select(c => (int?)c.SortOrder)
                    .FirstOrDefault();

                if (soldStageSortOrder.HasValue)
                {
                    tracingService.Trace("Sort order for 'Contract Sent' is: {0}", soldStageSortOrder.Value);
                }
                else
                {
                    tracingService.Trace("'Contract Sent' stage not found in the configuration.");
                }


                // Step 2: Fetch current stage name from agreement
                string fetchXml = $@"<fetch>
                                  <entity name='ats_agreement'>
                                    <filter>
                                      <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                                    </filter>
                                    <link-entity name='ats_agreementbusinessprocessflow' from='bpf_ats_agreementid' to='ats_agreementid' link-type='inner' alias='AgreeBpf'>
                                      <link-entity name='processstage' from='processstageid' to='activestageid' alias='AgreeProcess'>
                                        <attribute name='stagename' />
                                      </link-entity>
                                    </link-entity>
                                  </entity>
                                </fetch>";


                EntityCollection result = service.RetrieveMultiple(new FetchExpression(fetchXml));
                tracingService.Trace($"Fetched {result.Entities.Count} agreement record(s).");

                if (result.Entities.Count == 0)
                    throw new InvalidPluginExecutionException("Agreement not found or no stage assigned.");

                Entity agreementStageName = result.Entities.First();
                string currentStageName = agreementStageName.GetAttributeValue<AliasedValue>("AgreeProcess.stagename")?.Value?.ToString();

                tracingService.Trace("Current Stage Name: {0}", currentStageName);

                if (string.IsNullOrEmpty(currentStageName))
                    throw new InvalidPluginExecutionException("Active stage name could not be determined.");


                int? currentStageSortOrder = configuredStageList
                    .Where(c => string.Equals(c.StageName, currentStageName, StringComparison.OrdinalIgnoreCase))
                    .Select(c => (int?)c.SortOrder)
                    .FirstOrDefault();

                //validations based on the stage to make sure the user is authorized or not for the Agreement cart update. 
                if (currentStageSortOrder == soldStageSortOrder)
                {
                    tracingService.Trace("currentStageSortOrder == contractSentSortOrder");
                    return true;
                }
                else if (currentStageSortOrder < soldStageSortOrder)
                {
                    tracingService.Trace("currentStageSortOrder < contractSentSortOrder");
                    return false; //--> Agreement cart would be editable
                }
                else if (currentStageSortOrder > soldStageSortOrder)
                {
                    tracingService.Trace("current stage order is greater than contract sent sort order.");
                    return true; //--> Agreement Cart would not be editable. 
                }

                #endregion

                return true; //--> Agreement Cart would not be editable.
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



        string inventoryDataFetchXml = @"
                <fetch  output-format='xml-platform' mapping='logical'>
                    <entity name='ats_agreement'>
                        <filter>
                            <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                        </filter>
                        <link-entity name='ats_inventorybyseason' from='ats_season' to='ats_startseason' alias='IBS'>
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
                                <attribute name='ats_ispackage' />
                                <attribute name='description' />
                                <filter>
                                    <condition attribute='statecode' operator='eq' value='0' />
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

        string componentDataFetchXml = @"
                <fetch output-format='xml-platform' mapping='logical'>
                    <entity name='ats_packagetemplate'>
                        <attribute name='ats_packagetemplateid' />
                        <attribute name='ats_packagerateid' />
                        <attribute name='ats_componentrateid' />
                        <attribute name='ats_quantity' />
                        <attribute name='ats_quantityofevents' />
                        <filter>
                            <condition attribute='ats_packagerateid' operator='in'>
                                {rateIdsXml}            
                            </condition>
                        </filter>
                        <link-entity name='ats_rate' from='ats_rateid' to='ats_componentrateid' alias='CompRate'>
                            <attribute name='ats_rateid' />
                            <attribute name='ats_ratetype' />
                            <attribute name='ats_price' />
                            <attribute name='ats_lockunitrate' />
                            <attribute name='ats_hardcost' />
                            <attribute name='ats_hardcost2' />
                            <attribute name='ats_lockhardcost' />
                            <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='CompIBS'>
                                <attribute name='ats_inventorybyseasonid' />
                                <attribute name='ats_product' />
                                <attribute name='ats_quantityavailable' />
                                <attribute name='ats_eventschedule' />
                                <attribute name='ats_totalquantity' />
                                <attribute name='ats_quantitysold' />
                                <attribute name='ats_quantitypitched' />
                                <attribute name='ats_totalquantityperevent' />
                                <attribute name='ats_notavailable' />
                                <link-entity name='product' from='productid' to='ats_product' alias='CompProduct'>
                                    <attribute name='productid' />
                                    <attribute name='name' />
                                    <attribute name='ats_productfamily' />
                                    <attribute name='ats_productsubfamily' />
                                    <attribute name='ats_division' />
                                    <attribute name='ats_ispassthroughcost' />
                                    <attribute name='ats_ispackage' />
                                    <attribute name='description' />
                                </link-entity>
                            </link-entity>   
                        </link-entity>
                    </entity>
                </fetch>";

        string opportunityDataFetchXml = @"
            <fetch output-format='xml-platform' mapping='logical'>
                <entity name='opportunity'>
                <attribute name='opportunityid' />
                <attribute name='ats_dealvalue' />
                <attribute name='budgetamount' />
                <attribute name='ats_manualamount' />
                <attribute name='ats_pricingmode' />
                <attribute name='ats_totalhardcost' />
                <attribute name='ats_totalproductioncost' />
                <attribute name='ats_totalratecard' />
                <attribute name='ats_percentofrate' />
                <attribute name='ats_percentofratecard' /> 
                <attribute name='ats_barteramount' /> 
                <attribute name='ats_targetamount' /> 
                <attribute name='ats_cashamount' /> 
                <attribute name='ats_escalationtype' />
                <attribute name='ats_escalationvalue' />
                <attribute name='ats_startseason' />
                <filter>
                    <condition attribute='ats_agreement' operator='eq' value='{agreementId}' />
                </filter>
                <link-entity name='ats_season' from='ats_seasonid' to='ats_startseason' alias='Season'>
                    <attribute name='ats_name' />
                    <order attribute='ats_name' />
                </link-entity>
                </entity>
            </fetch>";

        //        string lineItemFetchXml = @"<fetch output-format='xml-platform' mapping='logical'>
        //  <entity name='ats_agreement'>
        //    <filter>
        //      <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
        //    </filter>
        //    <link-entity name='opportunity' from='ats_agreement' to='ats_agreementid' link-type='inner' alias='Opp'>
        //      <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' alias='OppProd'>
        //        <attribute name='ats_adjustedtotalprice' />
        //        <attribute name='ats_hardcost' />
        //        <attribute name='ats_hardcost2' />
        //        <attribute name='ats_quantity' />
        //        <attribute name='ats_quantityofevents' />
        //        <attribute name='ats_totalproductioncost' />
        //        <attribute name='opportunityid' />
        //        <attribute name='opportunityproductid' />
        //        <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='IBS'>
        //          <attribute name='ats_product' />
        //          <attribute name='ats_season' />
        //          <attribute name='ats_quantityavailable' />
        //          <attribute name='ats_unlimitedquantity' />
        //          <order attribute='ats_season' />
        //          <link-entity name='product' from='productid' to='ats_product' alias='Product'>
        //            <attribute name='ats_division' />
        //            <attribute name='ats_ispassthroughcost' />
        //            <attribute name='ats_productfamily' />
        //            <attribute name='ats_productsubfamily' />
        //            <attribute name='name' />
        //            <attribute name='productid' />
        //            <filter>
        //              <condition attribute='productid' operator='eq' value='{productId}' />
        //            </filter>
        //          </link-entity>
        //        </link-entity>
        //        <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' alias='Rate'>
        //          <attribute name='ats_hardcost' />
        //          <attribute name='ats_hardcost2' />
        //          <attribute name='ats_lockhardcost' />
        //          <attribute name='ats_lockunitrate' />
        //          <attribute name='ats_price' />
        //          <attribute name='ats_ratetype' />
        //          <attribute name='ats_lockproductioncost' />
        //          <attribute name='statecode' />
        //        </link-entity>
        //      </link-entity>
        //    </link-entity>
        //  </entity>
        //</fetch>";


        string hiddenFieldsFetchXml = @"
            <fetch top='1'>
              <entity name='ats_agiliteksettings'>
                <attribute name='ats_key' />
                <attribute name='ats_value' />
                <filter>
                  <condition attribute='ats_key' operator='eq' value='Opportunity Hidden Fields' />
                </filter>
              </entity>
            </fetch>";



        //Sunny(29-01-25)
        string lineItemFetchXml = @"
                <fetch output-format='xml-platform' mapping='logical'>
                    <entity name='ats_agreement'>
                        <filter>
                            <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                        </filter>
                        <link-entity name='opportunity' from='ats_agreement' to='ats_agreementid' link-type='inner' alias='Opp'>
                            <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' alias='OppProd'>
                            <attribute name='ats_adjustedtotalprice' />
                            <attribute name='ats_hardcost' />
                            <attribute name='ats_hardcost2' />
                            <attribute name='ats_quantity' />
                            <attribute name='ats_quantityofevents' />
                            <attribute name='ats_totalproductioncost' />
                            <attribute name='opportunityid' />
                            <attribute name='opportunityproductid' />
                            <attribute name='ats_sellingrate' />
                            <attribute name='ats_manualpriceoverride' />
                            <attribute name='ats_legaldefinition' />
                            <attribute name='ats_overwritelegaldefinition' />
                            <attribute name='description' />
                            <attribute name='ats_agreementopportunityproduct' />
                            <attribute name='ats_packagetemplate' />
                            <attribute name='ats_packagelineitem' />
                            {oliFilter}
                            <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='IBS'>
                                <attribute name='ats_product' />
                                <attribute name='ats_season' />
                                <attribute name='ats_quantityavailable' />
                                <attribute name='ats_unlimitedquantity' />
                                <attribute name='ats_legaldefinition' />
                                <attribute name='ats_totalquantity' />
                                <attribute name='ats_quantitysold' />
                                <attribute name='ats_quantitypitched' />
                                <attribute name='ats_notavailable' />
                                <order attribute='ats_season' />
                                <link-entity name='product' from='productid' to='ats_product' alias='Product'>
                                    <attribute name='ats_division' />
                                    <attribute name='ats_ispassthroughcost' />
                                    <attribute name='ats_productfamily' />
                                    <attribute name='ats_productsubfamily' />
                                    <attribute name='name' />
                                    <attribute name='productid' />
                                    <attribute name='ats_legaldefinition' />
                                    <attribute name='description' />
                                    <attribute name='ats_ispackage' />
                                </link-entity>
                                <link-entity name='ats_season' from='ats_seasonid' to='ats_season' alias='Season'>
                                    <attribute name='ats_name' />
                                </link-entity>
                            </link-entity>
                                <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' alias='Rate'>
                                    <attribute name='ats_hardcost' />
                                    <attribute name='ats_hardcost2' />
                                    <attribute name='ats_lockhardcost' />
                                    <attribute name='ats_lockunitrate' />
                                    <attribute name='ats_price' />
                                    <attribute name='ats_ratetype' />
                                    <attribute name='ats_lockproductioncost' />
                                    <attribute name='statecode' />
                                </link-entity>
                            </link-entity>
                        </link-entity>
                    </entity>
                </fetch>";



        //        string agreementProductFetchXml = @"<fetch distinct='true'>
        //  <entity name='ats_agreement'>
        //    <filter>
        //      <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
        //    </filter>
        //    <link-entity name='opportunity' from='ats_agreement' to='ats_agreementid' link-type='inner' alias='Opp'>
        //      <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' alias='OppProd'>
        //        <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='IBS'>
        //          <link-entity name='product' from='productid' to='ats_product' alias='Product'>
        //            <attribute name='ats_division' />
        //            <attribute name='ats_ispassthroughcost' />
        //            <attribute name='ats_productfamily' />
        //            <attribute name='ats_productsubfamily' />
        //            <attribute name='name' />
        //            <attribute name='productid' />
        //          </link-entity>
        //        </link-entity>
        //      </link-entity>
        //    </link-entity>
        //  </entity>
        //</fetch>";

        string agreementProductFetchXml = @"
                <fetch distinct='true'>
                    <entity name='ats_agreement'>
                        <filter>
                            <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
                        </filter>
                        <link-entity name='opportunity' from='ats_agreement' to='ats_agreementid' link-type='inner' alias='Opp'>
                            <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' alias='OppProd'>
                                <attribute name='ats_agreementopportunityproduct' />
                                <attribute name='opportunityproductid' />
                                <attribute name='ats_packagelineitem' />
                                <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='IBS'>
                                    <link-entity name='product' from='productid' to='ats_product' alias='Product'>
                                        <attribute name='ats_division' />
                                        <attribute name='ats_ispassthroughcost' />
                                        <attribute name='ats_productfamily' />
                                        <attribute name='ats_productsubfamily' />
                                        <attribute name='ats_ispackage' />
                                        <attribute name='name' />
                                        <attribute name='productid' />
                                    </link-entity>
                                </link-entity>
                            </link-entity>
                        </link-entity>
                    </entity>
                </fetch>";

        string parentOliFetchXml = @"
                <fetch>
                    <entity name='opportunityproduct'>
                        <attribute name='opportunityproductid' />
                        <filter>
                            <condition attribute='ats_agreementopportunityproduct' operator='eq' value='{agreementOpportunityProductId}' />
                        </filter>
                    </entity>
                </fetch>";

        /// <summary>
        /// getting the hidden fields Json
        /// </summary>
        /// <param name="service"></param>
        /// <param name="tracingService"></param>
        /// <returns></returns>
        private string GetHiddenFieldsJson(IOrganizationService service, ITracingService tracingService)
        {
            try
            {
                EntityCollection result = service.RetrieveMultiple(new FetchExpression(hiddenFieldsFetchXml));
                if (result.Entities.Count > 0 && result.Entities[0].Attributes.Contains("ats_value"))
                {
                    string json = result.Entities[0].GetAttributeValue<string>("ats_value");
                    tracingService.Trace($"Hidden Fields JSON: {json}");
                    return json;
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace($"Error retrieving hidden fields: {ex.Message}");
            }

            return "{}"; // Return empty object if not found
        }


        public string GetOptionSetLabel(IOrganizationService service, string entityLogicalName, string fieldName, int optionSetValue)
        {
            // Retrieve metadata for the specified entity and field
            var retrieveAttributeRequest = new RetrieveAttributeRequest
            {
                EntityLogicalName = entityLogicalName,
                LogicalName = fieldName,
                RetrieveAsIfPublished = true
            };

            var response = (RetrieveAttributeResponse)service.Execute(retrieveAttributeRequest);
            var metadata = (PicklistAttributeMetadata)response.AttributeMetadata;

            // Find the matching option value and return its label
            foreach (var option in metadata.OptionSet.Options)
            {
                if (option.Value == optionSetValue)
                {
                    return option.Label.UserLocalizedLabel.Label;
                }
            }

            return null; // Return null if no match is found
        }

        private List<InventoryData> GetInventoryData(IPluginExecutionContext context, string AgreementId, IOrganizationService service, ITracingService tracingService)
        {
            EntityCollection inventoryDataRecords = service.RetrieveMultiple(new FetchExpression(inventoryDataFetchXml.Replace("{agreementId}", AgreementId)));

            Logging.Log($"Before GetInventoryData {inventoryDataRecords.Entities.Count}", tracingService);

            // collect all package parent rate ids
            var packageRateIds = new List<string>();

            List<InventoryData> inventoryDatas = new List<InventoryData>();

            foreach (Entity entity in inventoryDataRecords.Entities)
            {
                InventoryData inventoryData = new InventoryData();

                inventoryData.ProductId = ((AliasedValue)entity.Attributes["Product.productid"]).Value.ToString();
                inventoryData.ProductName = ((AliasedValue)entity.Attributes["Product.name"]).Value.ToString();

                if (entity.Attributes.Contains("Product.description") && entity["Product.description"] != null)
                {
                    var descAliased = entity["Product.description"] as AliasedValue;
                    if (descAliased?.Value != null)
                    {
                        inventoryData.Description = descAliased.Value.ToString();
                    }
                    else
                    {
                        inventoryData.Description = string.Empty;
                    }
                }
                else
                {
                    inventoryData.Description = string.Empty;
                }

                EntityReference productFamilyRef = (EntityReference)((AliasedValue)entity.Attributes["Product.ats_productfamily"]).Value;
                inventoryData.ProductFamily = productFamilyRef.Name;
                EntityReference productSubFamilyRef = (EntityReference)((AliasedValue)entity.Attributes["Product.ats_productsubfamily"]).Value;
                inventoryData.ProductSubFamily = productSubFamilyRef.Name;
                EntityReference divisonRef = (EntityReference)((AliasedValue)entity.Attributes["Product.ats_division"]).Value;
                inventoryData.Division = divisonRef.Name;
                inventoryData.IsPassthroughCost = (bool)((AliasedValue)entity.Attributes["Product.ats_ispassthroughcost"]).Value;
                inventoryData.IsPackage = false;
                if (entity.Contains("Product.ats_ispackage"))
                    inventoryData.IsPackage = (bool)((AliasedValue)entity["Product.ats_ispackage"]).Value;

                if (entity.Attributes.Contains("Rate.ats_ratetype"))
                {
                    OptionSetValue rateTypeSetValue = (OptionSetValue)((AliasedValue)entity.Attributes["Rate.ats_ratetype"]).Value;
                    if (rateTypeSetValue != null)
                    {
                        inventoryData.RateType = GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateTypeSetValue.Value);
                    }
                }
                if (entity.Attributes.Contains("Rate.ats_price"))
                {
                    Money rateValue = (Money)((AliasedValue)entity.Attributes["Rate.ats_price"]).Value;
                    inventoryData.Rate = rateValue.Value;
                }
                if (entity.Attributes.Contains("Rate.ats_rateid"))
                {
                    inventoryData.RateId = ((AliasedValue)entity.Attributes["Rate.ats_rateid"]).Value.ToString();
                }
                if (entity.Attributes.Contains("Rate.ats_lockunitrate"))
                {
                    inventoryData.LockRate = (bool)((AliasedValue)entity.Attributes["Rate.ats_lockunitrate"]).Value;
                }
                if (entity.Attributes.Contains("Rate.ats_hardcost"))
                {
                    Money hardCostValue = (Money)((AliasedValue)entity.Attributes["Rate.ats_hardcost"]).Value;
                    inventoryData.HardCost = hardCostValue.Value;
                }
                if (entity.Attributes.Contains("Rate.ats_lockhardcost"))
                {
                    inventoryData.LockHardCost = (bool)((AliasedValue)entity.Attributes["Rate.ats_lockhardcost"]).Value;
                }
                if (entity.Attributes.Contains("Rate.ats_hardcost2"))
                {
                    Money productionCostValue = (Money)((AliasedValue)entity.Attributes["Rate.ats_hardcost2"]).Value;
                    inventoryData.ProductionCost = productionCostValue.Value;
                }
                if (entity.Attributes.Contains("Rate.ats_lockhardcost"))
                {
                    inventoryData.LockProductionCost = (bool)((AliasedValue)entity.Attributes["Rate.ats_lockhardcost"]).Value;
                }
                if (entity.Attributes.Contains("IBS.ats_quantityavailable"))
                {
                    inventoryData.QuantityAvailable = (int)((AliasedValue)entity.Attributes["IBS.ats_quantityavailable"]).Value;
                }
                if (entity.Attributes.Contains("IBS.ats_totalquantity"))
                {
                    //Sunny(30-01-25)
                    //Making the quantity units as always 1
                    //inventoryData.QtyUnits = (int)((AliasedValue)entity.Attributes["IBS.ats_totalquantity"]).Value;
                    inventoryData.QtyUnits = 1;
                }





                if (entity.Attributes.Contains("IBS.ats_totalquantityperevent"))
                {
                    //Sunny(30-01-25)
                    //Making the ats_totalquantityperevent  as always 1
                    //inventoryData.QtyEvents = (int)((AliasedValue)entity.Attributes["IBS.ats_totalquantityperevent"]).Value;

                    // if the Rate type is other than season, the show the QtyEvents as '1'; 
                    tracingService.Trace($"inventoryData.RateType: {inventoryData.RateType}");
                    if (inventoryData.RateType != "Season")
                    {
                        inventoryData.QtyEvents = 1;
                    }

                    //if the rate type is season, and the Eventt schedule is null, then also the QtyEvents= 1 ; 
                    if (inventoryData.RateType == "Season")
                    {
                        //validating the Event Schedule
                        if (entity.Attributes.Contains("IBS.ats_eventschedule"))
                        {
                            var aliasedEventSchedule = entity["IBS.ats_eventschedule"] as AliasedValue;
                            if (aliasedEventSchedule == null && aliasedEventSchedule.Value == null)
                            {
                                tracingService.Trace("ats_eventschedule is null inside AliasedValue.");
                                inventoryData.QtyEvents = 1;
                            }
                            else //If event schedule exist then the value of the QtyEvents would be the expected event quantity
                            {
                                tracingService.Trace("ats_eventschedule is not null inside AliasedValue.");
                                inventoryData.QtyEvents = (int)((AliasedValue)entity.Attributes["EventSched.ats_expectedeventquantity"]).Value;
                            }
                        }
                        else
                        {
                            tracingService.Trace("IBS.ats_eventschedule not found in entity attributes.");
                        }
                    }
                }

                //Sunny(8-1-26)
                //fixing the quantity and quantityEvents
                inventoryData.QtyUnits = inventoryData.QtyUnits == 0 ? 1 : inventoryData.QtyUnits;
                inventoryData.QtyEvents = inventoryData.QtyEvents == 0 ? 1 : inventoryData.QtyEvents;


                inventoryData.PackageComponents = new List<InventoryData>();

                inventoryDatas.Add(inventoryData);

                if (inventoryData.IsPackage && !string.IsNullOrEmpty(inventoryData.RateId))
                    packageRateIds.Add(inventoryData.RateId);
            }
            tracingService.Trace($"GetInventory - Created {inventoryDatas.Count} InventoryData records.");

            //Sunny(09-12-2025)--> handling if IBs is not found in the starting season of the Agreement
            #region handling if IBs is not found in the starting season of the Agreement
            var agreement = service.Retrieve("ats_agreement", new Guid(AgreementId), new ColumnSet("ats_startseason"));
            var seasonRef = agreement.GetAttributeValue<EntityReference>("ats_startseason");
            var seasonName = seasonRef?.Name ?? "Unknown Season";

            EntityCollection inventoryDataRecordsretrieval = service.RetrieveMultiple(new FetchExpression(inventoryDataFetchXml.Replace("{agreementId}", AgreementId)));
            Logging.Log($"Before GetInventoryData {inventoryDataRecordsretrieval.Entities.Count}", tracingService);

            if (inventoryDataRecords.Entities.Count == 0)
            {
                throw new InvalidPluginExecutionException($"IBS or Rate Record not found for Agreement Start Season: {seasonName}");
            }

            #endregion

            //Sunny(11-02-26)--> Handling the package product without package rate id
            if (packageRateIds.Count != 0)
            {

                var rateIdsXml = string.Join("", packageRateIds.Select(id => $"<value>{id}</value>"));
                tracingService.Trace($"GetInventory - rateIdsXml {rateIdsXml}");
                var fetchXmlComponents = componentDataFetchXml.Replace("{rateIdsXml}", rateIdsXml);
                EntityCollection componentRecords = service.RetrieveMultiple(new FetchExpression(fetchXmlComponents));

                tracingService.Trace($"GetInventory - Found {packageRateIds.Count} package parents.");
                tracingService.Trace($"GetInventory - Found {componentRecords.Entities.Count} component products.");
                var packageDict = inventoryDatas
                    .Where(i => i.IsPackage && !string.IsNullOrEmpty(i.RateId))
                    .GroupBy(i => i.RateId)
                    .ToDictionary(g => g.Key, g => g.First());

                var c = 0;

                foreach (var entity in componentRecords.Entities)
                {
                    c++;
                    if (!entity.Attributes.Contains("ats_packagerateid"))
                    {
                        tracingService.Trace($"Missing ats_packagerateid for component record {c}.");
                        continue;
                    }
                    var parentRateRef = entity.GetAttributeValue<EntityReference>("ats_packagerateid");
                    if (parentRateRef == null)
                    {
                        tracingService.Trace($"ats_packagerateid is null for component record {c}.");
                        continue;
                    }

                    var parentRateId = parentRateRef.Id.ToString().ToLowerInvariant();
                    //            tracingService.Trace($"Component {c} parentRateId = {parentRateId}");

                    if (!packageDict.TryGetValue(parentRateId, out var parent))
                    {
                        continue;
                    }

                    var rateType = string.Empty;
                    if (entity.Attributes.Contains("CompRate.ats_ratetype"))
                    {
                        var aliased = (AliasedValue)entity["CompRate.ats_ratetype"];
                        var rateTypeSetValue = aliased?.Value as OptionSetValue;

                        if (rateTypeSetValue != null)
                        {
                            rateType = GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateTypeSetValue.Value);
                        }
                    }
                    //            tracingService.Trace($"Component rate type {rateType}");
                    InventoryData component = new InventoryData();

                    component.ProductId = ((AliasedValue)entity.Attributes["CompProduct.productid"]).Value.ToString();
                    component.ProductName = ((AliasedValue)entity.Attributes["CompProduct.name"]).Value.ToString();
                    if (entity.Attributes.Contains("CompProduct.description") && entity["CompProduct.description"] != null)
                    {
                        var descAliased = entity["CompProduct.description"] as AliasedValue;
                        if (descAliased?.Value != null)
                        {
                            component.Description = descAliased.Value.ToString();
                        }
                        else
                        {
                            component.Description = string.Empty;
                        }
                    }
                    else
                    {
                        component.Description = string.Empty;
                    }
                    EntityReference productFamilyRef = (EntityReference)((AliasedValue)entity.Attributes["CompProduct.ats_productfamily"]).Value;
                    component.ProductFamily = productFamilyRef.Name;
                    EntityReference productSubFamilyRef = (EntityReference)((AliasedValue)entity.Attributes["CompProduct.ats_productsubfamily"]).Value;
                    component.ProductSubFamily = productSubFamilyRef.Name;
                    EntityReference divisonRef = (EntityReference)((AliasedValue)entity.Attributes["CompProduct.ats_division"]).Value;
                    component.Division = divisonRef.Name;
                    component.IsPassthroughCost = (bool)((AliasedValue)entity["CompProduct.ats_ispassthroughcost"]).Value;
                    component.IsPackage = false;
                    if (entity.Attributes.Contains("CompRate.ats_rateid"))
                    {
                        component.RateId = ((AliasedValue)entity.Attributes["CompRate.ats_rateid"]).Value.ToString();
                    }
                    component.RateType = rateType;
                    if (entity.Attributes.Contains("CompRate.ats_price"))
                    {
                        Money rateValue = (Money)((AliasedValue)entity.Attributes["CompRate.ats_price"]).Value;
                        component.Rate = rateValue.Value;
                    }
                    if (entity.Attributes.Contains("CompRate.ats_lockunitrate"))
                    {
                        component.LockRate = (bool)((AliasedValue)entity.Attributes["CompRate.ats_lockunitrate"]).Value;
                    }
                    if (entity.Attributes.Contains("CompRate.ats_hardcost"))
                    {
                        Money hardCostValue = (Money)((AliasedValue)entity.Attributes["CompRate.ats_hardcost"]).Value;
                        component.HardCost = hardCostValue.Value;
                    }
                    if (entity.Attributes.Contains("CompRate.ats_lockhardcost"))
                    {
                        component.LockHardCost = (bool)((AliasedValue)entity.Attributes["CompRate.ats_lockhardcost"]).Value;
                    }
                    if (entity.Attributes.Contains("CompRate.ats_hardcost2"))
                    {
                        Money productionCostValue = (Money)((AliasedValue)entity.Attributes["CompRate.ats_hardcost2"]).Value;
                        component.ProductionCost = productionCostValue.Value;
                    }
                    if (entity.Attributes.Contains("CompRate.ats_lockhardcost"))
                    {
                        component.LockProductionCost = (bool)((AliasedValue)entity.Attributes["CompRate.ats_lockhardcost"]).Value;
                    }

                    if (entity.Attributes.Contains("CompIBS.ats_quantityavailable"))
                    {
                        component.QuantityAvailable = (int)((AliasedValue)entity.Attributes["CompIBS.ats_quantityavailable"]).Value;
                    }

                    if (entity.Attributes.Contains("ats_quantity"))
                    {
                        component.QtyUnits = (int)entity.Attributes["ats_quantity"] == 0 ? 1 : (int)entity.Attributes["ats_quantity"];
                    }
                    else
                    {
                        component.QtyUnits = 1;
                    }
                    if (entity.Attributes.Contains("ats_quantityofevents"))
                    {
                        component.QtyEvents = (int)entity.Attributes["ats_quantityofevents"] == 0 ? 1 : (int)entity.Attributes["ats_quantityofevents"];
                    }
                    else
                    {
                        component.QtyEvents = 1;
                    }

                    component.PackageComponents = new List<InventoryData>();
                    parent.PackageComponents.Add(component);
                }


            }


            tracingService.Trace($"GetInventory - Found {inventoryDatas.Count} products.");
            return inventoryDatas;
        }

        private List<OpportunityData> GetOpportunityData(string AgreementId, IOrganizationService service, ITracingService tracingService)
        {
            EntityCollection oppDataRecords = service.RetrieveMultiple(new FetchExpression(opportunityDataFetchXml.Replace("{agreementId}", AgreementId)));

            Logging.Log($"Before Opportunity", tracingService);

            List<OpportunityData> opportunityDatas = new List<OpportunityData>();

            foreach (Entity entity in oppDataRecords.Entities)
            {

                OpportunityData opportunityData = new OpportunityData();

                opportunityData.Id = entity.Attributes["opportunityid"].ToString();


                if (entity.Attributes.Contains("ats_dealvalue"))
                {
                    Money dealValue = (Money)entity.Attributes["ats_dealvalue"];
                    opportunityData.DealValue = dealValue.Value;

                }

                if (entity.Attributes.Contains("budgetamount"))
                {
                    Money bugetAmountValue = (Money)entity.Attributes["budgetamount"];
                    opportunityData.AutomaticAmount = bugetAmountValue.Value;
                    opportunityData.TotalRateCard = bugetAmountValue.Value;
                }

                if (entity.Attributes.Contains("ats_manualamount"))
                {
                    Money manualAmountValue = (Money)entity.Attributes["ats_manualamount"];
                    opportunityData.ManualAmount = manualAmountValue.Value;

                }

                if (entity.Attributes.Contains("ats_pricingmode"))
                {
                    OptionSetValue pricingModeSetValue = entity.GetAttributeValue<OptionSetValue>("ats_pricingmode");

                    if (pricingModeSetValue != null)
                    {
                        opportunityData.PricingMode = GetOptionSetLabel(service, "opportunity", "ats_pricingmode", pricingModeSetValue.Value);

                    }
                }

                if (entity.Attributes.Contains("ats_totalhardcost"))
                {
                    Money totalHardCostValue = (Money)entity.Attributes["ats_totalhardcost"];
                    opportunityData.TotalHardCost = totalHardCostValue.Value;

                }

                if (entity.Attributes.Contains("ats_totalproductioncost"))
                {
                    Money totalProdCostValue = (Money)entity.Attributes["ats_totalproductioncost"];
                    opportunityData.TotalProductionCost = totalProdCostValue.Value;

                }

                /*if (entity.Attributes.Contains("ats_totalratecard"))
                {
                    Money totalRateCardValue = (Money)entity.Attributes["ats_totalratecard"];
                    opportunityData.TotalRateCard = totalRateCardValue.Value;

                }*/


                if (entity.Attributes.Contains("ats_percentofrate"))
                {
                    opportunityData.PercentOfRate = entity.Attributes["ats_percentofrate"] != null ? (decimal)entity.Attributes["ats_percentofrate"] : 0;

                }
                //Sunny(30-05-25)
                if (entity.Attributes.Contains("ats_percentofratecard"))
                {
                    opportunityData.PercentOfRateCard = entity.Attributes["ats_percentofratecard"] != null ? (decimal)entity.Attributes["ats_percentofratecard"] : 0;
                }
                //        tracingService.Trace($" opportunityData.PercentOfRateCard: {opportunityData.PercentOfRateCard}");

                //        tracingService.Trace($" opportunityData.PercentOfRateCard: {opportunityData.PercentOfRateCard}");
                if (entity.Attributes.Contains("ats_barteramount"))
                {
                    var rawBarter = entity["ats_barteramount"];
                    opportunityData.BarterAmount = rawBarter is Money moneyVal ? moneyVal.Value : rawBarter is decimal decVal ? decVal : (decimal?)null;

                    //            tracingService.Trace($"opportunityData.BarterAmount: {opportunityData.BarterAmount}");
                }

                if (entity.Attributes.Contains("ats_targetamount"))
                {
                    var rawTarget = entity["ats_targetamount"];
                    opportunityData.TargetAmount = rawTarget is Money moneyVal ? moneyVal.Value : rawTarget is decimal decVal ? decVal : (decimal?)null;

                    //            tracingService.Trace($"opportunityData.TargetAmount: {opportunityData.TargetAmount}");
                }

                if (entity.Attributes.Contains("ats_cashamount"))
                {
                    var rawCash = entity["ats_cashamount"];
                    opportunityData.CashAmount = rawCash is Money moneyVal ? moneyVal.Value : rawCash is decimal decVal ? decVal : (decimal?)null;

                    //            tracingService.Trace($"opportunityData.CashAmount: {opportunityData.CashAmount}");
                }




                //bool escalationType = (bool)entity.Attributes["ats_escalationtype"];
                //if (escalationType)
                //{
                //    opportunityData.EscalationType = GetOptionSetLabel(service, "opportunity", "ats_escalationtype", escalationTypeSetValue.Value);
                //    Logging.Log($"Escalation Type: {opportunityData.EscalationType}", tracingService);
                //}

                if (entity.Attributes.Contains("ats_escalationvalue"))
                {
                    //Sunny(18-03-25)
                    //updating the data Type of the escalation Value field
                    //opportunityData.EscalationValue = entity.Attributes["ats_escalationvalue"] != null ? (decimal)entity.Attributes["ats_escalationvalue"] : 0;
                    opportunityData.EscalationValue = entity.Attributes["ats_escalationvalue"] != null ? ((Money)entity.Attributes["ats_escalationvalue"]).Value : (0);
                }
                //Sunny(19-03-25)
                if (entity.Attributes.Contains("ats_escalationtype"))
                {
                    opportunityData.EscalationType = entity.FormattedValues.Contains("ats_escalationtype") ? entity.FormattedValues["ats_escalationtype"] : null;
                }


                if (entity.Attributes.Contains("ats_startseason"))
                {
                    opportunityData.SeasonName = entity.GetAttributeValue<EntityReference>("ats_startseason").Name;
                    //Adding the startSeason Id
                    opportunityData.StartSeason = (entity.GetAttributeValue<EntityReference>("ats_startseason").Id).ToString();

                }
                //        Logging.Log($"opportunityData: {opportunityData}", tracingService);
                opportunityDatas.Add(opportunityData);

            }

            if (opportunityDatas != null && opportunityDatas.Count > 0)
            {
                opportunityDatas = opportunityDatas
                .OrderBy(x => GetSeasonSortKey(x.SeasonName).StartYear)
                .ThenBy(x => GetSeasonSortKey(x.SeasonName).TypeRank)   // Transition first
                .ThenBy(x => GetSeasonSortKey(x.SeasonName).EndYear)    // Range after transition
                .ThenBy(x => x.SeasonName)
                .ToList();
            }

            return opportunityDatas;
        }

        private sealed class SeasonSortKey
        {
            public int StartYear { get; set; }
            public int TypeRank { get; set; } // 0 Transition, 1 Normal, 2 Unknown
            public int EndYear { get; set; }
        }

        private SeasonSortKey GetSeasonSortKey(string seasonName)
        {
            var key = new SeasonSortKey
            {
                StartYear = int.MaxValue,
                EndYear = int.MaxValue,
                TypeRank = 2
            };

            if (string.IsNullOrWhiteSpace(seasonName))
                return key;

            var s = seasonName.Trim();

            // Extract start year from the beginning, supports "2026" and "2027 - Transition" and "2027 - 2028"
            var startDigits = new string(s.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(startDigits, out int startYear))
                return key;

            key.StartYear = startYear;

            // Transition first
            if (s.IndexOf("transition", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                key.TypeRank = 0;
                key.EndYear = startYear; // keep it tight
                return key;
            }

            key.TypeRank = 1;

            // If it is a range like "2027 - 2028", try to extract end year
            // Get all digits groups, then use the second group as end year if present
            var groups = new List<string>();
            var current = new List<char>();

            foreach (var ch in s)
            {
                if (char.IsDigit(ch))
                {
                    current.Add(ch);
                }
                else
                {
                    if (current.Count > 0)
                    {
                        groups.Add(new string(current.ToArray()));
                        current.Clear();
                    }
                }
            }
            if (current.Count > 0)
                groups.Add(new string(current.ToArray()));

            if (groups.Count >= 2 && int.TryParse(groups[1], out int endYear))
                key.EndYear = endYear;
            else
                key.EndYear = startYear;

            return key;
        }

        //private List<LineItemData> GetLineItemData(string AgreementId, IOrganizationService service, ITracingService tracingService)
        //{
        //    List<LineItemData> lineItemDatas = new List<LineItemData>();

        //    EntityCollection agreementProducts = service.RetrieveMultiple(new FetchExpression(agreementProductFetchXml.Replace("{agreementId}", AgreementId)));

        //    Logging.Log($"Before LineItemData {agreementProducts.Entities.Count}", tracingService);

        //    var byOpId = agreementProducts.Entities
        //        .ToDictionary(
        //            e => ((AliasedValue)e["OppProd.opportunityproductid"]).Value.ToString(),
        //            e => e
        //        );
        //    // Calculate the grouping key for each item
        //    string GetGroupingKey(Entity e)
        //    {
        //        // If this item has a packagelineitem (component)
        //        var parentRef = e.GetAttributeValue<AliasedValue>("OppProd.ats_packagelineitem")?.Value as EntityReference;
        //        if (parentRef != null)
        //        {
        //            // Find the parent opportunityproduct row
        //            if (byOpId.TryGetValue(parentRef.Id.ToString(), out var parentEntity))
        //            {
        //                // Use the parent's AOP value
        //                return ((AliasedValue)parentEntity["OppProd.ats_agreementopportunityproduct"]).Value.ToString();
        //            }
        //        }
        //        // Not a component: use its own AOP value
        //        return ((AliasedValue)e["OppProd.ats_agreementopportunityproduct"]).Value?.ToString();
        //    }
        //    // Group the rows to get one record per product. These are the product lines that will be displayed
        //    // in the agreement UI. They are normal products or package parents
        //    var grouped = agreementProducts.Entities
        //        .GroupBy(GetGroupingKey)
        //        .Select(g =>
        //        {
        //            // Pick the parent OR the normal product
        //            var parentOrNormal = g.FirstOrDefault(e =>
        //            {
        //                var isPackage = e.GetAttributeValue<AliasedValue>("Product.ats_ispackage")?.Value as bool?;
        //                return isPackage == true || g.Count() == 1;
        //            });
        //            return parentOrNormal ?? g.First();
        //        })
        //        .OrderBy(e => e.GetAttributeValue<AliasedValue>("OppProd.ats_agreementopportunityproduct")?.Value)
        //        .ToList();

        //    Logging.Log($"Grouped count: {grouped.Count}", tracingService);
        //    foreach (Entity entity in grouped)
        //    {
        //        var prodId = entity.GetAttributeValue<AliasedValue>("Product.productid")?.Value?.ToString();
        //        var prodName = entity.GetAttributeValue<AliasedValue>("Product.name")?.Value?.ToString();
        //        var aop = entity.GetAttributeValue<AliasedValue>("OppProd.ats_agreementopportunityproduct")?.Value?.ToString();
        //        Logging.Log($"Grouped Entity: Product {prodName} ({prodId}), AOP {aop}", tracingService);
        //    }

        //    // Process each product line
        //    foreach (Entity entity in grouped)
        //    {
        //        var componentLookup = new Dictionary<string, LineItemData>();
        //        // Create an entry in the final result for this product
        //        // It is either a normal product or a package parent
        //        LineItemData parentLineItemData = new LineItemData
        //        {
        //            Product2 = new ProductData
        //            {
        //                Id = ((AliasedValue)entity.Attributes["Product.productid"]).Value.ToString(),
        //                Name = ((AliasedValue)entity.Attributes["Product.name"]).Value.ToString(),
        //                Division = ((EntityReference)((AliasedValue)entity.Attributes["Product.ats_division"]).Value).Name,
        //                ProductFamily = ((EntityReference)((AliasedValue)entity.Attributes["Product.ats_productfamily"]).Value).Name,
        //                ProductSubFamily = ((EntityReference)((AliasedValue)entity.Attributes["Product.ats_productsubfamily"]).Value).Name,
        //                IsPassthroughCost = (bool)((AliasedValue)entity.Attributes["Product.ats_ispassthroughcost"]).Value,
        //                IsPackage = (entity.GetAttributeValue<AliasedValue>("Product.ats_ispackage")?.Value as bool?) ?? false
        //            },
        //            rates = new List<RateData>(),
        //            items = new List<OpportunityLineItemData>(),
        //            PackageComponents = new List<LineItemData>(),
        //            IsPackage = (entity.GetAttributeValue<AliasedValue>("Product.ats_ispackage")?.Value as bool?) ?? false,
        //            IsPackageComponent = false
        //        };

        //        Logging.Log($"product data bind sucessfully", tracingService);

        //        string agreementOpportunityProductId = entity.Attributes.Contains("OppProd.ats_agreementopportunityproduct") ? ((AliasedValue)entity.Attributes["OppProd.ats_agreementopportunityproduct"]).Value.ToString() : string.Empty;
        //        string parentOliValues = "";
        //        string oliFilter;

        //        bool isPackage = (entity.GetAttributeValue<AliasedValue>("Product.ats_ispackage")?.Value as bool?) ?? false;

        //        Logging.Log($"agreementOpportunityProductId: {agreementOpportunityProductId}", tracingService);

        //        // Build a filter to use when retrieving OLIs for this product line
        //        // If the product is a normal product, it will filter on ats_agreementopportunityproduct, which
        //        // is the same for all OLIs for this product line across all the seasons in the agreement
        //        // If the product is a package parent, it will filter on both ats_agreementopportunityproduct and
        //        // ats_packagelineitem, to get both the parent OLI and all its component OLIs. The ats_packagelineitem
        //        // on the component OLIs will be the opportunityproductid of the parent OLI for each season, so
        //        // retrieve those first to build the filter
        //        if (isPackage)
        //        {
        //            var parentOliId = ((AliasedValue)entity["OppProd.opportunityproductid"]).Value.ToString();

        //            EntityCollection parentOliCollection = service.RetrieveMultiple(new FetchExpression(parentOliFetchXml
        //                                                            .Replace("{agreementOpportunityProductId}", agreementOpportunityProductId)));

        //            var parentOliIds = parentOliCollection.Entities
        //                .Select(e => e.Id.ToString())
        //                .ToList();

        //            parentOliValues = string.Join("", parentOliIds.Select(id => $"<value>{id}</value>"));

        //            oliFilter = $@"
        //                <filter type='or'>
        //                    <condition attribute='ats_agreementopportunityproduct' operator='eq' value='{agreementOpportunityProductId}' />
        //                    <condition attribute='ats_packagelineitem' operator='in'>
        //                        {parentOliValues}
        //                    </condition>
        //                </filter>";
        //        }
        //        else
        //        {
        //            oliFilter = $@"
        //                <filter>
        //                    <condition attribute='ats_agreementopportunityproduct' operator='eq' value='{agreementOpportunityProductId}' />
        //                </filter>";
        //        }
        //        Logging.Log($"oliFilter: {oliFilter}", tracingService);

        //        EntityCollection entityCollection = service.RetrieveMultiple(new FetchExpression(lineItemFetchXml
        //                                                .Replace("{agreementId}", AgreementId)
        //                                                .Replace("{oliFilter}", oliFilter)
        //                                                ));

        //        // Opportunity line item records should be sorted by season, then within each season, records with packagelineitem
        //        // ids of null should come before non-null packagelineitem ids
        //        // This ensures that a package parent item is processed before its components

        //        //Sunny(13-feb-26)--> Ordering the entities based on the correct season . 
        //        //var orderedEntities = entityCollection.Entities
        //        //    .OrderBy(e => e.GetAttributeValue<AliasedValue>("Season.ats_name")?.Value?.ToString())
        //        //    .ThenBy(e => e.GetAttributeValue<AliasedValue>("OppProd.ats_packagelineitem")?.Value == null ? 0 : 1)
        //        //    .ToList();

        //        var orderedEntities = entityCollection.Entities
        //                                .OrderBy(e =>
        //                                {
        //                                    var seasonName = e.GetAttributeValue<AliasedValue>("Season.ats_name")?.Value?.ToString();
        //                                    return GetSeasonSortKey(seasonName).StartYear;
        //                                })
        //                                .ThenBy(e =>
        //                                {
        //                                    var seasonName = e.GetAttributeValue<AliasedValue>("Season.ats_name")?.Value?.ToString();
        //                                    return GetSeasonSortKey(seasonName).TypeRank;   // Transition first
        //                                })
        //                                .ThenBy(e =>
        //                                {
        //                                    var seasonName = e.GetAttributeValue<AliasedValue>("Season.ats_name")?.Value?.ToString();
        //                                    return GetSeasonSortKey(seasonName).EndYear;
        //                                })
        //                                .ThenBy(e =>
        //                                    e.GetAttributeValue<AliasedValue>("Season.ats_name")?.Value?.ToString() ?? string.Empty
        //                                )
        //                                .ThenBy(e =>
        //                                    e.GetAttributeValue<AliasedValue>("OppProd.ats_packagelineitem")?.Value == null ? 0 : 1
        //                                )
        //                                .ToList();




        //        //Sunny 
        //        //check the collection count == 1 
        //        Logging.Log($"entityCollection.Entities.count: {entityCollection.Entities.Count}", tracingService);
        //        Logging.Log($"orderedEntities.Entities.count: {orderedEntities.Count}", tracingService);

        //        // Process the OLIs for this grouped product line
        //        foreach (Entity oppEntity in orderedEntities)
        //        {
        //            // Build rate data
        //            #region Bind Rate Data
        //            RateData rateData = new RateData();

        //            OptionSetValue rateType = (OptionSetValue)((AliasedValue)oppEntity.Attributes["Rate.ats_ratetype"]).Value;
        //            rateData.RateType = GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateType.Value);
        //            rateData.Rate = ((Money)((AliasedValue)oppEntity.Attributes["Rate.ats_price"]).Value).Value;
        //            rateData.HardCost = ((Money)((AliasedValue)oppEntity.Attributes["Rate.ats_hardcost"]).Value).Value;
        //            rateData.ProductionCost = oppEntity.Attributes.Contains("Rate.ats_hardcost2") ?
        //                    ((Money)((AliasedValue)oppEntity.Attributes["Rate.ats_hardcost2"]).Value).Value : 0;
        //            //rateData.ProductionCost = 0;

        //            rateData.LockHardCost = (bool)((AliasedValue)oppEntity.Attributes["Rate.ats_lockhardcost"]).Value;
        //            rateData.LockRate = (bool)((AliasedValue)oppEntity.Attributes["Rate.ats_lockunitrate"]).Value;
        //            rateData.LockProductionCost = entity.Attributes.Contains("Rate.ats_lockproductioncost") ?
        //                (bool)((AliasedValue)oppEntity.Attributes["Rate.ats_lockproductioncost"]).Value : false;
        //            EntityReference seasonRef = (EntityReference)((AliasedValue)oppEntity.Attributes["IBS.ats_season"]).Value;
        //            rateData.Season = seasonRef.Id.ToString();
        //            rateData.SeasonName = seasonRef.Name;
        //            rateData.UnlimitedQuantity = (bool)((AliasedValue)oppEntity.Attributes["IBS.ats_unlimitedquantity"]).Value;
        //            rateData.Product = ((AliasedValue)entity.Attributes["Product.productid"]).Value.ToString();
        //            Logging.Log($"rateData defined successfully", tracingService);
        //            #endregion

        //            var legalDefOppProd = oppEntity.Attributes.Contains("OppProd.ats_legaldefinition") ? ((AliasedValue)oppEntity.Attributes["OppProd.ats_legaldefinition"]).Value?.ToString() : null;
        //            var legalDefIBS = oppEntity.Attributes.Contains("IBS.ats_legaldefinition") ? ((AliasedValue)oppEntity.Attributes["IBS.ats_legaldefinition"]).Value?.ToString() : null;
        //            var legalDefProduct = oppEntity.Attributes.Contains("Product.ats_legaldefinition") ? ((AliasedValue)oppEntity.Attributes["Product.ats_legaldefinition"]).Value?.ToString() : null;
        //            var description = oppEntity.Attributes.Contains("OppProd.description") ? ((AliasedValue)oppEntity.Attributes["OppProd.description"]).Value?.ToString() : null;
        //            OptionSetValue stateCode = (OptionSetValue)((AliasedValue)oppEntity.Attributes["Rate.statecode"]).Value;

        //            // Build opportunity line item data
        //            OpportunityLineItemData opportunityLineItemData = new OpportunityLineItemData
        //            {
        //                Id = ((AliasedValue)oppEntity.Attributes["OppProd.opportunityproductid"]).Value.ToString(),
        //                Opportunity = ((EntityReference)((AliasedValue)oppEntity.Attributes["OppProd.opportunityid"]).Value).Id.ToString(),
        //                TotalValue = oppEntity.Attributes.Contains("OppProd.ats_adjustedtotalprice") ?
        //                                ((Money)((AliasedValue)oppEntity.Attributes["OppProd.ats_adjustedtotalprice"]).Value).Value : 0,
        //                QtyEvents = oppEntity.Attributes.Contains("OppProd.ats_quantityofevents") ?
        //                                (int)((AliasedValue)oppEntity.Attributes["OppProd.ats_quantityofevents"]).Value : 0,
        //                QtyUnits = oppEntity.Attributes.Contains("OppProd.ats_quantity") ?
        //                                (int)((AliasedValue)oppEntity.Attributes["OppProd.ats_quantity"]).Value : 0,

        //                HardCost = oppEntity.Attributes.Contains("OppProd.ats_hardcost") ?
        //                                ((Money)((AliasedValue)oppEntity.Attributes["OppProd.ats_hardcost"]).Value).Value : 0,
        //                TotalHardCost = oppEntity.Attributes.Contains("OppProd.ats_totalratevalue") ?
        //                                ((Money)((AliasedValue)oppEntity.Attributes["OppProd.ats_totalratevalue"]).Value).Value : 0,
        //                IsManualPriceOverride = oppEntity.Contains("OppProd.ats_manualpriceoverride") ?
        //                                ((bool)((AliasedValue)oppEntity["OppProd.ats_manualpriceoverride"]).Value) : false,
        //                ProductionCost = oppEntity.Attributes.Contains("OppProd.ats_hardcost2") ?
        //                                ((Money)((AliasedValue)oppEntity.Attributes["OppProd.ats_hardcost2"]).Value).Value : 0,
        //                TotalProductionCost = oppEntity.Attributes.Contains("OppProd.ats_totalproductioncost") ?
        //                                ((Money)((AliasedValue)oppEntity.Attributes["OppProd.ats_totalproductioncost"]).Value).Value : 0,
        //                LockProductionCost = oppEntity.Attributes.Contains("Rate.ats_lockproductioncost") ?
        //                                (bool)((AliasedValue)oppEntity.Attributes["Rate.ats_lockproductioncost"]).Value : false,
        //                Product2 = ((AliasedValue)oppEntity.Attributes["Product.productid"]).Value.ToString(),
        //                Rate = oppEntity.Attributes.Contains("OppProd.ats_sellingrate") ?
        //                            ((Money)((AliasedValue)oppEntity.Attributes["OppProd.ats_sellingrate"]).Value).Value : 0,
        //                QuantityAvailable = oppEntity.Attributes.Contains("IBS.ats_quantityavailable") ?
        //                            (int)((AliasedValue)oppEntity.Attributes["IBS.ats_quantityavailable"]).Value : 0,
        //                QuantityTotal = oppEntity.Attributes.Contains("IBS.ats_totalquantity") ?
        //                            (int)((AliasedValue)oppEntity.Attributes["IBS.ats_totalquantity"]).Value : 0,
        //                QuantitySold = oppEntity.Attributes.Contains("IBS.ats_quantitysold") ?
        //                            (int)((AliasedValue)oppEntity.Attributes["IBS.ats_quantitysold"]).Value : 0,
        //                QuantityPitched = oppEntity.Attributes.Contains("IBS.ats_quantitypitched") ?
        //                            (int)((AliasedValue)oppEntity.Attributes["IBS.ats_quantitypitched"]).Value : 0,
        //                NotAvailable = oppEntity.Attributes.Contains("IBS.ats_notavailable") ?
        //                            (bool)((AliasedValue)oppEntity.Attributes["IBS.ats_notavailable"]).Value : false,
        //                RateType = GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateType.Value),
        //                IsActive = stateCode.Value == 0 ? true : false,
        //                LockHardCost = oppEntity.Attributes.Contains("Rate.ats_lockhardcost") ?
        //                            (bool)((AliasedValue)oppEntity.Attributes["Rate.ats_lockhardcost"]).Value : false,
        //                LockRate = oppEntity.Attributes.Contains("Rate.ats_lockunitrate") ?
        //                            (bool)((AliasedValue)oppEntity.Attributes["Rate.ats_lockunitrate"]).Value : false,
        //                OverwriteLegalDefinition = oppEntity.Attributes.Contains("OppProd.ats_overwritelegaldefinition") ? (bool)((AliasedValue)oppEntity.Attributes["OppProd.ats_overwritelegaldefinition"]).Value : false,
        //                LegalDefinition = legalDefOppProd,
        //                LegalDefinitionInventoryBySeason = legalDefIBS,
        //                LegalDefinitionProduct = legalDefProduct,
        //                Description = description
        //            };

        //            //opportunityLineItemData.QtyUnits = opportunityLineItemData.QtyUnits == 0 ? 1 : opportunityLineItemData.QtyUnits; //Sunny 0==>1
        //            //opportunityLineItemData.QtyEvents = opportunityLineItemData.QtyEvents == 0 ? 1 : opportunityLineItemData.QtyEvents; //Sunny 0==>1



        //            Logging.Log($"opportunityLineItemData defined successfully", tracingService);

        //            // Determine the package line item id (if any)
        //            EntityReference pkgRef = null;
        //            if (oppEntity.Attributes.TryGetValue("OppProd.ats_packagelineitem", out var rawPkg))
        //            {
        //                if (rawPkg is AliasedValue aliased && aliased.Value is EntityReference er)
        //                {
        //                    pkgRef = er;
        //                    tracingService.Trace($"OppProd: {opportunityLineItemData.Id}, and the package line item : {pkgRef}");
        //                }
        //            }
        //            tracingService.Trace($"pkgRef?.Id.ToString(): {pkgRef?.Id.ToString()}");
        //            //bool isPackageLineItemId = false;
        //            //if (pkgRef.Id != null)
        //            //{
        //            //    tracingService.Trace("pkgref!=null");
        //            //    opportunityLineItemData.PackageLineItemId = pkgRef?.Id.ToString();
        //            //    Logging.Log($"opportunityLineItemData.PackageLineItemId: {opportunityLineItemData.PackageLineItemId}", tracingService);
        //            //    isPackageLineItemId = true;
        //            //}
        //            bool isPackageLineItemId = false;

        //            if (pkgRef != null && pkgRef.Id != Guid.Empty)
        //            {
        //                tracingService.Trace("pkgRef != null");
        //                opportunityLineItemData.PackageLineItemId = pkgRef.Id.ToString();
        //                Logging.Log(
        //                    $"opportunityLineItemData.PackageLineItemId: {opportunityLineItemData.PackageLineItemId}",
        //                    tracingService);
        //                isPackageLineItemId = true;
        //            }


        //            // If package line item is empty, this is a package parent or normal product so
        //            // use the parentLineItemData created earlier and add the new rateData and opportunityLineItemData
        //            // to the rates and items arrays on that record
        //            //if (string.IsNullOrEmpty(opportunityLineItemData.PackageLineItemId))
        //            //{
        //            if (!isPackageLineItemId)
        //            {
        //                tracingService.Trace($"package line item is empty, this is a package parent or normal product");
        //                parentLineItemData.items.Add(opportunityLineItemData);
        //                parentLineItemData.rates.Add(rateData);
        //                tracingService.Trace("Added successfully");
        //            }
        //            // Otherwise, this is a package component, so find or create the componentLineItemData entry 
        //            // Check the component lookup dictionary first to see if this component has already been added
        //            else
        //            {
        //                string componentProductId = ((AliasedValue)oppEntity.Attributes["Product.productid"]).Value.ToString();
        //                string componentAgreementOpportunityProduct = ((AliasedValue)oppEntity.Attributes["OppProd.ats_agreementopportunityproduct"]).Value.ToString();
        //                string componentKey = componentProductId + '|' + componentAgreementOpportunityProduct;
        //                Logging.Log($"componentKey: {componentKey}", tracingService);
        //                LineItemData componentLineItemData;
        //                // Component has not been added to this parent yet, so create a componentLineItemData record
        //                // Use this as the componentLineItemData
        //                if (!componentLookup.TryGetValue(componentKey, out componentLineItemData))
        //                {
        //                    // Create a new component entry for this product
        //                    componentLineItemData = new LineItemData
        //                    {
        //                        Product2 = new ProductData
        //                        {
        //                            Id = ((AliasedValue)oppEntity.Attributes["Product.productid"]).Value.ToString(),
        //                            Name = ((AliasedValue)oppEntity.Attributes["Product.name"]).Value.ToString(),
        //                            Division = ((EntityReference)((AliasedValue)oppEntity.Attributes["Product.ats_division"]).Value).Name,
        //                            ProductFamily = ((EntityReference)((AliasedValue)oppEntity.Attributes["Product.ats_productfamily"]).Value).Name,
        //                            ProductSubFamily = ((EntityReference)((AliasedValue)oppEntity.Attributes["Product.ats_productsubfamily"]).Value).Name,
        //                            IsPassthroughCost = (bool)((AliasedValue)oppEntity.Attributes["Product.ats_ispassthroughcost"]).Value,
        //                            IsPackage = false
        //                        },
        //                        rates = new List<RateData>(),
        //                        items = new List<OpportunityLineItemData>(),
        //                        PackageComponents = new List<LineItemData>(),
        //                        IsPackage = false,
        //                        IsPackageComponent = true
        //                    };
        //                    // Add this component entry to the component lookup dictionary and
        //                    // to the parentLineItemData's PackageComponents array
        //                    componentLookup[componentKey] = componentLineItemData;
        //                    parentLineItemData.PackageComponents.Add(componentLineItemData);
        //                }
        //                tracingService.Trace($"Added the component entry to the component lookup dictonary");
        //                // Add the rate and opportunity line item data for current OLI being processed to the
        //                // componentLineItemData record
        //                componentLineItemData.items.Add(opportunityLineItemData);
        //                tracingService.Trace($"Added the component entry to the component lookup dictonary");

        //                componentLineItemData.rates.Add(rateData);
        //                tracingService.Trace($"Added the component entry to the component lookup dictonary");

        //            }
        //        }
        //        // Add the parentLineItemData (which may now include componentLineItemData entries)
        //        // to the final result array
        //        lineItemDatas.Add(parentLineItemData);
        //        tracingService.Trace($"parentLineItemData added in lineItemDatas");

        //    }
        //    tracingService.Trace("returning from the function");
        //    return lineItemDatas;
        //}

        private List<LineItemData> GetLineItemData(string AgreementId, IOrganizationService service, ITracingService tracingService)
        {
            List<LineItemData> lineItemDatas = new List<LineItemData>();

            EntityCollection agreementProducts = service.RetrieveMultiple(
                new FetchExpression(agreementProductFetchXml.Replace("{agreementId}", AgreementId)));

            Logging.Log($"Before LineItemData {agreementProducts.Entities.Count}", tracingService);

            var byOpId = agreementProducts.Entities
                .Where(e => !string.IsNullOrWhiteSpace(GetAliasedString(e, "OppProd.opportunityproductid")))
                .GroupBy(e => GetAliasedString(e, "OppProd.opportunityproductid"))
                .ToDictionary(g => g.Key, g => g.First());

            string GetGroupingKey(Entity e)
            {
                var parentRef = GetAliasedEntityReference(e, "OppProd.ats_packagelineitem");
                if (parentRef != null)
                {
                    if (byOpId.TryGetValue(parentRef.Id.ToString(), out var parentEntity))
                    {
                        var parentAop = GetAliasedString(parentEntity, "OppProd.ats_agreementopportunityproduct");
                        if (!string.IsNullOrWhiteSpace(parentAop))
                            return parentAop;
                    }
                }

                return GetAliasedString(e, "OppProd.ats_agreementopportunityproduct");
            }

            var grouped = agreementProducts.Entities
                .GroupBy(GetGroupingKey)
                .Select(g =>
                {
                    var parentOrNormal = g.FirstOrDefault(e =>
                    {
                        var isPackage = GetAliasedValueOrDefault<bool?>(e, "Product.ats_ispackage", false);
                        return isPackage == true || g.Count() == 1;
                    });

                    return parentOrNormal ?? g.First();
                })
                .OrderBy(e => GetAliasedString(e, "OppProd.ats_agreementopportunityproduct"))
                .ToList();

            Logging.Log($"Grouped count: {grouped.Count}", tracingService);
            foreach (Entity entity in grouped)
            {
                var prodId = GetAliasedString(entity, "Product.productid");
                var prodName = GetAliasedString(entity, "Product.name");
                var aop = GetAliasedString(entity, "OppProd.ats_agreementopportunityproduct");
                Logging.Log($"Grouped Entity: Product {prodName} ({prodId}), AOP {aop}", tracingService);
            }

            foreach (Entity entity in grouped)
            {
                var productId = GetAliasedString(entity, "Product.productid");
                var productName = GetAliasedString(entity, "Product.name");
                var agreementOpportunityProductId = GetAliasedString(entity, "OppProd.ats_agreementopportunityproduct");

                if (string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(agreementOpportunityProductId))
                {
                    tracingService.Trace("Skipping grouped product because Product.productid or OppProd.ats_agreementopportunityproduct is missing.");
                    continue;
                }

                var componentLookup = new Dictionary<string, LineItemData>();

                LineItemData parentLineItemData = new LineItemData
                {
                    Product2 = new ProductData
                    {
                        Id = productId,
                        Name = productName,
                        Division = GetAliasedEntityReference(entity, "Product.ats_division")?.Name ?? string.Empty,
                        ProductFamily = GetAliasedEntityReference(entity, "Product.ats_productfamily")?.Name ?? string.Empty,
                        ProductSubFamily = GetAliasedEntityReference(entity, "Product.ats_productsubfamily")?.Name ?? string.Empty,
                        IsPassthroughCost = GetAliasedBoolValue(entity, "Product.ats_ispassthroughcost"),
                        IsPackage = GetAliasedBoolValue(entity, "Product.ats_ispackage")
                    },
                    rates = new List<RateData>(),
                    items = new List<OpportunityLineItemData>(),
                    PackageComponents = new List<LineItemData>(),
                    IsPackage = GetAliasedBoolValue(entity, "Product.ats_ispackage"),
                    IsPackageComponent = false
                };

                Logging.Log($"product data bind sucessfully", tracingService);

                string parentOliValues = "";
                string oliFilter;

                bool isPackage = GetAliasedBoolValue(entity, "Product.ats_ispackage");

                Logging.Log($"agreementOpportunityProductId: {agreementOpportunityProductId}", tracingService);

                if (isPackage)
                {
                    var parentOliId = GetAliasedString(entity, "OppProd.opportunityproductid");

                    EntityCollection parentOliCollection = service.RetrieveMultiple(
                        new FetchExpression(parentOliFetchXml.Replace("{agreementOpportunityProductId}", agreementOpportunityProductId)));

                    var parentOliIds = parentOliCollection.Entities
                        .Select(e => e.Id.ToString())
                        .ToList();

                    parentOliValues = string.Join("", parentOliIds.Select(id => $"<value>{id}</value>"));

                    oliFilter = $@"
                <filter type='or'>
                    <condition attribute='ats_agreementopportunityproduct' operator='eq' value='{agreementOpportunityProductId}' />
                    <condition attribute='ats_packagelineitem' operator='in'>
                        {parentOliValues}
                    </condition>
                </filter>";
                }
                else
                {
                    oliFilter = $@"
                <filter>
                    <condition attribute='ats_agreementopportunityproduct' operator='eq' value='{agreementOpportunityProductId}' />
                </filter>";
                }

                Logging.Log($"oliFilter: {oliFilter}", tracingService);

                EntityCollection entityCollection = service.RetrieveMultiple(
                    new FetchExpression(lineItemFetchXml
                        .Replace("{agreementId}", AgreementId)
                        .Replace("{oliFilter}", oliFilter)));

                var orderedEntities = entityCollection.Entities
                    .OrderBy(e =>
                    {
                        var seasonName = GetAliasedString(e, "Season.ats_name");
                        return GetSeasonSortKey(seasonName).StartYear;
                    })
                    .ThenBy(e =>
                    {
                        var seasonName = GetAliasedString(e, "Season.ats_name");
                        return GetSeasonSortKey(seasonName).TypeRank;
                    })
                    .ThenBy(e =>
                    {
                        var seasonName = GetAliasedString(e, "Season.ats_name");
                        return GetSeasonSortKey(seasonName).EndYear;
                    })
                    .ThenBy(e => GetAliasedString(e, "Season.ats_name"))
                    .ThenBy(e => GetAliasedEntityReference(e, "OppProd.ats_packagelineitem") == null ? 0 : 1)
                    .ToList();

                Logging.Log($"entityCollection.Entities.count: {entityCollection.Entities.Count}", tracingService);
                Logging.Log($"orderedEntities.Entities.count: {orderedEntities.Count}", tracingService);

                foreach (Entity oppEntity in orderedEntities)
                {
                    try
                    {
                        var oppProdId = GetAliasedString(oppEntity, "OppProd.opportunityproductid");
                        var oppIdRef = GetAliasedEntityReference(oppEntity, "OppProd.opportunityid");
                        var oppProductId = GetAliasedString(oppEntity, "Product.productid");

                        if (string.IsNullOrWhiteSpace(oppProdId) || oppIdRef == null || string.IsNullOrWhiteSpace(oppProductId))
                        {
                            tracingService.Trace("Skipping oppEntity because mandatory aliased values are missing.");
                            continue;
                        }

                        #region Bind Rate Data
                        RateData rateData = new RateData();

                        OptionSetValue rateType = GetAliasedOptionSetValue(oppEntity, "Rate.ats_ratetype");
                        rateData.RateType = rateType != null
                            ? GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateType.Value)
                            : null;

                        rateData.Rate = GetAliasedMoneyValue(oppEntity, "Rate.ats_price");
                        rateData.HardCost = GetAliasedMoneyValue(oppEntity, "Rate.ats_hardcost");
                        rateData.ProductionCost = GetAliasedMoneyValue(oppEntity, "Rate.ats_hardcost2");
                        rateData.LockHardCost = GetAliasedBoolValue(oppEntity, "Rate.ats_lockhardcost");
                        rateData.LockRate = GetAliasedBoolValue(oppEntity, "Rate.ats_lockunitrate");
                        rateData.LockProductionCost = GetAliasedBoolValue(oppEntity, "Rate.ats_lockproductioncost");

                        EntityReference seasonRef = GetAliasedEntityReference(oppEntity, "IBS.ats_season");
                        rateData.Season = seasonRef?.Id.ToString() ?? string.Empty;
                        rateData.SeasonName = seasonRef?.Name ?? string.Empty;
                        rateData.UnlimitedQuantity = GetAliasedBoolValue(oppEntity, "IBS.ats_unlimitedquantity");
                        rateData.Product = productId;

                        Logging.Log($"rateData defined successfully", tracingService);
                        #endregion

                        var legalDefOppProd = GetAliasedString(oppEntity, "OppProd.ats_legaldefinition", null);
                        var legalDefIBS = GetAliasedString(oppEntity, "IBS.ats_legaldefinition", null);
                        var legalDefProduct = GetAliasedString(oppEntity, "Product.ats_legaldefinition", null);
                        var description = GetAliasedString(oppEntity, "OppProd.description", null);
                        OptionSetValue stateCode = GetAliasedOptionSetValue(oppEntity, "Rate.statecode");

                        OpportunityLineItemData opportunityLineItemData = new OpportunityLineItemData
                        {
                            Id = oppProdId,
                            Opportunity = oppIdRef.Id.ToString(),
                            TotalValue = GetAliasedMoneyValue(oppEntity, "OppProd.ats_adjustedtotalprice"),
                            QtyEvents = GetAliasedIntValue(oppEntity, "OppProd.ats_quantityofevents"),
                            QtyUnits = GetAliasedIntValue(oppEntity, "OppProd.ats_quantity"),
                            HardCost = GetAliasedMoneyValue(oppEntity, "OppProd.ats_hardcost"),
                            TotalHardCost = GetAliasedMoneyValue(oppEntity, "OppProd.ats_totalratevalue"),
                            IsManualPriceOverride = GetAliasedBoolValue(oppEntity, "OppProd.ats_manualpriceoverride"),
                            ProductionCost = GetAliasedMoneyValue(oppEntity, "OppProd.ats_hardcost2"),
                            TotalProductionCost = GetAliasedMoneyValue(oppEntity, "OppProd.ats_totalproductioncost"),
                            LockProductionCost = GetAliasedBoolValue(oppEntity, "Rate.ats_lockproductioncost"),
                            Product2 = oppProductId,
                            Rate = GetAliasedMoneyValue(oppEntity, "OppProd.ats_sellingrate"),
                            QuantityAvailable = GetAliasedIntValue(oppEntity, "IBS.ats_quantityavailable"),
                            QuantityTotal = GetAliasedIntValue(oppEntity, "IBS.ats_totalquantity"),
                            QuantitySold = GetAliasedIntValue(oppEntity, "IBS.ats_quantitysold"),
                            QuantityPitched = GetAliasedIntValue(oppEntity, "IBS.ats_quantitypitched"),
                            NotAvailable = GetAliasedBoolValue(oppEntity, "IBS.ats_notavailable"),
                            RateType = rateType != null ? GetOptionSetLabel(service, "ats_rate", "ats_ratetype", rateType.Value) : null,
                            IsActive = stateCode != null && stateCode.Value == 0,
                            LockHardCost = GetAliasedBoolValue(oppEntity, "Rate.ats_lockhardcost"),
                            LockRate = GetAliasedBoolValue(oppEntity, "Rate.ats_lockunitrate"),
                            OverwriteLegalDefinition = GetAliasedBoolValue(oppEntity, "OppProd.ats_overwritelegaldefinition"),
                            LegalDefinition = legalDefOppProd,
                            LegalDefinitionInventoryBySeason = legalDefIBS,
                            LegalDefinitionProduct = legalDefProduct,
                            Description = description
                        };

                        Logging.Log($"opportunityLineItemData defined successfully", tracingService);

                        EntityReference pkgRef = null;
                        if (oppEntity.Attributes.TryGetValue("OppProd.ats_packagelineitem", out var rawPkg))
                        {
                            if (rawPkg is AliasedValue aliased && aliased.Value is EntityReference er)
                            {
                                pkgRef = er;
                                tracingService.Trace($"OppProd: {opportunityLineItemData.Id}, and the package line item : {pkgRef}");
                            }
                        }

                        tracingService.Trace($"pkgRef?.Id.ToString(): {pkgRef?.Id.ToString()}");

                        bool isPackageLineItemId = false;

                        if (pkgRef != null && pkgRef.Id != Guid.Empty)
                        {
                            tracingService.Trace("pkgRef != null");
                            opportunityLineItemData.PackageLineItemId = pkgRef.Id.ToString();
                            Logging.Log(
                                $"opportunityLineItemData.PackageLineItemId: {opportunityLineItemData.PackageLineItemId}",
                                tracingService);
                            isPackageLineItemId = true;
                        }

                        if (!isPackageLineItemId)
                        {
                            tracingService.Trace($"package line item is empty, this is a package parent or normal product");
                            parentLineItemData.items.Add(opportunityLineItemData);
                            parentLineItemData.rates.Add(rateData);
                            tracingService.Trace("Added successfully");
                        }
                        else
                        {
                            string componentProductId = GetAliasedString(oppEntity, "Product.productid");
                            string componentAgreementOpportunityProduct = GetAliasedString(oppEntity, "OppProd.ats_agreementopportunityproduct");

                            if (string.IsNullOrWhiteSpace(componentProductId) || string.IsNullOrWhiteSpace(componentAgreementOpportunityProduct))
                            {
                                tracingService.Trace("Skipping package component because component key values are missing.");
                                continue;
                            }

                            string componentKey = componentProductId + "|" + componentAgreementOpportunityProduct;
                            Logging.Log($"componentKey: {componentKey}", tracingService);

                            LineItemData componentLineItemData;

                            if (!componentLookup.TryGetValue(componentKey, out componentLineItemData))
                            {
                                componentLineItemData = new LineItemData
                                {
                                    Product2 = new ProductData
                                    {
                                        Id = componentProductId,
                                        Name = GetAliasedString(oppEntity, "Product.name"),
                                        Division = GetAliasedEntityReference(oppEntity, "Product.ats_division")?.Name ?? string.Empty,
                                        ProductFamily = GetAliasedEntityReference(oppEntity, "Product.ats_productfamily")?.Name ?? string.Empty,
                                        ProductSubFamily = GetAliasedEntityReference(oppEntity, "Product.ats_productsubfamily")?.Name ?? string.Empty,
                                        IsPassthroughCost = GetAliasedBoolValue(oppEntity, "Product.ats_ispassthroughcost"),
                                        IsPackage = false
                                    },
                                    rates = new List<RateData>(),
                                    items = new List<OpportunityLineItemData>(),
                                    PackageComponents = new List<LineItemData>(),
                                    IsPackage = false,
                                    IsPackageComponent = true
                                };

                                componentLookup[componentKey] = componentLineItemData;
                                parentLineItemData.PackageComponents.Add(componentLineItemData);
                            }

                            tracingService.Trace($"Added the component entry to the component lookup dictonary");

                            componentLineItemData.items.Add(opportunityLineItemData);
                            tracingService.Trace($"Added the component entry to the component lookup dictonary");

                            componentLineItemData.rates.Add(rateData);
                            tracingService.Trace($"Added the component entry to the component lookup dictonary");
                        }
                    }
                    catch (Exception ex)
                    {
                        tracingService.Trace($"Error processing line item row. OppProdId: {GetAliasedString(oppEntity, "OppProd.opportunityproductid")}. Exception: {ex.Message}");
                        throw;
                    }
                }

                lineItemDatas.Add(parentLineItemData);
                tracingService.Trace($"parentLineItemData added in lineItemDatas");
            }

            tracingService.Trace("returning from the function");
            return lineItemDatas;
        }



        private T GetAliasedValueOrDefault<T>(Entity entity, string attributeName, T defaultValue = default(T))
        {
            if (entity == null || string.IsNullOrWhiteSpace(attributeName))
                return defaultValue;

            if (!entity.Attributes.Contains(attributeName))
                return defaultValue;

            var aliasedValue = entity.Attributes[attributeName] as AliasedValue;
            if (aliasedValue == null || aliasedValue.Value == null)
                return defaultValue;

            if (aliasedValue.Value is T typedValue)
                return typedValue;

            try
            {
                return (T)Convert.ChangeType(aliasedValue.Value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        private string GetAliasedString(Entity entity, string attributeName, string defaultValue = "")
        {
            var value = GetAliasedValueOrDefault<object>(entity, attributeName, null);
            return value != null ? value.ToString() : defaultValue;
        }

        private bool GetAliasedBoolValue(Entity entity, string attributeName, bool defaultValue = false)
        {
            var value = GetAliasedValueOrDefault<object>(entity, attributeName, null);

            if (value == null)
                return defaultValue;

            if (value is bool boolValue)
                return boolValue;

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) ? parsed : defaultValue;
        }

        private int GetAliasedIntValue(Entity entity, string attributeName, int defaultValue = 0)
        {
            var value = GetAliasedValueOrDefault<object>(entity, attributeName, null);

            if (value == null)
                return defaultValue;

            if (value is int intValue)
                return intValue;

            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : defaultValue;
        }

        private decimal GetAliasedMoneyValue(Entity entity, string attributeName, decimal defaultValue = 0)
        {
            var money = GetAliasedValueOrDefault<Money>(entity, attributeName, null);
            return money != null ? money.Value : defaultValue;
        }

        private EntityReference GetAliasedEntityReference(Entity entity, string attributeName)
        {
            return GetAliasedValueOrDefault<EntityReference>(entity, attributeName, null);
        }

        private OptionSetValue GetAliasedOptionSetValue(Entity entity, string attributeName)
        {
            return GetAliasedValueOrDefault<OptionSetValue>(entity, attributeName, null);
        }












    }
    //Sunny(30-05-25)
    public class OpportunityDataResponse
    {
        public List<OpportunityData> Opportunities { get; set; }
        public Dictionary<string, bool> HiddenFields { get; set; } // or JObject if you prefer dynamic parsing
        public bool isAuthorized { get; set; }
    }




}
