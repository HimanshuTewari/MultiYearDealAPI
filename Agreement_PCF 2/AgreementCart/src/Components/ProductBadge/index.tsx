import * as React from "react";
import { ProductData } from "../../../models";
import "./index.css";
import ArrowRightIcon from '@mui/icons-material/ArrowRight';
import DiamondIcon from '@mui/icons-material/Diamond';
import PackageIcon from '@mui/icons-material/Inventory';
import { useAppState } from "../../../context/useAppState";

export default function ProductBadge({ product }: { product: ProductData }) {

    const { context } = useAppState();
    const productBreadcrumbs = React.useMemo(() => {
        try {
            return [product?.Division, product?.ProductFamily, product?.ProductSubFamily].filter(
                (i) => !!i
            );
        } catch (error) {
            console.log(error);
            return [];
        }
    }, [product]);

    function openProductRecordPage() {
        try {
            console.log("openProductRecordPage", context);
            context.navigation.openForm({
                entityName: "product",
                entityId: product.Id,
                openInNewWindow: true
            })
        } catch (error) {
            console.log(error);
        }
    }

    return (
        <div className="product-badge-wrapper">
            <div className="product-badge">
                {product.IsPackage &&
                    <div className="product-icon-wrapper"><PackageIcon fontSize="small" /></div>
                }
                {product.IsPassthroughCost &&
                    <div className="product-icon-wrapper"><DiamondIcon fontSize="medium" /></div>
                }
                <div className="product-badge-container">
                    {Array.isArray(productBreadcrumbs) && productBreadcrumbs.length > 0 && (
                        <div className="product-badge-crumbs">
                            {productBreadcrumbs.map((p, index) => (
                                <React.Fragment key={index}>
                                    <span>{p}</span>
                                    {index !== productBreadcrumbs.length - 1 && (
                                        <ArrowRightIcon fontSize="small" />
                                    )}
                                </React.Fragment>
                            ))}
                        </div>
                    )}
                    <h2><button onClick={openProductRecordPage} className="product-title-button clickable">{product.Name}</button></h2>
                </div>         
            </div>
        </div>
    );
}
