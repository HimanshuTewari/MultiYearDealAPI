import * as React from "react";
import { HiddenFields, OpportunityLineItemData, ProductData } from "../../../../models";
import Field from "../../Field";
import { formatDollarValue, formatNumberValue } from "../../../../utilities";
import "./index.css";
import VisibilityIcon from "@mui/icons-material/Visibility";
import VisibilityOffIcon from "@mui/icons-material/VisibilityOff";
import LockIcon from "@mui/icons-material/Lock";
import SellIcon from '@mui/icons-material/Sell';
import { Popover } from "antd";
import { FieldProperties } from "../../Field";
import { useAppState } from "../../../../context/useAppState";

export default function AgreementTableCell({
    item,
    product,
    isCollapsed,
    openLineItemModal,
    handleUndoPriceOverride,
    openQtyAvailableModal,
    hiddenFields,
    alternateUI,
    isPackage,
    isPackageComponent
}: {
    item: OpportunityLineItemData;
    product: ProductData;
    isCollapsed: boolean;
    openLineItemModal: (data: OpportunityLineItemData) => void;
    handleUndoPriceOverride: (data: OpportunityLineItemData) => void;
    openQtyAvailableModal: (data: OpportunityLineItemData) => void;
    hiddenFields: HiddenFields;
    alternateUI?: boolean;
    isPackage?: boolean;
    isPackageComponent: boolean;
}) {
    const { isAuthorized } = useAppState();

    const [isHovered, setIsHovered] = React.useState(false);

    const totalValueFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Total Value",
                value: formatDollarValue(item.TotalValue),
                valueAction: () => handleOpenLineItemModal(),
                valueTextAlign: "right",
                orientation: alternateUI ? "horizontal" : "vertical",
            };

            let valueIcon: FieldProperties["valueIcon"] = undefined;

            if (product.IsPassthroughCost || item.LockRate) {
                properties.valueAction = undefined;
                valueIcon = {};
                valueIcon.element = <LockIcon sx={{ fontSize: 13 }} />;
                valueIcon.position = alternateUI ? "left" : "right";
                if (product.IsPassthroughCost)
                    valueIcon.helpText =
                        "Total Value is locked because it's a passthrough cost.";
                if (item.LockRate) valueIcon.helpText = "Total Value is locked.";
            }

            if (item.IsManualPriceOverride) {
                properties.labelIcon = {
                    element: <SellIcon sx={{ fontSize: 13 }} />,
                    position: "right",
                    helpText: `The ${properties.label} has been overridden. Click to undo.`,
                    action: () => handleUndoPriceOverride(item)
                }
            }

            properties.valueIcon = valueIcon;

            if (!isAuthorized) {
                delete properties.valueAction;
                if (properties.labelIcon) {
                    delete properties.labelIcon.action;
                    properties.labelIcon.helpText = `The ${properties.label} has been overridden.`
                }

            }

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Total Value",
                value: "!N/A",
                valueTextAlign: "right",
            };
        }
    }, [item, product, isAuthorized]);

    const qtyEventsFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "# Events",
                value: formatNumberValue(item?.QtyEvents || 0),
                orientation: "horizontal",
                size: "small",
                valueAction: () =>
                    handleOpenLineItemModal(),
            };

            if (!isAuthorized) delete properties.valueAction;

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "# Events",
                value: "!N/A",
                size: "small",
                orientation: "horizontal",
            };
        }
    }, [item, isAuthorized]);

    const qtyUnitsFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "# Units",
                value: formatNumberValue(item?.QtyUnits || 0),
                orientation: "horizontal",
                size: "small",
                valueAction: () =>
                    handleOpenLineItemModal(),
            };

            if (!isAuthorized) delete properties.valueAction;

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "# Units",
                value: "!N/A",
                size: "small",
                orientation: "horizontal",
            };
        }
    }, [item, isAuthorized]);

    const unitPriceFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Unit Price",
                value: formatDollarValue(item.Rate),
                valueTextAlign: "right",
                size: "small",
                orientation: alternateUI ? "horizontal" : "vertical",
            };

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Unit Price",
                value: "!N/A",
                valueTextAlign: "right",
                size: "small",
            };
        }
    }, [item]);

    const qtyAvailableFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Qty Available",
                value:
                    item.QuantityAvailable === "Unlimited"
                        ? "Unlimited"
                        : formatNumberValue(item.QuantityAvailable),
                valueTextAlign: "right",
                size: "small",
                labelIcon: {
                    element: <VisibilityIcon sx={{ fontSize: 12 }} />,
                    action: () => openQtyAvailableModal(item),
                },
                orientation: alternateUI ? "horizontal" : "vertical",
            };

            const isError =
                item.QuantityAvailable !== "Unlimited" &&
                !isNaN(item.QuantityAvailable) &&
                item.QuantityAvailable < 0;

            if (isError) properties.customValueStyle = { color: "var(--danger-1)" };

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Qty Available",
                value: "!N/A",
                valueTextAlign: "right",
                size: "small",
            };
        }
    }, [item]);

    const hardCostFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Unit Hard Cost",
                value: formatDollarValue(item.HardCost),
                valueAction: () => handleOpenLineItemModal(),
                valueTextAlign: "right",
                size: "small",
                orientation: alternateUI ? "horizontal" : "vertical",
            };

            let valueIcon: FieldProperties["valueIcon"] = undefined;

            if (item.LockHardCost) {
                valueIcon = {};
                valueIcon.element = <LockIcon sx={{ fontSize: 13 }} />;
                valueIcon.position = alternateUI ? "left" : "right";
                valueIcon.helpText = "Unit Hard Cost is locked.";
                properties.valueAction = undefined;
            }

            properties.valueIcon = valueIcon;

            if (isPackage) delete properties.valueAction;

            if (!isAuthorized) delete properties.valueAction;

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Unit Hard Cost",
                value: "!N/A",
                valueTextAlign: "right",
                size: "small",
            };
        }
    }, [item, isAuthorized]);

    const productionCostFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Production Cost",
                //Sunny(13-03-25)
                //commenting the below line, and adding the "TotalProductionCost" instead ProductionCost
                //value: formatDollarValue(item.ProductionCost),
                value: formatDollarValue(item.TotalProductionCost),
                valueAction: () => handleOpenLineItemModal(),
                valueTextAlign: "right",
                size: "small",
                orientation: alternateUI ? "horizontal" : "vertical",
            };

            let valueIcon: FieldProperties["valueIcon"] = undefined;

            if (item.LockProductionCost) {
                valueIcon = {};
                valueIcon.element = <LockIcon sx={{ fontSize: 13 }} />;
                valueIcon.position = alternateUI ? "left" : "right";
                valueIcon.helpText = "Production Cost is locked.";
                properties.valueAction = undefined;
            }

            properties.valueIcon = valueIcon;

            if (isPackage) delete properties.valueAction;

            if (!isAuthorized) delete properties.valueAction;

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Production Cost",
                value: "!N/A",
                valueTextAlign: "right",
                size: "small",
            };
        }
    }, [item, isAuthorized]);

    function handleOpenLineItemModal() {
        let i = { ...item }
        if (product.IsPassthroughCost) i.LockRate = true;
        openLineItemModal(i);
    }

    const displayProductValues = item?.QtyUnits != 0;

    const isIBSAvailable = !item?.NotAvailable;

    const iconProps = {
        onMouseEnter: () => setIsHovered(true),
        onMouseLeave: () => setIsHovered(false),
        onClick: handleOpenLineItemModal,
        title: "Enter product quantities",
        style: { cursor: "pointer", fontSize: 12 },
    };

    const containerClass = alternateUI ? "field-container column" : "field-container grid";
    const innerDivStyle: React.CSSProperties | undefined = !alternateUI
                        ? { display: "flex", flexDirection: "column", gap: ".25rem" }
                        : undefined;

    return (
        <div className="table-cell-container">
            {displayProductValues ? (
                <div className={containerClass}>
                    {alternateUI ? (
                        <>
                            <div style={innerDivStyle}>
                                <Field {...qtyUnitsFieldProperties} />
                                 {item.RateType !== "Season" && (
                                    <Field {...qtyEventsFieldProperties} />
                                )}
                            </div>
                            {!isCollapsed && !isPackageComponent && (
                                <Field {...qtyAvailableFieldProperties} />
                            )}
                            <Field {...totalValueFieldProperties} />
                            {!isCollapsed && !isPackageComponent && (
                                <>
                                    <Field {...unitPriceFieldProperties} />
                                    <Field {...hardCostFieldProperties} />
                                    {!hiddenFields?.ProductionCost &&
                                        <Field {...productionCostFieldProperties} />
                                    }
                                </>
                            )}
                        </>
                    ) : (
                        <>
                            <Field {...totalValueFieldProperties} />
                            <div style={innerDivStyle}>
                                {item.RateType !== "Season" && (
                                    <Field {...qtyEventsFieldProperties} />
                                )}
                                <Field {...qtyUnitsFieldProperties} />
                            </div>
                            {!isCollapsed && !isPackageComponent && (
                                <>
                                    <Field {...unitPriceFieldProperties} />
                                    <Field {...qtyAvailableFieldProperties} />
                                    <Field {...hardCostFieldProperties} />
                                    {!hiddenFields?.ProductionCost &&
                                        <Field {...productionCostFieldProperties} />
                                    }
                                </>
                            )}
                        </>
                    )}
                </div>
            ) : (
                isIBSAvailable ? (
                    <div
                        style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100%' }}>
                        <Popover content={iconProps.title}>
                            <button
                                className="default-btn field-icon-wrapper clickable"
                                onClick={handleOpenLineItemModal}
                            >
                                {isHovered ? (
                                    <VisibilityIcon {...iconProps} sx={{ color: 'var(--info-1)' }}/>
                                ) : (
                                    <VisibilityOffIcon {...iconProps}/>
                                )}
                            </button>
                        </Popover>
                    </div>
                ) : (
                    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100%' }}>
                        <div style={{ fontSize: '14px', fontWeight: 'bold', color: 'rgba(0, 0, 0, 0.08)' }}>
                            N/A
                        </div>
                    </div>

                )
            )}
        </div>
    );
}
