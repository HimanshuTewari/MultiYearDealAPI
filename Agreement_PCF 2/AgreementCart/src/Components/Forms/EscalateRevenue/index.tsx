import * as React from "react";
import TrendingUpIcon from "@mui/icons-material/TrendingUp";
import { Button, Select } from "antd";
import SaveIcon from "@mui/icons-material/Save";
import FormattedInputNumber from "../../FormattedInputNumber";

export default function EscalateRevenueForm({
    onSubmit,
}: {
    onSubmit: (
        escalationType: "Fixed" | "Percent" | string,
        escalationValue: number
    ) => void;
}) {
    const [formData, setFormData] = React.useState({
        escalationType: "",
        escalationValue: 0,
    });

    function handleFieldChange(
        field: "escalationType" | "escalationValue",
        value: string | number | null
    ) {
        setFormData((formData) => {
            let f = { ...formData };

            f[field as any] = value;

            return f;
        });
    }

    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <TrendingUpIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>Escalate Revenue</h3>
            </div>
            <div className="form-fields">
                <div className="fieldset">
                    <label>Escalation Type</label>
                    <Select
                        onChange={(e) => handleFieldChange("escalationType", e)}
                        value={formData.escalationType}
                        style={{ width: "100%" }}
                    >
                        <Select.Option value={""}>None</Select.Option>
                        <Select.Option value={"Fixed"}>Fixed</Select.Option>
                        <Select.Option value={"Percent"}>Percent</Select.Option>
                    </Select>
                </div>

                {formData.escalationType &&
                    ["Fixed", "Percent"].includes(formData.escalationType) && (
                        <div className="fieldset">
                            <label>Escalation Value</label>
                            <FormattedInputNumber
                                style={{ width: "100%" }}
                                value={formData.escalationValue}
                                onChange={(e) => handleFieldChange("escalationValue", e)}
                                formatType={
                                    formData.escalationType === "Fixed"
                                        ? "dollar"
                                        : "number"
                                }
                            />
                        </div>
                    )}

                <div className="form-footer">
                    <Button
                        type="primary"
                        onClick={() => onSubmit(formData.escalationType, formData.escalationValue)}
                        icon={<SaveIcon fontSize="small" sx={{ color: "white" }} />}
                    >
                        Submit
                    </Button>
                </div>
            </div>
        </div>
    );
}
