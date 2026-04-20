import * as React from "react";
import Field, { FieldProperties } from "../../Field";
import { formatDollarValue, formatNumberValue } from "../../../../utilities";
import { HiddenFields, OpportunityData } from "../../../../models";
import { useAppState } from "../../../../context/useAppState";
import CheckIcon from '@mui/icons-material/Check';
import WarningIcon from '@mui/icons-material/Warning';

export default function AgreementTableHead({
    opportunity,
    isFirstYear,
    isCollapsed,
    hiddenFields,
    openOpportunityModal
}: {
    opportunity: OpportunityData;
    isFirstYear: boolean;
    isCollapsed: boolean;
    hiddenFields: HiddenFields
    openOpportunityModal: (data: OpportunityData) => void;
}) {
    const { context, isAuthorized } = useAppState();

    const revenueFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Revenue",
                value: formatDollarValue(opportunity.DealValue),
                valueAction: () => handleOpenOpportunityModal(),
                orientation: "horizontal",
            };

            if (!isAuthorized) {
                delete properties.valueAction;
            }

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Revenue",
                value: "!N/A",
                orientation: "horizontal",
            };
        }
    }, [opportunity, isAuthorized]);

    const pricingModeFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Pricing Mode",
                value: opportunity.PricingMode,
                valueAction: () => handleOpenOpportunityModal(),
                orientation: "horizontal",
                size: "small"
            };

            if (isFirstYear === false) {
                let value = "No Escalation";

                if (opportunity.EscalationType === "Percent") {
                    value = `${formatNumberValue(
                        opportunity?.EscalationValue || 0,
                        2
                    )}% Escalation`;
                } else {
                    value = `${formatDollarValue(
                        opportunity?.EscalationValue || 0
                    )} Escalation`;
                }

                if (opportunity.PricingMode === "Automatic")
                    value = opportunity.PricingMode;
                properties.value = value;
            }

            if (!isAuthorized) {
                delete properties.valueAction;
            }

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Pricing Mode",
                value: "!N/A",
                orientation: "horizontal",
            };
        }
    }, [opportunity, isAuthorized]);

    const percentOfRateCardFieldProperties: FieldProperties = React.useMemo(() => {
        try {

            const { TargetAmount, DealValue } = opportunity;

            const properties: FieldProperties = {
                label: "% of Rate Card",
                value: `${formatNumberValue(opportunity.PercentOfRateCard)}%`,
                orientation: "horizontal",
                size: "small"
            };

            try {
                if (!TargetAmount) throw new Error("TargetAmount undefined")
                const meetsTarget = DealValue >= TargetAmount;

                if (meetsTarget) {
                    properties.labelIcon = {
                        element: <CheckIcon sx={{ fontSize: 13 }} />,
                        position: "right",
                    }
                    properties.customValueStyle = { color: "mediumseagreen" }
                } else {
                    properties.labelIcon = {
                        element: <WarningIcon sx={{ fontSize: 13 }} />,
                        position: "right",
                    }
                    properties.customValueStyle = { color: "red" }
                }
            } catch (error) {
                console.log(error);
            }

            if (!isAuthorized) {
                delete properties.valueAction;
            }

            return properties
        } catch (error) {
            console.log(error);
            return {
                label: "% of Rate Card",
                value: "!N/A",
                orientation: "horizontal",
                size: "small"
            }
        }
    }, [opportunity, isAuthorized]);

    const targetAmountFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const { TargetAmount } = opportunity || {};

            if (TargetAmount !== 0 && [null, undefined].includes(TargetAmount as null | undefined)) throw new Error();

            const properties: FieldProperties = {
                label: "Target Amount",
                value: formatDollarValue(TargetAmount as number),
                orientation: "horizontal",
                size: "small"
            };

            return properties
        } catch (error) {
            console.log(error);
            return {
                label: "Target Amount",
                value: "!N/A",
                orientation: "horizontal",
                size: "small"
            }
        }
    }, [opportunity]);

    const cashAmountFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const { CashAmount } = opportunity || {};

            if (CashAmount !== 0 && [null, undefined].includes(CashAmount as null | undefined)) throw new Error();

            const properties: FieldProperties = {
                label: "Cash Amount",
                value: formatDollarValue(CashAmount as number),
                orientation: "horizontal",
                size: "small"
            };

            return properties
        } catch (error) {
            console.log(error);
            return {
                label: "Cash Amount",
                value: "!N/A",
                orientation: "horizontal",
                size: "small"
            }
        }
    }, [opportunity]);

    const barterAmountFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            let { BarterAmount } = opportunity || {};

            if (!BarterAmount) BarterAmount = 0

            const properties: FieldProperties = {
                label: "Barter Amount",
                value: formatDollarValue(BarterAmount),
                valueAction: () => handleOpenOpportunityModal(),
                orientation: "horizontal",
                size: "small"
            };

            if (!isAuthorized) {
                delete properties.valueAction;
            }

            return properties
        } catch (error) {
            console.log(error);
            return {
                label: "Barter Amount",
                value: "!N/A",
                valueAction: () => handleOpenOpportunityModal(),
                orientation: "horizontal",
                size: "small"
            }
        }
    }, [opportunity, isAuthorized]);

    const totalHardCostFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const { TotalHardCost } = opportunity || {};

            const properties: FieldProperties = {
                label: "Total Hard Cost",
                value: formatDollarValue(TotalHardCost as number),
                orientation: "horizontal",
                size: "small"
            };

            return properties
        } catch (error) {
            console.log(error);
            return {
                label: "Total Hard Cost",
                value: "!N/A",
                orientation: "horizontal",
                size: "small"
            }
        }
    }, [opportunity]);

    const totalProductionCostFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const { TotalProductionCost } = opportunity || {};

            const properties: FieldProperties = {
                label: "Total Production Cost",
                value: formatDollarValue(TotalProductionCost as number),
                orientation: "horizontal",
                size: "small"
            };

            return properties
        } catch (error) {
            console.log(error);
            return {
                label: "Total Production Cost",
                value: "!N/A",
                orientation: "horizontal",
                size: "small"
            }
        }
    }, [opportunity])

    function handleOpenOpportunityModal() {
        let i = { ...opportunity }
        if (isFirstYear) i.IsFirstYear = true;
        openOpportunityModal(i);
    }

    function openOpportunityRecordPage() {
        try {
            console.log("openOpportunityRecordPage", context);
            context.navigation.openForm({
                entityName: "opportunity",
                entityId: opportunity.Id,
                openInNewWindow: true
            })
        } catch (error) {
            console.log(error);
        }
    }

    return (
        <div className="table-head-container">
            <h4>
                <button
                    className="header-button"
                    onClick={openOpportunityRecordPage}
                >
                    {opportunity.SeasonName}
                </button>
            </h4>
            <div
                className="field-container"
                style={{ display: "flex", flexDirection: "column", gap: "0.25rem" }}
            >
                <Field {...revenueFieldProperties} />

                {
                    !isCollapsed &&
                    <div style={{ background: "rgba(0, 0, 0, 0.035)", padding: "0.25rem .5rem", borderRadius: 2, display: "flex", flexDirection: "column", gap: "2px" }}>
                        <Field {...pricingModeFieldProperties} />
                        <Field {...percentOfRateCardFieldProperties} />
                        <Field {...targetAmountFieldProperties} />
                        <Field {...cashAmountFieldProperties} />
                        <Field {...barterAmountFieldProperties} />
                        <Field {...totalHardCostFieldProperties} />
                        {
                            !hiddenFields?.ProductionCost &&
                            <Field {...totalProductionCostFieldProperties} />
                        }
                    </div>
                }

            </div>
        </div>
    );
}
