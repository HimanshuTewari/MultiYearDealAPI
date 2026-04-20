import axios from "axios";
import {
    InventoryData,
    OpportunityData,
    OpportunityLineItemData,
    AddProductResponse,
    AddProductInitialResponse,
    AddProductBatchingResponse,
    AddProductEscalateResponse,
    AddProductFlowResult,
    CheckProductsAvailabilityPayload,
    CheckProductsAvailabilityResponse,
    AddProductsBatchPayload,
    AddProductsBatchResponse,
    AddProductsBatchRequestProduct,
    AddProductsBatchFailedOli,
    AddProductsBatchOliSpec,
    RecalculateOpportunitiesPayload,
    RecalculateOpportunitiesResponse,
    RecalculateOpportunitiesFailedOpp,
} from "../models";
import { first } from "@tiptap/core/dist/commands";

const BASE_URL = window.location.origin + "/api/data/v9.0/ats_AgreementCartAction";
const CUSTOMAPIADDPRODUCTBASE_URL = window.location.origin + "/api/data/v9.0/ats_customAPIAddProductAgreementCartAction";
const BASE_CLONE_URL = window.location.origin + "/api/data/v9.0/ats_CloneAgreementAction";
// Bulk add-products + recalc flow (v3 — 20-Apr-2026).
const CHECK_PRODUCTS_AVAILABILITY_URL = window.location.origin + "/api/data/v9.0/ats_CheckProductsAvailability";
const ADD_PRODUCTS_BATCH_URL = window.location.origin + "/api/data/v9.0/ats_AddProductsBatch";
const RECALCULATE_OPPORTUNITIES_URL = window.location.origin + "/api/data/v9.0/ats_RecalculateOpportunities";

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
 * One-shot call to ats_AddProductsBatch. Accepts EITHER `products` (first
 * call — the server expands them into OLIs and persists as many as it can
 * inside its 90 s budget) OR `olis` (resume — the server consumes the
 * pre-resolved specs as-is). The discriminator is which key is present.
 *
 * The server-side OLI commit batch size is controlled by the D365
 * environment variable `ats_OliBatchSize` — NOT passed from the PCF.
 */
