import * as React from "react";
import {
    HiddenFields,
    InventoryData,
    LineItemsData,
    OpportunityData,
    OpportunityLineItemData,
    ProductData,
} from "../../../models";
import AgreementTableHeader from "../AgreementTable/AgreementTableHeader";
import AgreementTableBody from "../AgreementTable/AgreementTableBody";
import { Button, Modal, Input, Spin } from "antd";
import AddIcon from "@mui/icons-material/Add";
import CopyIcon from "@mui/icons-material/ContentCopy";
import InventoryTable from "../InventoryTable";
import CascadingSearch from "../CascadingSearch";
import "./index.css";
import { useDebounce } from "../../../hooks/useDebounce";
import { likeMatch } from "../../../utilities";
import usePagination from "../../../hooks/usePagination";
import PaginationControls from "../PaginationControls";
import { AppStateProvider } from "../../../context/useAppState";

import {
    // addProduct,
    executeAddProductFlow,
    deleteOpportunityLineItems,
    escalateRevenue,
    updateOpportunity,
    updateOpportunityLineItem,
    getAvailableSeasonsByProduct,
    cloneAgreement,
    // Bulk add-products flow (v2 — 17-Apr-2026)
    callCheckProductsAvailability,
    executeAddProductsBatchUntilComplete,
} from "../../../api";
import type {
    AddProductsBatchRequestProduct,
    AddProductsBatchProductFields,
} from "../../../models";
import { useNotification } from "../../../context/useNotification";
import { useAppState } from "../../../context/useAppState";
import OpportunityLineItemForm from "../Forms/OpportunityLineItem";
import OpportunityForm from "../Forms/Opportunity";
import OpportunityLineItemDescriptionForm from "../Forms/OpportunityLineItemDescription";
import OpportunityLineItemLegalDefForm from "../Forms/OpportunityLineItemLegalDef";
import EscalateRevenueForm from "../Forms/EscalateRevenue";
import QuantityAvailable from "../QuantityAvailable";
import CloneAgreementForm from "../Forms/CloneAgreement";
import { first } from "@tiptap/core/dist/commands";





