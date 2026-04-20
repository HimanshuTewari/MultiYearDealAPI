using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Workflows;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.IdentityModel.Metadata;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace MultiYearDeal.Plugins 
{
    public class OpportunitesImpactPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
           
            string functionName = "Execute";

            try
            {
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Tracing initialized");

                if (context.MessageName != "ats_AgreementOpportunityImpact")
                {
                    TraceHelper.Trace(tracingService, "Message name is not equal to ats_AgreementOpportunityImpact");
                    return;
                }

                if (!context.InputParameters.Contains("AgreementReference") || !(context.InputParameters["AgreementReference"] is EntityReference))
                    throw new InvalidPluginExecutionException("Agreement reference is missing");

                EntityReference agreementRef = (EntityReference)context.InputParameters["AgreementReference"];

                // Retrieve Agreement Record
                Entity agreement = service.Retrieve(agreementRef.LogicalName, agreementRef.Id, new ColumnSet("ats_contractlengthyears", "ats_startseason", "statecode"));

                int opportunityCount = -1;
                #region logic for when Agreement is in active state, then reactivate the all associated opportunities.
                //EntityCollection opportunities = null;
                if (agreement.Attributes.Contains("statecode"))
                {
                    OptionSetValue stateCodeOption = agreement.GetAttributeValue<OptionSetValue>("statecode");
                    int stateCodeValue = stateCodeOption.Value;

                    if (stateCodeValue == 0) // Active
                    {
                        TraceHelper.Trace(tracingService, "stateCodeValue == 0 (Active)");

                        // Agreement ID for filtering
                        Guid agreementId = agreementRef.Id;

                        string fetchXml = $@"
                                            <fetch aggregate='true'>
                                              <entity name='opportunity'>
                                                <attribute name='opportunityid' alias='opportunitycount' aggregate='count' />
                                                <filter type='and'>
                                                  <condition attribute='ats_agreement'
                                                             operator='eq'
                                                             value='{agreementId}' />
                                                </filter>
                                              </entity>
                                            </fetch>";



                        EntityCollection result = service.RetrieveMultiple(new FetchExpression(fetchXml));


                        if (result.Entities.Count > 0)
                        {
                             opportunityCount = (int)((AliasedValue)result.Entities[0]["opportunitycount"]).Value;
                        }


                        TraceHelper.Trace(tracingService, "Retrieved {0} opportunities related to Agreement {1}.", opportunityCount, agreementId);

                        //foreach (Entity opp in opportunities.Entities)
                        //{
                        //    OptionSetValue state = opp.GetAttributeValue<OptionSetValue>("statecode");

                        //    if (state != null && (state.Value == 1 || state.Value == 2)) // Inactive or Cancelled
                        //    {
                        //        Guid opportunityId = opp.Id;

                        //        Entity updateOpportunity = new Entity("opportunity", opportunityId)
                        //        {
                        //            ["statecode"] = new OptionSetValue(0),
                        //            ["statuscode"] = new OptionSetValue(1) // In Progress (default)
                        //        };

                        //        service.Update(updateOpportunity);
                        //        TraceHelper.Trace(tracingService, "Updated opportunity '{0}' ({1}) to Active (statecode 0).", opp.GetAttributeValue<string>("name"), opp.Id);
                        //    }
                        //}
                    }
                    else
                    {
                        throw new InvalidPluginExecutionException($"Agreement record id: {agreementRef.Id} is in InActive state."); 
                    }
                }
                else
                {
                    TraceHelper.Trace(tracingService, "Statecode field not found on agreement record.");
                }
                #endregion

                //retireving the value from the Action real time data
                int contractLength = context.InputParameters.Contains("ContractLengthYears") ? (int)context.InputParameters["ContractLengthYears"] : 0;


                //Retrieving the value from the Action real time data
                string startSeasonRefName = context.InputParameters.Contains("StartSeasonYearName") ? context.InputParameters["StartSeasonYearName"].ToString() : string.Empty;
                //tracingService.Trace($"startSeasonRefName: {startSeasonRefName}");
                if (startSeasonRefName == string.Empty)
                {
                    TraceHelper.Trace(tracingService, "startSeasonRefName is empty. Exiting.");
                    return;
                }

                // Lists to track impacted Opportunities
                List<string> toBeCreated = new List<string>();
                List<string> toBeUpdated = new List<string>();
                List<string> toBeDeleted = new List<string>();


                //Retreving the total opportuinities count
                int oppCount = opportunityCount;

                Dictionary<int, Entity> seasonDict = null; 
                if (oppCount > contractLength)
                {
                     seasonDict = GetSeasonsChainBySeasonName(service, startSeasonRefName, tracingService, oppCount);
                }
                else
                {
                     seasonDict = GetSeasonsChainBySeasonName(service, startSeasonRefName, tracingService, contractLength);
                }




                // Example
                //var startSeason = seasonDict[0];
                //var nextSeason = seasonDict.ContainsKey(1) ? seasonDict[1] : null;
                TraceHelper.Trace(tracingService, $"seasonDict.cout: {seasonDict.Count}"); 



               
                if (oppCount == 0 && contractLength > 0 && seasonDict.Count > 0) //No opportunity exist on the Agreement
                {
                    TraceHelper.Trace(tracingService, "No Opportinities exist for the AgreementId: {0}", agreementRef.Id);
                    TraceHelper.Trace(tracingService, "contract length > 0");

                    //Sunny(09-02-27)--> if the contract length is greater than 0, and season is less, then throw the error for the missing season.
                    if (contractLength > seasonDict.Count)
                    {
                        TraceHelper.Trace(tracingService, "contractLength > seasonDict.Count");
                        throw new InvalidPluginExecutionException($"Contract Length is greater than the seasons count. Contract Length: {contractLength}, Seasons Count: {seasonDict.Count}. Please add the missing seasons and try again.");
                    }



                    for(int i = 0; i<=contractLength-1; i++)
                    {
                        Entity seasonRecord = seasonDict[i].Attributes.Contains("ats_name") ? seasonDict[i] : null;
                        string seasonName = seasonRecord != null ? (string) seasonRecord["ats_name"] : string.Empty;
                        toBeCreated.Add(seasonName);
                        TraceHelper.Trace(tracingService, "SeasonName: {0}, Added sucessfully in the to be created list", seasonName);
                    }
                    TraceHelper.Trace(tracingService, "tobeCreated Count: {0}", toBeCreated.Count);
                }
                else
                {
                    TraceHelper.Trace(tracingService, "oppCount == 0 && contractLength > 0 && seasonDict.Count > 0 fails"); 
                    //return;
                }


                if(oppCount > 0 && seasonDict.Count >0) //Handling for the exisitng oppotunities
                {
                    // Add opportunity
                    if (oppCount < contractLength)
                    {
                        TraceHelper.Trace(tracingService, "oppCount < contractLength"); 
                        for(int i=0; i<=contractLength-1; i++)
                        {
                            //Handling if the season record is missing
                            if (!seasonDict.ContainsKey(i))
                            {
                                throw new InvalidPluginExecutionException("Season record is missing"); 
                            }

                            Entity season = seasonDict[i];
                            string seasonName = season.Contains("ats_name") ? (string)season["ats_name"] : string.Empty;
                            TraceHelper.Trace(tracingService, "Current SeasonName: {0}", seasonName);
                            if (i <= (opportunityCount - 1))
                            {
                               toBeUpdated.Add(seasonName); 
                            }
                            else//New Added season
                            {
                                toBeCreated.Add(seasonName);
                            }
                        }

                    }

                    // delete
                    if (oppCount > contractLength)
                    {
                        TraceHelper.Trace(tracingService, "oppCount > contractLength");
                        for(int i = 0; i <= oppCount-1; i++)
                        {
                            Entity season = seasonDict[i];
                            string seasonName = season.Contains("ats_name") ? (string)season["ats_name"] : string.Empty;

                            if (i <= contractLength-1)
                            {
                                toBeUpdated.Add(seasonName); 
                            }
                            else
                            {
                                toBeDeleted.Add(seasonName);
                            }
                        }
                        TraceHelper.Trace(tracingService, "toBeCreated: {0}, toBeUpdate: {1}, toBeDeleted:{2}", toBeCreated, toBeUpdated, toBeDeleted);
                        
                    }
                }



                // Return the results as output parameters
                context.OutputParameters["ToBeCreated"] = string.Join(",", toBeCreated);
                context.OutputParameters["ToBeUpdated"] = string.Join(",", toBeUpdated);
                context.OutputParameters["ToBeDeleted"] = string.Join(",", toBeDeleted);

                TraceHelper.Trace(tracingService, "OpportunityImpactPlugin executed successfully.");


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


        public  Dictionary<int, Entity> GetSeasonsChainBySeasonName( IOrganizationService service,string currentSeasonName,ITracingService tracingService, int contractLength)
        {
            string functionName = "GetSeasonsChainBySeasonName";

            try
            {
                var seasonDict = new Dictionary<int, Entity>();


                TraceHelper.Trace(tracingService, $"GetSeasonsChain started. CurrentSeasonName = {currentSeasonName}");

                if (string.IsNullOrWhiteSpace(currentSeasonName))
                {
                    TraceHelper.Trace(tracingService, "CurrentSeasonName is null or empty. Returning empty dictionary.");
                    return seasonDict;
                }
                //string safeSeasonName = System.Security.SecurityElement.Escape(currentSeasonName);
                // 1) Get current season by name (FetchXML once)
                string fetch = $@"
                                <fetch top='1'>
                                  <entity name='ats_season'>
                                    <attribute name='ats_seasonid' />
                                    <attribute name='ats_name' />
                                    <attribute name='ats_nextseason' />
                                    <filter>
                                      <condition attribute='ats_name' operator='eq'
                                                 value='{currentSeasonName}' />
                                    </filter>
                                  </entity>
                                </fetch>";

                TraceHelper.Trace(tracingService, "Fetching current season using FetchXML.");

                EntityCollection currentSeasonCollection = service .RetrieveMultiple(new FetchExpression(fetch));
                Entity currentSeason = currentSeasonCollection.Entities.FirstOrDefault();

                if (currentSeason == null)
                {
                    TraceHelper.Trace(tracingService, "Current season not found. Returning empty dictionary.");
                    throw new InvalidPluginExecutionException($"No Seasom record found for Season Name: {currentSeasonName}");
                }
                

                // Index 0 = Start season
                seasonDict[0] = currentSeason;

                TraceHelper.Trace(
                    tracingService,
                    $"Season added to dictionary. Index = 0, SeasonId = {currentSeason.Id}, SeasonName = {currentSeason.GetAttributeValue<string>("ats_name")}"
                );

                // 2) Loop through next seasons
                var nextRef = currentSeason.GetAttributeValue<EntityReference>("ats_nextseason");
                int maxDepth =  contractLength-1; 
                int index = 1;

                while (nextRef != null && nextRef.Id != Guid.Empty && index <= maxDepth)
                {
                    TraceHelper.Trace(
                        tracingService,
                        $"Fetching next season. Index = {index}, SeasonId = {nextRef.Id}"
                    );

                    
                    var nextSeason = service.Retrieve(
                        "ats_season",
                        nextRef.Id,
                        new ColumnSet("ats_seasonid", "ats_name", "ats_nextseason")
                    );


                    seasonDict[index] = nextSeason ;

                    TraceHelper.Trace(
                        tracingService,
                        $"Season added to dictionary. Index = {index}, SeasonId = {nextSeason.Id}, SeasonName = {nextSeason.GetAttributeValue<string>("ats_name")}"
                    );

                    nextRef = nextSeason.GetAttributeValue<EntityReference>("ats_nextseason");
                    index++;

                    if (index <= maxDepth && nextRef == null) //Next Season is missing, throw the error
                    {
                        throw new InvalidPluginExecutionException($"Next Season is not found in the Season: {nextSeason.Id}, and Name : {nextSeason["ats_name"]}"); 
                    }
                }

                TraceHelper.Trace(
                    tracingService,
                    $"Returning from GetSeasonsChain. Total seasons stored = {seasonDict.Count}"
                );


                return seasonDict;
            }
            catch (InvalidPluginExecutionException ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}");
            }
        }

    }
}

