import * as React from "react";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import SaveIcon from "@mui/icons-material/Save";
import { Button, Radio } from "antd";

export default function CloneAgreementForm({
    agreementId,
    onSubmit,
    onCancel
}: {
    agreementId: string | null;
    onSubmit: (
        firstYearOnly: boolean
    ) => void;
    onCancel: () => void;
}) {
    const [firstYearOnly, setFirstYearOnly] = React.useState(true);

    const style: React.CSSProperties = {
        display: 'flex',
        flexDirection: 'column',
        gap: 8,
    };

    if (!agreementId) {
        return <div>No agreement selected</div>;
    }

    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <ContentCopyIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>Clone Agreement</h3>
            </div>
            <div className="form-fields">
                <div className="fieldset">
                    <label>Please select an option to Clone Agreement:</label>
                    <Radio.Group
                        value={firstYearOnly}
                        style={style}
                        options={[
                            { value: true, label: 'Clone first year of the agreement only' },
                            { value: false, label: 'Clone all years of the agreement' }
                        ]}
                        onChange={(e) => setFirstYearOnly(e.target.value)}
                    />               
                </div>

                <div className="form-footer" style={{ gap: "1rem" }}>
                    <Button
                        type="primary"
                        ghost
                        onClick={() => onCancel()}
                    >
                        Cancel
                    </Button>
                    <Button
                        type="primary"
                        onClick={() => onSubmit(firstYearOnly)}
                        icon={<SaveIcon fontSize="small" sx={{ color: "white" }} />}
                    >
                        Submit
                    </Button>
                </div>
            </div>
        </div>
    );
}
