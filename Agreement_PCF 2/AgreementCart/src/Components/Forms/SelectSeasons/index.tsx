import * as React from "react";
import EmojiEventsIcon from "@mui/icons-material/EmojiEvents";
import SaveIcon from "@mui/icons-material/Save";
import { Button, Checkbox } from "antd";
import ProductBadge from "../../ProductBadge";
import { InventoryData } from "../../../../models";

export default function SelectSeasonsForm({
    item,
    productSeasons,
    onSubmit
}: {
    item: InventoryData,
    productSeasons: Array<{ value: string; label: string }>;
    onSubmit: (
        selectedSeasons: string[],
        item?: InventoryData
    ) => void;
}) {
    
    const allValues = productSeasons.map(season => season.value);
    
    const [selectedSeasons, setSelectedSeasons] = React.useState<string[]>(allValues);

    React.useEffect(() => {
        const newValues = productSeasons.map(season => season.value);
        setSelectedSeasons(newValues);
    }, [productSeasons]);

    const isAllSelected = selectedSeasons.length === allValues.length;
    
    const handleChange = (checkedValues: string[]) => {
        console.log('Checked values:', checkedValues);
        setSelectedSeasons(checkedValues);
    };

    const handleToggleSelectAll = () => {
        if (isAllSelected) {
            setSelectedSeasons([]);
        }
        else {
            setSelectedSeasons(allValues);
        }
    };

    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <EmojiEventsIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>Select Seasons</h3>
            </div>
            <ProductBadge
                product={{
                    Id: item.ProductId,
                    Name: item.ProductName,
                    Division: item.Division,
                    ProductFamily: item.ProductFamily,
                    ProductSubFamily: item.ProductSubFamily,
                    IsPassthroughCost: item.IsPassthroughCost,
                }}
            />
            <div className="form-fields">
                <div className="fieldset">
                    <label>Select seasons for this product:</label>
                    
                    <Checkbox.Group
                        options={productSeasons}
                        value={selectedSeasons}
                        onChange={handleChange}
                    />                  
                </div>
                <Button
                    type="link"
                    size="small"
                    onClick={handleToggleSelectAll}
                    style={{ justifyContent: "flex-start" }}
                    >
                    {isAllSelected ? "Deselect all" : "Select all"}
                </Button>

                <div className="form-footer">
                    <Button
                        type="primary"
                        onClick={() => onSubmit(selectedSeasons, item)}
                        icon={<SaveIcon fontSize="small" sx={{ color: "white" }} />}
                    >
                        Submit
                    </Button>
                </div>
            </div>
        </div>
    );
}
