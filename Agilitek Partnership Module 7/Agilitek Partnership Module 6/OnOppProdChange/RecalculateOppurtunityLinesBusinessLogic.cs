using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Agilitek_Partnership
{
    public class OpportunityProductInfoPackage
    {
        public Guid OpportunityProductId { get; set; }
    }

    public class RecalculateOppurtunityLinesBusinessLogic
    {
        private static readonly string[] units =
        {
            "Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Nine","Ten","Eleven",
            "Twelve","Thirteen","Fourteen","Fifteen","Sixteen","Seventeen","Eighteen","Nineteen"
        };

        private static readonly string[] tens =
        {
            "","","Twenty","Thirty","Forty","Fifty","Sixty","Seventy","Eighty","Ninety"
        };

        private readonly string getAllOppLinesFromOpp = @"
<fetch version='1.0' output-format='xml-platform' mapping='logical' distinct='true'>
  <entity name='opportunityproduct'>
    <attribute name='opportunityproductid' />
    <attribute name='productid' />
    <attribute name='priceperunit' />
    <attribute name='ats_quantityofevents' />
    <attribute name='ats_quantity' />
    <attribute name='opportunityproductid' />
    <attribute name='opportunityproductname' />
    <attribute name='ats_sellingrate' />
    <attribute name='ats_legaldef' />
    <attribute name='ats_inventorybyseason' />
    <attribute name='ats_unadjustedtotalprice' />
    <attribute name='ats_adjustedtotalprice' />
    <attribute name='ats_hardcost' />
    <attribute name='ats_discount' />
    <attribute name='description' />
    <attribute name='ats_manualpriceoverride' />
    <attribute name='ats_packagetemplate' />
    <filter type='and'>
      <condition attribute='opportunityid' operator='eq' value='{0}' />
      <condition attribute='ats_packagelineitem' operator='null' />
    </filter>

    <link-entity name='opportunity' from='opportunityid' to='opportunityid' link-type='inner' alias='Opp'>
      <attribute name='ats_type' />
      <attribute name='ats_startseason' />
      <attribute name='ats_salesgoal' />
      <attribute name='ats_pricingmode' />
      <attribute name='ats_manualamount' />
      <attribute name='ats_tradeamount' />
      <attribute name='ats_barteramount' />
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
      <attribute name='ats_playoffeligible' />
      <attribute name='ats_ispackage' />
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

        private readonly string getIbsByOppProdId = @"<fetch>
  <entity name='opportunityproduct'>
    <attribute name='ats_quantity' />
    <attribute name='ats_quantityofevents' />
    <filter>
      <condition attribute='opportunityproductid' operator='eq' value='{0}' />
    </filter>
    <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' alias='Rate'>
      <attribute name='ats_inactive' />
      <attribute name='ats_ratetype' />
    </link-entity>
    <link-entity name='ats_inventorybyseason' from='ats_inventorybyseasonid' to='ats_inventorybyseason' alias='IBS'>
      <attribute name='ats_allowoverselling' />
      <attribute name='ats_unlimitedquantity' />
      <attribute name='ats_quantityavailable' />
      <link-entity name='ats_eventschedule' from='ats_eventscheduleid' to='ats_eventschedule' alias='ES'>
        <attribute name='ats_expectedeventquantity' />
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

        private enum RateType
        {
            Season = 114300000,
            Individual = 114300001
        }

        private class ProrationLine
        {
            public Guid OppProdId { get; set; }
            public Entity Source { get; set; }

            public RateType RateType { get; set; }
            public int Qty { get; set; }
            public int QtyEvents { get; set; }

            public bool IsManualOverride { get; set; }
            public bool IsPassThrough { get; set; }
            public bool HasValidUnits { get; set; }
            public bool IsEligible { get; set; }

            public decimal PricePerUnit { get; set; }
            public decimal UnadjustedTotal { get; set; }

            public decimal BaseAdjusted { get; set; }
            public decimal FinalAdjusted { get; set; }
        }

        public void RecalculateOppProdLines(Dictionary<string, string> args, IOrganizationService service, ITracingService tracingService)
        {
            string functionName = "RecalculateOppProdLines";

            try
            {
                Logging.Log("Inside RecalculateOppProdLines******", tracingService);

                EntityCollection oppLinesRetrieved =
                    service.RetrieveMultiple(new FetchExpression(string.Format(getAllOppLinesFromOpp, args["OppId"])));

                int pricingMode = 0;
                decimal manualAmount = 0m;
                decimal barterAmount = 0m;

                decimal automaticAmount = 0m;
                decimal passThroughAmount = 0m;
                decimal hardCostAmount = 0m;

                decimal sumAdjustedManualOverrideLines = 0m;

                var oppProdsUpdate = new EntityCollection();
                var oppProductPackageList = new List<OpportunityProductInfoPackage>();

                // If no lines exist, load opp header values (kept from your original)
                if (oppLinesRetrieved.Entities.Count == 0)
                {
                    var oppNoLine = service.Retrieve(
                        "opportunity",
                        new Guid(args["OppId"]),
                        new ColumnSet(new[] { "ats_manualamount", "ats_barteramount", "ats_pricingmode" }));

                    manualAmount = oppNoLine.Attributes.ContainsKey("ats_manualamount")
                        ? ((Money)oppNoLine["ats_manualamount"]).Value
                        : 0m;

                    barterAmount = oppNoLine.Attributes.ContainsKey("ats_barteramount")
                        ? ((Money)oppNoLine["ats_barteramount"]).Value
                        : 0m;

                    pricingMode = oppNoLine.Attributes.ContainsKey("ats_pricingmode")
                        ? ((OptionSetValue)oppNoLine["ats_pricingmode"]).Value
                        : 0;
                }

                // -------------------------
                // FIRST LOOP: totals + package detection
                // -------------------------
                foreach (var oppline in oppLinesRetrieved.Entities)
                {
                    pricingMode = oppline.Contains("Opp.ats_pricingmode")
                        ? ((OptionSetValue)((AliasedValue)oppline["Opp.ats_pricingmode"]).Value).Value
                        : pricingMode;

                    manualAmount = oppline.Contains("Opp.ats_manualamount")
                        ? ((Money)((AliasedValue)oppline["Opp.ats_manualamount"]).Value).Value
                        : manualAmount;

                    barterAmount = oppline.Contains("Opp.ats_barteramount")
                        ? ((Money)((AliasedValue)oppline["Opp.ats_barteramount"]).Value).Value
                        : barterAmount;

                    var rateType = (RateType)((OptionSetValue)((AliasedValue)oppline["Rate.ats_ratetype"]).Value).Value;

                    int qty = oppline.Attributes.ContainsKey("ats_quantity") ? Convert.ToInt32(oppline["ats_quantity"]) : 0;
                    int qtyEvents = oppline.Attributes.ContainsKey("ats_quantityofevents") ? Convert.ToInt32(oppline["ats_quantityofevents"]) : 0;

                    bool manualOverride = oppline.Attributes.ContainsKey("ats_manualpriceoverride")
                        && (bool)oppline["ats_manualpriceoverride"];

                    // Robust pass-through read
                    bool isPassThrough =
                        oppline.Attributes.ContainsKey("Prod.ats_ispassthroughcost") &&
                        (bool)((AliasedValue)oppline["Prod.ats_ispassthroughcost"]).Value;

                    decimal pricePerUnit = oppline.Attributes.ContainsKey("priceperunit")
                        ? ((Money)oppline["priceperunit"]).Value
                        : 0m;

                    bool hasValidUnits = rateType == RateType.Season
                        ? qty > 0
                        : (qty > 0 && qtyEvents > 0);

                    // YOUR RULE: Unadjusted always derived from priceperunit * units
                    decimal computedUnadjusted = 0m;
                    if (hasValidUnits)
                    {
                        computedUnadjusted = rateType == RateType.Season
                            ? pricePerUnit * qty
                            : pricePerUnit * qty * qtyEvents;
                    }

                    decimal storedAdjusted = oppline.Attributes.ContainsKey("ats_adjustedtotalprice")
                        ? ((Money)oppline["ats_adjustedtotalprice"]).Value
                        : 0m;

                    // AutomaticAmount helper
                    if (manualOverride)
                    {
                        automaticAmount += storedAdjusted;
                        sumAdjustedManualOverrideLines += storedAdjusted;
                    }
                    else
                    {
                        automaticAmount += computedUnadjusted;
                    }

                    if (isPassThrough)
                        passThroughAmount += computedUnadjusted;

                    decimal hardCostPerLine = oppline.Attributes.ContainsKey("ats_hardcost")
                        ? ((Money)oppline["ats_hardcost"]).Value
                        : 0m;

                    if (rateType == RateType.Season)
                        hardCostAmount += hardCostPerLine * qty;
                    else
                        hardCostAmount += hardCostPerLine * qty * qtyEvents;

                    // Collect package opp prods (same as your original approach)
                    var packageTemplateRef = oppline.GetAttributeValue<EntityReference>("ats_packagetemplate");
                    if (packageTemplateRef != null && packageTemplateRef.Id != Guid.Empty)
                    {
                        oppProductPackageList.Add(new OpportunityProductInfoPackage
                        {
                            OpportunityProductId = oppline.Id
                        });
                    }
                }

                decimal dealValue;
                decimal playOffEligibleRevenue = 0m;

                // -------------------------
                // AUTOMATIC MODE
                // -------------------------
                if (pricingMode == 559240000)
                {
                    dealValue = automaticAmount;

                    foreach (var oppline in oppLinesRetrieved.Entities)
                    {
                        var update = new Entity("opportunityproduct") { Id = oppline.Id };

                        var rateType = (RateType)((OptionSetValue)((AliasedValue)oppline["Rate.ats_ratetype"]).Value).Value;
                        int qty = oppline.Attributes.ContainsKey("ats_quantity") ? Convert.ToInt32(oppline["ats_quantity"]) : 0;
                        int qtyEvents = oppline.Attributes.ContainsKey("ats_quantityofevents") ? Convert.ToInt32(oppline["ats_quantityofevents"]) : 0;

                        decimal pricePerUnit = oppline.Attributes.ContainsKey("priceperunit")
                            ? ((Money)oppline["priceperunit"]).Value
                            : 0m;

                        bool manualOverride = oppline.Attributes.ContainsKey("ats_manualpriceoverride")
                            && (bool)oppline["ats_manualpriceoverride"];

                        bool hasValidUnits = rateType == RateType.Season
                            ? qty > 0
                            : (qty > 0 && qtyEvents > 0);

                        decimal unadjusted = hasValidUnits
                            ? (rateType == RateType.Season
                                ? pricePerUnit * qty
                                : pricePerUnit * qty * qtyEvents)
                            : 0m;

                        decimal adjusted = manualOverride
                            ? (oppline.Attributes.ContainsKey("ats_adjustedtotalprice")
                                ? ((Money)oppline["ats_adjustedtotalprice"]).Value
                                : 0m)
                            : unadjusted;

                        update["ats_unadjustedtotalprice"] = new Money(unadjusted);
                        update["ats_adjustedtotalprice"] = new Money(adjusted);

                        decimal sellingRate = 0m;
                        if (hasValidUnits)
                        {
                            sellingRate = rateType == RateType.Season
                                ? adjusted / qty
                                : adjusted / (qty * qtyEvents);
                        }
                        update["ats_sellingrate"] = new Money(sellingRate);

                        oppProdsUpdate.Entities.Add(update);

                        bool playoffEligible =
                            oppline.Attributes.ContainsKey("Prod.ats_playoffeligible") &&
                            (bool)((AliasedValue)oppline["Prod.ats_playoffeligible"]).Value;

                        if (playoffEligible)
                            playOffEligibleRevenue += adjusted;
                    }
                }
                // -------------------------
                // MANUAL MODE (proportional distribution across all eligible)
                // -------------------------
                else
                {
                    dealValue = manualAmount;

                    // Factor is used to compute base adjusted for eligible lines
                    decimal factor = 0m;

                    decimal effectiveAuto = automaticAmount - sumAdjustedManualOverrideLines;
                    decimal effectiveManual = manualAmount - sumAdjustedManualOverrideLines;
                    decimal effectiveNet = effectiveAuto - passThroughAmount;

                    if (effectiveManual != 0m && effectiveNet != 0m)
                        factor = (effectiveAuto - effectiveManual) / effectiveNet;

                    tracingService.Trace($"ManualProrationV6: factor={factor}");

                    var lines = new List<ProrationLine>();

                    foreach (var oppline in oppLinesRetrieved.Entities)
                    {
                        var rateType = (RateType)((OptionSetValue)((AliasedValue)oppline["Rate.ats_ratetype"]).Value).Value;

                        int qty = oppline.Attributes.ContainsKey("ats_quantity") ? Convert.ToInt32(oppline["ats_quantity"]) : 0;
                        int qtyEvents = oppline.Attributes.ContainsKey("ats_quantityofevents") ? Convert.ToInt32(oppline["ats_quantityofevents"]) : 0;

                        decimal pricePerUnit = oppline.Attributes.ContainsKey("priceperunit")
                            ? ((Money)oppline["priceperunit"]).Value
                            : 0m;

                        bool isManualOverride = oppline.Attributes.ContainsKey("ats_manualpriceoverride")
                            && (bool)oppline["ats_manualpriceoverride"];

                        bool isPassThrough =
                            oppline.Attributes.ContainsKey("Prod.ats_ispassthroughcost") &&
                            (bool)((AliasedValue)oppline["Prod.ats_ispassthroughcost"]).Value;

                        bool hasValidUnits = rateType == RateType.Season
                            ? qty > 0
                            : (qty > 0 && qtyEvents > 0);

                        // YOUR RULE: Unadjusted is always derived from unit price and units
                        decimal unadjusted = hasValidUnits
                            ? (rateType == RateType.Season
                                ? pricePerUnit * qty
                                : pricePerUnit * qty * qtyEvents)
                            : 0m;

                        decimal storedAdjusted = oppline.Attributes.ContainsKey("ats_adjustedtotalprice")
                            ? ((Money)oppline["ats_adjustedtotalprice"]).Value
                            : 0m;

                        decimal baseAdjusted;
                        if (isManualOverride)
                        {
                            // manual override stays as stored adjusted
                            baseAdjusted = storedAdjusted;
                        }
                        else if (isPassThrough)
                        {
                            // pass-through stays aligned to unadjusted
                            baseAdjusted = unadjusted;
                        }
                        else
                        {
                            // eligible base uses factor
                            baseAdjusted = unadjusted - (factor * unadjusted);
                        }

                        // Eligible means: valid units + not manual override + not pass-through + positive unadjusted
                        bool isEligible =
                            hasValidUnits &&
                            !isManualOverride &&
                            !isPassThrough &&
                            unadjusted > 0m;

                        lines.Add(new ProrationLine
                        {
                            OppProdId = oppline.Id,
                            Source = oppline,
                            RateType = rateType,
                            Qty = qty,
                            QtyEvents = qtyEvents,
                            PricePerUnit = pricePerUnit,
                            UnadjustedTotal = unadjusted,
                            IsManualOverride = isManualOverride,
                            IsPassThrough = isPassThrough,
                            HasValidUnits = hasValidUnits,
                            IsEligible = isEligible,
                            BaseAdjusted = baseAdjusted,
                            FinalAdjusted = baseAdjusted
                        });

                        tracingService.Trace($"ManualProrationV6: OLI={oppline.Id} PT={isPassThrough} MO={isManualOverride} Valid={hasValidUnits} Unadj={unadjusted} BaseAdj={baseAdjusted} Eligible={isEligible}");
                    }

                    decimal fixedTotal = lines.Where(l => !l.IsEligible).Sum(l => l.BaseAdjusted);
                    var eligible = lines.Where(l => l.IsEligible).ToList();

                    decimal eligibleBaseTotal = eligible.Sum(l => l.BaseAdjusted);
                    decimal initialTotal = fixedTotal + eligibleBaseTotal;

                    decimal diff = dealValue - initialTotal;

                    tracingService.Trace($"ManualProrationV6: deal={dealValue} fixed={fixedTotal} eligibleBase={eligibleBaseTotal} initial={initialTotal} diff={diff} eligibleCount={eligible.Count}");

                    if (diff != 0m && eligible.Count > 0)
                    {
                        // Weight by base adjusted (abs) first; if somehow 0, fallback to unadjusted
                        decimal weightSum = eligible.Sum(l => Math.Abs(l.BaseAdjusted));
                        if (weightSum == 0m)
                            weightSum = eligible.Sum(l => l.UnadjustedTotal);

                        decimal allocated = 0m;

                        for (int i = 0; i < eligible.Count; i++)
                        {
                            var line = eligible[i];

                            if (i == eligible.Count - 1)
                            {
                                line.FinalAdjusted = line.BaseAdjusted + (diff - allocated);
                            }
                            else
                            {
                                decimal weight = Math.Abs(line.BaseAdjusted);
                                if (weightSum == 0m) weight = line.UnadjustedTotal;

                                decimal raw = diff * (weight / weightSum);
                                decimal rounded = Math.Round(raw, 2, MidpointRounding.AwayFromZero);

                                line.FinalAdjusted = line.BaseAdjusted + rounded;
                                allocated += rounded;
                            }

                            tracingService.Trace($"ManualProrationV6: APPLY diff → OLI={line.OppProdId} BaseAdj={line.BaseAdjusted} FinalAdj={line.FinalAdjusted}");
                        }
                    }

                    // Build updates
                    foreach (var line in lines)
                    {
                        var update = new Entity("opportunityproduct") { Id = line.OppProdId };

                        update["ats_unadjustedtotalprice"] = new Money(line.UnadjustedTotal);
                        update["ats_adjustedtotalprice"] = new Money(line.FinalAdjusted);

                        decimal sellingRate = 0m;
                        if (line.HasValidUnits)
                        {
                            sellingRate = line.RateType == RateType.Season
                                ? (line.FinalAdjusted / line.Qty)
                                : (line.FinalAdjusted / (line.Qty * line.QtyEvents));
                        }
                        update["ats_sellingrate"] = new Money(sellingRate);

                        oppProdsUpdate.Entities.Add(update);

                        bool playoffEligible =
                            line.Source.Attributes.ContainsKey("Prod.ats_playoffeligible") &&
                            (bool)((AliasedValue)line.Source["Prod.ats_playoffeligible"]).Value;

                        if (playoffEligible)
                            playOffEligibleRevenue += line.FinalAdjusted;
                    }
                }

                // Bulk update using UpdateMultiple
                const int chunkSize = 5;
                var allUpdates = oppProdsUpdate.Entities.ToList();

                for (int i = 0; i < allUpdates.Count; i += chunkSize)
                {
                    var chunk = allUpdates.Skip(i).Take(chunkSize).ToList();
                    if (chunk.Count == 0) continue;

                    var chunkCollection = new EntityCollection(chunk)
                    {
                        EntityName = chunk[0].LogicalName
                    };

                    var req = new UpdateMultipleRequest
                    {
                        Targets = chunkCollection
                    };

                    service.Execute(req);
                }

                // Update Opportunity
                var oppUpdate = new Entity("opportunity") { Id = new Guid(args["OppId"]) };
                oppUpdate["ats_dealvalue"] = new Money(dealValue);
                oppUpdate["budgetamount"] = new Money(automaticAmount);
                oppUpdate["ats_totalhardcost"] = new Money(hardCostAmount);
                oppUpdate["ats_cashamount"] = new Money(dealValue - barterAmount);
                oppUpdate["ats_playoffeligiblerevenue"] = new Money(playOffEligibleRevenue);

                service.Update(oppUpdate);

                // Package recalculation
                tracingService.Trace("Proceeding for the Product package bundling recal Opp lines");
                tracingService.Trace($"oppProductPackageList.Count= {oppProductPackageList.Count}");
                oppProductPackageList.ForEach(item =>
                {
                    tracingService.Trace($"Processing → {item.OpportunityProductId}");
                    calOppProductPackagComponent(item.OpportunityProductId, tracingService, service);
                    tracingService.Trace($"Recal Execution completed for the OppProd: {item.OpportunityProductId}");
                });

                tracingService.Trace($"Exit functionName: {functionName}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
        }

        // Sunny(16-Nov-2025)
        public void calOppProductPackagComponent(Guid packageOpplineId, ITracingService tracingService, IOrganizationService service)
        {
            string functionName = "calOppProductPackagComponent";

            try
            {
                tracingService.Trace($"function Begins: {functionName}");

                Entity packageOLIObj = service.Retrieve("opportunityproduct", packageOpplineId, new ColumnSet("ats_adjustedtotalprice", "ats_manualpriceoverride"));
                Money packageOLINetAmount = packageOLIObj.Contains("ats_adjustedtotalprice") ? (Money)packageOLIObj["ats_adjustedtotalprice"] : new Money(0);
                bool manualPriceOverride = packageOLIObj.GetAttributeValue<bool>("ats_manualpriceoverride");

                if (packageOLINetAmount.Value != 0)
                    tracingService.Trace($"packageOLINetAmount: {packageOLINetAmount.Value}");

                //if (!manualPriceOverride)
                //{
                //    tracingService.Trace("package OLI is not kept manual, return");
                //    return;
                //}

                var fetch = $@"
                            <fetch>
                              <entity name='opportunityproduct'>
                                <attribute name='opportunityproductid' />
                                <attribute name='productid' />
                                <attribute name='priceperunit' />
                                <attribute name='ats_quantityofevents' />
                                <attribute name='ats_quantity' />
                                <attribute name='opportunityproductname' />
                                <attribute name='ats_sellingrate' />
                                <attribute name='ats_unadjustedtotalprice' />
                                <attribute name='ats_adjustedtotalprice' />
                                <attribute name='ats_manualpriceoverride' />
                                <filter>
                                  <condition attribute='ats_packagelineitem' operator='eq' value='{packageOpplineId}' />
                                </filter>
                                <link-entity name='product' from='productid' to='productid' link-type='inner' alias='Prod'>
                                  <attribute name='ats_ispassthroughcost' />
                                </link-entity>
                                <link-entity name='ats_rate' from='ats_rateid' to='ats_rate' link-type='inner' alias='Rate'>
                                  <attribute name='ats_ratetype' />
                                </link-entity>
                              </entity>
                            </fetch>";

                var results = service.RetrieveMultiple(new FetchExpression(fetch));

                var rows = new List<(Entity row, decimal unadj, decimal weightBase, bool isPassthrough, bool isManual, decimal adjustedVal)>();

                foreach (var compOppProd in results.Entities)
                {
                    var unit = compOppProd.GetAttributeValue<Money>("priceperunit")?.Value ?? 0m;

                    var qty = compOppProd.Contains("ats_quantity") ? (int)compOppProd["ats_quantity"] : 0;
                    var qtyevents = compOppProd.Contains("ats_quantityofevents") ? (int)compOppProd["ats_quantityofevents"] : 0;

                    bool isPassthrough = compOppProd.Attributes.Contains("Prod.ats_ispassthroughcost")
                        && (bool)((AliasedValue)compOppProd["Prod.ats_ispassthroughcost"]).Value;

                    bool isManual = compOppProd.GetAttributeValue<bool?>("ats_manualpriceoverride") ?? false;

                    int? rateTypeValue = null;
                    if (compOppProd.Contains("Rate.ats_ratetype") && compOppProd["Rate.ats_ratetype"] is AliasedValue av && av.Value is OptionSetValue osv)
                        rateTypeValue = osv.Value;

                    bool hasValidUnits = (rateTypeValue == 114300000) ? qty > 0 : (qty > 0 && qtyevents > 0);

                    // Keep your proration method as you had, but ensure unadj respects your formula
                    decimal unadj = hasValidUnits
                        ? ((rateTypeValue == 114300000) ? unit * qty : unit * qty * qtyevents)
                        : 0m;

                    decimal adjustedVal = compOppProd.GetAttributeValue<Money>("ats_adjustedtotalprice")?.Value ?? unadj;

                    decimal multiplier = (rateTypeValue == 114300000) ? qty : qty * qtyevents;

                    decimal finalAdjustedVal = adjustedVal * multiplier;
                    var weightBase = unadj * multiplier;

                    rows.Add((compOppProd, unadj, weightBase, isPassthrough, isManual, finalAdjustedVal));
                }

                decimal passthroughSum = rows.Where(r => r.isPassthrough).Sum(r => r.adjustedVal);
                decimal manualSum = rows.Where(r => r.isManual).Sum(r => r.adjustedVal);

                var eligOppProd = rows.Where(r => !r.isPassthrough && !r.isManual).ToList();
                decimal eligBaseSum = eligOppProd.Sum(r => r.weightBase);

                decimal remaining = packageOLINetAmount.Value - (passthroughSum + manualSum);

                tracingService.Trace($"passthroughSum: {passthroughSum}, manualSum: {manualSum}, eligBaseSum: {eligBaseSum}, remaining: {remaining}");

                decimal allocated = 0m;
                Guid lastEligId = eligOppProd.Count > 0 ? eligOppProd.Last().row.Id : Guid.Empty;

                foreach (var r in rows)
                {
                    var oppProduct = new Entity("opportunityproduct") { Id = r.row.Id };

                    if (r.isPassthrough || r.isManual)
                    {
                        oppProduct["ats_adjustedtotalprice"] = new Money(r.adjustedVal);
                        oppProduct["ats_unadjustedtotalprice"] = new Money(r.unadj);
                    }
                    else
                    {
                        decimal finalAdj;

                        if (eligOppProd.Count <= 1 || eligBaseSum == 0m)
                        {
                            finalAdj = Math.Round(remaining, 2, MidpointRounding.AwayFromZero);
                        }
                        else if (r.row.Id == lastEligId)
                        {
                            finalAdj = Math.Round(remaining - allocated, 2, MidpointRounding.AwayFromZero);
                        }
                        else
                        {
                            var raw = remaining * (r.weightBase / eligBaseSum);
                            finalAdj = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
                            allocated += finalAdj;
                        }

                        oppProduct["ats_adjustedtotalprice"] = new Money(finalAdj);
                        oppProduct["ats_unadjustedtotalprice"] = new Money(r.unadj);
                    }

                    service.Update(oppProduct);
                }

                tracingService.Trace($"Exit functionName: {functionName}");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"functionName: {functionName},Exception: {ex.Message}");
            }
        }

        // ==============================
        //
        // ==============================
        public void UpdateOppProdLines(Dictionary<string, string> args, IOrganizationService service, ITracingService tracingService)
        {
            Logging.Log("Inside UpdateOppProdLines******", tracingService);

            Entity OpportunityProduct = new Entity("opportunityproduct");
            Logging.Log("Before Parse(args[OppId]);*****", tracingService);

            OpportunityProduct.Attributes["opportunityproductid"] = Guid.Parse(args["OppProdId"]);

            string fieldName = string.Empty;
            string fieldVal = string.Empty;
            string legalFormatted = string.Empty;

            if (args.ContainsKey("ats_hardcost"))
            {
                Logging.Log("Inside ats_hardcost******", tracingService);
                fieldName = "ats_hardcost";
                fieldVal = args["ats_hardcost"];
                OpportunityProduct.Attributes[fieldName] = new Money(Convert.ToDecimal(fieldVal));
            }
            if (args.ContainsKey("ats_quantity"))
            {
                Logging.Log("Inside ats_quantity******", tracingService);
                fieldName = "ats_quantity";
                fieldVal = args["ats_quantity"];
                OpportunityProduct.Attributes[fieldName] = Convert.ToInt32(fieldVal);
            }
            if (args.ContainsKey("ats_quantityofevents"))
            {
                Logging.Log("Inside ats_quantityofevents******", tracingService);
                fieldName = "ats_quantityofevents";
                fieldVal = args["ats_quantityofevents"];
                OpportunityProduct.Attributes[fieldName] = Convert.ToInt32(fieldVal);
            }
            if (args.ContainsKey("ats_sellingrate"))
            {
                Logging.Log("Inside ats_sellingrate******", tracingService);
                fieldName = "ats_sellingrate";
                fieldVal = args["ats_sellingrate"];
                OpportunityProduct.Attributes[fieldName] = new Money(Convert.ToDecimal(fieldVal));
            }
            if (args.ContainsKey("description"))
            {
                Logging.Log("Inside description******", tracingService);
                fieldName = "description";
                fieldVal = args["description"];
                OpportunityProduct.Attributes[fieldName] = fieldVal;
            }

            if (args.ContainsKey("ats_legaldefinition"))
            {
                Logging.Log("Inside legaldefinition******", tracingService);
                fieldName = "ats_legaldef";
                fieldVal = args["LegalDefinition"];
                OpportunityProduct.Attributes[fieldName] = fieldVal;

                string qtyEventWords;
                string qtyWords = ConvertAmount(Convert.ToInt64(args["ats_quantity"]));
                Logging.Log("Qty Words******" + qtyWords, tracingService);

                legalFormatted = args["ats_legaldefinition"].ToString();

                if (args["ats_quantityofevents"] != null)
                {
                    qtyEventWords = ConvertAmount(Convert.ToInt64(args["ats_quantityofevents"]));
                    legalFormatted = legalFormatted.Replace("{#Events}", qtyEventWords + " (" + args["ats_quantityofevents"].ToString() + ")");
                    legalFormatted = legalFormatted.Replace("{#events}", qtyEventWords.ToLower() + " (" + args["ats_quantityofevents"].ToString() + ")");
                }

                legalFormatted = legalFormatted.Replace("{#units}", qtyWords.ToLower() + " (" + args["ats_quantity"].ToString() + ")");
                legalFormatted = legalFormatted.Replace("{#Units}", qtyWords + " (" + args["ats_quantity"].ToString() + ")");

                Logging.Log("Legal Definition Formatted******" + legalFormatted, tracingService);

                OpportunityProduct.Attributes["ats_legaldefinitionformatted"] = legalFormatted;
            }

            if (args.ContainsKey("ats_overwritelegaldefinition"))
            {
                Logging.Log("Inside overwritelegaldef******", tracingService);
                fieldName = "ats_overwritelegaldefinition";
                fieldVal = args["ats_overwritelegaldefinition"];

                Logging.Log("Overwrite Legal Definition******" + args["ats_overwritelegaldefinition"].ToString(), tracingService);

                if (fieldVal == "false")
                    OpportunityProduct.Attributes[fieldName] = false;
                else
                    OpportunityProduct.Attributes[fieldName] = true;
            }

            OpportunityProduct.Attributes["opportunityproductid"] = Guid.Parse(args["OppProdId"]);

            service.Update(OpportunityProduct);
            Logging.Log("After - service.ToString()" + service.ToString() + "****" + OpportunityProduct.ToString(), tracingService);
        }

        // ==============================
        // 
        // ==============================
        public string ValidateOppProdLines(Dictionary<string, string> args, IOrganizationService service, ITracingService tracingService)
        {
            Logging.Log("Before RetrieveMultiple In Validation******", tracingService);
            EntityCollection oppProductLinesRetrieved = service.RetrieveMultiple(new FetchExpression(string.Format(getIbsByOppProdId, args["OppProdId"])));
            Logging.Log("After RetrieveMultiple In Validation******", tracingService);

            var oppProductDetails = oppProductLinesRetrieved.Entities.FirstOrDefault();

            Logging.Log("oppProdQty : " + (oppProductDetails.Attributes["ats_quantity"]).ToString() + " oppProdQtyofEvents : " + (oppProductDetails.Attributes["ats_quantityofevents"]).ToString(), tracingService);

            var ibsQtyAvailable = oppProductDetails.Attributes.ContainsKey("IBS.ats_quantityavailable")
                ? (int)((AliasedValue)oppProductDetails.Attributes["IBS.ats_quantityavailable"]).Value
                : 0;

            var ibsOverselling = oppProductDetails.Attributes.ContainsKey("IBS.ats_allowoverselling")
                ? ((bool)((AliasedValue)oppProductDetails.Attributes["IBS.ats_allowoverselling"]).Value)
                : false;

            var ibsUnlimitedQuantity = oppProductDetails.Attributes.ContainsKey("IBS.ats_unlimitedquantity")
                ? ((bool)((AliasedValue)oppProductDetails.Attributes["IBS.ats_unlimitedquantity"]).Value)
                : false;

            var rateInactive = oppProductDetails.Attributes.ContainsKey("Rate.ats_inactive")
                ? ((bool)((AliasedValue)oppProductDetails.Attributes["Rate.ats_inactive"]).Value)
                : false;

            var esExpectedEventQty = oppProductDetails.Attributes.ContainsKey("ES.ats_expectedeventquantity")
                ? (int)((AliasedValue)oppProductDetails.Attributes["ES.ats_expectedeventquantity"]).Value
                : 1;

            var oppProdQty = oppProductDetails.Attributes.ContainsKey("ats_quantity")
                ? (int)oppProductDetails.Attributes["ats_quantity"]
                : 0;

            var oppProdQtyofEvents = oppProductDetails.Attributes.ContainsKey("ats_quantityofevents")
                ? (int)oppProductDetails.Attributes["ats_quantityofevents"]
                : 1;

            var type = (RateType)Convert.ToInt32(((OptionSetValue)((AliasedValue)oppProductDetails.Attributes["Rate.ats_ratetype"]).Value).Value);

            var newCartQty = Convert.ToInt32(args["CartQty"]);
            var newCartQtyOfEvents = Convert.ToInt32(args["CartQtyOfEvents"]);

            long lineQty = 0;
            long newLineQty = 0;

            if (type == RateType.Season)
            {
                lineQty = oppProdQty * esExpectedEventQty;
                newLineQty = newCartQty * esExpectedEventQty;
            }
            if (type == RateType.Individual)
            {
                lineQty = oppProdQty * oppProdQtyofEvents;
                newLineQty = newCartQty * newCartQtyOfEvents;
            }

            long newRequestedLineQty = newLineQty - lineQty;
            Logging.Log("New Requested Link Qty = " + newRequestedLineQty.ToString() + " ******", tracingService);

            if (rateInactive)
                return "ProductInactive";

            if (newRequestedLineQty > ibsQtyAvailable)
            {
                if (!ibsOverselling && !ibsUnlimitedQuantity)
                    return "OSNotAllowed";
            }

            return "Success";
        }

        #region Methods to Convert Numbers to Words
        public static string ConvertAmount(long amount)
        {
            long amount_int = amount;
            return ConvertValue(amount_int);
        }

        public static string ConvertValue(long i)
        {
            if (i < 20) return units[i];
            if (i < 100) return tens[i / 10] + ((i % 10 > 0) ? " " + ConvertValue(i % 10) : "");
            if (i < 1000) return units[i / 100] + " Hundred" + ((i % 100 > 0) ? " And " + ConvertValue(i % 100) : "");
            if (i < 100000) return ConvertValue(i / 1000) + " Thousand " + ((i % 1000 > 0) ? " " + ConvertValue(i % 1000) : "");
            if (i < 10000000) return ConvertValue(i / 100000) + " Million " + ((i % 100000 > 0) ? " " + ConvertValue(i % 100000) : "");
            if (i < 1000000000) return ConvertValue(i / 10000000) + " Million " + ((i % 10000000 > 0) ? " " + ConvertValue(i % 10000000) : "");
            return ConvertValue(i / 1000000000) + " Billion " + ((i % 1000000000 > 0) ? " " + ConvertValue(i % 1000000000) : "");
        }
        #endregion
    }

}