export default function AgreementTable({
    opportunities,
    lineItems,
    inventory,
    updateView,
    hiddenFields,
    alternateUI
}: {
    opportunities: Array<OpportunityData>;
    lineItems: LineItemsData;
    inventory: Array<InventoryData>;
    updateView: () => Promise<void>;
    hiddenFields: HiddenFields,
    alternateUI?: boolean
}) {
    // --------------------------------------------------------
    // STATE
    // --------------------------------------------------------

    const { context } = useAppState();

    type StagedProduct = InventoryData & { //Sunny --> Add Product Extension
        seasonIds: string;
        packageLineId: string;
        stagedId: string;
    };

    const [selectedProducts, setSelectedProducts] = React.useState<StagedProduct[]>([]); //Sunny --> Add Product Extension 

    const [selectedSeason, setSelectedSeason] = React.useState("");
    const addProductRequestRef = React.useRef<Set<string>>(new Set()); //Sunny(20-01-26)--> Handling the twice submit of the Add Product request


    //#region Sunny--> percentage bar 
    const [submitProgress, setSubmitProgress] = React.useState(0);
    const [isSubmittingProducts, setIsSubmittingProducts] = React.useState(false);
    const [submitProgressText, setSubmitProgressText] = React.useState("");
    //#endregion
    const [inventoryModalOpen, setInventoryModalOpen] = React.useState(false);
    const [lineItemModalOpen, setLineItemModalOpen] = React.useState(false);
    const [opportunityModalOpen, setOpportunityModalOpen] = React.useState(false);
    const [descriptionModalOpen, setDescriptionModalOpen] = React.useState(false);
    const [legalDefModalOpen, setLegalDefModalOpen] = React.useState(false);
    const [escalateRevenueModalOpen, setEscalateRevenueModalOpen] = React.useState(false);
    const [qtyAvailableModalOpen, setQtyAvailableModalOpen] = React.useState(false);
    const [cloneAgreementModalOpen, setCloneAgreementModalOpen] = React.useState(false);

    const [includePackages, setIncludePackages] = React.useState(false);
    const [packageLineId, setPackageLineId] = React.useState("");

    const [cachedLineItem, setCachedLineItem] =
        React.useState<OpportunityLineItemData | null>(null);
    const [cachedOpportunity, setCachedOpportunity] =
        React.useState<OpportunityData | null>(null);

    const [collapsedItems, setCollapsedItems] = React.useState<
        Record<string, boolean>
    >({});

    const [productFilters, setProductFilters] = React.useState<
        Record<string, string>
    >({});
    const [searchQuery, setSearchQuery] = React.useState("");

    const [loading, setLoading] = React.useState(false);
    const [addProductLoading, setAddProductLoading] = React.useState(false);
    const [updateLineItemLoading, setUpdateLineItemLoading] =
        React.useState(false);
    const [opportunityLoading, setOpportunityLoading] = React.useState(false);
    const [escalateRevenueLoading, setEscalateRevenueLoading] =
        React.useState(false);

    // --------------------------------------------------------
    // HOOKS
    // --------------------------------------------------------

    const debouncedSearchQuery = useDebounce(searchQuery, 1000);
    const { setNotification } = useNotification();
    const { agreementId, isAuthorized } = useAppState();

    // --------------------------------------------------------
    // MEMO
    // --------------------------------------------------------

    const uiLineItems = React.useMemo(() => {
        try {
            return lineItems.map((i) => ({
                ...i,
                uid: crypto.randomUUID(),
                isExpanded: true,
                PackageComponents: Array.isArray(i.PackageComponents) ? i.PackageComponents.map((itemComponent) => ({
                    ...itemComponent,
                    uid: crypto.randomUUID(),
                })) : []

            }));
        } catch (error) {
            console.log(error);
            return [];
        }
    }, [lineItems]);

    const isAllItemsExpanded = React.useMemo(() => {
        try {
            return (
                uiLineItems.every((item) => collapsedItems[item.uid] === false) &&
                collapsedItems["header"] === false
            );
        } catch (error) {
            console.log(error);
            return false;
        }
    }, [collapsedItems, uiLineItems]);

    const cascaderValue = React.useMemo(() => {
        try {
            const { Division, ProductFamily, ProductSubFamily } =
                (productFilters as Record<keyof ProductData, string>) || {};

            const items: Array<string> = [];
            if (Division) items.push(Division);
            if (Division && ProductFamily) items.push(ProductFamily);
            if (Division && ProductFamily && ProductSubFamily)
                items.push(ProductSubFamily);

            return items;
        } catch (error) {
            return [];
        }
    }, [productFilters]);

    const filteredItems = React.useMemo(() => {
        let items = [...uiLineItems];
        try {
            if (Object.keys(productFilters).length > 0) {
                console.log(productFilters);
                items = items.filter((i) =>
                    Object.entries(productFilters).every(([key, value]) => {
                        try {
                            return i.Product2[key] === value;
                        } catch (error) {
                            console.log(error);
                            return false;
                        }
                    })
                );
            }

            if (debouncedSearchQuery && debouncedSearchQuery.length > 2) {
                // Split the search query using the original delimiters
                const searchTerms = searchQuery
                    .trim()
                    .toLowerCase()
                    .split(/,| |-+/) // Split by commas, spaces, or hyphens (one or more)
                    .filter((term) => term); // Filter out empty terms

                if (searchTerms.length > 0) {
                    items = items.filter((item) => {

                        let hasMatchingPackageComponents = false;
                        if (Array.isArray(item.PackageComponents) && item.PackageComponents.length > 0) {
                            hasMatchingPackageComponents = item.PackageComponents.some((itemComponent) => {
                                let name = itemComponent.Product2.Name;
                                name = name.toLowerCase();
                                return likeMatch(searchTerms, name)
                            })
                        }

                        return Object.values(item.Product2).some((attribute) => {
                            let attr = attribute;
                            if (typeof attr === "string") attr = attr.toLowerCase();

                            // Use the likeMatch method to check if all search terms are in the attribute
                            return likeMatch(searchTerms, attr) || hasMatchingPackageComponents;
                        });
                    });
                }
            }

            items.sort((a, b) => {
                const familyCompare = (a.Product2.ProductFamily || '').localeCompare(b.Product2.ProductFamily || '');
                if (familyCompare !== 0) return familyCompare;

                const subFamilyCompare = (a.Product2.ProductSubFamily || '').localeCompare(b.Product2.ProductSubFamily || '');
                if (subFamilyCompare !== 0) return subFamilyCompare;

                return (a.Product2.Name || '').localeCompare(b.Product2.Name || '');
            });

            return items;
        } catch (error) {
            console.log(error);
            return items;
        }
    }, [productFilters, debouncedSearchQuery, uiLineItems]);

    const paginationData = usePagination({
        items: filteredItems,
        defaultPageSize: 50,
    });

    // --------------------------------------------------------
    // UI FUNCTIONS
    // --------------------------------------------------------

    const resetSubmitProgress = () => { //Sunny --> Add Product extensiom percentage 
        setSubmitProgress(0);
        setSubmitProgressText("");
        setIsSubmittingProducts(false);
    };




    function toggleCollapse(uid: string) {
        try {
            setCollapsedItems((collapsedItems) => {
                const items = { ...collapsedItems };
                items[uid] = !items[uid];

                return items;
            });
        } catch (error) {
            console.log(error);
        }
    }

    function toggleAllCollapsed() {
        if (isAllItemsExpanded) {
            const collapsedItemState = {};

            uiLineItems.forEach((i) => (collapsedItemState[i.uid] = true));
            collapsedItemState["header"] = true;
            setCollapsedItems(collapsedItemState);
        } else {
            const collapsedItemState = {};

            uiLineItems.forEach((i) => (collapsedItemState[i.uid] = false));
            collapsedItemState["header"] = false;
            setCollapsedItems(collapsedItemState);
        }
    }

    function onChangeCascadingSearch(values) {
        if (!productFilters) return setProductFilters({} as any);

        setProductFilters((f: Record<string, string>) => {
            const value = { ...f };

            if (!values) return {};

            if (values[0]) value.Division = values[0].toString();
            if (values[1]) value.ProductFamily = values[1].toString();
            if (values[2]) value.ProductSubFamily = values[2].toString();

            return value;
        });
    }

    // --------------------------------------------------------
    // MODAL FUNCTIONS
    // --------------------------------------------------------

    function openLineItemModal(data: OpportunityLineItemData) {
        if (data) {
            setCachedLineItem(data);

            setTimeout(() => {
                setLineItemModalOpen(true);
            }, 100);
        }
    }

    function closeLineItemModal() {
        setCachedLineItem(null);
        setLineItemModalOpen(false);
    }

    function openOpportunityModal(data: OpportunityData) {
        if (data) {
            setCachedOpportunity(data);

            setTimeout(() => {
                setOpportunityModalOpen(true);
            }, 100);
        }
    }

    function closeOpportunityModal() {
        setCachedOpportunity(null);
        setOpportunityModalOpen(false);
    }

    function openDescriptionModal(data: OpportunityLineItemData) {
        if (data) {
            setCachedLineItem(data);

            setTimeout(() => {
                setDescriptionModalOpen(true);
            }, 100);
        }
    }

    function closeDescriptionModal() {
        setCachedLineItem(null);
        setDescriptionModalOpen(false);
    }

    function openLegalDefModal(data: OpportunityLineItemData) {
        if (data) {
            setCachedLineItem(data);

            setTimeout(() => {
                setLegalDefModalOpen(true);
            }, 100);
        }
    }

    function closeLegalDefModal() {
        setCachedLineItem(null);
        setLegalDefModalOpen(false);
    }

    function openQtyAvailableModal(data: OpportunityLineItemData) {
        if (data) {
            setCachedLineItem(data);

            setTimeout(() => {
                setQtyAvailableModalOpen(true);
            }, 100)
        }
    }

    function closeQtyAvailableModal() {
        setCachedLineItem(null);
        setQtyAvailableModalOpen(false);
    }

    function openNewPackageComponentModal(packageLineItemId: string) {
        console.log("openNewPackageComponentModal: ", packageLineItemId);
        setIncludePackages(false);
        setPackageLineId(packageLineItemId);
        setInventoryModalOpen(true);
    }

    // --------------------------------------------------------
    // DATABASE ACTIONS
    // --------------------------------------------------------

    async function handleUpdateView() {
        setLoading(true);
        await updateView();
        setLoading(false);
    }

    // async function handleInsertProduct(item: InventoryData, seasonIds: string, packageLineId: string) {
    //     setAddProductLoading(true);
    //     try {
    //         if (!agreementId) throw new Error("Missing agreement id");
    //         const result = await addProduct(item, agreementId, seasonIds, packageLineId);

    //         if (result.error) throw new Error(result.message);

    //         setNotification("Successfully added product", "success");
    //         console.log('Successfully added product');
    //         await handleUpdateView();
    //     } catch (error) {
    //         setNotification("Error adding product", "error");
    //         console.log(error);
    //     }
    //     setAddProductLoading(false);
    // }


    // sunny(2-01-26)--> handling the twice call of Add Prduct API 
    async function handleInsertProduct(
        item: InventoryData,
        seasonIds: string,
        packageLineId: string
    ) {
        const requestKey = `${item.ProductId}|${seasonIds}|${packageLineId || "root"}`;

        if (addProductRequestRef.current.has(requestKey)) {
            console.warn("Duplicate addProduct prevented:", requestKey);
            return;
        }

        addProductRequestRef.current.add(requestKey);

        setAddProductLoading(true);

        try {
            if (!agreementId) {
                throw new Error("Missing agreement id");
            }

            const result = await executeAddProductFlow(
                item,
                agreementId,
                seasonIds,
                packageLineId
            );

            if (!result.success) {
                throw new Error(
                    result.details ||
                    result.message ||
                    "Failed to add product."
                );
            }

            setNotification("Successfully added product", "success");
            await handleUpdateView();
        } catch (error: any) {
            console.error("Error while adding product:", error);
            setNotification(
                error?.message || "Error adding product",
                "error"
            );
        } finally {
            setAddProductLoading(false);
            addProductRequestRef.current.delete(requestKey);
        }
    }

    //Sunny --> AddProductExtension
    function handleRemoveSelectedProduct(stagedId: string) {
        setSelectedProducts((prev) =>
            prev.filter((item) => item.stagedId !== stagedId)
        );
    }


    //Sunny--> AddProductExtension
    function handleStageProduct(
        item: InventoryData,
        seasonIds: string,
        packageLineId: string
    ) {
        const stagedItem = {
            ...item,
            seasonIds,
            packageLineId,
            stagedId: `${item.ProductId}-${Date.now()}-${Math.random()}`,
            QtyUnits: item.QtyUnits ?? 1,
            QtyEvents: item.QtyEvents ?? 1
        };

        setSelectedProducts((prev) => [...prev, stagedItem]);
    }

    //Sunny--> AddProoductExtension 
    function handleSelectedProductChange(
        stagedId: string,
        field: "QtyUnits" | "QtyEvents",
        value: number
    ) {
        setSelectedProducts((prev) =>
            prev.map((item) =>
                item.stagedId === stagedId
                    ? { ...item, [field]: value }
                    : item
            )
        );
    }

    //Sunny--> AddProoductExtension 
    // async function handleSubmitSelectedProducts() {
    //     try {
    //         if (!selectedProducts.length) {
    //             setNotification("No products selected", "warning");
    //             return;
    //         }

    //         for (const item of selectedProducts) {
    //             await handleInsertProduct(
    //                 item,
    //                 item.seasonIds || "",
    //                 item.packageLineId || ""
    //             );
    //         }

    //         setSelectedProducts([]);
    //         setInventoryModalOpen(false);
    //         setNotification("Successfully submitted selected products", "success");
    //     } catch (error) {
    //         console.log(error);
    //         setNotification("Error submitting selected products", "error");
    //     }
    // }

    /**
     * Bulk-submit flow (v2 — 17-Apr-2026).
     *
     * Flow:
     *   1. Map staged products (including any package products, with their
     *      PackageComponents nested) into AddProductsBatchRequestProduct shape.
     *   2. Call ats_CheckProductsAvailability — in v2 this ALSO auto-creates
     *      any missing IBS / Rate records. If any verdict is still non-ok
     *      (e.g. no template IBS exists for the product anywhere) we abort
     *      with a clear message.
     *   3. Call executeAddProductsBatchUntilComplete, which loops
     *      ats_AddProductsBatch while the server returns leftoverProducts.
     *      onProgress fires after every sub-call so the progress bar stays
     *      smooth even when the server yields at its 90 s soft-timeout.
     *   4. Show per-product failures surfaced by the backend in the final
     *      notification — those rows were rolled back server-side so data
     *      stays consistent.
     *   5. Single handleUpdateView() at the very end.
     *
     * No legacy per-product fallback remains — the new plugin handles
     * packages too.
     */
    async function handleSubmitSelectedProducts() {
        try {
            if (!selectedProducts.length) {
                setNotification("No products selected", "warning");
                return;
            }

            if (!agreementId) {
                setNotification("Missing agreement id", "error");
                return;
            }

            setIsSubmittingProducts(true);
            setSubmitProgress(0);
            setSubmitProgressText("Checking product availability…");

            // 1. Build the bulk request.
            // Staged products carry PackageComponents already in the cart-model
            // shape; project them into the API shape so seasonIds / IsPackage
            // is explicit on every request, and components inherit their
            // parent's seasonIds when they don't specify their own.
            const toRequest = (p: any): AddProductsBatchRequestProduct => ({
                ProductId: p.ProductId,
                ProductName: p.ProductName,
                RateType: p.RateType,
                Rate: p.Rate,
                RateId: p.RateId,
                HardCost: p.HardCost,
                ProductionCost: p.ProductionCost,
                QtyUnits: p.QtyUnits ?? 1,
                QtyEvents: p.QtyEvents ?? 1,
                seasonIds: p.seasonIds || "",
                packageLineId: p.packageLineId || "",
                IsPackage: !!p.IsPackage,
                Description: p.Description,
                PackageComponents: Array.isArray(p.PackageComponents)
                    ? p.PackageComponents.map((c: any): AddProductsBatchProductFields => ({
                          ProductId: c.ProductId,
                          ProductName: c.ProductName,
                          RateType: c.RateType,
                          Rate: c.Rate,
                          RateId: c.RateId,
                          HardCost: c.HardCost,
                          ProductionCost: c.ProductionCost,
                          QtyUnits: c.QtyUnits ?? 1,
                          QtyEvents: c.QtyEvents ?? 1,
                          seasonIds: c.seasonIds || "", // inherits parent at server side
                          Description: c.Description,
                      }))
                    : undefined,
            });

            const bulkRequest: AddProductsBatchRequestProduct[] =
                selectedProducts.map(toRequest);

            // 2. Pre-flight (auto-generates IBS/Rate as needed in v2).
            const preflight = await callCheckProductsAvailability(agreementId, bulkRequest);

            if ("error" in preflight) {
                console.error("Pre-flight error:", preflight);
                setNotification(preflight.message || "Availability check failed", "error");
                setIsSubmittingProducts(false);
                return;
            }

            if (!preflight.allOk) {
                // Surface the first blocking verdict in the toast.
                const firstBlock = preflight.results.find((r) => r.verdict !== "ok");
                const reason =
                    firstBlock?.verdict === "ibs_missing"
                        ? "No existing Inventory-By-Season to template from"
                        : firstBlock?.verdict === "rate_missing"
                        ? "Rate could not be created"
                        : firstBlock?.verdict === "oversell_blocked"
                        ? "Not enough quantity available"
                        : firstBlock?.verdict ?? "availability check failed";
                setNotification(
                    `Submission aborted — ${reason}${
                        firstBlock?.productId ? ` (product ${firstBlock.productId})` : ""
                    }.`,
                    "error"
                );
                setIsSubmittingProducts(false);
                return;
            }

            // Info-only: tell the user which records got auto-generated.
            const autoGenCount = preflight.results.filter(
                (r) => r.ibsAutoCreated || r.rateAutoCreated
            ).length;
            if (autoGenCount > 0) {
                console.info(
                    `${autoGenCount} IBS/Rate record(s) auto-generated during pre-flight.`
                );
            }

            // 3. Loop ats_AddProductsBatch with leftover-driven pagination.
            setSubmitProgressText(`Adding ${bulkRequest.length} product(s)…`);

            const final = await executeAddProductsBatchUntilComplete(
                agreementId,
                bulkRequest,
                {
                    // Smaller chunk = smoother progress bar; larger chunk =
                    // fewer HTTP round-trips. 25 is a good sweet spot in a
                    // sandbox — bump to 100 once latency numbers are known.
                    chunkSize: 25,
                    onProgress: (p) => {
                        const pct =
                            p.total > 0
                                ? Math.min(100, Math.round((p.processed / p.total) * 100))
                                : 0;
                        setSubmitProgress(pct);
                        setSubmitProgressText(
                            `${p.processed} of ${p.total} product(s) added (${pct}%)${
                                p.failed.length > 0 ? ` — ${p.failed.length} failed` : ""
                            }`
                        );
                    },
                }
            );

            // 4. Report back to the user. Partial successes ARE persisted in D365
            // (per-product transaction atomicity); the UI makes this explicit so
            // the user knows what to retry.
            if (final.error || final.errorMessage) {
                console.error("AddProductsBatch loop error:", final);
                setNotification(
                    final.errorMessage || "Bulk add encountered a transport error.",
                    "error"
                );
            } else if (final.failed.length > 0) {
                const firstFailReason = final.failed[0]?.reason ?? "unknown error";
                setNotification(
                    `Added ${final.createdOpportunityProductIds.length} opp-product(s); ${final.failed.length} product(s) failed (first: ${firstFailReason}).`,
                    "warning"
                );
            } else {
                setNotification(
                    `Successfully added ${final.createdOpportunityProductIds.length} opp-product(s).`,
                    "success"
                );
            }

            // Clear staging + close modal only when EVERYTHING succeeded; otherwise
            // keep the failed products staged so the user can retry them.
            if (final.success) {
                setSelectedProducts([]);
                setInventoryModalOpen(false);
            } else if (final.failed.length > 0) {
                const failedIds = new Set(final.failed.map((f) => f.productId));
                setSelectedProducts((prev) =>
                    prev.filter((p) => failedIds.has(p.ProductId))
                );
            }

            // 5. Single refresh at the end.
            await handleUpdateView();
        } catch (error) {
            console.error("handleSubmitSelectedProducts error:", error);
            setNotification("Error submitting selected products", "error");
        } finally {
            setIsSubmittingProducts(false);
        }
    }


    async function handleUpdateLineItem(data: OpportunityLineItemData) {
        setUpdateLineItemLoading(true);
        try {
            const result = await updateOpportunityLineItem(data);

            if (result.error) throw new Error(result.message);

            setNotification("Successfully updated line item", "success");

            closeLineItemModal();
            closeLegalDefModal();
            closeDescriptionModal();

            await handleUpdateView();
        } catch (error) {
            setNotification("Error updating line item", "error");
            console.log(error);
        }
        setUpdateLineItemLoading(false);
    }

    async function handleUndoPriceOverride(data: OpportunityLineItemData) {
        setLoading(true);
        try {
            let d = {
                ...data,
                ResetOverride: true,

            };

            const result = await updateOpportunityLineItem(d);

            if (result.error) throw new Error(result.message);

            if (opportunityModalOpen) setOpportunityModalOpen(false);

            await handleUpdateView();
        } catch (error) {
            setNotification("Error undoing price override", "error");
            console.log(error);
        }
        setLoading(false);
    }

    async function handleUpdateOpportunity(data: OpportunityData) {
        setOpportunityLoading(true);
        try {
            const result = await updateOpportunity(data);

            if (result.error) throw new Error(result.message);
            setNotification("Successfully updated opportunity", "success");
            closeOpportunityModal();
            await handleUpdateView();
        } catch (error) {
            setNotification("Error updating opportunity", "error");
            console.log(error);
        }

        setOpportunityLoading(false);
    }

    async function handleDeleteLineItems(ids: Array<string>) {
        setLoading(true);
        try {
            const result = await deleteOpportunityLineItems(ids);

            if (result.error) throw new Error(result.message);
            setNotification("Successfully deleted opportunity line items", "success");
            await handleUpdateView();
        } catch (error) {
            setNotification("Error deleting opportunity line items", "error");
            console.log(error);
        }

        setLoading(false);
    }

    async function handleEscalateRevenue(
        escalationType: "Fixed" | "Percent" | string,
        escalationValue: number
    ) {
        setEscalateRevenueLoading(true);
        try {
            if (!agreementId) throw new Error("Missing agreement id");
            const result = await escalateRevenue(
                escalationType,
                escalationValue,
                agreementId
            );

            if (result.error) throw new Error(result.message);

            setEscalateRevenueModalOpen(false);
            await handleUpdateView();
        } catch (error) {
            setNotification("Error escalating revenue", "error");
            console.log(error);
        }
        setEscalateRevenueLoading(false);
    }

    async function handleGetAvailableSeasons(
        ProductId: string,
        seasonIds: string
    ) {
        console.log("handleGetAvailableSeasons: ", ProductId, seasonIds);
        setLoading(true);
        try {
            const result = await getAvailableSeasonsByProduct(
                ProductId,
                seasonIds
            );
            setLoading(false);
            return result.map((s) => ({
                value: s.value,
                label: s.label,
            }));
        } catch (error) {
            setNotification("Error retrieving available seasons", "error");
            console.log(error);
        }
        setLoading(false);
        return [];
    }

    async function handleCloneAgreement(firstYearOnly: boolean) {
        setAddProductLoading(true);
        console.log("Cloning agreement: firstYearOnly = ", firstYearOnly);
        try {
            if (!agreementId) throw new Error("Missing agreement id");
            const result = await cloneAgreement(agreementId, firstYearOnly);

            if (result.error) throw new Error(result.message);

            if (result.ClonedAgreementId) {
                setTimeout(async () => {
                    await openAgreementRecordPage(result.ClonedAgreementId);
                }, 1500);
            }
            console.log("result.ClonedAgreementId = ", result.ClonedAgreementId);
            setNotification("Successfully cloned agreement", "success");
            setCloneAgreementModalOpen(false);

        } catch (error) {
            setNotification("Error cloning agreement", "error");
            console.log(error);
        }
        setAddProductLoading(false);
    }

    async function openAgreementRecordPage(agreementId: string) {
        try {
            console.log("openAgreementRecordPage", context);
            return await context.navigation.openForm({
                entityName: "ats_agreement",
                entityId: agreementId,
                openInNewWindow: true
            });
        } catch (error) {
            console.log("openAgreementRecordPage error", error);
        }
    }

    // --------------------------------------------------------
    // EFFECTS
    // --------------------------------------------------------

    React.useEffect(() => {
        try {
            if (!selectedSeason) setSelectedSeason(opportunities[0].StartSeason);
        } catch (error) {
            console.log(error);
        }
    }, [opportunities]);

    React.useEffect(() => {
        try {

            const collapsedItemState = {};

            uiLineItems.forEach((i) => {
                collapsedItemState[i.uid] = false;
            });

            collapsedItemState["header"] = collapsedItems["header"] ? true : false;

            setCollapsedItems(collapsedItemState);
        } catch (error) {
            console.log(error);
        }
    }, [uiLineItems]);

    const agreementSeasons = opportunities.map((opportunity) => ({
        value: opportunity.StartSeason,
        label: opportunity?.SeasonName || "Season"
    }));

    const agreementLineItems =
        lineItems.map(item => ({
            ProductId: item.Product2.Id,
            RateType: item.rates.length > 0 ? item.rates[0].RateType : null
        }));

    return (
        <div>
            {
                Array.isArray(opportunities) && opportunities.length > 0 &&
                <div
                    style={{
                        padding: "0.5rem",
                        display: "flex",
                        justifyContent: "flex-start",
                        gap: "1rem",
                    }}
                >
                    <div
                        style={{
                            display: "flex",
                            flexDirection: "column",
                            gap: ".25rem",
                            flex: "1",
                        }}
                    >
                        <label
                            style={{
                                fontSize: ".8em",
                                textAlign: "left",
                                paddingLeft: ".25rem",
                            }}
                        >
                            Search
                        </label>
                        <Input
                            value={searchQuery}
                            onChange={(e) => setSearchQuery(e.target.value)}
                            placeholder="Search items..."
                        />
                    </div>
                    <div
                        style={{
                            display: "flex",
                            flexDirection: "column",
                            gap: ".25rem",
                            flex: "1",
                        }}
                    >
                        <label
                            style={{
                                fontSize: ".8em",
                                textAlign: "left",
                                paddingLeft: ".25rem",
                            }}
                        >
                            Filter by Division/Product Family/Product Sub-Family
                        </label>
                        <CascadingSearch
                            items={lineItems.map((i) => i?.Product2)}
                            hierarchy={["Division", "ProductFamily", "ProductSubFamily"]}
                            value={cascaderValue}
                            onChange={onChangeCascadingSearch}
                        />
                    </div>
                </div>
            }

            {
                Array.isArray(opportunities) && opportunities.length > 0 &&
                <div
                    style={{
                        padding: "0.5rem",
                        display: "flex",
                        justifyContent: "flex-end",
                        gap: "2rem",
                    }}
                >
                    <Button
                        type="link"
                        size="small"
                        onClick={() => setEscalateRevenueModalOpen(true)}
                        disabled={!isAuthorized}
                    >
                        Escalate Revenue
                    </Button>
                    <Button type="link" size="small" onClick={toggleAllCollapsed}>
                        {isAllItemsExpanded ? "Collapse All" : "Expand All"}
                    </Button>
                </div>
            }

            <div className="agreement-table-container">
                <table className="agreement-table">
                    {Array.isArray(opportunities) && opportunities.length > 0 ? (
                        <AgreementTableHeader
                            opportunities={opportunities}
                            setSelectedSeason={setSelectedSeason}
                            selectedSeason={selectedSeason}
                            isCollapsed={!!collapsedItems["header"]}
                            toggleCollapse={toggleCollapse}
                            openOpportunityModal={openOpportunityModal}
                            hiddenFields={hiddenFields}
                            lineItemsCount={lineItems.length}
                        />
                    ) : (
                        <div style={{ padding: "0.5rem", width: "100%" }}>
                            <p>
                                Please populate the agreement's Start Season and Contract Length
                                (Years) fields to continue.
                            </p>
                        </div>
                    )}

                    {Array.isArray(opportunities) && opportunities.length > 0 && (
                        <AgreementTableBody
                            opportunities={opportunities}
                            lineItems={paginationData.paginatedItems}
                            setSelectedSeason={setSelectedSeason}
                            selectedSeason={selectedSeason}
                            collapsedItems={collapsedItems}
                            toggleCollapse={toggleCollapse}
                            openLineItemModal={openLineItemModal}
                            deleteLineItems={handleDeleteLineItems}
                            openDescriptionModal={openDescriptionModal}
                            openLegalDefModal={openLegalDefModal}
                            openQtyAvailableModal={openQtyAvailableModal}
                            handleUndoPriceOverride={handleUndoPriceOverride}
                            openNewPackageComponentModal={openNewPackageComponentModal}
                            hiddenFields={hiddenFields}
                            alternateUI={alternateUI}
                        />
                    )}
                </table>
            </div>

            {
                Array.isArray(opportunities) && opportunities.length > 0 &&
                <footer
                    style={{
                        display: "flex",
                        flexDirection: "column",
                        gap: "0.5rem",
                        padding: "1rem",
                    }}
                >
                    <PaginationControls {...paginationData} />
                    <div style={{ display: "flex", justifyContent: "flex-start", gap: "1rem" }}>
                        <Button
                            type="primary"
                            onClick={() => {
                                setIncludePackages(true);
                                setPackageLineId("");
                                setInventoryModalOpen(true);
                            }}
                            icon={<AddIcon fontSize="medium" sx={{ color: !isAuthorized ? "#ccc" : "white" }} />}
                            disabled={!isAuthorized}
                        >
                            Add Product
                        </Button>
                        <Button
                            type="primary"
                            ghost
                            onClick={() => setCloneAgreementModalOpen(true)}
                            icon={<CopyIcon fontSize="small" />}
                            disabled={!isAuthorized}
                        >
                            Clone Agreement
                        </Button>
                    </div>
                </footer>
            }

            <Modal
                open={inventoryModalOpen}
                afterClose={() => {
                    setInventoryModalOpen(false);
                    setSelectedProducts([]);
                    resetSubmitProgress();
                }}
                onCancel={() => {
                    setInventoryModalOpen(false);
                    setSelectedProducts([]);
                    resetSubmitProgress();
                }}
                footer={null}
                width="80vw"
            >
                <div
                    style={{
                        display: "flex",
                        gap: "16px",
                        alignItems: "stretch",
                        width: "100%"
                    }}
                >
                    <div
                        style={{
                            flex: 2,
                            minWidth: 0
                        }}
                    >
                        <div className="modal-container">
                            <InventoryTable
                                inventory={includePackages ? inventory : inventory.filter(item => !item.IsPackage)}
                                agreementSeasons={agreementSeasons}
                                agreementLineItems={includePackages ? agreementLineItems : []}
                                packageLineId={packageLineId}
                                handleInsertProduct={handleStageProduct}
                                handleGetAvailableSeasons={handleGetAvailableSeasons}
                            />
                        </div>
                    </div>

                    <div
                        style={{
                            flex: 1,
                            minWidth: "320px",
                            border: "1px solid #d9d9d9",
                            borderRadius: "8px",
                            padding: "12px",
                            minHeight: "300px",
                            background: "#fff"
                        }}
                    >
                        <h3 style={{ marginTop: 0 }}>Selected Products</h3>

                        {selectedProducts.map((product) => (
                            <div
                                key={product.stagedId}
                                style={{
                                    padding: "8px 0",
                                    borderBottom: "1px solid #f0f0f0"
                                }}
                            >
                                <div
                                    style={{
                                        display: "grid",
                                        gridTemplateColumns: "minmax(0, 1fr) 88px 88px 70px",
                                        columnGap: "12px",
                                        alignItems: "center",
                                        width: "100%"
                                    }}
                                >
                                    <div
                                        style={{
                                            minWidth: 0,
                                            maxWidth: "100%",
                                            overflow: "hidden",
                                            textOverflow: "ellipsis",
                                            whiteSpace: "nowrap"
                                        }}
                                        title={product.ProductName}
                                    >
                                        {product.ProductName}
                                    </div>

                                    <input
                                        type="number"
                                        min={1}
                                        value={product.QtyUnits ?? 1}
                                        onChange={(e) =>
                                            handleSelectedProductChange(
                                                product.stagedId,
                                                "QtyUnits",
                                                Math.max(1, Number(e.target.value) || 1)
                                            )
                                        }
                                        style={{
                                            width: "88px",
                                            height: "36px",
                                            border: "1px solid #d9d9d9",
                                            borderRadius: "8px",
                                            padding: "0 10px",
                                            boxSizing: "border-box",
                                            outline: "none",
                                            background: "#fff"
                                        }}
                                    />

                                    {product.RateType === "Individual" ? (
                                        <input
                                            type="number"
                                            min={1}
                                            value={product.QtyEvents ?? 1}
                                            onChange={(e) =>
                                                handleSelectedProductChange(
                                                    product.stagedId,
                                                    "QtyEvents",
                                                    Math.max(1, Number(e.target.value) || 1)
                                                )
                                            }
                                            style={{
                                                width: "88px",
                                                height: "36px",
                                                border: "1px solid #d9d9d9",
                                                borderRadius: "8px",
                                                padding: "0 10px",
                                                boxSizing: "border-box",
                                                outline: "none",
                                                background: "#fff"
                                            }}
                                        />
                                    ) : (
                                        <div
                                            style={{
                                                width: "88px",
                                                height: "36px"
                                            }}
                                        />
                                    )}

                                    <Button
                                        type="link"
                                        danger
                                        size="small"
                                        onClick={() => handleRemoveSelectedProduct(product.stagedId)}
                                        style={{
                                            padding: 0,
                                            justifySelf: "start"
                                        }}
                                    >
                                        Remove
                                    </Button>
                                </div>
                            </div>
                        ))}

                        {isSubmittingProducts && (
                            <div style={{ marginTop: "16px", marginBottom: "12px" }}>
                                <div
                                    style={{
                                        fontSize: "13px",
                                        fontWeight: 500,
                                        color: "#262626",
                                        marginBottom: "8px"
                                    }}
                                >
                                    Total Products Added:
                                </div>

                                <div
                                    style={{
                                        width: "100%",
                                        height: "28px",
                                        border: "1px solid #d9d9d9",
                                        borderRadius: "8px",
                                        background: "#f5f5f5",
                                        overflow: "hidden",
                                        position: "relative"
                                    }}
                                >
                                    <div
                                        style={{
                                            width: `${submitProgress}%`,
                                            height: "100%",
                                            background: "linear-gradient(90deg, #1677ff 0%, #4096ff 100%)",
                                            transition: "width 0.3s ease",
                                            display: "flex",
                                            alignItems: "center",
                                            justifyContent: "center",
                                            color: "#fff",
                                            fontSize: "13px",
                                            fontWeight: 600,
                                            whiteSpace: "nowrap"
                                        }}
                                    >
                                        {submitProgress > 0 ? `${submitProgress}%` : ""}
                                    </div>

                                    {submitProgress === 0 && (
                                        <div
                                            style={{
                                                position: "absolute",
                                                inset: 0,
                                                display: "flex",
                                                alignItems: "center",
                                                justifyContent: "center",
                                                fontSize: "13px",
                                                fontWeight: 600,
                                                color: "#595959"
                                            }}
                                        >
                                            0%
                                        </div>
                                    )}
                                </div>

                                <div
                                    style={{
                                        marginTop: "6px",
                                        fontSize: "12px",
                                        color: "#595959"
                                    }}
                                >
                                    {submitProgressText}
                                </div>
                            </div>
                        )}

                        <div style={{ marginTop: "16px" }}>
                            <Button
                                type="primary"
                                block
                                onClick={handleSubmitSelectedProducts}
                                disabled={!selectedProducts.length || isSubmittingProducts}
                                loading={isSubmittingProducts}
                            >
                                {isSubmittingProducts ? "Submitting Products..." : "Submit"}
                            </Button>
                        </div>
                    </div>
                </div>

                {addProductLoading && (
                    <div className="overlay">
                        <Spin />
                    </div>
                )}
            </Modal>

            

            <Modal
                open={lineItemModalOpen}
                afterClose={() => closeLineItemModal()}
                onCancel={() => closeLineItemModal()}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    {cachedLineItem ? (
                        <OpportunityLineItemForm
                            data={cachedLineItem}
                            onSubmit={handleUpdateLineItem}
                            handleUndoOverride={async (data: OpportunityLineItemData) => {
                                setUpdateLineItemLoading(true);
                                await handleUndoPriceOverride(data);
                                setUpdateLineItemLoading(false);
                            }}
                            hiddenFields={hiddenFields}
                        />
                    ) : (
                        <p>There was an error initializing this form.</p>
                    )}
                </div>

                {updateLineItemLoading && (
                    <div className="overlay">
                        <Spin />
                    </div>
                )}
            </Modal>

            <Modal
                open={opportunityModalOpen}
                afterClose={() => closeOpportunityModal()}
                onCancel={() => closeOpportunityModal()}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    {cachedOpportunity ? (
                        <OpportunityForm
                            data={cachedOpportunity}
                            onSubmit={handleUpdateOpportunity}
                            hiddenFields={hiddenFields}
                        />
                    ) : (
                        <p>There was an error initializing this form.</p>
                    )}
                </div>

                {opportunityLoading && (
                    <div className="overlay">
                        <Spin />
                    </div>
                )}
            </Modal>

            <Modal
                open={descriptionModalOpen}
                afterClose={() => closeDescriptionModal()}
                onCancel={() => closeDescriptionModal()}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    {cachedLineItem ? (
                        <OpportunityLineItemDescriptionForm
                            data={cachedLineItem}
                            onSubmit={handleUpdateLineItem}
                            disabled={!isAuthorized}
                        />
                    ) : (
                        <p>There was an error initializing this form.</p>
                    )}
                </div>

                {updateLineItemLoading && (
                    <div className="overlay">
                        <Spin />
                    </div>
                )}
            </Modal>

            <Modal
                open={legalDefModalOpen}
                afterClose={() => closeLegalDefModal()}
                onCancel={() => closeLegalDefModal()}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    {cachedLineItem ? (
                        <OpportunityLineItemLegalDefForm
                            data={cachedLineItem}
                            onSubmit={handleUpdateLineItem}
                            disabled={!isAuthorized}
                        />
                    ) : (
                        <p>There was an error initializing this form.</p>
                    )}
                </div>

                {updateLineItemLoading && (
                    <div className="overlay">
                        <Spin />
                    </div>
                )}
            </Modal>

            <Modal
                open={escalateRevenueModalOpen}
                afterClose={() => setEscalateRevenueModalOpen(false)}
                onCancel={() => setEscalateRevenueModalOpen(false)}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    <EscalateRevenueForm onSubmit={handleEscalateRevenue} />
                </div>

                {escalateRevenueLoading && (
                    <div className="overlay">
                        <Spin />
                    </div>
                )}
            </Modal>

            <Modal
                open={qtyAvailableModalOpen}
                afterClose={() => closeQtyAvailableModal()}
                onCancel={() => closeQtyAvailableModal()}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    {cachedLineItem ? (
                        <QuantityAvailable data={cachedLineItem} />

                    ) : (
                        <p>There was an error initializing this form.</p>
                    )}

                </div>
            </Modal>

            <Modal
                open={cloneAgreementModalOpen}
                afterClose={() => setCloneAgreementModalOpen(false)}
                onCancel={() => setCloneAgreementModalOpen(false)}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    <CloneAgreementForm
                        agreementId={agreementId || null}
                        onSubmit={(firstYearOnly) => handleCloneAgreement(firstYearOnly)}
                        onCancel={() => setCloneAgreementModalOpen(false)}
                    />
                </div>
                {addProductLoading && (
                    <div className="overlay">
                        <Spin />
                    </div>
                )}
            </Modal>

            {loading && (
                <div className="overlay">
                    <Spin />
                </div>
            )}
        </div>
    );
}
