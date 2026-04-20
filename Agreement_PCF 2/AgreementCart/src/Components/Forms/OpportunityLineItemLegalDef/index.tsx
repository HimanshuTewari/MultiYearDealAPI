import * as React from 'react'
import { OpportunityLineItemData } from '../../../../models'
import GavelIcon from "@mui/icons-material/Gavel";
import { Button, Radio } from 'antd';
import SaveIcon from "@mui/icons-material/Save";
import RichTextEditor from '../../RichTextEditor';

export default function OpportunityLineItemLegalDefForm({
    data,
    disabled,
    onSubmit
}: {
    data: OpportunityLineItemData;
    disabled?: boolean;
    onSubmit: (data: OpportunityLineItemData) => void;
}) {
    const [overwriteLegalDefinition, setOverwriteLegalDefinition] = React.useState<boolean>(false);
    const legalDefinition = React.useRef<string>("")

    const resolvedLegalDefinition = React.useMemo(() => {
        try {
            if (data.LegalDefinition && data.OverwriteLegalDefinition) {
                return data.LegalDefinition
            } else if (data.LegalDefinitionInventoryBySeason) {
                return data.LegalDefinitionInventoryBySeason
            } else if (data.LegalDefinitionProduct) {
                return data.LegalDefinitionProduct
            }
        } catch (error) {
            return "";
        }
    }, [data]);

    function handleFieldChange(value: string) {
        legalDefinition.current = value;
    }

    function handleSubmit() {
        onSubmit({
            ...data,
            LegalDefinition: legalDefinition.current,
            OverwriteLegalDefinition: overwriteLegalDefinition
        })
    }

    React.useEffect(() => {
        setOverwriteLegalDefinition(data.OverwriteLegalDefinition);
        legalDefinition.current = data?.LegalDefinition || "";
    }, [data])

    React.useEffect(() => {
        return () => {
            legalDefinition.current = "";
        }
    }, [])

    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <GavelIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>Description</h3>
            </div>

            <div className="form-fields">
                <div className="fieldset">
                    <label>Override Legal Definition</label>
                    <Radio.Group disabled={disabled} onChange={(e) => setOverwriteLegalDefinition(e.target.value)} value={overwriteLegalDefinition}>
                        <Radio value={true}>Yes</Radio>
                        <Radio value={false}>No</Radio>
                    </Radio.Group>
                </div>
                <div className="fieldset">
                    <label>Legal Definition</label>
                    <RichTextEditor
                        disabled={disabled}
                        content={overwriteLegalDefinition ? legalDefinition?.current || "" : resolvedLegalDefinition}
                        editable={overwriteLegalDefinition}
                        onUpdate={(value) => overwriteLegalDefinition ? handleFieldChange(value) : null}
                    />
                </div>

                <div className="form-footer">
                    <Button
                        type="primary"
                        onClick={handleSubmit}
                        disabled={!overwriteLegalDefinition}
                        icon={<SaveIcon fontSize="small" sx={{ color: !overwriteLegalDefinition || !!disabled ? "#ccc" : "white" }} />}
                    >
                        Submit
                    </Button>
                </div>
            </div>
        </div>
    )
}
