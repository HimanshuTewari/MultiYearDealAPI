using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using MultiYearDeal.Model;
using MultiYearDeal.Workflows;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Markup;

namespace MultiYearDeal.Plugins
{
    public class OpportunityProductData
    {
        public string OpportunityLineItemId { get; set; }
        public string EventScheduleId { get; set; }

        public string Name { get; set; }
        public string Division { get; set; }
        public string RateType { get; set; }
        public int QtyUnits { get; set; }
        public int? QtyEvents { get; set; }
        public string InventoryBySeasonId { get; set; }
        public int TotalQuantity { get; set; }
        public int ScheduledQuantity { get; set; }
        public int ExpectedEventQuantity { get; set; }
        public bool AllowArbitraryScheduling { get; set; }
    }
    public class EventData
    {
        public Guid EventId { get; set; }
        public string Name { get; set; }
        public string Date { get; set; }
        public decimal QuantityAvailable { get; set; }
        public string InventoryBySeasonId { get; set; }
    }


    public class AssetsSchedulerPlugin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            string functionName = "AssetsSchedulerPlugin.Execute";
            
            try
            {
                TraceHelper.Initialize(service);
                TraceHelper.Trace(tracingService, "Start {0} | Message={1} | Stage={2} | Depth={3}", functionName, context.MessageName, context.Stage, context.Depth);

                string actionName = context.InputParameters.Contains("actionName") ? (context.InputParameters["actionName"]?.ToString() ?? string.Empty) : string.Empty;
                TraceHelper.Trace(tracingService, "actionName={0}", actionName);

                #region Asset Scheduler - Confirm Schedule functionality
                if (string.Equals(actionName, "ConfirmSchedule", StringComparison.OrdinalIgnoreCase))
                {
                    string strOppProdId = context.InputParameters.Contains("OpportunityProductId") ? (context.InputParameters["OpportunityProductId"]?.ToString() ?? string.Empty) : string.Empty;
                    int qtyScheduled = context.InputParameters.Contains("QtyScheduled") ? Convert.ToInt32(context.InputParameters["QtyScheduled"]) : 0;

                    string strEvent = context.InputParameters.Contains("EventId") ? (context.InputParameters["EventId"]?.ToString() ?? string.Empty) : string.Empty;
                   

                    string dateString = (context.InputParameters.Contains("Date") && context.InputParameters["Date"] is string)
                        ? (string)context.InputParameters["Date"]
                        : null;

                    Guid.TryParse(strOppProdId, out Guid oppProdId);
                    Guid.TryParse(strEvent, out Guid eventId);

                    DateTime parsedDate = DateTime.TryParse(dateString, out DateTime tempDate) ? tempDate : DateTime.MinValue;

                    TraceHelper.Trace(tracingService, "ConfirmSchedule inputs | OppProdIdStr={0} OppProdId={1} QtyScheduled={2} EventIdStr={3} EventId={4} DateStr={5} ParsedDate={6}",
                        strOppProdId, oppProdId, qtyScheduled, strEvent, eventId, dateString, parsedDate);

                    if (oppProdId == Guid.Empty) { TraceHelper.Trace(tracingService, "ConfirmSchedule failed: OppProdId is empty"); throw new InvalidPluginExecutionException("OpportunityProductId is missing/invalid."); }
                    if (eventId == Guid.Empty) { TraceHelper.Trace(tracingService, "ConfirmSchedule failed: EventId is empty"); throw new InvalidPluginExecutionException("EventId is missing/invalid."); }
                    if (parsedDate == DateTime.MinValue) { TraceHelper.Trace(tracingService, "ConfirmSchedule warning: Date parse failed, DateTime.MinValue used"); }

                    Entity newFulfillment = new Entity("ats_fulfillment");
                    newFulfillment["ats_quantityscheduled"] = qtyScheduled;
                    newFulfillment["ats_opportunityproduct"] = new EntityReference("opportunityproduct", oppProdId);
                    newFulfillment["ats_event"] = new EntityReference("ats_event", eventId);
                    newFulfillment["ats_date"] = parsedDate;

                    Guid fulfillmentId = service.Create(newFulfillment);
                    TraceHelper.Trace(tracingService, "Fulfillment created | Id={0}", fulfillmentId);

                    TraceHelper.Trace(tracingService, "End {0} (ConfirmSchedule)", functionName);
                    return;
                }
                #endregion

                // OppId input validation
                if (!context.InputParameters.Contains("OppId") || !(context.InputParameters["OppId"] is string opportunityIdString) || !Guid.TryParse(opportunityIdString, out Guid opportunityId))
                {
                    TraceHelper.Trace(tracingService, "OppId missing/invalid. OppIdRaw={0}", context.InputParameters.Contains("OppId") ? context.InputParameters["OppId"] : null);
                    throw new InvalidPluginExecutionException("Input parameter 'OppId' is missing or invalid.");
                }

                TraceHelper.Trace(tracingService, "OpportunityId={0}", opportunityId);

                // Retrieve Opportunity Products
                var opportunityProductsList = RetrieveOpportunityProducts(opportunityId, tracingService, service);
                context.OutputParameters["OpportunityDataOutput"] = JsonConvert.SerializeObject(opportunityProductsList);
                TraceHelper.Trace(tracingService, "OpportunityDataOutput set | Count={0}", opportunityProductsList != null ? opportunityProductsList.Count : 0);

                // Retrieve Event Data
                var groupedEventData = RetrieveEventData(opportunityId, tracingService, service);
                context.OutputParameters["EventDataOutput"] = JsonConvert.SerializeObject(groupedEventData);
                TraceHelper.Trace(tracingService, "EventDataOutput set");

                TraceHelper.Trace(tracingService, "End {0}", functionName);
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException($"An error occurred in {functionName}: {ex.Message}", ex);
            }
        }





        private List<OpportunityProductData> RetrieveOpportunityProducts(Guid opportunityId, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "RetrieveOpportunityProducts";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} | opportunityId={1}", functionName, opportunityId);

                if (service == null)
                {
                    TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName);
                    return new List<OpportunityProductData>();
                }

                if (opportunityId == Guid.Empty)
                {
                    TraceHelper.Trace(tracingService, "opportunityId is empty, exiting {0}", functionName);
                    return new List<OpportunityProductData>();
                }

                string fetchXml = $@"
            <fetch>
              <entity name='opportunityproduct'>
                <attribute name='opportunityproductid' />
                <attribute name='opportunityid' />
                <attribute name='ats_quantity' />
                <attribute name='ats_quantityofevents' />
                <attribute name='productname' />
                <filter>
                  <condition attribute='opportunityid' operator='eq' value='{opportunityId}' />
                </filter>
                <link-entity name='product' from='productid' to='productid' alias='product'>
                  <link-entity name='ats_division' from='ats_divisionid' to='ats_division' alias='division'>
                    <attribute name='ats_name' />
                  </link-entity>
                </link-entity>
                <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' alias='Rate'>
                  <attribute name='ats_ratetype' />
                </link-entity>
                <link-entity name='ats_fulfillment' from='ats_opportunityproduct' to='opportunityproductid' link-type='outer' alias='Fulfillement'>
                  <attribute name='ats_quantityscheduled' />
                  <attribute name='ats_unscheduled' />
                </link-entity>
                <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='IBS'>
                  <attribute name='ats_totalquantityperevent' />
                  <attribute name='ats_inventorybyseasonid' />
                  <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventschedule' link-type='outer' alias='EventScheduled'>
                    <attribute name='ats_eventscheduleid' />
                    <attribute name='ats_expectedeventquantity' />
                  </link-entity>
                </link-entity>
              </entity>
            </fetch>";

                EntityCollection result = service.RetrieveMultiple(new FetchExpression(fetchXml));
                TraceHelper.Trace(tracingService, "OpportunityProducts retrieved count={0}", result.Entities.Count);

                var opportunityProductsList = new List<OpportunityProductData>();

                foreach (Entity entity in result.Entities)
                {
                    // ---------------------------
                    // Rate Type
                    // ---------------------------
                    OptionSetValue rateTypeOption = entity.Contains("Rate.ats_ratetype")
                        ? (entity["Rate.ats_ratetype"] as AliasedValue)?.Value as OptionSetValue
                        : null;

                    int rateTypeValue = rateTypeOption?.Value ?? -1;
                    string rateTypeLabel =
                        rateTypeValue == 114300000 ? "Season" :
                        rateTypeValue == 114300001 ? "Individual" :
                        string.Empty;

                    TraceHelper.Trace(tracingService, "OLI {0} | rateTypeValue={1} | rateTypeLabel={2}",
                        entity.Id, rateTypeValue, rateTypeLabel);

                    var lineItem = new OpportunityProductData
                    {
                        OpportunityLineItemId = entity.Id.ToString(),
                        Name = entity.GetAttributeValue<string>("productname"),
                        Division = entity.GetAttributeValue<AliasedValue>("division.ats_name")?.Value?.ToString(),
                        RateType = rateTypeLabel,
                        QtyUnits = entity.GetAttributeValue<int>("ats_quantity"),
                        QtyEvents = entity.GetAttributeValue<int?>("ats_quantityofevents"),
                        InventoryBySeasonId = entity.GetAttributeValue<AliasedValue>("IBS.ats_inventorybyseasonid")?.Value?.ToString(),
                        TotalQuantity = entity.GetAttributeValue<AliasedValue>("IBS.ats_totalquantityperevent")?.Value is int tq ? tq : 0,
                        ScheduledQuantity = entity.GetAttributeValue<AliasedValue>("Fulfillement.ats_quantityscheduled")?.Value is int sq ? sq : 0,
                        EventScheduleId = entity.GetAttributeValue<AliasedValue>("EventScheduled.ats_eventscheduleid")?.Value?.ToString(),
                        ExpectedEventQuantity = entity.GetAttributeValue<AliasedValue>("EventScheduled.ats_expectedeventquantity")?.Value is int eq ? eq : 0,
                        AllowArbitraryScheduling = false
                    };

                    opportunityProductsList.Add(lineItem);
                }

                TraceHelper.Trace(tracingService, "End {0} | totalItems={1}", functionName, opportunityProductsList.Count);
                return opportunityProductsList;
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException(
                    $"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
        }


        private Dictionary<string, List<object>> RetrieveEventData(Guid opportunityId, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "RetrieveEventData";

            try
            {
                TraceHelper.Trace(tracingService, "Start {0} | opportunityId={1}", functionName, opportunityId);

                if (service == null)
                {
                    TraceHelper.Trace(tracingService, "Service is null, exiting {0}", functionName);
                    return new Dictionary<string, List<object>>();
                }

                if (opportunityId == Guid.Empty)
                {
                    TraceHelper.Trace(tracingService, "opportunityId is empty, exiting {0}", functionName);
                    return new Dictionary<string, List<object>>();
                }

                string fetchXml = $@"
            <fetch distinct='true'>
              <entity name='ats_event'>
                <attribute name='ats_eventid' />
                <attribute name='ats_name' />
                <attribute name='ats_date' />
                <link-entity name='ats_event_ats_eventschedule' from='ats_eventid' to='ats_eventid' link-type='outer'>
                  <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventscheduleid' link-type='outer' alias='EventSch'>
                    <link-entity name='ats_inventorybyseason' from='ats_eventschedule' to='ats_eventscheduleid' alias='IBS'>
                      <attribute name='ats_totalquantityperevent' />
                      <attribute name='ats_inventorybyseasonid' />
                      <link-entity name='opportunityproduct' from='ats_inventorybyseason' to='ats_inventorybyseasonid' alias='OppProd'>
                        <attribute name='opportunityid' />
                        <attribute name='opportunityproductid' />
                        <attribute name='opportunityproductname' />
                        <filter>
                          <condition attribute='opportunityid' operator='eq' value='{opportunityId}' />
                        </filter>
                        <link-entity name='ats_fulfillment' from='ats_opportunityproduct' to='opportunityproductid' alias='fulfillment' link-type='outer'>
                          <attribute name='ats_quantityscheduled' />
                        </link-entity>
                      </link-entity>
                    </link-entity>
                  </link-entity>
                </link-entity>
              </entity>
            </fetch>";

                EntityCollection result = service.RetrieveMultiple(new FetchExpression(fetchXml));
                TraceHelper.Trace(tracingService, "Fetch returned rows={0}", result.Entities.Count);

                var groupedData = new Dictionary<string, List<object>>();
                var seenEventIds = new Dictionary<string, HashSet<Guid>>();

                int skippedNoIbs = 0;
                int skippedDuplicates = 0;
                int added = 0;

                foreach (Entity entity in result.Entities)
                {
                    string inventoryBySeasonId = entity.GetAttributeValue<AliasedValue>("IBS.ats_inventorybyseasonid")?.Value?.ToString();
                    if (string.IsNullOrWhiteSpace(inventoryBySeasonId))
                    {
                        skippedNoIbs++;
                        continue;
                    }

                    if (!groupedData.ContainsKey(inventoryBySeasonId))
                    {
                        groupedData[inventoryBySeasonId] = new List<object>();
                        seenEventIds[inventoryBySeasonId] = new HashSet<Guid>();
                    }

                    Guid eventId = entity.GetAttributeValue<Guid>("ats_eventid");

                    if (seenEventIds[inventoryBySeasonId].Contains(eventId))
                    {
                        skippedDuplicates++;
                        continue;
                    }
                    seenEventIds[inventoryBySeasonId].Add(eventId);

                    int totalQty = (entity.GetAttributeValue<AliasedValue>("IBS.ats_totalquantityperevent")?.Value is int tq) ? tq : 0;
                    int qtyScheduled = (entity.GetAttributeValue<AliasedValue>("fulfillment.ats_quantityscheduled")?.Value is int qs) ? qs : 0;

                    int quantityAvailable = totalQty - qtyScheduled;

                    var eventObj = new
                    {
                        EventId = eventId,
                        Name = entity.GetAttributeValue<string>("ats_name"),
                        Date = entity.Contains("ats_date") ? entity.GetAttributeValue<DateTime>("ats_date").ToString("MM-dd-yyyy") : null,
                        QuantityAvailable = quantityAvailable
                    };

                    groupedData[inventoryBySeasonId].Add(eventObj);
                    added++;
                }

                TraceHelper.Trace(tracingService, "GroupedData built | keys={0} added={1} skippedNoIbs={2} skippedDup={3}", groupedData.Count, added, skippedNoIbs, skippedDuplicates);
                TraceHelper.Trace(tracingService, "End {0}", functionName);

                return groupedData;
            }
            catch (InvalidPluginExecutionException ex)
            {
                TraceHelper.Trace(tracingService, "InvalidPluginExecutionException in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                TraceHelper.Trace(tracingService, "Unhandled exception in {0}: {1}", functionName, ex.Message);
                throw new InvalidPluginExecutionException($"functionName: {functionName}, Exception: {ex.Message}", ex);
            }
        }



    }
}
