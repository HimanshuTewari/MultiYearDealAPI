import * as React from 'react'
import { OpportunityLineItemData } from '../../../models'
import GrainIcon from '@mui/icons-material/Grain';
import "./index.css"
import { formatNumberValue } from '../../../utilities';

export default function QuantityAvailable({
    data
}: {
    data: OpportunityLineItemData
}) {
    const isUnlimited = React.useMemo(() => {
        try {
            return data.QuantityAvailable === "Unlimited";
        } catch (error) {
            console.log(error);
            return false;
        }
    }, [data])


    const qtyTotal = React.useMemo(() => {
        try {
            if (isUnlimited) return "Unlimited"
            if (data.QuantityTotal === null || isNaN(data.QuantityTotal)) throw new Error("QuantityTotal is null or not a number.");

            return formatNumberValue(data.QuantityTotal, 0);
        } catch (error) {
            console.log(error);
            return "!N/A"
        }
    }, [isUnlimited, data]);

    const qtySold = React.useMemo(() => {
        try {
            if (data.QuantitySold === null || isNaN(data.QuantitySold)) throw new Error("QuantitySold is null or not a number.");

            return formatNumberValue(data.QuantitySold, 0)
        } catch (error) {
            console.log(error);
            return "!N/A";
        }
    }, [data]);


    const qtyAvailable = React.useMemo(() => {
        try {
            if (isUnlimited || data.QuantityAvailable === "Unlimited") return "Unlimited";
            if (data.QuantityAvailable === null || isNaN(data.QuantityAvailable)) return "QuantityAvailable is null or not a number.";

            return formatNumberValue(data.QuantityAvailable, 0);
        } catch (error) {
            console.log(error);
            return "!N/A";
        }
    }, [isUnlimited, data]);

    const qtyPitched = React.useMemo(() => {
        try {
            if (data.QuantityPitched === null || isNaN(data.QuantityPitched)) throw new Error("QuantityPitched is null or not a number.");

            return formatNumberValue(data.QuantityPitched, 0);
        } catch (error) {
            console.log(error);
            return "!N/A"
        }
    }, [data]);

    const qtyNeitherPitchedOrSold = React.useMemo(() => {
        try {
            const qAvailable = data.QuantityAvailable;
            const qPitched = data.QuantityPitched;
            if (qAvailable === "Unlimited") return "Unlimited";

            if (qAvailable === null || isNaN(qAvailable) || qPitched === null || isNaN(qPitched)) throw new Error("QuantityPitched or QuantityAvailable is null or not a number");

            return formatNumberValue(qAvailable - qPitched, 0)

        } catch (error) {
            console.log(error);
            return "!N/A"
        }
    }, [data])

    return (
        <div className="form-container">
            <div className="form-header">
                <div className="icon-wrapper">
                    <GrainIcon sx={{ fontSize: 21, color: "var(--text-3)" }} />
                </div>

                <h3>Quantity Available</h3>
            </div>

            <div className="breakdown-container">
                <div className="breakdown-line">
                    <span className="breakdown-qty">
                        Quantity Total
                    </span>
                    <span className="breakdown-val">
                        {qtyTotal}
                    </span>
                </div>

                <div className="breakdown-line">
                    <span className="breakdown-qty">
                        Quantity Sold
                    </span>
                    <span className="underline breakdown-val">
                        {qtySold}
                    </span>
                </div>

                <div className="breakdown-line">
                    <span className="tabbed breakdown-qty">
                        &nbsp; &nbsp; Quantity Available
                    </span>
                    <span className="breakdown-val">
                        {qtyAvailable}
                    </span>
                </div>

                <div className="breakdown-line">
                    <span className="breakdown-qty">
                        Quantity Pitched
                    </span>
                    <span className="underline breakdown-val">
                        {qtyPitched}
                    </span>
                </div>

                <div className="breakdown-line">
                    <span className="tabbed breakdown-qty">
                        &nbsp; &nbsp; Neither Pitched or Sold
                    </span>
                    <span className="double-underline breakdown-val">
                        {qtyNeitherPitchedOrSold}
                    </span>
                </div>
            </div>
        </div>
    )
}
