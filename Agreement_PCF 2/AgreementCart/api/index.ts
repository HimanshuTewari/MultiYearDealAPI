import axios from "axios";
import { InventoryData, OpportunityData, OpportunityLineItemData, AddProductResponse, AddProductInitialResponse, AddProductBatchingResponse, AddProductEscalateResponse, AddProductFlowResult, CheckProductsAvailabilityPayload, CheckProductsAvailabilityResponse, AddProductsBatchPayload, AddProductsBatchResponse, AddProductsBatchRequestProduct, AddProductsBatchFailedProduct } from "../models";
import { first } from "@tiptap/core/dist/commands";

const BASE_URL = window.location.origin + "/api/data/v9.0/ats_AgreementCartAction";
const CUSTOMAPIADDPRODUCTBASE_URL = window.location.origin + "/api/data/v9.0/ats_customAPIAddProductAgreementCartAction";
const BASE_CLONE_URL = window.location.origin + "/api/data/v9.0/ats_CloneAgreementAction";
// New Phase C/D endpoints — keep alongside the legacy per-product flow so
// package products and other edge cases can still fall back to it.
const CHECK_PRODUCTS_AVAILABILITY_URL = window.location.origin + "/api/data/v9.0/ats_CheckProductsAvailability";
const ADD_PRODUCTS_BATCH_URL = window.location.origin + "/api/data/v9.0/ats_AddProductsBatch";

// export const addProduct = async (data: InventoryData, agreementId: string, seasonIds: string, packageLineId: string) => {
//     try {
//         const requestData = {
//             ...data,
//             PackageComponents: JSON.stringify(data.PackageComponents || []),
//             agreementId: agreementId,
//             seasonIds: seasonIds,
//             packageLineId: packageLineId,
//             actionName: "AddProduct"
//         }
//         console.log("addProduct", requestData);

//         const response = await axios.post(BASE_URL, JSON.stringify(requestData), {
//             headers: {
//                 Accept: "application/json",
//                 "Content-Type": "application/json; charset=utf-8",
//                 "OData-MaxVersion": "4.0",
//                 "OData-Version": "4.0",
//             },
//             maxBodyLength: Infinity
//         });

//         return response.data;
//     } catch (error: any) {
//         console.log(error)
//         return {
//             error: error,
//             message: error.message,
//             details: error?.response?.data?.error?.message,
//         }
//     }
// }





//#region Sunny--> Add Product API through Custom API 
const commonHeaders = {
    Accept: "application/json",
    "Content-Type": "application/json; charset=utf-8",
    "OData-MaxVersion": "4.0",
    "OData-Version": "4.0",
};

function getApiError(error: any) {
    return {
        error,
        message: error?.message || "Unexpected error occurred while calling AgreementCartAction.",
        details: error?.response?.data?.error?.message || error?.response?.data || null,
    };
}


export const callAddProduct = async (
    data: InventoryData,
    agreementId: string,
    seasonIds: string,
    packageLineId?: string
): Promise<AddProductInitialResponse> => {
    try {
        const requestData = {
            inputActionName: "AddProduct",
            agreementId,
            packageLineItemIdAddProduct: packageLineId ?? "",
        };

        const response = await axios.post<AddProductInitialResponse>(CUSTOMAPIADDPRODUCTBASE_URL, requestData, {
            headers: commonHeaders,
            maxBodyLength: Infinity,
        });

        return response.data;
    } catch (error: any) {
        return getApiError(error);
    }
};

export const callAddProductBatching = async (
    addProductOpportunityGuid: string,
    data: InventoryData,
    seasonIds: string,
    packageComponents: any[] = []
): Promise<AddProductBatchingResponse> => {
    try {
        const requestData = {
            inputActionName: "AddProductBatching",
            AddProductOpportunityGuid: addProductOpportunityGuid,
            seasonIds: seasonIds ?? "",
            inventoryData: JSON.stringify(data),
            PackageComponents: JSON.stringify(packageComponents || []),
        };

        const response = await axios.post<AddProductBatchingResponse>(CUSTOMAPIADDPRODUCTBASE_URL, requestData, {
            headers: commonHeaders,
            maxBodyLength: Infinity,
        });

        return response.data;
    } catch (error: any) {
        return getApiError(error);
    }
};

