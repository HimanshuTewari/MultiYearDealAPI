export interface OpportunityData {
    Id: string;
    DealValue: number;
    AutomaticAmount: number;
    ManualAmount: number;
    PricingMode: "Automatic" | "Manual",
    TotalHardCost: number;
    TotalProductionCost: number;
    TotalRateCard: number;
    PercentOfRate: number;
    PercentOfRateCard: number;
    BarterAmount?: number | null;
    TargetAmount?: number | null;
    CashAmount?: number | null;
    EscalationType: null | "Fixed" | "Percent";
    EscalationValue: null | number;
    SeasonName: string;
    StartSeason: string;
    IsFirstYear?: boolean;
}


//#region Sunny --> Add Product Extension
export interface AddProductInitialResponse {
    AddProductOpportunityGuid?: string;
    AgreementCartActionbatchingActionName?: string;
    AgreementOpportunityUniqueGuid?: string;
    NewAddProductOpportunityGuid?: string | null;
    isAddProductBatching?: boolean;
    response?: string | null;
    error?: any;
    message?: string;
    details?: string;
}

export interface AddProductBatchingResponse {
    AddProductOpportunityGuid?: string;
    NewAddProductOpportunityGuid?: string;
    AgreementCartActionbatchingActionName?: string;
    isAddProductBatching?: boolean;
    response?: string | null;
    error?: any;
    message?: string;
    details?: string;
}

export interface AddProductEscalateResponse {
    response?: string | null;
    error?: any;
    message?: string;
    details?: string;
}

export interface AddProductResponse {
    AgreementOpportunityUniqueGuid?: string;
    AddProductOpportunityGuid?: string;
    AgreementCartActionbatchingActionName?: string;
    isAddProductBatching?: boolean;
    response?: string;
    error?: any;
    message?: string;
    details?: string;
}

export interface AddProductFlowResult {
    success: boolean;
    initialResponse?: AddProductInitialResponse;
    batchingResponses?: AddProductBatchingResponse[];
    finalBatchingResponse?: AddProductBatchingResponse;
    escalationResponse?: any;
    error?: any;
    message?: string;
    details?: string;
}



//#endregion


//#region Bulk Add-Products flow (ats_AddProductsBatch + ats_CheckProductsAvailability)
// Wire matches the C# plugins CustomAPICheckProductsAvailability.cs and
// CustomAPIAddProductsBatch.cs in MultiYearDeal/Plugins.
//
// V2 (17-Apr-2026):
//   - "use_legacy_flow" verdict removed — packages are handled by the
//     batch plugin directly.
//   - CheckProductsAvailabilityPair now surfaces ibsAutoCreated /
//     rateAutoCreated booleans so the UI can tell the user that missing
//     records were auto-generated.
//   - AddProductsBatchPayload carries leftoverProducts / processedCount /
//     totalCount / failedProducts to support the soft-timeout loop. The
//     PCF keeps calling the endpoint with `leftoverProducts` as the next
//     request until leftoverProducts is empty.
//   - AddProductsBatchRequestProduct carries an optional PackageComponents
//     array so package / template products are created in the same flow.

export type AvailabilityVerdict =
    | "ok"
    | "ibs_missing"
    | "rate_missing"
    | "oversell_blocked";

export interface CheckProductsAvailabilityPair {
    productId: string;
    seasonId: string | null;
    verdict: AvailabilityVerdict;
    ibsExists: boolean;
    rateExists: boolean;
    qtyAvailable: number;
    allowOverselling: boolean;
    unlimitedQuantity: boolean;
    ibsId?: string;
    rateId?: string;
    /** True when the backend auto-generated the IBS during this check. */
    ibsAutoCreated?: boolean;
    /** True when the backend auto-generated the Rate during this check. */
    rateAutoCreated?: boolean;
}

export interface CheckProductsAvailabilityPayload {
    allOk: boolean;
    generatedAt: string;
    results: CheckProductsAvailabilityPair[];
}