export const callAddProductsBatch = async (
    agreementId: string,
    options: {
        products?: AddProductsBatchRequestProduct[];
        olis?: AddProductsBatchOliSpec[];
        softTimeoutMs?: number;
    }
): Promise<AddProductsBatchPayload | { error: any; message?: string; details?: any }> => {
    try {
        const requestData: Record<string, any> = { agreementId };
        if (options.products && options.products.length > 0) {
            requestData.products = JSON.stringify(options.products);
        }
        if (options.olis && options.olis.length > 0) {
            requestData.olis = JSON.stringify(options.olis);
        }
        if (typeof options.softTimeoutMs === "number" && options.softTimeoutMs > 0) {
            requestData.softTimeoutMs = options.softTimeoutMs;
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
    /** OLIs processed so far across all sub-calls (succeeded + failed). */
    processed: number;
    /** Original total OLIs — captured from the FIRST response's totalOliCount so the progress bar stays monotonic. */
    total: number;
    /** OLIs that failed mid-flight (rows already rolled back server-side). */
    failed: AddProductsBatchFailedOli[];
    /** IDs of opp-products successfully created so far. */
    createdOpportunityProductIds: string[];
    /** Union of opportunity ids touched across all sub-calls — fed to ats_RecalculateOpportunities. */
    touchedOpportunityIds: string[];
}

export interface AddProductsBatchFinalResult extends AddProductsBatchProgress {
    /** True iff every OLI was processed AND none failed. */
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
/**
 * Loops callAddProductsBatch until the server's leftoverOlis list is empty.
 *
 *   - First sub-call sends `products` and the server expands them to OLIs.
 *   - Every subsequent sub-call sends `leftoverOlis` as-is so the server
 *     doesn't re-expand anything — it just commits the remainder until the
 *     soft-timeout yields again.
 *   - Progress bar tracks `processedOliCount / totalOliCount(first-response)`.
 *   - Adaptive chunk-halving is still present but operates on OLI-list
 *     length during the resume phase: if the server returns zero progress,
 *     we slice a smaller leftover back to it (trading fewer OLIs per call
 *     for more timer checks). At chunk size 1 with N consecutive zero-
 *     progress responses we abort cleanly with a descriptive error.
 *
 * OLIs that were created in earlier sub-calls stay persisted even if the
 * loop ultimately fails — their ids are in `createdOpportunityProductIds`.
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

    const createdOpportunityProductIds: string[] = [];
    const touchedOpportunityIds: string[] = [];
    const failed: AddProductsBatchFailedOli[] = [];
    let processed = 0;
    // `total` becomes the original OLI count reported by the FIRST response.
    // Until then (during the products→OLIs expansion call) we use the product
    // count as a placeholder so the UI has something to display.
    let total = products.length;
    let firstResponseSeen = false;
    let lastPayload: AddProductsBatchPayload | undefined;

    let currentChunkSize = initialChunkSize;
    let consecutiveZeroProgressAtMinChunk = 0;

    // First call: send products. After that we switch to sending OLIs from
    // the server's leftoverOlis stream.
    let firstCall = true;
    let productsToSend: AddProductsBatchRequestProduct[] = products.slice();
    let oliQueue: AddProductsBatchOliSpec[] = [];

    while (firstCall || oliQueue.length > 0) {
        let thisCallInputSize: number;
        let result: Awaited<ReturnType<typeof callAddProductsBatch>>;

        if (firstCall) {
            thisCallInputSize = productsToSend.length;
            result = await callAddProductsBatch(agreementId, {
                products: productsToSend,
                softTimeoutMs,
            });
            firstCall = false;
        } else {
            // Resume path — slice the next chunk of pending OLIs.
            const chunk = oliQueue.slice(0, currentChunkSize);
            const rest = oliQueue.slice(currentChunkSize);
            thisCallInputSize = chunk.length;
            result = await callAddProductsBatch(agreementId, {
                olis: chunk,
                softTimeoutMs,
            });
            // Rebuild the queue after the call (server's leftover FIRST,
            // then anything we held back).
            oliQueue = rest;
        }

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
        const thisCallProcessed = result.processedOliCount ?? 0;

        if (!firstResponseSeen) {
            total = result.totalOliCount ?? total;
            firstResponseSeen = true;
        }

        // Merge cumulative counters.
        for (const id of result.createdOpportunityProductIds ?? [])
            createdOpportunityProductIds.push(id);
        for (const id of result.touchedOpportunityIds ?? [])
            if (!touchedOpportunityIds.includes(id)) touchedOpportunityIds.push(id);
        for (const f of result.failedOlis ?? []) failed.push(f);

        processed += thisCallProcessed;

        // Prepend the server's leftover OLIs to the queue so the next call
        // picks them up first.
        const serverLeftover = result.leftoverOlis ?? [];
        if (serverLeftover.length > 0) {
            oliQueue = [...serverLeftover, ...oliQueue];
        }

        onProgress?.({
            processed, total, failed,
            createdOpportunityProductIds, touchedOpportunityIds
        });

        // Adaptive chunk-size policy — same as before, but "chunk.length" is
        // now the OLI-count we sent (or the number of OLIs the server
        // derived from the products in the first call).
        const leftoverLen = serverLeftover.length;
        const serverMadeProgress =
            thisCallProcessed > 0 || leftoverLen < thisCallInputSize;

        if (serverMadeProgress) {
            consecutiveZeroProgressAtMinChunk = 0;
        } else {
            if (currentChunkSize > 1) {
                const nextChunkSize = Math.max(1, Math.floor(currentChunkSize / 2));
                console.warn(
                    `[AddProductsBatch] Server returned zero progress on chunk of ${currentChunkSize}; halving to ${nextChunkSize} and retrying.`
                );
                currentChunkSize = nextChunkSize;
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
                            `The plugin may be hitting its soft-timeout during pre-loop prep. ` +
                            `OLIs created so far are persisted; check plugin trace logs for details.`,
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


//#region Recalculate Opportunities flow (ats_RecalculateOpportunities)
// Called by the PCF after executeAddProductsBatchUntilComplete finishes, with
// the union of touchedOpportunityIds accumulated across the OLI-creation loop.

export const callRecalculateOpportunities = async (
    opportunityIds: string[],
    softTimeoutMs?: number
): Promise<RecalculateOpportunitiesPayload | { error: any; message?: string; details?: any }> => {
    try {
        const requestData: Record<string, any> = {
            opportunityIds: JSON.stringify(opportunityIds),
        };
        if (typeof softTimeoutMs === "number" && softTimeoutMs > 0) {
            requestData.softTimeoutMs = softTimeoutMs;
        }

        const response = await axios.post<RecalculateOpportunitiesResponse>(
            RECALCULATE_OPPORTUNITIES_URL,
            requestData,
            { headers: commonHeaders, maxBodyLength: Infinity }
        );

        const raw = response.data?.response;
        if (!raw) {
            return {
                error: new Error("empty_response"),
                message: "RecalculateOpportunities returned an empty response.",
            };
        }
        try {
            return JSON.parse(raw) as RecalculateOpportunitiesPayload;
        } catch (parseErr: any) {
            return {
                error: parseErr,
                message: "RecalculateOpportunities response was not valid JSON.",
                details: raw,
            };
        }
    } catch (error: any) {
        return getApiError(error);
    }
};

export interface RecalculateOpportunitiesProgress {
    processed: number;
    total: number;
    failed: RecalculateOpportunitiesFailedOpp[];
    recalculatedOpportunityIds: string[];
}

export interface RecalculateOpportunitiesFinalResult extends RecalculateOpportunitiesProgress {
    success: boolean;
    lastPayload?: RecalculateOpportunitiesPayload;
    error?: any;
    errorMessage?: string;
    errorDetails?: any;
}

/**
 * Loops callRecalculateOpportunities until the server's leftover list is
 * empty. Mirrors the adaptive chunk-halving + consecutive-no-progress guard
 * used by the OLI-creation loop so slow pre-loop prep doesn't trip the UI.
 */
export const executeRecalculateOpportunitiesUntilComplete = async (
    opportunityIds: string[],
    opts?: {
        chunkSize?: number;
        softTimeoutMs?: number;
        onProgress?: (p: RecalculateOpportunitiesProgress) => void;
        maxZeroProgressRetries?: number;
    }
): Promise<RecalculateOpportunitiesFinalResult> => {
    const initialChunkSize = Math.max(1, opts?.chunkSize ?? 1000);
    const softTimeoutMs = opts?.softTimeoutMs;
    const onProgress = opts?.onProgress;
    const maxZeroProgressRetries = Math.max(1, opts?.maxZeroProgressRetries ?? 3);

    const total = opportunityIds.length;
    const recalculatedOpportunityIds: string[] = [];
    const failed: RecalculateOpportunitiesFailedOpp[] = [];
    let processed = 0;
    let lastPayload: RecalculateOpportunitiesPayload | undefined;

    let queue = opportunityIds.slice();
    let currentChunkSize = initialChunkSize;
    let consecutiveZeroProgressAtMinChunk = 0;

    while (queue.length > 0) {
        const chunk = queue.slice(0, currentChunkSize);
        const rest = queue.slice(currentChunkSize);

        const result = await callRecalculateOpportunities(chunk, softTimeoutMs);

        if ("error" in result) {
            const progress: RecalculateOpportunitiesProgress = {
                processed, total, failed, recalculatedOpportunityIds
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

        for (const id of result.recalculatedOpportunityIds ?? [])
            recalculatedOpportunityIds.push(id);
        for (const f of result.failedOpportunities ?? []) failed.push(f);

        processed += thisCallProcessed;

        // Rebuild queue: server's leftover FIRST, then anything we held back.
        queue = [...(result.leftoverOpportunityIds ?? []), ...rest];

        onProgress?.({
            processed, total, failed, recalculatedOpportunityIds
        });

        const leftoverLen = result.leftoverOpportunityIds?.length ?? 0;
        const serverMadeProgress =
            thisCallProcessed > 0 || leftoverLen < chunk.length;

        if (serverMadeProgress) {
            consecutiveZeroProgressAtMinChunk = 0;
        } else if (currentChunkSize > 1) {
            const nextChunkSize = Math.max(1, Math.floor(currentChunkSize / 2));
            console.warn(
                `[RecalculateOpportunities] Zero progress on chunk of ${currentChunkSize}; halving to ${nextChunkSize}.`
            );
            currentChunkSize = nextChunkSize;
        } else {
            consecutiveZeroProgressAtMinChunk += 1;
            console.warn(
                `[RecalculateOpportunities] Zero progress at chunk size 1 (attempt ${consecutiveZeroProgressAtMinChunk}/${maxZeroProgressRetries}).`
            );
            if (consecutiveZeroProgressAtMinChunk >= maxZeroProgressRetries) {
                return {
                    success: false,
                    processed, total, failed, recalculatedOpportunityIds,
                    lastPayload,
                    error: new Error("no_progress"),
                    errorMessage:
                        `Server made no progress on opportunity recalc after ${maxZeroProgressRetries} attempt(s) at chunk size 1. ` +
                        `Opportunities recalculated so far are persisted.`,
                    errorDetails: { lastResult: result },
                };
            }
        }
    }

    return {
        success: failed.length === 0,
        processed, total, failed, recalculatedOpportunityIds,
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