export const callAddProductEscalateTotalDealAgreement = async (
    addProductOpportunityGuid: string
): Promise<AddProductEscalateResponse> => {
    try {
        const requestData = {
            inputActionName: "AddProductEscalateTotalDealAgreement",
            AddProductOpportunityGuid: addProductOpportunityGuid,
        };

        const response = await axios.post<AddProductEscalateResponse>(CUSTOMAPIADDPRODUCTBASE_URL, requestData, {
            headers: commonHeaders,
            maxBodyLength: Infinity,
        });

        return response.data;
    } catch (error: any) {
        return getApiError(error);
    }
};



// export const addProduct = async (
//     data: InventoryData,
//     agreementId: string,
//     seasonIds: string,
//     packageLineId?: string
// ): Promise<AddProductResponse> => {
//     try {
//         const requestData = {
//             inputActionName: "AddProduct",
//             agreementId,
//             seasonIds: seasonIds ?? "",
//             packageLineItemIdAddProduct: packageLineId ?? "",
//             inventoryData: JSON.stringify(data),
//             PackageComponents: JSON.stringify(data.PackageComponents || [])
//         };

//         console.log("ats_AgreementCartAction request:", requestData);

//         const response = await axios.post<AddProductResponse>(CUSTOMAPIADDPRODUCTBASE_URL, requestData, {
//             headers: {
//                 Accept: "application/json",
//                 "Content-Type": "application/json; charset=utf-8",
//                 "OData-MaxVersion": "4.0",
//                 "OData-Version": "4.0",
//             },
//             maxBodyLength: Infinity
//         });
//         console.log("response.data: ",response.data);
//         return response.data;

//     } catch (error: any) {
//         console.error("ats_AgreementCartAction failed:", error);

//         return {
//             error,
//             message: error?.message || "Unexpected error occurred while calling Custom API.",
//             details: error?.response?.data?.error?.message || error?.response?.data
//         };
//     }
// };




export const executeAddProductFlow = async (
    data: InventoryData,
    agreementId: string,
    seasonIds: string,
    packageLineId?: string
): Promise<AddProductFlowResult> => {
    const batchingResponses: AddProductBatchingResponse[] = [];

    try {
        const initialResponse = await callAddProduct(
            data,
            agreementId,
            seasonIds,
            packageLineId
        );

        if (initialResponse?.error) {
            return {
                success: false,
                error: initialResponse.error,
                message: initialResponse.message,
                details: initialResponse.details,
            };
        }

        let currentOpportunityGuid =
            initialResponse.NewAddProductOpportunityGuid ||
            initialResponse.AddProductOpportunityGuid ||
            "";

        if (!currentOpportunityGuid) {
            return {
                success: false,
                initialResponse,
                message: "AddProductOpportunityGuid was not returned from AddProduct.",
            };
        }

        let shouldContinueBatching = initialResponse.isAddProductBatching === true;

        while (shouldContinueBatching) {
            const batchingResponse = await callAddProductBatching(
                currentOpportunityGuid,
                data,
                seasonIds,
                data.PackageComponents || []
            );

            batchingResponses.push(batchingResponse);

            if (batchingResponse?.error) {
                return {
                    success: false,
                    initialResponse,
                    batchingResponses,
                    error: batchingResponse.error,
                    message: batchingResponse.message,
                    details: batchingResponse.details,
                };
            }

            currentOpportunityGuid =
                batchingResponse.NewAddProductOpportunityGuid ||
                batchingResponse.AddProductOpportunityGuid ||
                currentOpportunityGuid;

            shouldContinueBatching = batchingResponse.isAddProductBatching === true;

            if (!shouldContinueBatching) {
                const nextAction = batchingResponse.AgreementCartActionbatchingActionName;

                if (nextAction === "AddProductEscalateTotalDealAgreement") {
                    const escalationResponse =
                        await callAddProductEscalateTotalDealAgreement(currentOpportunityGuid);

                    if (escalationResponse?.error) {
                        return {
                            success: false,
                            initialResponse,
                            batchingResponses,
                            finalBatchingResponse: batchingResponse,
                            escalationResponse,
                            error: escalationResponse.error,
                            message: escalationResponse.message,
                            details: escalationResponse.details,
                        };
                    }

                    return {
                        success: true,
                        initialResponse,
                        batchingResponses,
                        finalBatchingResponse: batchingResponse,
                        escalationResponse,
                    };
                }

                return {
                    success: true,
                    initialResponse,
                    batchingResponses,
                    finalBatchingResponse: batchingResponse,
                };
            }
        }

        return {
            success: true,
            initialResponse,
            batchingResponses,
        };
    } catch (error: any) {
        return {
            success: false,
            error,
            message: error?.message || "Unexpected error occurred during add product flow.",
            details: error?.response?.data?.error?.message || error?.response?.data || null,
        };
    }
};