export interface CheckProductsAvailabilityResponse {
    response?: string;
    error?: any;
    message?: string;
    details?: string;
}

export type AddProductsBatchErrorCode =
    | "ibs_missing"
    | "rate_missing"
    | "transaction_failed"
    | "input_invalid";

/** Individual per-OLI failure surfaced by the backend. */
export interface AddProductsBatchFailedOli {
    oliId: string;
    productId: string;
    reason: string;
}

/**
 * Pre-resolved OLI specification the backend produces on the first call
 * (via the `products` input) and consumes on resume calls (via the `olis`
 * input). Every field is already resolved — the backend does NO further
 * lookups on resume; it just maps each spec to a Create (or a batched
 * CreateMultiple).
 */
export interface AddProductsBatchOliSpec {
    oliId: string;                     // pre-assigned Guid (string form)
    opportunityId: string;
    productId: string;
    ibsId: string;
    rateId: string;
    rateType: number;                  // 114300000 (Season) | 114300001 (Individual)
    qtyUnits: number;
    qtyEvents: number;
    rate: number;
    hardCost: number;
    productionCost: number;
    description?: string | null;
    uomId?: string | null;
    agreementOppProductTag?: string | null;
    packageLineItemOliId?: string | null;   // parent OLI's pre-assigned id; null for non-components
}

export interface AddProductsBatchPayload {
    success: boolean;
    agreementId: string;
    /** OLIs processed during THIS call (succeeded + failed). */
    processedOliCount: number;
    /** Total OLIs the backend received as input for THIS call. PCF captures this from the first response and uses it as the stable progress total. */
    totalOliCount: number;
    createdOpportunityProductIds: string[];
    /** Per-OLI errors — rolled back on the server. */
    failedOlis: AddProductsBatchFailedOli[];
    /** OLI specs not processed in this call due to soft-timeout; resend as-is on the next call. */
    leftoverOlis: AddProductsBatchOliSpec[];
    /** Opportunities that received OLIs in THIS call — PCF unions these across the loop and feeds them to ats_RecalculateOpportunities. */
    touchedOpportunityIds: string[];
    message?: string;
    errorCode?: AddProductsBatchErrorCode;
}

export interface AddProductsBatchResponse {
    response?: string;
    error?: any;
    message?: string;
    details?: string;
}

/** Shared shape between a ProductRequest and one of its PackageComponents. */
export interface AddProductsBatchProductFields {
    ProductId: string;
    ProductName?: string;
    RateType: "Individual" | "Season";
    Rate?: number;
    RateId?: string;
    HardCost?: number;
    ProductionCost?: number;
    QtyUnits: number;
    QtyEvents: number;
    /** Comma-separated list of season GUIDs. Components inherit parent's when empty. */
    seasonIds?: string;
    Description?: string;
}

/** Request shape sent to the backend plugin. Field names & casing match the C# DTO. */
export interface AddProductsBatchRequestProduct extends AddProductsBatchProductFields {
    /** Required on every top-level product. Must be CSV of season GUIDs. */
    seasonIds: string;
    packageLineId?: string;
    IsPackage?: boolean;
    /** Component opp-products created in the same transaction, linked via ats_packagelineitem. */
    PackageComponents?: AddProductsBatchProductFields[];
    /**
     * Optional tag stamped on every OLI produced by this request so downstream
     * "group by tag" reports keep working. If omitted the backend generates one.
     */
    AgreementOpportunityProductTag?: string;
}

//#endregion


//#region Recalculate Opportunities flow (ats_RecalculateOpportunities)
// Separate endpoint called by the PCF after ats_AddProductsBatch finishes.
// Handles opportunity totals recalc with the same soft-timeout + leftover
// pattern the batch-add uses.

export type RecalculateOpportunitiesErrorCode =
    | "input_invalid"
    | "recalc_failed";

export interface RecalculateOpportunitiesFailedOpp {
    opportunityId: string;
    reason: string;
}

