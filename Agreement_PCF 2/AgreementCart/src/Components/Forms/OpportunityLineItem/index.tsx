import * as React from 'react'
import { HiddenFields, OpportunityLineItemData } from '../../../../models'
import QrCodeIcon from '@mui/icons-material/QrCode';
import { Button } from 'antd';
import { formatNumberValue } from '../../../../utilities';
import Field from '../../Field';
import type { FieldProperties } from '../../Field';
import SaveIcon from '@mui/icons-material/Save';
import SellIcon from '@mui/icons-material/Sell';
import FormattedInputNumber from '../../FormattedInputNumber';

export default function OpportunityLineItemForm({
    data,
    onSubmit,
    handleUndoOverride,
    hiddenFields
}: {
    data: OpportunityLineItemData;
    onSubmit: (data: OpportunityLineItemData) => void
    handleUndoOverride?: (data: OpportunityLineItemData) => void
    hiddenFields: HiddenFields
}) {
    const [formData, setFormData] = React.useState(data);

    const isPackageComponent = !!formData.PackageLineItemId;

    const qtyAvailableFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Qty Available",
                value:
                    data.QuantityAvailable === "Unlimited"
                        ? "Unlimited"
                        : formatNumberValue(data.QuantityAvailable),
                valueTextAlign: "left",
            };

            const isError =
                data.QuantityAvailable !== "Unlimited" &&
                !isNaN(data.QuantityAvailable) &&
                data.QuantityAvailable < 0;

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
    }, [data]);

    function handleFieldChange(field: keyof OpportunityLineItemData, value: string | number | null) {
        setFormData((formData) => {
            let f: OpportunityLineItemData = { ...formData };

            f[field as any] = value

            return f
        })
    }

    React.useEffect(() => {
        setFormData(data);
    }, [data])

    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <QrCodeIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>Opportunity Line Item</h3>
            </div>

            <div className="form-fields">
                <div className="fieldset">
                    <label>Total Value</label>
                    <FormattedInputNumber
                        style={{ width: "100%" }}
                        value={formData.TotalValue}
                        onChange={(e) => handleFieldChange("TotalValue", e)}
                        disabled={data.LockRate}
                        formatType="dollar"
                    />
                    {
                        data.IsManualPriceOverride &&
                        <button
                            className="default-btn clickable"
                            onClick={() => handleUndoOverride ? handleUndoOverride(data) : null}
                        >
                            <SellIcon sx={{ fontSize: 13 }} /> Reset Override
                        </button>
                    }
                </div>

                <div className="fieldset-columns" style={{ "--cols": 2 } as React.CSSProperties}>
                    <div className="fieldset">
                        <label># Units</label>
                        <FormattedInputNumber
                            style={{ width: "100%" }}
                            value={formData.QtyUnits}
                            onChange={(e) => handleFieldChange("QtyUnits", e)}
                            formatType="number"
                        />
                    </div>

                    {
                        data.RateType !== "Season" &&
                        <div className="fieldset">
                            <label># Events</label>
                            <FormattedInputNumber
                                style={{ width: "100%" }}
                                value={formData.QtyEvents}
                                formatType="number"
                                onChange={(e) => handleFieldChange("QtyEvents", e)}
                            />
                        </div>
                    }
                </div>

                <div className="fieldset">
                    <label>Unit Price</label>
                    <FormattedInputNumber
                        style={{ width: "100%" }}
                        value={formData.Rate}
                        readOnly
                        disabled={true}
                        formatType="dollar"
                    />
                </div>

                <div className="fieldset-columns" style={{ "--cols": 2 } as React.CSSProperties}>
                    <div className="fieldset">
                        <label>Hard Cost</label>
                        <FormattedInputNumber
                            style={{ width: "100%" }}
                            value={formData.HardCost}
                            onChange={(e) => handleFieldChange("HardCost", e)}
                            disabled={(formData.LockHardCost || isPackageComponent)}
                            formatType="dollar"
                        />
                    </div>
                    {
                        !hiddenFields?.ProductionCost &&
                        <div className="fieldset">
                            <label>Production Cost</label>
                            <FormattedInputNumber
                                style={{ width: "100%" }}
                                value={formData.ProductionCost}
                                onChange={(e) => handleFieldChange("ProductionCost", e)}
                                disabled={(formData.LockProductionCost || isPackageComponent)}
                                formatType="dollar"
                            />
                        </div>
                    }

                </div>
            </div>

            <div className="form-metadata">
                <div className="form-metadata-field">
                    <Field {...qtyAvailableFieldProperties} />
                </div>
            </div>

            <div className="form-footer">
                <Button
                    type="primary"
                    onClick={() => onSubmit(formData)}
                    icon={<SaveIcon fontSize="small" sx={{ color: "white" }} />}
                >
                    Submit
                </Button>
            </div>
        </div>
    )
}