//#endregion


//#region Bulk Add-Products flow (ats_CheckProductsAvailability + ats_AddProductsBatch)
// These endpoints collapse the legacy N-round-trips-per-product flow into two
// HTTP calls: a read-only pre-flight and an all-or-nothing bulk create.
// See plan §8 (AddProductBatching refactor) and the C# plugins
// CustomAPICheckProductsAvailability.cs / CustomAPIAddProductsBatch.cs.

/**
 * Pre-flight availability check. Validates IBS + Rate existence and quantity
 * available for every (product, season) pair in a single round-trip.
 *
 * V2 behaviour (17-Apr-2026): when an IBS or Rate is missing, the plugin
 * auto-generates them (cloning IBS from an existing one for the same product;
 * creating Rate with the input's Rate/HardCost/ProductionCost). Pair verdicts
 * then come back as "ok" with ibsAutoCreated / rateAutoCreated flags set so the
 * UI can tell the user that records were auto-generated. Packages are no longer
 * routed to a legacy flow — they flow straight into ats_AddProductsBatch.
 */
export const callCheckProductsAvailability = async (
    agreementId: string,
    products: AddProductsBatchRequestProduct[]
): Promise<CheckProductsAvailabilityPayload | { error: any; message?: string; details?: any }> => {
    try {
        const requestData = {
            agreementId,
            products: JSON.stringify(products),
        };

        const response = await axios.post<CheckProductsAvailabilityResponse>(
            CHECK_PRODUCTS_AVAILABILITY_URL,
            requestData,
            { headers: commonHeaders, maxBodyLength: Infinity }
        );

        // The plugin returns its payload as a JSON string in the `response`
        // output parameter so the D365 custom-API envelope stays primitive.
        const raw = response.data?.response;
        if (!raw) {
            return {
                error: new Error("empty_response"),
                message: "CheckProductsAvailability returned an empty response.",
            };
        }
        try {
            return JSON.parse(raw) as CheckProductsAvailabilityPayload;
        } catch (parseErr: any) {
            return {
                error: parseErr,
                message: "CheckProductsAvailability response was not valid JSON.",
                details: raw,
            };
        }
    } catch (error: any) {
        return getApiError(error);
    }
};

/**
 * One-shot bulk create. Sends an array of products and returns whatever the
 * server processed before its soft-timeout hit. The server may return
 * `leftoverProducts` if the full list couldn't be processed inside the
 * synchronous budget — pair this call with `executeAddProductsBatchUntilComplete`
 * below to loop until the leftover list is empty.
 *
 * Each product can be a simple opp-product OR a package (IsPackage=true) with
 * a PackageComponents array. The server creates the main row + its components
 * atomically via ExecuteTransactionRequest per product.
 */
