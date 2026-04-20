import * as React from "react";
import { HiddenFields, RateData } from "../../../models";
import Field from "../Field";
import "./index.css";
import { formatDollarValue } from "../../../utilities";

// Phase D.4 (partial) — wrap RateCard in React.memo so the row-level RateCard
// doesn't re-render on unrelated parent updates (e.g. when only the search
// input changes). Equality is shallow on (rates, selectedSeason, style,
// hiddenFields). Callers that pass a stable `rates` reference (Phase D.3 will
// normalise this on the API boundary) get the full benefit.
function RateCardImpl({
    rates,
    selectedSeason,
    style,
    hiddenFields
}: {
    rates: Array<RateData>;
    selectedSeason: string;
    style?: React.CSSProperties;
    hiddenFields: HiddenFields
}) {
    // .find is cheaper and clearer than .filter()[0]; same observable result.
    const selectedRateCard = React.useMemo(() => {
        try {
            return rates.find((r) => r.Season === selectedSeason) ?? null;
        } catch (error) {
            console.error("RateCard rate-lookup error:", error);
            return null;
        }
    }, [rates, selectedSeason]);
    return (
        <div className="rate-card-card" style={{ ...style }}>
            {selectedRateCard ? (
                <>
                    <Field
                        label="Rate Type"
                        value={selectedRateCard.RateType}
                        size="small"
                    />

                    <Field
                        label="Rate"
                        value={formatDollarValue(selectedRateCard.Rate)}
                        size="small"
                    />

                    <Field
                        label="Hard Cost"
                        value={formatDollarValue(selectedRateCard.HardCost)}
                        size="small"
                    />

                    {
                        !hiddenFields?.ProductionCost &&
                        <Field
                            label="Production Cost"
                            value={formatDollarValue(selectedRateCard.ProductionCost)}
                            size="small"
                        />
                    }


                </>
            ) : (
                <>
                    <Field
                        label="Rate Type"
                        value={"!N/A"}
                        size="small"
                    />

                    <Field
                        label="Rate"
                        value={"!N/A"}
                        size="small"
                    />

                    <Field
                        label="Hard Cost"
                        value={"!N/A"}
                        size="small"
                    />

                    {
                        !hiddenFields?.ProductionCost &&
                        <Field
                            label="Production Cost"
                            value={"!N/A"}
                            size="small"
                        />
                    }

                </>
            )}
        </div>
    );
}

const RateCard = React.memo(RateCardImpl);
export default RateCard;
