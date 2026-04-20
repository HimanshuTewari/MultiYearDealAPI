import * as React from "react";
import ProductBadge from "../../ProductBadge";
import RateCard from "../../RateCard";
import { HiddenFields, LineItemData, OpportunityData, OpportunityLineItemData } from "../../../../models";
import DeleteIcon from "@mui/icons-material/Delete";
import ForumIcon from "@mui/icons-material/Forum";
import GavelIcon from "@mui/icons-material/Gavel";
import ConfirmAction from "../../ConfirmAction";
import { useAppState } from "../../../../context/useAppState";

export default function AgreementTableRowHeader({
    opportunities,
    lineItem,
    selectedSeason,
    isCollapsed,
    deleteLineItems,
    openDescriptionModal,
    openLegalDefModal,
    hiddenFields,
    isPackage
}: {
    opportunities: Array<OpportunityData>;
    lineItem: LineItemData;
    selectedSeason: string;
    isCollapsed: boolean;
    deleteLineItems: (ids: Array<string>) => void;
    openDescriptionModal: (data: OpportunityLineItemData) => void;
    openLegalDefModal: (data: OpportunityLineItemData) => void;
    hiddenFields: HiddenFields;
    isPackage?: boolean;
}) {
    const { isAuthorized } = useAppState();

    const currentLineItem = React.useMemo(() => {
        try {
            const selectedOpportunity = opportunities.filter(
                (o) => o.StartSeason === selectedSeason
            )[0];

            if (!selectedOpportunity) return null;

            return lineItem.items.filter(
                (i) => i.Opportunity === selectedOpportunity.Id
            )[0];
        } catch (error) {
            console.log(error);
            return null;
        }
    }, [lineItem, selectedSeason, opportunities]);

    function handleEditDescription() {
        if (currentLineItem) openDescriptionModal(currentLineItem);
    }

    function handleEditLegalDefinition() {
        if (currentLineItem) openLegalDefModal(currentLineItem);
    }

    const hasLegalDefinition = () => {
            if (!currentLineItem) return false;
            if (currentLineItem.LegalDefinition !== null && currentLineItem.LegalDefinition !== '') {
                return true;
            }
            else {
                return false;
            } 
    };

    const hasComments = () => {
            if (!currentLineItem) return false;
            if (currentLineItem.Description !== null && currentLineItem.Description !== '') {
                return true;
            }
            else {
                return false;
            } 
    };
    
    return (
        <div
            className="product-table-cell"
            style={{ padding: ".75rem", minWidth: 425 }}
        >
            <div
                style={{
                    display: "flex",
                    alignItems: "flex-start",
                    justifyContent: "space-between",
                    gap: "2rem",
                    padding: "0.5rem 0rem",
                }}
            >
                <ProductBadge product={lineItem.Product2} />

                <div style={{ display: "flex", alignItems: "center", gap: "0.25rem" }}>
                    <button
                        className="icon-wrapper clickable inverted default-btn"
                        onClick={handleEditLegalDefinition}
                    >
                        {hasLegalDefinition() ? (
                                    <GavelIcon sx={{ fontSize: 14, color: 'var(--info-1)' }}/>
                                ) : (
                                    <GavelIcon sx={{ fontSize: 14 }}/>
                                )}
                    </button>
                    <button
                        className="icon-wrapper clickable inverted default-btn"
                        onClick={handleEditDescription}
                    >
                        {hasComments() ? (
                                    <ForumIcon sx={{ fontSize: 14, color: 'var(--info-1)' }}/>
                                ) : (
                                    <ForumIcon sx={{ fontSize: 14 }}/>
                                )}               
                    </button>
                    <ConfirmAction
                        confirmationMessage="Are you sure you want to delete this item?"
                        action={() => deleteLineItems(lineItem.items.map((i) => i.Id))}
                        hex="var(--danger-1)"
                        disabled={!isAuthorized}
                    >
                        <button
                            className="icon-wrapper clickable inverted default-btn"
                            disabled={!isAuthorized}
                        >
                            <DeleteIcon sx={{ fontSize: 14 }} />
                        </button>
                    </ConfirmAction>
                </div>
            </div>

            {!isCollapsed && !lineItem.IsPackageComponent && (
                <>
                    <RateCard
                        rates={lineItem.rates}
                        selectedSeason={selectedSeason}
                        style={{ marginTop: ".8rem" }}
                        hiddenFields={hiddenFields}
                    />
                </>
            )}
        </div>
    );
}
