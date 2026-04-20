using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//Manage Inventory of IBS
namespace Agilitek_Partnership
{
    public class SetCalculatedFields
    {
        string getIbsByOppId = @"<fetch>
  <entity name='opportunity'>
    <attribute name='opportunityid' />
    <attribute name='statuscode' />
    <filter>
      <condition attribute='opportunityid' operator='eq' value='{0}' />
    </filter>
    <link-entity name='opportunityproduct' from='opportunityid' to='opportunityid' link-type='inner' alias='oppProd'>
      <attribute name='opportunityproductid' />
      <attribute name='ats_quantity' />
      <attribute name='ats_quantityofevents' />
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


        public void RecalculateIbsSoldAndPitchedQty(Dictionary<string,string> args, IOrganizationService service, ITracingService tracingService)
        {
            Logging.Log("Inside RecalculateIbsSoldQtyFromOpp******", tracingService);
            Logging.Log("Before retrievedIbsByOppId******", tracingService);


            var query = string.Empty;

            if (args.ContainsKey("OppId"))
            {
                query= string.Format(getIbsByOppId, args["OppId"]);
            }
            else if (args.ContainsKey("OppProdId"))
            {
                query = string.Format(getIbsByOppProdId, args["OppProdId"]);
            }

            EntityCollection retrievedIbs = service.RetrieveMultiple(new FetchExpression(query));

            Dictionary<string, int> ibsIdAndOppLineQty = new Dictionary<string, int>(); // Calculates the qty fields per ibs in Opp Lines
            Dictionary<string, int> ibsIdAndIbsSoldQty = new Dictionary<string, int>(); // Calculates the qty already SOLD for ibs linked with Opp Lines
            Dictionary<string, int> ibsIdAndIbsPitchedQty = new Dictionary<string, int>(); // Calculates the qty already PITCHED for ibs linked with Opp Lines

            OppStatusReason oppStatusReason = (OppStatusReason)Convert.ToInt32(((OptionSetValue)retrievedIbs.Entities.FirstOrDefault().Attributes["statuscode"]).Value);

            Logging.Log("oppStatusReason = " + oppStatusReason.ToString(), tracingService);

            foreach (var oppProd in retrievedIbs.Entities)
            {
                Logging.Log("Before ((Entity)oppProd.Attributes[ibs.ats_inventorybyseasonid])  " + ((AliasedValue)oppProd.Attributes["ibs.ats_inventorybyseasonid"]), tracingService);
                var ibsId = ((AliasedValue)oppProd.Attributes["ibs.ats_inventorybyseasonid"]).Value.ToString();
                Logging.Log("Before ((Entity)oppProd.Attributes[ibs.ats_inventorybyseasonid])  " + ((AliasedValue)oppProd.Attributes["ibs.ats_inventorybyseasonid"]).Value.ToString(), tracingService);


                Logging.Log("Before Convert.ToInt32(oppProd.Attributes[ibs.ats_quantitysold])  ", tracingService);
                var ibsSoldQty = oppProd.Attributes.ContainsKey("ibs.ats_quantitysold") ?
                    Convert.ToInt32(((AliasedValue)oppProd.Attributes["ibs.ats_quantitysold"]).Value) : 0;


                Logging.Log("Before Convert.ToInt32(oppProd.Attributes[ibs.ats_quantitypitched])  ", tracingService);
                var ibsPitchedQty = oppProd.Attributes.ContainsKey("ibs.ats_quantitypitched") ?
                     Convert.ToInt32(((AliasedValue)oppProd.Attributes["ibs.ats_quantitypitched"]).Value) : 0;

                Logging.Log("Before Convert.ToInt32(oppProd.Attributes[oppProd.ats_quantity])  ", tracingService);
                var oppProdQty = oppProd.Attributes.ContainsKey("oppProd.ats_quantity") ?
                    Convert.ToInt32(((AliasedValue)oppProd.Attributes["oppProd.ats_quantity"]).Value) : 1;

                Logging.Log("Before Convert.ToInt32(oppProd.Attributes[oppProd.ats_quantityofevents])  ", tracingService);
                var oppProdQtyOfEvents = oppProd.Attributes.ContainsKey("oppProd.ats_quantityofevents") ?
                    Convert.ToInt32(((AliasedValue)oppProd.Attributes["oppProd.ats_quantityofevents"]).Value) : 1;


                Logging.Log("Before Convert.ToInt32(oppProd.Attributes[es.ats_expectedeventquantity])  ", tracingService);
                var expectedEventQty = oppProd.Attributes.ContainsKey("es.ats_expectedeventquantity") ?
                    Convert.ToInt32(((AliasedValue)oppProd.Attributes["es.ats_expectedeventquantity"]).Value) : 1;

                Logging.Log("Before Convert.ToInt32(((OptionSetValue)((AliasedValue)oppProd.Attributes[r.ats_ratetype]).Value).Value)  ", tracingService);
                var type = (RateType)Convert.ToInt32(((OptionSetValue)((AliasedValue)oppProd.Attributes["r.ats_ratetype"]).Value).Value);

                Logging.Log("************************TYPE******************: " + type.ToString(), tracingService);
                Logging.Log("oppProdQty: " + oppProdQty.ToString(), tracingService);
                Logging.Log("expectedEventQty: " + expectedEventQty.ToString(), tracingService);
                Logging.Log("oppProdQtyOfEvents: " + oppProdQtyOfEvents.ToString(), tracingService);

                var lineQty = 0;

                if (type == RateType.Season)
                {
                    lineQty = oppProdQty * expectedEventQty;
                }
                if (type == RateType.Individual)
                {
                    lineQty = oppProdQty * oppProdQtyOfEvents;
                }

                Logging.Log("After lineQty: " + lineQty.ToString(), tracingService);

                if (ibsIdAndOppLineQty.ContainsKey(ibsId))
                {
                    // Value of key value pair ibsAndQty[key], key = ibsId
                    ibsIdAndOppLineQty[ibsId] = ibsIdAndOppLineQty[ibsId] + lineQty;
                    Logging.Log("After adding ibsIdAndOppLineQty[ibsId]: " + ibsIdAndOppLineQty[ibsId].ToString(), tracingService);
                    //ibsIdAndIbsSoldQty[ibsId] = ibsIdAndIbsSoldQty[ibsId] + ibsSoldQty;
                    //Logging.Log("After adding ibsIdAndIbsSoldQty[ibsId]" + ibsIdAndIbsSoldQty[ibsId].ToString(), tracingService);
                    //ibsIdAndIbsPitchedQty[ibsId] = ibsIdAndIbsPitchedQty[ibsId] + ibsPitchedQty;
                    //Logging.Log("After adding ibsIdAndIbsPitchedQty[ibsId]: " + ibsIdAndIbsPitchedQty[ibsId].ToString(), tracingService);
                }
                else
                {
                    ibsIdAndOppLineQty.Add(ibsId, lineQty);
                    Logging.Log("After ibsIdAndOppLineQty.Add(ibsId, lineQty); " + ibsIdAndOppLineQty[ibsId].ToString(), tracingService);
                    ibsIdAndIbsSoldQty.Add(ibsId, ibsSoldQty);
                    Logging.Log("After ibsIdAndIbsSoldQty.Add(ibsId, ibsSoldQty); " + ibsIdAndOppLineQty[ibsId].ToString(), tracingService);
                    ibsIdAndIbsPitchedQty.Add(ibsId, ibsPitchedQty);
                    Logging.Log("After ibsIdAndIbsPitchedQty.Add(ibsId, ibsPitchedQty); " + ibsIdAndOppLineQty[ibsId].ToString(), tracingService);
                }
            }

            foreach (var ibs in ibsIdAndOppLineQty)
            {
                // check Opp status and update (maybe switch) qty sold and qty pitched
                Entity ibsToUpdate = new Entity("ats_inventorybyseason");
                ibsToUpdate["ats_inventorybyseasonid"] = Guid.Parse(ibs.Key);

                var existingIbsSoldQty = ibsIdAndIbsSoldQty.Where(item => item.Key == ibs.Key).Select(item => item.Value).Sum();
                Logging.Log("After existingIbsSoldQty: " + existingIbsSoldQty.ToString(), tracingService);
                var existingIbsPitchedQty = ibsIdAndIbsPitchedQty.Where(item => item.Key == ibs.Key).Select(item => item.Value).Sum();
                Logging.Log("After existingIbsPitchedQty: " + existingIbsPitchedQty.ToString(), tracingService);

                Logging.Log("After aexistingIbsSoldQty: " + existingIbsSoldQty.ToString(), tracingService);

                // check Opp status and update qty sold and qty pitched
                if (oppStatusReason == OppStatusReason.Contract)
                {
                    ibsToUpdate["ats_quantitysold"] = existingIbsSoldQty + ibs.Value;
                    Logging.Log("After ibsToUpdate[ats_quantitysold]" + ibsToUpdate["ats_quantitysold"].ToString(), tracingService);
                    //ibsToUpdate["ats_quantitypitched"] = existingIbsPitchedQty - ibs.Value;
                    //Logging.Log("After ibsToUpdate[ats_quantitypitched]" + ibsToUpdate["ats_quantitypitched"].ToString(), tracingService);
                }
                if (oppStatusReason == OppStatusReason.Proposal)
                {
                    //ibsToUpdate["ats_quantitysold"] = existingIbsSoldQty - ibs.Value;
                    //Logging.Log("After ibsToUpdate[ats_quantitysold]" + ibsToUpdate["ats_quantitysold"].ToString(), tracingService);
                    ibsToUpdate["ats_quantitypitched"] = existingIbsPitchedQty + ibs.Value;
                    Logging.Log("After ibsToUpdate[ats_quantitypitched]" + ibsToUpdate["ats_quantitypitched"].ToString(), tracingService);
                }
                if (oppStatusReason == OppStatusReason.Opportunity)
                {
                    // ? 
                }

                Logging.Log("Before service.Update(ibsToUpdate)", tracingService);
                service.Update(ibsToUpdate);
                Logging.Log("After service.Update(ibsToUpdate)", tracingService);
            }
        }
    }
}