export const callAddProductsBatch = async (
    agreementId: string,
    products: AddProductsBatchRequestProduct[],
    softTimeoutMs?: number
): Promise<AddProductsBatchPayload | { error: any; message?: string; details?: any }> => {
    try {
        const requestData: Record<string, any> = {
            agreementId,
            products: JSON.stringify(products),
        };
        if (typeof softTimeoutMs === "number" && softTimeoutMs > 0) {
            requestData.softTimeoutMs = softTimeoutMs;
        }

        const response = await axios.post<AddProductsBatchResponse>(
            ADD_PRODUCTS_BATCH_URL,
            requestData,
            { headers: commonHeaders, maxBodyLength: Infinity }
        );

        const raw = response.data?.response;
        if (!raw) {
            return {
                error: new Error("empty_response"),
                message: "AddProductsBatch returned an empty response.",
            };
        }
        try {
            return JSON.parse(raw) as AddProductsBatchPayload;
        } catch (parseErr: any) {
            return {
                error: parseErr,
                message: "AddProductsBatch response was not valid JSON.",
                details: raw,
            };
        }
    } catch (error: any) {
        return getApiError(error);
    }
};

export interface AddProductsBatchProgress {
    /** Count of products processed so far across all sub-calls. */
    processed: number;
    /** Total products the caller asked us to submit. */
    total: number;
    /** Products that errored mid-flight (rows already rolled back server-side). */
    failed: AddProductsBatchFailedProduct[];
    /** IDs of opp-products successfully created so far. */
    createdOpportunityProductIds: string[];
    /** Opportunities whose lines were recalculated so far. */
    touchedOpportunityIds: string[];
}

export interface AddProductsBatchFinalResult extends AddProductsBatchProgress {
    /** True iff every product was processed AND none failed. */
    success: boolean;
    /** The last sub-call's payload (useful for debugging). */
    lastPayload?: AddProductsBatchPayload;
    /** Populated if a sub-call returned an envelope error. */
    error?: any;
    errorMessage?: string;
    errorDetails?: any;
}

/**
 * Loops callAddProductsBatch until the server-returned leftoverProducts is empty.
 *
 * Use this when you have many products to submit and want to:
 *   - show a progress bar (onProgress callback fires after each sub-call),
 *   - respect the plugin's 2-minute ceiling (server yields at ~90 s and we
 *     immediately send the leftover back),
 *   - track failures without blocking successful rows.
 *
 * Semantics:
 *   - The CHUNK the PCF sends per HTTP call is configurable via chunkSize.
 *     Default = 1000 (server soft-timeout is the real limiter). Use a small
 *     value (e.g. 25) if you want finer progress feedback even when the
 *     server could chew through more in one call.
 *   - onProgress receives cumulative counts after every sub-call so the UI
 *     can update the progress bar without doing its own math.
 *   - If any sub-call hits a transport/envelope error (network, 5xx, JSON
 *     parse), the loop stops and returns with `success: false` + error info.
 *     Products that were already created in prior sub-calls stay created —
 *     their ids are in `createdOpportunityProductIds` for the UI to report.
 */
