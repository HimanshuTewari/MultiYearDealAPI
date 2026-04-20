import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import { createRoot } from "react-dom/client";
import { sampleInventory, sampleLineItems, sampleOpportunities } from "./sampleData";
import axios from "axios";
import { AgreementCartProps, HiddenFields, InventoryData, LineItemData, OpportunitiesData, OpportunityData } from "./models";
import Main from './main';

export class AgreementCart
  implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private _container: HTMLDivElement;
  private _context: ComponentFramework.Context<IInputs>;


  /**
   * Empty constructor.
   */
  constructor() { }

  /**
   * Used to initialize the control instance. Controls can kick off remote server calls and other initialization actions here.
   * Data-set values are not initialized here, use updateView.
   * @param context The entire property bag available to control via Context Object; It contains values as set up by the customizer mapped to property names defined in the manifest, as well as utility functions.
   * @param notifyOutputChanged A callback method to alert the framework that the control has new outputs ready to be retrieved asynchronously.
   * @param state A piece of data that persists in one session for a single user. Can be set at any point in a controls life cycle by calling 'setControlState' in the Mode interface.
   * @param container If a control is marked control-type='standard', it will receive an empty div element within which it can render its content.
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this._container = document.createElement("div");
    this._context = context;
    container.appendChild(this._container);
  }

  /**
   * Called when any value in the property bag has changed. This includes field values, data-sets, global values such as container height and width, offline status, control metadata values such as label, visible, etc.
   * @param context The entire property bag available to control via Context Object; It contains values as set up by the customizer mapped to names defined in the manifest, as well as utility functions
   */
  public async updateView(context: ComponentFramework.Context<IInputs>): Promise<void> {

    console.log("Context", this._context);
    console.log('Refreshing data');

    const isLocal = false; // Set to true if running locally for testing

    if (!this._context) this._context = context;
    const entityId = (<any>context.mode).contextInfo.entityId;

    const root = createRoot(this._container);

    // --- Fetch system setting, hardcode for local testing ---
    let alternateUI = false;
    if (isLocal) {
      alternateUI = true;
    }
    else {
      const response = await context.webAPI.retrieveMultipleRecords(
        "ats_agiliteksettings",
        fetchXML.settings
      );
      alternateUI = response.entities.length > 0 && response.entities[0].ats_value === "true" ? true : false;
    }
    console.log("Alternate UI: " + alternateUI);
    
    const data = await this.retrieveData(context);

    console.log("Data from API");
    console.log(data);

    // Extract arrays from new response structure
    var inventoryData = Array<InventoryData>(); // assuming you populate later
    var opportunitiesData: OpportunitiesData = {} as OpportunitiesData
    var opportunityData = Array<OpportunityData>();
    var lineItemData = Array<LineItemData>(); // assuming you populate later

    // Optional: use or log hidden fields
    var hiddenFields = {} as HiddenFields;

    var isAuthorized = true;

    if (data) {
      inventoryData = JSON.parse(data["InventoryDataOutput"]);
      opportunitiesData = JSON.parse(data["OpportunityDataOutput"]);
      opportunityData = opportunitiesData.Opportunities;
      hiddenFields = opportunitiesData.HiddenFields;
      isAuthorized = opportunitiesData.isAuthorized;
      lineItemData = JSON.parse(data["LineItemDataOutput"]);
    }

    console.log("Inventory Data");
    console.log(inventoryData);
    console.log("------------------------------------------------");
    console.log("Opportunity Data");
    console.log(opportunityData);
    console.log("------------------------------------------------");
    console.log("Hidden Fields");
    console.log(hiddenFields);
    console.log("------------------------------------------------");
    console.log("Line Item Data");
    console.log(lineItemData);
    console.log("------------------------------------------------");
    console.log("Is Authorized");
    console.log(isAuthorized);

    if (!data) {
      opportunityData = sampleOpportunities;
      inventoryData = sampleInventory;
      lineItemData = sampleLineItems;
    }
    console.log('Finished refreshing data');

    root.render(React.createElement(Main, {
      opportunities: opportunityData,
      lineItems: lineItemData,
      inventory: inventoryData,
      hiddenFields: hiddenFields,
      isAuthorized: isAuthorized,
      agreementId: entityId,
      alternateUI: alternateUI,
      context: this._context,
      updateView: () => this.updateView(this._context)//Sunny (18-03-25)
    }));

    // root.render(React.createElement(Main, {
    //     opportunities: sampleOpportunities,
    //     lineItems: sampleLineItems,
    //     inventory: sampleInventory
    // }));
  }

  /**
   * It is called by the framework prior to a control receiving new data.
   * @returns an object based on nomenclature defined in manifest, expecting object[s] for property marked as "bound" or "output"
   */
  public getOutputs(): IOutputs {
    return {};
  }

  /**
   * Called when the control is to be removed from the DOM tree. Controls should use this call for cleanup.
   * i.e. cancelling any pending remote calls, removing listeners, etc.
   */
  public destroy(): void {
    // Add code to cleanup control if necessary
  }

  // ================================================
  // Methods
  // ================================================
  // private async retrieveInventoryData(context: ComponentFramework.Context<IInputs>): Promise<void> {
  //   let entityId = (<any>context.mode).contextInfo.entityId;

  //   console.log("Entity Id: " + entityId);

  //   let response= await context.webAPI.retrieveMultipleRecords("ats_agreement", 
  //       fetchXML.invenotyData.replace("{agreementId}", entityId));
  //       console.log("Inventory Data");
  //   console.log(response.entities);
  //   console.log("------------------------------------------------");
  // }

  // private async retrieveOpportunityData(context: ComponentFramework.Context<IInputs>): Promise<void> {
  //   let entityId = (<any>context.mode).contextInfo.entityId;

  //   let response= await context.webAPI.retrieveMultipleRecords("opportunity", 
  //       fetchXML.opportunityData.replace("{agreementId}", entityId));

  //       console.log("Opportunity Data");
  //       console.log(response.entities);
  //       console.log("------------------------------------------------");
  // }


  private async retrieveData(context: ComponentFramework.Context<IInputs>): Promise<any> {
    let entityId = (<any>context.mode).contextInfo.entityId;

    var BASE_URL = window.location.origin + "/api/data/v9.0/ats_AgreementCartData";

    let d: AgreementCartProps = {
      AgreementId: entityId
    }

    try {
      let response = await axios.post(BASE_URL, JSON.stringify(d), {
        headers: {
          "Content-Type": "application/json; charset=utf-8",
          "Accept": "application/json",
          "OData-MaxVersion": "4.0",
          "OData-Version": "4.0"
        },
        maxBodyLength: Infinity
      });

      console.log("Response from API");
      return response.data;

    } catch (error) {
      console.error("Error retrieving data:", error);
    }
  }

}

