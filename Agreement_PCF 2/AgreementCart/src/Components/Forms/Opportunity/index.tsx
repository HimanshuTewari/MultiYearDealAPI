import * as React from "react";
import { HiddenFields, OpportunityData } from "../../../../models";
import EmojiEventsIcon from "@mui/icons-material/EmojiEvents";
import { Button, Select, Collapse } from "antd";
import { formatDollarValue } from "../../../../utilities";
import FormattedInputNumber from "../../FormattedInputNumber";
import Field, { FieldProperties } from "../../Field";
import SaveIcon from "@mui/icons-material/Save";

export default function OpportunityForm({
    data,
    onSubmit,
    hiddenFields
}: {
    data: OpportunityData;
    onSubmit: (data: OpportunityData) => void;
    hiddenFields: HiddenFields
}) {
    const [formData, setFormData] = React.useState(data);

    const escalationSummary = React.useMemo(() => {
        try {
            const { EscalationType, EscalationValue } = data;

            if (!EscalationType) return "(No Escalation)";

            if (EscalationType === "Fixed") {
                return `(${formatDollarValue(EscalationValue || 0)} Escalation)`;
            } else {
                return `(${EscalationValue}% Escalation)`;
            }
        } catch (error) {
            console.log(error);
            return "(No Escalation)";
        }
    }, [data]);

    const totalRateCardFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Total Rate Card",
                value: formatDollarValue(data.TotalRateCard),
            };

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Total Rate Card",
                value: "!N/A",
                valueTextAlign: "left",
                size: "small",
            };
        }
    }, [data]);

    const totalHardCostFieldProperties: FieldProperties = React.useMemo(() => {
        try {
            const properties: FieldProperties = {
                label: "Total Hard Cost",
                value: formatDollarValue(data.TotalHardCost),
            };

            return properties;
        } catch (error) {
            console.log(error);
            return {
                label: "Total Hard Cost",
                value: "!N/A",
                valueTextAlign: "left",
                size: "small",
            };
        }
    }, [data]);

    const totalProductionCostFieldProperties: FieldProperties =
        React.useMemo(() => {
            try {
                const properties: FieldProperties = {
                    label: "Total Production Cost",
                    value: formatDollarValue(data.TotalProductionCost),
                };

                return properties;
            } catch (error) {
                console.log(error);
                return {
                    label: "Total Production Cost",
                    value: "!N/A",
                    valueTextAlign: "left",
                    size: "small",
                };
            }
        }, [data]);

    function handleFieldChange(
        field: keyof OpportunityData,
        value: string | number | null
    ) {
        setFormData((formData) => {
            let f: OpportunityData = { ...formData };

            f[field as any] = value;

            return f;
        });
    }

    React.useEffect(() => {
        setFormData((formData) => {
            formData.EscalationValue = 0;
            return formData;
        });
    }, [formData.EscalationType]);

    React.useEffect(() => {
        setFormData(data);
    }, [data]);

    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <EmojiEventsIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>{data.SeasonName}</h3>
            </div>

            <div className="form-fields">
                {formData.PricingMode === "Manual" ? (
                    <div className="fieldset">
                        <label>Manual Amount</label>
                        <FormattedInputNumber
                            style={{ width: "100%" }}
                            value={formData.ManualAmount}
                            disabled={
                                formData.EscalationType === "Percent" &&
                                formData.EscalationValue !== null &&
                                formData.EscalationValue > 0
                            }
                            onChange={(e) => handleFieldChange("ManualAmount", e)}
                            formatType="dollar"
                        />
                        {formData.EscalationType === "Percent" &&
                            formData.EscalationValue !== null &&
                            formData.EscalationValue > 0 && (
                                <p className="mime">
                                    Manual Amount is disabled if there is an active % escalation.
                                </p>
                            )}
                    </div>
                ) : (
                    <div className="fieldset">
                        <label>Automatic Amount</label>
                        <FormattedInputNumber
                            style={{ width: "100%" }}
                            value={formData.AutomaticAmount}
                            disabled={true}
                            formatType="dollar"
                        />
                    </div>
                )}

                <div className="fieldset">
                    <label>Pricing Mode</label>
                    <Select
                        onChange={(e) => handleFieldChange("PricingMode", e)}
                        value={formData.PricingMode}
                        style={{ width: "100%" }}
                    >
                        <Select.Option value={"Automatic"}>Automatic</Select.Option>
                        <Select.Option value={"Manual"}>Manual</Select.Option>
                    </Select>
                </div>

                <div style={{ display: "flex", gap: "1.25rem", width: "100%" }}>
                    <div className="fieldset" style={{ flex: 1 }}>
                        <label>Cash Amount</label>
                        <FormattedInputNumber
                            style={{ width: "100%" }}
                            value={formData?.CashAmount}
                            disabled={true}
                            formatType="dollar"
                        />
                    </div>
                    <div className="fieldset" style={{ flex: 1 }}>
                        <label>Barter Amount</label>
                        <FormattedInputNumber
                            style={{ width: "100%" }}
                            value={formData?.BarterAmount || 0}
                            onChange={(e) => handleFieldChange("BarterAmount", e)}
                            disabled={false}
                            formatType="dollar"
                        />
                    </div>
                </div>

                {!data?.IsFirstYear && (
                    <div className="fieldset">
                        <Collapse
                            items={[
                                {
                                    key: "1",
                                    label: `Escalate from prior year ${escalationSummary}`,
                                    children: (
                                        <div className="form-container">
                                            <div className="form-fields">
                                                <div className="fieldset">
                                                    <label>Escalation Type</label>
                                                    <Select
                                                        onChange={(e) =>
                                                            handleFieldChange("EscalationType", e === "" ? null : e)
                                                        }
                                                        value={formData.EscalationType === null ? "" : formData.EscalationType}
                                                        style={{ width: "100%" }}
                                                    >
                                                        <Select.Option value={""}>None</Select.Option>
                                                        <Select.Option value={"Fixed"}>Fixed</Select.Option>
                                                        <Select.Option value={"Percent"}>
                                                            Percent
                                                        </Select.Option>
                                                    </Select>
                                                </div>

                                                {formData.EscalationType &&
                                                    ["Fixed", "Percent"].includes(
                                                        formData.EscalationType
                                                    ) && (
                                                        <div className="fieldset">
                                                            <label>Escalation Value</label>
                                                            <FormattedInputNumber
                                                                style={{ width: "100%" }}
                                                                value={formData.EscalationValue}
                                                                onChange={(e) =>
                                                                    handleFieldChange("EscalationValue", e)
                                                                }
                                                                formatType={
                                                                    formData.EscalationType === "Fixed"
                                                                        ? "dollar"
                                                                        : "number"
                                                                }
                                                            />
                                                        </div>
                                                    )}
                                            </div>
                                        </div>
                                    ),
                                },
                            ]}
                        ></Collapse>
                    </div>
                )}

                <div className="form-metadata">
                    <div className="form-metadata-field">
                        <Field {...totalRateCardFieldProperties} />
                    </div>
                    <div className="form-metadata-field">
                        <Field {...totalHardCostFieldProperties} />
                    </div>
                    {
                        !hiddenFields?.ProductionCost &&
                        <div className="form-metadata-field">
                            <Field {...totalProductionCostFieldProperties} />
                        </div>
                    }
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
        </div>
    );
}
