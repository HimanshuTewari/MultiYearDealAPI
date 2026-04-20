import * as React from 'react'
import { OpportunityLineItemData } from '../../../../models'
import ForumIcon from "@mui/icons-material/Forum";
import SaveIcon from "@mui/icons-material/Save";
import { Input, Button } from "antd";

export default function OpportunityLineItemDescriptionForm({
    data,
    onSubmit,
    disabled
}: {
    data: OpportunityLineItemData
    onSubmit: (data: OpportunityLineItemData) => void;
    disabled?: boolean;
}) {
    const [description, setDescription] = React.useState("");

    function handleFieldChange(value: string) {
        setDescription(value);
    }

    function handleSubmit() {
        onSubmit({
            ...data,
            Description: description
        })
    }

    React.useEffect(() => {
        setDescription(data?.Description || "");
    }, [data]);


    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <ForumIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>Description</h3>
            </div>

            <div className="form-fields">
                <div className="fieldset">
                    <label>Description</label>
                    <Input.TextArea
                        disabled={disabled}
                        style={{ width: "100%" }}
                        value={description}
                        onChange={(e) => handleFieldChange(e.target.value)}
                    />
                </div>

                <div className="form-footer">
                    <Button
                        disabled={disabled}
                        type="primary"
                        onClick={() => handleSubmit()}
                        icon={<SaveIcon fontSize="small" sx={{ color: disabled ? "#ccc" : "white" }} />}
                    >
                        Submit
                    </Button>
                </div>
            </div>
        </div>
    )
}
