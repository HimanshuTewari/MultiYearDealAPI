import * as React from "react";
import { HiddenFields, OpportunityData } from "../../../../models";
import "./index.css";
import { Select, Button } from "antd";
import AgreementTableHead from "../AgreementTableHead";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";
import ExpandMoreIcon from "@mui/icons-material/ExpandMore";

export default function AgreementTableHeader({
    opportunities,
    setSelectedSeason,
    selectedSeason,
    isCollapsed,
    toggleCollapse,
    openOpportunityModal,
    hiddenFields,
    lineItemsCount
}: {
    opportunities: Array<OpportunityData>;
    setSelectedSeason: React.Dispatch<React.SetStateAction<string>>;
    selectedSeason: string;
    isCollapsed: boolean;
    toggleCollapse: (uid: string) => void;
    openOpportunityModal: (data: OpportunityData) => void;
    hiddenFields: HiddenFields;
    lineItemsCount: number;
}) {
    const seasonOptions = React.useMemo(() => {
        try {
            return opportunities.map((opportunity) => ({
                value: opportunity.StartSeason,
                label: opportunity.SeasonName,
            }));
        } catch (error) {
            console.log(error);
            return [];
        }
    }, [opportunities]);

    return (
        <thead className="agreement-table-header">
            <tr>
                <th className="agreement-table-head-header sticky-col left">
                    <div className="table-head-header-container">
                        <div>
                            <h4>
                                Inventory Assets
                                {lineItemsCount > 0 && (
                                    <> ({lineItemsCount})</>
                                )}
                            </h4>
                        </div>
                        <div className="season-rate-selector">
                            <p>Rate Card:</p>
                            <Select onChange={setSelectedSeason} value={selectedSeason}>
                                {seasonOptions.map((season) => (
                                    <Select.Option key={season.value} value={season.value}>
                                        {season.label}
                                    </Select.Option>
                                ))}
                            </Select>
                        </div>
                    </div>
                </th>
                {Array.isArray(opportunities) &&
                    opportunities.length > 0 &&
                    opportunities.map((opportunity, index) => (
                        <th
                            key={opportunity.Id}
                            className={
                                opportunity.StartSeason === selectedSeason
                                    ? "highlight"
                                    : undefined
                            }
                            onClick={() => setSelectedSeason(opportunity.StartSeason)}
                        >
                            <AgreementTableHead
                                opportunity={opportunity}
                                isFirstYear={index === 0}
                                isCollapsed={isCollapsed}
                                openOpportunityModal={openOpportunityModal}
                                hiddenFields={hiddenFields}
                            />
                        </th>
                    ))}

                <th style={{ width: "100%" }} />
                <th className="sticky-col right" style={{ padding: "0.8rem" }}>
                    <Button
                        type="default"
                        icon={
                            isCollapsed ? (
                                <ChevronRightIcon sx={{ color: "var(--text-3)" }} />
                            ) : (
                                <ExpandMoreIcon sx={{ color: "var(--text-3)" }} />
                            )
                        }
                        onClick={() => toggleCollapse("header")}
                    ></Button>
                </th>
            </tr>
        </thead>
    );
}
