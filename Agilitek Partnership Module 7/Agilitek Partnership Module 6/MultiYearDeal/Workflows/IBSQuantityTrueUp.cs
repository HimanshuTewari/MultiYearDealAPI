using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using MultiYearDeal.Workflows;
using System;
using System.Activities;
using System.IdentityModel.Metadata;
using System.Linq;
using System.Management.Instrumentation;

public class IBSQuantityTrueUp : CodeActivity
{
    enum RateType
    {
        Season = 114300000,
        Individual = 114300001
    }

    [Input("Record")]
    [ReferenceTarget("ats_inventorybyseason")]   
    public InArgument<EntityReference> Record { get; set; }


    [Output("Response")]
    public OutArgument<string> Response { get; set; }

    protected override void Execute(CodeActivityContext context)
    {
        string functionName = "Execute";

        IWorkflowContext workflowContext = context.GetExtension<IWorkflowContext>();
        IOrganizationServiceFactory serviceFactory = context.GetExtension<IOrganizationServiceFactory>();
        IOrganizationService service = serviceFactory.CreateOrganizationService(workflowContext.UserId);
        ITracingService tracingService = context.GetExtension<ITracingService>();

        try
        {
            TraceHelper.Initialize(service);
            TraceHelper.Trace(tracingService, "Tracing initialized");


            EntityReference record = Record.Get(context);

            string recordId = record.Id.ToString();
            TraceHelper.Trace(tracingService, "Inputs =>RecordId: {0}", recordId);

            if (string.IsNullOrEmpty(recordId))
            {
                TraceHelper.Trace(tracingService, "RecordId is null or empty. Exiting.");
                return;
            }

            Entity ibsRecord = service.Retrieve("ats_inventorybyseason", new Guid(recordId), new ColumnSet("ats_totalquantity"));
            // Build FetchXML with dynamic record ID
            string fetchXml = $@"
                            <fetch>
                              <entity name='ats_inventorybyseason'>
                                <!-- Attributes from ats_inventorybyseason -->
                                <attribute name='ats_totalquantity' />

                                <filter>
                                  <condition attribute='ats_inventorybyseasonid' operator='eq' value='{recordId}' />
                                </filter>

                                <!-- Link to OpportunityProduct -->
                                <link-entity name='opportunityproduct' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='inner' alias='OppProd'>
                                  <attribute name='ats_quantity' />
                                  <attribute name='ats_quantityofevents' />

                                  <!-- Link to Opportunity -->
                                  <link-entity name='opportunity' from='opportunityid' to='opportunityid' alias='Opp'>
                                    <attribute name='statecode' />
                                    <attribute name='ats_bpfstatus' />
                                    <attribute name='opportunityid' />
                                  </link-entity>
                                </link-entity>

                                <!-- Link to Rate -->
                                <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' link-type='outer' alias='Rate'>
                                  <attribute name='ats_ratetype' />
                                </link-entity>
                              </entity>
                            </fetch>";

            EntityCollection results = service.RetrieveMultiple(new FetchExpression(fetchXml));
            TraceHelper.Trace(tracingService, "Fetched {0} records.", results.Entities.Count);

            Entity ibs = results.Entities.FirstOrDefault();

            #region If no related OpportunityProduct records are found, update IBS with Total Quantity
            // 
            if (ibs == null)
            {
                TraceHelper.Trace(tracingService, "No Opportunity product is associated with the IBS ");

                int ibsTotalQty = ibsRecord.Contains("ats_totalquantity") ? ((int)ibsRecord["ats_totalquantity"]) : 0;
                if (ibsTotalQty != 0)
                {
                    ibsRecord["ats_quantityavailable"] = ibsTotalQty;
                    ibsRecord["ats_syncstatus"] = new OptionSetValue(114300001); //Completed
                    ibsRecord["ats_quantitypitched"] = 0;
                    ibsRecord["ats_quantitysold"] = 0;
                    service.Update(ibsRecord);
                    TraceHelper.Trace(tracingService, "IBS record updated successfully with Quantity Available as Total Quantity.");
                    return;
                }
                else
                {
                    TraceHelper.Trace(tracingService, "Total Quantity field is 0 for IBS record {0}", recordId);
                    ibsRecord["ats_quantityavailable"] = ibsTotalQty;
                    ibsRecord["ats_syncstatus"] = new OptionSetValue(114300001); //Completed
                    ibsRecord["ats_quantitypitched"] = 0;
                    ibsRecord["ats_quantitysold"] = 0;
                    service.Update(ibsRecord);
                    TraceHelper.Trace(tracingService, "IBS record updated successfully with Quantity Available as Total Quantity.");
                    return;
                }
            }
            #endregion

            int agreeTotalQty = ibs.Contains("ats_totalquantity") ? ((int)ibs["ats_totalquantity"]) : 0;

            int? qtyAvailable = 0;
            int? qtyPitchedAttr = 0;
            int? qtySoldAttr = 0;

            foreach (var entity in results.Entities)
            {
                //Retrieve the total quantity of the IBS record
                int totalQty = entity.Contains("ats_totalquantity") ? ((int)entity["ats_totalquantity"]) : 0;

                Guid? opportunityId = entity.Contains("Opp.opportunityid") ? (Guid?)((AliasedValue)entity["Opp.opportunityid"]).Value : null;

                // opportunityproduct attributes
                int? quantity = entity.Contains("OppProd.ats_quantity")
                    ? (int?)((AliasedValue)entity["OppProd.ats_quantity"]).Value
                    : null;

                int? quantityOfEvents = entity.Contains("OppProd.ats_quantityofevents")
                    ? (int?)((AliasedValue)entity["OppProd.ats_quantityofevents"]).Value
                    : null;

                // opportunity attributes
                OptionSetValue statecode = entity.Contains("Opp.statecode")
                    ? (OptionSetValue)((AliasedValue)entity["Opp.statecode"]).Value
                    : null;

                OptionSetValue oppBPFStatus = entity.Contains("Opp.ats_bpfstatus")
                    ? (OptionSetValue)((AliasedValue)entity["Opp.ats_bpfstatus"]).Value
                    : null;

                // ats_rate attributes
                OptionSetValue ratetype = entity.Contains("Rate.ats_ratetype")
                    ? (OptionSetValue)((AliasedValue)entity["Rate.ats_ratetype"]).Value
                    : null;

                // Trace all variables
                TraceHelper.Trace(
                    tracingService,
                    "QtyAvailable: {0}, QtyPitched: {1}, QtySold: {2}, OppProd.Quantity: {3}, OppProd.Events: {4}, Opp.StateCode: {5}, Opp.BPFStatus: {6}, Rate.RateType: {7}",
                    qtyAvailable,
                    qtyPitchedAttr,
                    qtySoldAttr,
                    quantity,
                    quantityOfEvents,
                    statecode?.Value,
                    oppBPFStatus?.Value,
                    ratetype?.Value
                );

                //Logic for updating the IBS record based on the fetched data
                #region opportunity is open
                if (Convert.ToInt32(statecode.Value) == 0) //Open
                {
                    TraceHelper.Trace(tracingService, "statecode ==0");
                    if (Convert.ToInt32(ratetype.Value) == 114300000) //Season
                    {
                        TraceHelper.Trace(tracingService, "ratetype == Season");
                        if (oppBPFStatus.Value == 114300000) // Pre-Pitched
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Pre-Pitched, Have to do nothing");
                        }
                        else if (oppBPFStatus.Value == 114300001) //Pitched
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Pitched, Have to update the IBS");
                            //calculating the pitched variable 
                            qtyPitchedAttr = qtyPitchedAttr + quantity;
                            TraceHelper.Trace(tracingService, "New qtyPitchedAttr is {0}", qtyPitchedAttr);
                        }
                        else if (oppBPFStatus.Value == 114300003) //Closed-Won 
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Closed-Won, Have to update the IBS");
                            //Calculating the Quantity sold variable
                            qtySoldAttr = qtySoldAttr + quantity;

                            //calculating the Quantity Available variable
                            qtyAvailable = totalQty - quantity;
                            TraceHelper.Trace(tracingService, "New qtySoldAttr is {0}, New qtyAvailable is {1}", qtySoldAttr, qtyAvailable);
                        }
                        else if (oppBPFStatus.Value == 114300004) //Clsoed Lost
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Closed-Lost, No need for update");
                        }
                        else
                        {
                            ibsRecord["ats_syncstatus"] = new OptionSetValue(114300002); //failed
                            service.Update(ibsRecord);

                            TraceHelper.Trace(tracingService, "oppBPFStatus is {0}, no action taken, for opportunity: {1}", oppBPFStatus.Value, opportunityId);
                            throw new InvalidPluginExecutionException(string.Format("oppBPFStatus is null, no action taken, for opportunity: {0}", opportunityId));
                        }
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "ratetype == Individual");
                        if (oppBPFStatus.Value == 114300000) // Pre-Pitched
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Pre-Pitched, Have to do nothing");
                        }
                        else if (oppBPFStatus.Value == 114300001) //Pitched
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Pitched, Have to update the IBS");
                            //calculating the pitched variable 
                            qtyPitchedAttr = qtyPitchedAttr + (quantity * quantityOfEvents);
                            TraceHelper.Trace(tracingService, "New qtyPitchedAttr is {0}", qtyPitchedAttr);
                        }
                        else if (oppBPFStatus.Value == 114300003) //Closed-Won 
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Closed-Won, Have to update the IBS");
                            //Calculating the Quantity sold variable
                            qtySoldAttr = qtySoldAttr + (quantity * quantityOfEvents);

                            //calculating the Quantity Available variable
                            qtyAvailable = totalQty - (quantity * quantityOfEvents);
                            TraceHelper.Trace(tracingService, "New qtySoldAttr is {0}, New qtyAvailable is {1}", qtySoldAttr, qtyAvailable);
                        }
                        else if (oppBPFStatus.Value == 114300004)
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus == Closed-Lost, No need for update");
                        }
                        else
                        {
                            TraceHelper.Trace(tracingService, "oppBPFStatus is {0}, no action taken, for opportunity: {1}", oppBPFStatus.Value, opportunityId);
                        }
                    }
                }
                #endregion

                #region opportunity is won 
                else if (Convert.ToInt32(statecode.Value) == 1) //Won
                {
                    TraceHelper.Trace(tracingService, "statecode ==1");
                    if (Convert.ToInt32(ratetype.Value) == 114300000) //Season
                    {
                        TraceHelper.Trace(tracingService, "ratetype == Season");

                        TraceHelper.Trace(tracingService, "oppBPFStatus == Closed-Won, Have to update the IBS");
                        //Calculating the Quantity sold variable
                        qtySoldAttr = qtySoldAttr + quantity;

                        //calculating the Quantity Available variable
                        qtyAvailable = totalQty - quantity;
                        TraceHelper.Trace(tracingService, "New qtySoldAttr is {0}, New qtyAvailable is {1}", qtySoldAttr, qtyAvailable);
                    }
                    else
                    {
                        TraceHelper.Trace(tracingService, "ratetype == Individual");

                        TraceHelper.Trace(tracingService, "oppBPFStatus == Closed-Won, Have to update the IBS");
                        //Calculating the Quantity sold variable
                        qtySoldAttr = qtySoldAttr + (quantity * quantityOfEvents);

                        //calculating the Quantity Available variable
                        qtyAvailable = totalQty - (quantity * quantityOfEvents);
                        TraceHelper.Trace(tracingService, "New qtySoldAttr is {0}, New qtyAvailable is {1}", qtySoldAttr, qtyAvailable);
                    }
                }
                #endregion

                else if (Convert.ToInt32(statecode.Value) == 2)//Lost
                {
                    TraceHelper.Trace(tracingService, "statecode ==2");
                    TraceHelper.Trace(tracingService, "No action needed for Lost Opportunity: {0}", opportunityId);
                }
            }

            TraceHelper.Trace(tracingService, "Updating the IBS record {0} with qtyPitched: {1}, qtySold: {2}, qtyAvailable: {3}", ibs.Id, qtyPitchedAttr, qtySoldAttr, qtyAvailable);

            try
            {
                ibs["ats_quantitypitched"] = qtyPitchedAttr;
                ibs["ats_quantitysold"] = qtySoldAttr;
                ibs["ats_quantityavailable"] = qtyAvailable;
                ibs["ats_syncstatus"] = new OptionSetValue(114300001); //Completed
                service.Update(ibs);
                TraceHelper.Trace(tracingService, "IBS record updated successfully.");

                TraceHelper.Trace(tracingService, "Exit functionName: {0}", functionName);
                Response.Set(context, "Success");
            }
            catch (InvalidPluginExecutionException ex)
            {
                ibs["ats_syncstatus"] = new OptionSetValue(114300002); //Failed
                service.Update(ibs);
                TraceHelper.Trace(tracingService, "IBS update failed with error: {0}", ex.Message);
                throw new InvalidPluginExecutionException("IBS update failed");
            }


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