export const executeAddProductsBatchUntilComplete = async (
    agreementId: string,
    products: AddProductsBatchRequestProduct[],
    opts?: {
        chunkSize?: number;
        softTimeoutMs?: number;
        onProgress?: (p: AddProductsBatchProgress) => void;
        /** How many consecutive zero-progress responses at chunk size 1 before giving up. Default 3. */
        maxZeroProgressRetries?: number;
    }
): Promise<AddProductsBatchFinalResult> => {
    const initialChunkSize = Math.max(1, opts?.chunkSize ?? 1000);
    const softTimeoutMs = opts?.softTimeoutMs;
    const onProgress = opts?.onProgress;
    const maxZeroProgressRetries = Math.max(1, opts?.maxZeroProgressRetries ?? 3);

    const total = products.length;
    const createdOpportunityProductIds: string[] = [];
    const touchedOpportunityIds: string[] = [];
    const failed: AddProductsBatchFailedProduct[] = [];
    let processed = 0;
    let lastPayload: AddProductsBatchPayload | undefined;

    // Work queue — we re-load it from the server's leftoverProducts after
    // every sub-call (the server may have split a chunk across multiple calls).
    let queue: AddProductsBatchRequestProduct[] = products.slice();

    // Adaptive chunk sizing. If the server yields with zero progress, we
    // halve the chunk size and try again — the usual cause is that the
    // pre-loop prep (bulk IBS/Rate resolution + recalc) already ate the
    // 90 s budget, and a smaller chunk gives the server room to process
    // at least one product before yielding.
    let currentChunkSize = initialChunkSize;
    let consecutiveZeroProgressAtMinChunk = 0;

    while (queue.length > 0) {
        const chunk = queue.slice(0, currentChunkSize);
        const rest = queue.slice(currentChunkSize);

        const result = await callAddProductsBatch(agreementId, chunk, softTimeoutMs);

        if ("error" in result) {
            const progress: AddProductsBatchProgress = {
                processed, total, failed,
                createdOpportunityProductIds, touchedOpportunityIds
            };
            onProgress?.(progress);
            return {
                success: false,
                ...progress,
                lastPayload,
                error: result.error,
                errorMessage: result.message,
                errorDetails: result.details,
            };
        }

        lastPayload = result;
        const thisCallProcessed = result.processedCount ?? 0;

        // Merge per-call results into cumulative counters.
        for (const id of result.createdOpportunityProductIds ?? [])
            createdOpportunityProductIds.push(id);
        for (const id of result.touchedOpportunityIds ?? [])
            if (!touchedOpportunityIds.includes(id)) touchedOpportunityIds.push(id);
        for (const f of result.failedProducts ?? []) failed.push(f);

        processed += thisCallProcessed;

        // Rebuild the queue: server's leftover FIRST, then anything we held back.
        queue = [...(result.leftoverProducts ?? []), ...rest];

        onProgress?.({
            processed, total, failed,
            createdOpportunityProductIds, touchedOpportunityIds
        });

        // Adaptive chunk-size policy based on whether this call made progress:
        //   - progress made → reset failure counter; keep (or cautiously grow) chunk.
        //   - zero progress AND leftover not smaller → halve chunk; if already at 1,
        //     increment the failure counter. Abort only after N consecutive zero-
        //     progress responses at the minimum chunk size.
        const leftoverLen = result.leftoverProducts?.length ?? 0;
        const serverMadeProgress =
            thisCallProcessed > 0 || leftoverLen < chunk.length;

        if (serverMadeProgress) {
            consecutiveZeroProgressAtMinChunk = 0;
        } else {
            if (currentChunkSize > 1) {
                const nextChunkSize = Math.max(1, Math.floor(currentChunkSize / 2));
                console.warn(
                    `[AddProductsBatch] Server returned zero progress on chunk of ${currentChunkSize}; halving to ${nextChunkSize} and retrying.`
                );
                currentChunkSize = nextChunkSize;
                // Don't increment the failure counter yet — we haven't tried
                // the smaller chunk. Loop continues with the newly-reformed
                // queue (server's leftover already at the front).
            } else {
                consecutiveZeroProgressAtMinChunk += 1;
                console.warn(
                    `[AddProductsBatch] Server still making zero progress at chunk size 1 (attempt ${consecutiveZeroProgressAtMinChunk}/${maxZeroProgressRetries}).`
                );
                if (consecutiveZeroProgressAtMinChunk >= maxZeroProgressRetries) {
                    return {
                        success: false,
                        processed, total, failed,
                        createdOpportunityProductIds, touchedOpportunityIds,
                        lastPayload,
                        error: new Error("no_progress"),
                        errorMessage:
                            `Server made no progress on the batch after ${maxZeroProgressRetries} attempt(s) at chunk size 1. ` +
                            `The plugin may be hitting its soft-timeout during pre-loop prep (bulk IBS/Rate resolution). ` +
                            `Products created so far are persisted; retry remaining items or investigate plugin trace logs.`,
                        errorDetails: { lastResult: result },
                    };
                }
            }
        }
    }

    return {
        success: failed.length === 0,
        processed, total, failed,
        createdOpportunityProductIds, touchedOpportunityIds,
        lastPayload,
    };
};

