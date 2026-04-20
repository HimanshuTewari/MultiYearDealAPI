import * as React from "react";
import { InventoryData } from "../../../models";
import ProductBadge from "../ProductBadge";
import {
    formatDollarValue,
    formatNumberValue,
    likeMatch,
} from "../../../utilities";
import { Input, Button, Modal, Popover } from "antd";
import InfoIcon from '@mui/icons-material/Info';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import ShoppingCartIcon from "@mui/icons-material/ShoppingCart";
import "./index.css";
import CascadingSearch from "../CascadingSearch";
import { useDebounce } from "../../../hooks/useDebounce";
import usePagination from "../../../hooks/usePagination";
import PaginationControls from "../PaginationControls";
import FormattedInputNumber from "../FormattedInputNumber";
import SelectSeasonsForm from "../Forms/SelectSeasons";

interface ExtendedInventoryData extends InventoryData {
    uid?: string;
    InCart: number;
}

export default function InventoryTable({
    inventory,
    agreementSeasons,
    agreementLineItems,
    packageLineId,
    handleInsertProduct,
    handleGetAvailableSeasons
}: {
    inventory: Array<InventoryData>;
    agreementSeasons: Array<{ value: string; label: string }>;
    agreementLineItems: Array<{ ProductId: string; RateType: string | null }>;
    packageLineId: string;
    handleInsertProduct: (item: InventoryData, seasonIds: string, packageLineId: string) => void;
    handleGetAvailableSeasons: (productId: string, seasonIds: string) => Promise<Array<{ value: string; label: string }>>;
}) {
    const [inventoryState, setInventoryState] = React.useState<Record<string, ExtendedInventoryData>>({});
    const [productFilters, setProductFilters] = React.useState<Record<string, string>>({});
    const [searchQuery, setSearchQuery] = React.useState("");

    const [selectSeasonModalOpen, setSelectSeasonModalOpen] = React.useState(false);
    const [pendingInsertItem, setPendingInsertItem] = React.useState<InventoryData | null>(null);
    const [productSeasons, setProductSeasons] = React.useState<{ value: string; label: string }[]>([]);
    const [expandedItems, setExpandedItems] = React.useState<Record<string, boolean>>({});

    // ✅ NEW: Prevent duplicate submit causing double API hits
    const seasonSubmitLockRef = React.useRef(false);

    const debouncedSearchQuery = useDebounce(searchQuery, 1000);

    const cascaderValue = React.useMemo(() => {
        try {
            const { Division, ProductFamily, ProductSubFamily } =
                (productFilters as Record<keyof InventoryData, string>) || {};

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

    const formattedInventory: Array<ExtendedInventoryData> = React.useMemo(() => {
        try {
            return inventory.map((i) => ({
                ...i,
                InCart: countExistingInCart(i.ProductId, i.RateType) ? countExistingInCart(i.ProductId, i.RateType) : 0,
                uid: crypto.randomUUID()
            }));
        } catch (error) {
            console.log(error);
            return [];
        }
    }, [inventory]);

    const filteredItems = React.useMemo(() => {
        let items = [...formattedInventory];
        try {
            if (Object.keys(productFilters).length > 0) {
                console.log(productFilters);
                items = items.filter((i) =>
                    Object.entries(productFilters).every(([key, value]) => {
                        try {
                            return (i as any)[key] === value;
                        } catch (error) {
                            console.log(error);
                            return false;
                        }
                    })
                );
            }

            if (debouncedSearchQuery && debouncedSearchQuery.length > 2) {
                const searchTerms = searchQuery
                    .trim()
                    .toLowerCase()
                    .split(/,| |-+/)
                    .filter((term) => term);

                if (searchTerms.length > 0) {
                    items = items.filter((item) => {

                        let hasMatchingPackageComponents = false;
                        if (Array.isArray(item.PackageComponents) && item.PackageComponents.length > 0) {
                            hasMatchingPackageComponents = item.PackageComponents.some((itemComponent: any) => {
                                let name = itemComponent.ProductName;
                                name = name.toLowerCase();
                                return likeMatch(searchTerms, name)
                            })
                        }

                        return Object.values(item as any).some((attribute: any) => {
                            let attr = attribute;
                            if (typeof attr === "string") attr = attr.toLowerCase();
                            return likeMatch(searchTerms, attr) || hasMatchingPackageComponents;
                        });
                    });
                }
            }

            items.sort((a, b) => {
                const familyCompare = (a.ProductFamily || '').localeCompare(b.ProductFamily || '');
                if (familyCompare !== 0) return familyCompare;

                const subFamilyCompare = (a.ProductSubFamily || '').localeCompare(b.ProductSubFamily || '');
                if (subFamilyCompare !== 0) return subFamilyCompare;

                return (a.ProductName || '').localeCompare(b.ProductName || '');
            });

            return items;
        } catch (error) {
            console.log(error);
            return items;
        }
    }, [productFilters, debouncedSearchQuery, formattedInventory, searchQuery]);

    function countExistingInCart(productId: string, rateType: string) {
        console.log('checking existance in cart already');
        return agreementLineItems.filter((item) => item.ProductId === productId && item.RateType === rateType).length;
    }

    const paginationData = usePagination({
        items: filteredItems,
        defaultPageSize: 20,
    });

    function handleChange(index: string, field: string, value: any) {
        setInventoryState((state) => {
            const s = { ...state };
            s[index][field] = value;
            return s;
        });
    }

    function handleCascadingFilter(values: any) {
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

    async function handleAddProduct(uid: string) {
        console.log('In handle add product');
        console.log('packageLineId:', packageLineId);

        const product = inventoryState[uid];
        const { InCart, uid: productUid, ...item } = product;
        setPendingInsertItem(item);

        const seasonIds = agreementSeasons.map(season => season.value).join(',');
        const selectedSeasons = agreementSeasons.map(s => s.value);

        if (packageLineId && packageLineId !== '') {
            // do not display season select if adding a product as a package component, use all seasons
            await handleSelectSeasons(selectedSeasons, item);
        }
        else {
            // display season select if adding a product to the agreement
            try {
                const availableSeasons = await handleGetAvailableSeasons(
                    product.ProductId,
                    seasonIds
                );
                setProductSeasons(availableSeasons);
                setSelectSeasonModalOpen(true);
            }
            catch (err) {
                console.error("Failed to load available seasons", err);
            }
        }
    }

    async function handleSelectSeasons(selectedSeasons: string[], product?: InventoryData) {
        // ✅ NEW: lock to prevent double-submit / double API call
        if (seasonSubmitLockRef.current) return;
        seasonSubmitLockRef.current = true;

        try {
            const itemToInsert = product ?? pendingInsertItem;
            if (itemToInsert) {
                const seasonIds = selectedSeasons.join(',');
                handleInsertProduct(itemToInsert, seasonIds, packageLineId);
            }
            setSelectSeasonModalOpen(false);
        } finally {
            // ✅ unlock after closing modal (next tick)
            setTimeout(() => {
                seasonSubmitLockRef.current = false;
            }, 0);
        }
    }

    function toggleExpandItem(uid: string) {
        setExpandedItems(prev => ({
            ...prev,
            [uid]: !prev[uid],
        }));
    }

    React.useEffect(() => {
        try {
            if (Object.values(inventoryState).length < 1) {
                const state: Record<string, ExtendedInventoryData> = {};

                formattedInventory.forEach((i) => {
                    if (i.uid) {
                        const inv = { ...i }
                        delete (inv as any).uid;
                        state[i.uid] = inv;

                        if (Array.isArray(i.PackageComponents)) {
                            i.PackageComponents.forEach((c: any) => {
                                const componentUid = crypto.randomUUID();
                                state[componentUid] = {
                                    ...c,
                                    InCart: 0,
                                    uid: componentUid
                                } as ExtendedInventoryData;
                            });
                        }
                    }
                });

                setInventoryState(state);
            }
        } catch (error) {
            console.log(error);
        }
    }, [formattedInventory, inventoryState]);

    return (
        <>
            <div
                style={{
                    padding: "0.5rem 0 1rem 0",
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
                        items={inventory.map((i) => i)}
                        hierarchy={["Division", "ProductFamily", "ProductSubFamily"]}
                        value={cascaderValue}
                        onChange={handleCascadingFilter}
                    />
                </div>
            </div>

            <div className="inventory-table-container">
                <table className="inventory-table">
                    <thead>
                        <tr>
                            <th className="sticky-col left">Product</th>
                            {agreementLineItems?.length > 0 && <th>In Cart</th>}
                            <th>Rate Type</th>
                            <th>Rate</th>
                            <th>Quantity Available</th>
                            <th>Quantity</th>
                            <th>Quantity of Events</th>
                            <th className="sticky-col right" />
                        </tr>
                    </thead>
                    <tbody>
                        {Array.isArray(paginationData.paginatedItems) &&
                            paginationData.paginatedItems.length > 0 &&
                            paginationData.paginatedItems.map((item, index) => (
                                <React.Fragment key={index}>
                                    <tr>
                                        <td className="sticky-col left" style={{ padding: '0' }}>
                                            <div style={{ display: 'flex', flexDirection: 'column' }}>
                                                <div style={{ minWidth: 300, display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0.5rem' }}>
                                                    <ProductBadge
                                                        product={{
                                                            Id: item.ProductId,
                                                            Name: item.ProductName,
                                                            Division: item.Division,
                                                            ProductFamily: item.ProductFamily,
                                                            ProductSubFamily: item.ProductSubFamily,
                                                            IsPassthroughCost: item.IsPassthroughCost,
                                                            IsPackage: item.IsPackage
                                                        }}
                                                    />
                                                    {item.IsPackage && (
                                                        <div className="product-icon-wrapper"
                                                            onClick={() => toggleExpandItem(item.uid!)}
                                                            style={{ cursor: "pointer", display: "flex", alignItems: "center" }}
                                                            title={expandedItems[item.uid!] ? "Collapse package" : "Expand package"}>
                                                            {expandedItems[item.uid!] ? (
                                                                <ExpandMoreIcon fontSize="medium" />
                                                            ) : (
                                                                <ChevronRightIcon fontSize="medium" />
                                                            )}
                                                        </div>
                                                    )}
                                                    {!item.IsPackage && item.Description && (
                                                        <Popover
                                                            content={item.Description}
                                                            overlayStyle={{ maxWidth: 250, whiteSpace: "normal", wordWrap: "break-word" }}>
                                                            <div className="product-icon-wrapper">
                                                                <InfoIcon fontSize="small" />
                                                            </div>
                                                        </Popover>
                                                    )}
                                                </div>
                                                {item.IsPackage && expandedItems[item.uid!] && (
                                                    <div className="package-decoration-inventory">
                                                        <span className="strike"></span>
                                                    </div>
                                                )}
                                            </div>
                                        </td>
                                        {agreementLineItems?.length > 0 && (
                                            <td>
                                                {item.InCart > 0 ? (
                                                    <p>{formatNumberValue(item.InCart)}</p>
                                                ) : (
                                                    <p></p>
                                                )}
                                            </td>
                                        )}
                                        <td>
                                            <p>{item.RateType}</p>
                                        </td>
                                        <td>
                                            <p>{formatDollarValue(item.Rate)}</p>
                                        </td>
                                        <td>
                                            <p>{formatNumberValue(item.QuantityAvailable)}</p>
                                        </td>
                                        <td style={{ width: "140px" }}>
                                            <FormattedInputNumber
                                                style={{ width: "100%" }}
                                                value={
                                                    inventoryState[item.uid]
                                                        ? inventoryState[item.uid].QtyUnits
                                                        : 1
                                                }
                                                onChange={(e) =>
                                                    handleChange(item.uid!, "QtyUnits", e)
                                                }
                                                disabled={item.IsPackage === true}
                                                formatType="number"
                                            />
                                        </td>
                                        <td style={{ width: "140px" }}>
                                            {!item.IsPackage && (
                                                <FormattedInputNumber
                                                    style={{ width: "100%" }}
                                                    value={
                                                        inventoryState[item.uid]
                                                            ? inventoryState[item.uid].QtyEvents
                                                            : 1
                                                    }
                                                    onChange={(e) =>
                                                        handleChange(item.uid!, "QtyEvents", e)
                                                    }
                                                    disabled={item.RateType === "Season"}
                                                    formatType="number"
                                                />
                                            )}
                                        </td>
                                        <td className="sticky-col right" style={{ width: "75px" }}>
                                            <Button
                                                type="primary"
                                                onClick={() => handleAddProduct(item.uid!)}
                                            >
                                                <ShoppingCartIcon sx={{ color: "white" }} />
                                            </Button>
                                        </td>
                                    </tr>

                                    {expandedItems[item.uid!] && item.PackageComponents &&
                                        item.PackageComponents.map((component: any, cIndex: number) => (
                                            <tr key={`${index}-component-${cIndex}`} className="package-component-row">
                                                <td className="sticky-col left" style={{ minWidth: 300, display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0' }}>
                                                    <div style={{ display: 'flex', alignItems: 'stretch', gap: '1rem', flexGrow: '1' }}>
                                                        <div className="package-component-decoration">
                                                            <span className="strike"></span>
                                                        </div>
                                                        <div style={{ minWidth: 300, display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0.5rem', flexGrow: '1' }}>
                                                            <ProductBadge
                                                                product={{
                                                                    Id: component.ProductId,
                                                                    Name: component.ProductName,
                                                                    Division: component.Division,
                                                                    ProductFamily: component.ProductFamily,
                                                                    ProductSubFamily: component.ProductSubFamily,
                                                                    IsPassthroughCost: component.IsPassthroughCost,
                                                                    IsPackage: false
                                                                }}
                                                            />
                                                            {component.Description && (
                                                                <Popover
                                                                    content={component.Description}
                                                                    overlayStyle={{ maxWidth: 250, whiteSpace: "normal", wordWrap: "break-word" }}>
                                                                    <div className="product-icon-wrapper">
                                                                        <InfoIcon fontSize="small" />
                                                                    </div>
                                                                </Popover>
                                                            )}
                                                        </div>
                                                    </div>
                                                </td>
                                                <td></td>
                                                <td>
                                                    <p>{component.RateType}</p>
                                                </td>
                                                <td>
                                                    <p>{formatDollarValue(component.Rate)}</p>
                                                </td>
                                                <td>
                                                    <p>{formatNumberValue(component.QuantityAvailable)}</p>
                                                </td>
                                                <td>
                                                    <FormattedInputNumber
                                                        style={{ width: "100%" }}
                                                        value={component.QtyUnits}
                                                        disabled={true}
                                                        formatType="number"
                                                    />
                                                </td>
                                                <td>
                                                    <FormattedInputNumber
                                                        style={{ width: "100%" }}
                                                        value={component.QtyEvents}
                                                        disabled={true}
                                                        formatType="number"
                                                    />
                                                </td>
                                                <td className="sticky-col right" style={{ width: "75px" }}></td>
                                            </tr>
                                        ))}
                                </React.Fragment>
                            ))}
                    </tbody>
                </table>
            </div>

            <div style={{ padding: "0.5rem 0 1rem 0" }}>
                <PaginationControls {...paginationData} />
            </div>

            <Modal
                open={selectSeasonModalOpen}
                afterClose={() => setSelectSeasonModalOpen(false)}
                onCancel={() => setSelectSeasonModalOpen(false)}
                footer={null}
                width="700px"
            >
                <div className="modal-container">
                    {pendingInsertItem && (
                        <SelectSeasonsForm
                            item={pendingInsertItem}
                            productSeasons={productSeasons}
                            onSubmit={handleSelectSeasons}
                        />
                    )}
                </div>
            </Modal>
        </>
    );
}