const fetchXML = {
  invenotyData: `?fetchXml=<fetch  output-format='xml-platform' mapping='logical'>
     <entity name='ats_agreement'>
       <filter>
         <condition attribute='ats_agreementid' operator='eq' value='{agreementId}' />
       </filter>
       <link-entity name='ats_inventorybyseason' from='ats_season' to='ats_startseason' alias='IBS'>
         <attribute name='ats_quantityavailable' />
         <attribute name='ats_totalquantity' />
         <attribute name='ats_totalquantityperevent' />
         <filter>
           <condition attribute='statecode' operator='eq' value='0' />
         </filter>
         <link-entity name='ats_rate' from='ats_inventorybyseason' to='ats_inventorybyseasonid' alias='Rate'>
           <attribute name='ats_hardcost' />
           <attribute name='ats_hardcost2' />
           <attribute name='ats_lockhardcost' />
           <attribute name='ats_price' />
           <attribute name='ats_rateid' />
           <attribute name='ats_ratetype' />
           <attribute name='ats_lockunitrate' />
           <filter>
             <condition attribute='statecode' operator='eq' value='0' />
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
           </filter>
         </link-entity>
       </link-entity>
     </entity>
   </fetch>`,
  opportunityData: `?fetchXml=<fetch output-format='xml-platform' mapping='logical'>
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
       <attribute name='ats_escalationtype' />
       <attribute name='ats_escalationvalue' />
       <attribute name='ats_startseason' />
       <filter>
         <condition attribute='ats_agreement' operator='eq' value='{agreementId}' />
       </filter>
       <link-entity name='ats_season' from='ats_seasonid' to='ats_startseason' alias='Season'>
         <attribute name='ats_name' />
       </link-entity>
     </entity>
   </fetch>`,
  settings: `?fetchXml=<fetch top='1'>
    <entity name='ats_agiliteksettings'>
      <attribute name='ats_key' />
      <attribute name='ats_value' />
      <filter>
          <condition attribute='ats_key' operator='eq' value='Alternate Agreement Grid Layout' />
      </filter>
    </entity>
  </fetch>`
}