export interface RecalculateOpportunitiesPayload {
    success: boolean;
    processedCount: number;
    totalCount: number;
    recalculatedOpportunityIds: string[];
    leftoverOpportunityIds: string[];
    failedOpportunities: RecalculateOpportunitiesFailedOpp[];
    message?: string;
    errorCode?: RecalculateOpportunitiesErrorCode;
}

export interface RecalculateOpportunitiesResponse {
    response?: string;
    error?: any;
    message?: string;
    details?: string;
}
//#endregion


export interface HiddenFields {
    ProductionCost: boolean;
}

export interface OpportunitiesData {
    HiddenFields: HiddenFields;
    Opportunities: Array<OpportunityData>
    isAuthorized: boolean;
}

export interface ProductData {
    Id: string;
    Name: string;
    Division: string;
    ProductFamily: string;
    ProductSubFamily: string;
    IsPassthroughCost: boolean;
    IsPackage?: boolean;
}

export interface RateData {
    RateType: "Individual" | "Season";
    Rate: number;
    HardCost: number;
    ProductionCost: number;
    LockHardCost: boolean;
    LockRate: boolean;
    LockProductionCost: boolean;
    Season: string;
    SeasonName: string;
    UnlimitedQuantity: boolean;
    Product: string;
}

export interface OpportunityLineItemData {
    Id: string;
    Opportunity: string;
    TotalValue: number;
    QtyEvents: number;
    QtyUnits: number;
    HardCost: number;
    TotalHardCost: number;
    ProductionCost: number;
    TotalProductionCost: number;
    LockProductionCost: boolean;
    Product2: string;
    Rate: number;
    QuantityAvailable: number | "Unlimited";
    RateType: "Individual" | "Season";
    IsActive: boolean;
    LockHardCost: boolean;
    LockRate: boolean;
    IsManualPriceOverride: boolean;
    ResetOverride: boolean;
    LegalDefinitionProduct: string | null,
    LegalDefinitionInventoryBySeason: string | null,
    LegalDefinition: string | null,
    OverwriteLegalDefinition: boolean,
    Description: string | null,
    QuantityTotal: number | null,
    QuantitySold: number | null,
    QuantityPitched: number | null,
    NotAvailable: boolean,
    PackageLineItemId: string | null
}

export type LineItemData = {
    Product2: ProductData;
    rates: Array<RateData>;
    items: Array<OpportunityLineItemData>;
    IsPackage: boolean;
    IsPackageComponent: boolean;
    PackageComponents: Array<LineItemData>;
}

export interface uiLineItemData extends Omit<LineItemData, "packageComponents"> {
    uid: string;
    PackageComponents: Array<uiLineItemData>;
}

export type LineItemsData = Array<LineItemData>


export interface InventoryData {
    ProductId: string;
    ProductName: string;
    ProductFamily: string;
    ProductSubFamily: string;
    Division: string;
    IsPassthroughCost: boolean;
    RateType: "Individual" | "Season";
    Rate: number;
    RateId: string;
    LockRate: boolean;
    HardCost: number;
    LockHardCost: boolean;
    ProductionCost: number;
    LockProductionCost: boolean;
    QuantityAvailable: number;
    QtyUnits: number;
    QtyEvents: number;
    IsPackage: boolean;
    Description: string;
    PackageComponents: Array<ComponentData>;
}

export interface ComponentData {
    ProductId: string;
    ProductName: string;
    ProductFamily: string;
    ProductSubFamily: string;
    Division: string;
    IsPassthroughCost: boolean;
    RateType: "Individual" | "Season";
    Rate: number;
    RateId: string;
    LockRate: boolean;
    HardCost: number;
    LockHardCost: boolean;
    ProductionCost: number;
    LockProductionCost: boolean;
    QuantityAvailable: number;
    QtyUnits: number;
    QtyEvents: number;
    Description: string;
}

export interface AgreementCartProps {
    AgreementId: string;
}