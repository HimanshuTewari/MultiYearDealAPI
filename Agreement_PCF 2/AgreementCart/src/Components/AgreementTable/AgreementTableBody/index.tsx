import * as React from "react";
import {
    HiddenFields,
    OpportunityData,
    OpportunityLineItemData,
    uiLineItemData,
} from "../../../../models";
import AgreementTableCell from "../AgreementTableCell";
import AgreementTableRowHeader from "../AgreementTableRowHeader";
import { Button } from "antd";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";

export default function AgreementTableBody({
    opportunities,
    lineItems,
    selectedSeason,
    setSelectedSeason,
    collapsedItems,
    toggleCollapse,
    openLineItemModal,
    deleteLineItems,
    openDescriptionModal,
    openLegalDefModal,
    openQtyAvailableModal,
    handleUndoPriceOverride,
    openNewPackageComponentModal,
    hiddenFields,
    alternateUI
}: {
    opportunities: Array<OpportunityData>;
    lineItems: Array<uiLineItemData>;
    selectedSeason: string;
    setSelectedSeason: React.Dispatch<React.SetStateAction<string>>;
    collapsedItems: Record<string, boolean>;
    toggleCollapse: (uid: string) => void;
    openLineItemModal: (data: OpportunityLineItemData) => void;
    deleteLineItems: (ids: Array<string>) => void;
    openDescriptionModal: (data: OpportunityLineItemData) => void;
    openLegalDefModal: (data: OpportunityLineItemData) => void;
    openQtyAvailableModal: (data: OpportunityLineItemData) => void;
    handleUndoPriceOverride: (data: OpportunityLineItemData) => void;
    openNewPackageComponentModal: (packageLineItemId: string) => void;
    hiddenFields: HiddenFields,
    alternateUI?: boolean
}) {
    const selectedOpportunityId = React.useMemo(() => {
        try {
            const foundOpportunity = opportunities.filter(
                (o) => o.StartSeason === selectedSeason
            )[0];
            if (!foundOpportunity) return "";
            return foundOpportunity.Id;
        } catch (error) {
            console.log(error);
            return "";
        }
    }, [opportunities, selectedSeason]);

    function setSeason(opportunityId) {
        try {
            const foundOpportunity = opportunities.filter(
                (o) => o.Id === opportunityId
            )[0];
            setSelectedSeason(foundOpportunity.StartSeason);
        } catch (error) {
            console.log(error);
        }
    }

    const opportunitiesLength = React.useMemo(() => {
        try {
            return opportunities.length;
        } catch (error) {
            return 0;
        }
    }, [opportunities]);

    function handleAddNewPackageComponent(e) {
        const uid = e?.currentTarget?.dataset?.uid;
        if(!uid) return;
        const lineItem = lineItems.find(item => item.uid === uid);
        const packageLineItemId = lineItem?.items[0].Id;
        console.log("handleAddNewPackageComponent packageLineItemId: ", packageLineItemId);
        openNewPackageComponentModal(packageLineItemId || "");
    }

    return (
        <tbody className="agreement-table-body">
            {Array.isArray(lineItems) && lineItems.length > 0 ? (
                lineItems.map((lineItem, index) => (
                    <>
                        <tr key={lineItem.Product2.Id + index}>
                            
                            <th className="agreement-table-body-header sticky-col left">
                                <div style={{ display: "flex", flexDirection: "column" }}>
                                    <AgreementTableRowHeader
                                        lineItem={lineItem}
                                        opportunities={opportunities}
                                        selectedSeason={selectedSeason}
                                        isCollapsed={!!collapsedItems[lineItem.uid]}
                                        deleteLineItems={deleteLineItems}
                                        openDescriptionModal={openDescriptionModal}
                                        openLegalDefModal={openLegalDefModal}
                                        hiddenFields={hiddenFields}
                                        isPackage={lineItem.IsPackage}
                                    />
                                    {!collapsedItems[lineItem.uid] && lineItem.IsPackage && (        
                                        <div style={{ display: "flex", background: "#fff" }}>
                                            <div className="package-decoration">
                                                <span className="strike"></span>
                                            </div>
                                            <div style={{ width: "100%" }}>
                                            </div>
                                        </div>
                                    )}
                                </div>
                            </th>
                                
                            {Array.isArray(lineItem.items) &&
                                lineItem.items.length > 0 &&
                                lineItem.items.map((item, index) => (
                                    <td
                                        key={index}
                                        className={
                                            item.Opportunity === selectedOpportunityId
                                                ? "highlight"
                                                : undefined
                                        }
                                        onClick={() => setSeason(item.Opportunity)}
                                    >
                                        <AgreementTableCell
                                            item={item}
                                            product={lineItem.Product2}
                                            isCollapsed={!!collapsedItems[lineItem.uid]}
                                            openLineItemModal={openLineItemModal}
                                            openQtyAvailableModal={openQtyAvailableModal}
                                            handleUndoPriceOverride={handleUndoPriceOverride}
                                            hiddenFields={hiddenFields}
                                            alternateUI={alternateUI}
                                            isPackage={lineItem.Product2.IsPackage}
                                            isPackageComponent={lineItem.IsPackageComponent}
                                        />
                                    </td>
                                ))}

                            <td style={{ width: "100%" }} />
                            <td className="sticky-col right" style={{ padding: "0.8rem" }}>
                                <Button
                                    type="default"
                                    icon={
                                        collapsedItems[lineItem.uid] ? (
                                            <ChevronRightIcon sx={{ color: "var(--text-3)" }} />
                                        ) : (
                                            <ExpandMoreIcon sx={{ color: "var(--text-3)" }} />
                                        )
                                    }
                                    onClick={() => toggleCollapse(lineItem.uid)}
                                ></Button>
                            </td>
                        </tr>
                        {lineItem.IsPackage && !collapsedItems[lineItem.uid] && (
                            <>
                                {Array.isArray(lineItem.PackageComponents) &&
                                    lineItem.PackageComponents.length > 0 &&
                                    lineItem.PackageComponents.map((component) => (  
                                        <tr key={component.Product2.Id} className="table-row">
                                            <th className="agreement-table-body-header sticky-col left">
                                                <div style={{ display: "flex", height: "100%", gap: "1rem" }}>
                                                    {component.IsPackageComponent && (
                                                        <div className="package-component-decoration">
                                                            <span className="strike"></span>
                                                        </div>
                                                    )}
                                                    <AgreementTableRowHeader
                                                        lineItem={component}
                                                        opportunities={opportunities}
                                                        selectedSeason={selectedSeason}
                                                        isCollapsed={!!collapsedItems[component.uid]}
                                                        deleteLineItems={deleteLineItems}
                                                        openDescriptionModal={openDescriptionModal}
                                                        openLegalDefModal={openLegalDefModal}
                                                        hiddenFields={hiddenFields}
                                                        isPackage={component.IsPackage}
                                                    />
                                                </div>
                                            </th>
                                            {Array.isArray(component.items) && (
                                                component.items.length > 0 &&
                                                component.items.map((item, index) => (
                                                    <td
                                                        key={index}
                                                        className={
                                                            item.Opportunity === selectedOpportunityId
                                                                ? "highlight"
                                                                : undefined
                                                        }
                                                        onClick={() => setSeason(item.Opportunity)}
                                                    >
                                                        <AgreementTableCell
                                                            item={item}
                                                            product={component.Product2}
                                                            isCollapsed={!!collapsedItems[component.uid]}
                                                            openLineItemModal={openLineItemModal}
                                                            openQtyAvailableModal={openQtyAvailableModal}
                                                            handleUndoPriceOverride={handleUndoPriceOverride}
                                                            hiddenFields={hiddenFields}
                                                            alternateUI={alternateUI}
                                                            isPackage={component.IsPackage}
                                                            isPackageComponent={true}
                                                        />
                                                    </td>
                                                ))
                                            )}
                                            <td style={{ width: "100%" }} />
                                            <td className="sticky-col right"></td>
                                        </tr>  
                                    ))
                                }
                                <tr key={lineItem.uid} style={{ borderBottom: "1px solid #eee" }}>
                                    <td className="row-heading sticky-col left row-header unset-pseudoborder">
                                        <div style={{ display: "flex", background: "#fff" }}>
                                            <div className="package-decoration decoration-footer">
                                                <span className="strike"></span>
                                            </div>
                                            <div style={{ width: "100%", paddingLeft: "1rem", paddingTop: "2px" }}>
                                                <Button
                                                    type="link" 
                                                    size="small" 
                                                    onClick={handleAddNewPackageComponent}
                                                    data-uid={lineItem.uid}>
                                                    + Add New Package Component
                                                </Button>             
                                            </div>
                                        </div>
                                    </td>
                                    <td colSpan={opportunitiesLength} className="unset-pseudoborder"></td>
                                    <td className="dummy-column unset-pseudoborder"><div></div></td>
                                    <td className="row-actions sticky-col row-footer unset-pseudoborder"></td>
                                </tr>
                            </>
                        )}
                    </>
                ))
            ) : (
                <tr>
                    <th className="agreement-table-body-header sticky-col left">
                        <p style={{ padding: "0.5rem" }}>
                            This agreement has no products or your query returns no products.
                        </p>
                    </th>
                    <td colSpan={opportunitiesLength + 2} />
                </tr>
            )}
        </tbody>
    );
}