//#endregion



export const updateOpportunityLineItem = async (data: OpportunityLineItemData) => {
    try {

        const reqData = {
            actionName: "updateOpportunityLineItem",
            HardCost: data.HardCost,
            id: data.Id,
            isActive: data.IsActive,
            LockHardCost: data.LockHardCost,
            LockProductionCost: data.LockProductionCost,
            LockRate: data.LockRate,
            opportunity: data.Opportunity,
            product2: data.Product2,
            ProductionCost: data.ProductionCost,
            QtyEvents: data.QtyEvents,
            QtyUnits: data.QtyUnits,
            QuantityAvailable: data.QuantityAvailable,
            Rate: data.Rate,
            RateType: data.RateType,
            totalHardCost: data.TotalHardCost,
            totalProductionCost: data.TotalProductionCost,
            totalValue: data.TotalValue,
            ResetOverride: data.ResetOverride,
            LegalDefinition: data.LegalDefinition,
            OverwriteLegalDefinition: data.OverwriteLegalDefinition,
            Description: data.Description
        }

        console.log("updateOpportunityLineItem", reqData);

        const response = await axios.post(BASE_URL, JSON.stringify(reqData), {
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json; charset=utf-8",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            maxBodyLength: Infinity
        });

        return response.data;
    } catch (error: any) {
        console.log(error)
        return {
            error: error,
            message: error.message,
            details: error?.response?.data?.error?.message,
        }
    }
}

export const updateOpportunity = async (data: OpportunityData) => {
    try {
        const reqData = {
            actionName: "UpdateOpportunity",
            automaticAmount: data.AutomaticAmount,
            dealValue: data.DealValue,
            escalationType: data.EscalationType,
            escalationValue: data.EscalationValue,
            id: data.Id,
            isFirstYear: data.IsFirstYear,
            manualAmount: data.ManualAmount,
            percentOfRate: data.PercentOfRate,
            pricingMode: data.PricingMode,
            seasonName: data.SeasonName,
            startSeason: data.StartSeason,
            totalHardCost: data.TotalHardCost,
            totalProductionCost: data.TotalProductionCost,
            totalRateCard: data.TotalRateCard,
            BarterAmount: data.BarterAmount
        }

        console.log("updateOpportunity", reqData);

        const response = await axios.post(BASE_URL, JSON.stringify(reqData), {
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json; charset=utf-8",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            maxBodyLength: Infinity
        });

        return response.data;
    } catch (error: any) {
        console.log(error);
        return {
            error: error,
            message: error.message,
            details: error?.response?.data?.error?.message,
        }
    }
}

export const deleteOpportunityLineItems = async (ids: Array<string>) => {
    try {
        console.log("deleteOpportunityLineItems", ids);

        const requestData = {
            OppProdId: JSON.stringify(ids),
            actionName: "Delete"
        }


        // var arr=JSON.stringify(requestData);

        // var arr2=JSON.stringify(arr); //stringify the array twice to get the string format for the request

        console.log("Arr 2 ", requestData);

        //console.log("Again stringify ", JSON.stringify(arr2));

        const response = await axios.post(BASE_URL, requestData, {
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json; charset=utf-8",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            maxBodyLength: Infinity
        });

        return response.data;
    } catch (error: any) {
        console.log(error);
        return {
            error: error,
            message: error.message,
            details: error?.response?.data?.error?.message,
        }
    }
}


export const escalateRevenue = async (escalationType: "Fixed" | "Percent" | string, escalationValue: number, agreementId: string) => {
    try {
        console.log("escalateRevenue", escalationType, escalationValue, agreementId);

        const response = await axios.post(BASE_URL, JSON.stringify({
            escalationType,
            escalationValue,
            agreementId,
            actionName: "RevenueEscalate"
        }), {
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json; charset=utf-8",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            maxBodyLength: Infinity
        });

        return response.data;
    } catch (error: any) {
        console.log(error);
        return {
            error: error,
            message: error.message,
            details: error?.response?.data?.error?.message,
        }
    }
}

// -------------------------------------------------------------------------
// Phase D.7 — TTL cache + in-flight dedup for GetAvailableSeasons.
// (plan: atomic-jumping-rabin.md §Phase D.7)
//
// The legacy code re-fetched seasons for the same product on every "Add
// Product" click. The cache makes the second click free for SEASON_LOOKUP_TTL_MS,
// and the in-flight Promise map collapses concurrent requests for the
// same key into a single network round-trip.
// -------------------------------------------------------------------------
type SeasonOption = { value: string; label: string };
const _seasonCache = new Map<string, { at: number; data: SeasonOption[] }>();
const _seasonInFlight = new Map<string, Promise<SeasonOption[]>>();
const SEASON_TTL_MS = 2 * 60 * 1000; // 2 minutes — see constants.ts SEASON_LOOKUP_TTL_MS

export const getAvailableSeasonsByProduct = async (
    ProductId: string,
    seasonIds: string
): Promise<SeasonOption[]> => {
    const cacheKey = `${ProductId}|${seasonIds || ""}`;

    const cached = _seasonCache.get(cacheKey);
    if (cached && Date.now() - cached.at < SEASON_TTL_MS) {
        return cached.data;
    }

    const inFlight = _seasonInFlight.get(cacheKey);
    if (inFlight) return inFlight;

    const promise = (async (): Promise<SeasonOption[]> => {
        try {
            const requestData = {
                ProductId,
                seasonIds,
                actionName: "GetAvailableSeasons"
            };

            const response = await axios.post(BASE_URL, JSON.stringify(requestData), {
                headers: {
                    Accept: "application/json",
                    "Content-Type": "application/json; charset=utf-8",
                    "OData-MaxVersion": "4.0",
                    "OData-Version": "4.0"
                },
                maxBodyLength: Infinity
            });
            const raw: string = response?.data?.response ?? "";
            const checkboxOptions: SeasonOption[] = raw
                ? raw.split(";").map((item: string) => {
                    const [label, value] = item.split("|");
                    return { label, value };
                })
                : [];

            _seasonCache.set(cacheKey, { at: Date.now(), data: checkboxOptions });
            return checkboxOptions;
        } catch (error: any) {
            console.error("getAvailableSeasonsByProduct error:", error);
            return [];
        } finally {
            _seasonInFlight.delete(cacheKey);
        }
    })();

    _seasonInFlight.set(cacheKey, promise);
    return promise;
};

/** Test/utility hook — clear the season cache (e.g. after an explicit refresh). */
export const clearAvailableSeasonsCache = () => {
    _seasonCache.clear();
    _seasonInFlight.clear();
};

export const cloneAgreement = async (agreementId: string, firstYearOnly: boolean) => {
    try {
        const requestData = {
            AgreementId: agreementId,
            IsFirstYearClone: firstYearOnly
        }
        const response = await axios.post(BASE_CLONE_URL, JSON.stringify(requestData), {
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json; charset=utf-8",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
            maxBodyLength: Infinity
        });
        console.log("cloneAgreement response: ", JSON.stringify(response.data));
        return response.data;
    } catch (error: any) {
        console.log(error)
        return {
            error: error,
            message: error.message,
            details: error?.response?.data?.error?.message,
        }
    }
}