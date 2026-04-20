import * as React from "react";
import { HiddenFields, InventoryData, LineItemsData, OpportunityData } from "./models";
import AgreementTable from "./src/Components/AgreementTable";
import "./main.css";
import { NotificationProvider } from "./context/useNotification";
import { AppStateProvider } from "./context/useAppState";
import { IInputs } from "./generated/ManifestTypes";

// -------------------------------------------------------------------------
// Phase E — React error boundary (plan: atomic-jumping-rabin.md §Phase E)
//
// Without this, any uncaught render-time exception in a deep child
// component blanks the whole PCF (D365 just shows an empty iframe). The
// boundary catches the error, surfaces a user-readable fallback, and
// logs the original to the console so we can recover the stack from the
// browser DevTools. It does NOT swallow business-logic errors raised
// from event handlers — those still fall through to setNotification or
// the existing try/catch sites.
// -------------------------------------------------------------------------
class CartErrorBoundary extends React.Component<
    { children: React.ReactNode },
    { error: Error | null }
> {
    constructor(props: { children: React.ReactNode }) {
        super(props);
        this.state = { error: null };
    }

    static getDerivedStateFromError(error: Error) {
        return { error };
    }

    componentDidCatch(error: Error, info: React.ErrorInfo) {
        // Use console.error explicitly so the stack is preserved; the legacy
        // codebase often used console.log on catch which loses the trace tab.
        console.error("AgreementCart render error:", error, info);
    }

    render() {
        if (this.state.error) {
            return (
                <div role="alert" style={{ padding: 16, color: "#a00" }}>
                    <strong>Something went wrong loading the Agreement Cart.</strong>
                    <div style={{ marginTop: 8, fontSize: 12, opacity: 0.8 }}>
                        {this.state.error.message}
                    </div>
                    <div style={{ marginTop: 8, fontSize: 12, opacity: 0.6 }}>
                        Please refresh the form. If the problem persists, contact support
                        with the browser console output.
                    </div>
                </div>
            );
        }
        return this.props.children;
    }
}

export default function Main({
    opportunities,
    lineItems,
    inventory,
    agreementId,
    isAuthorized,
    context,
    hiddenFields,
    alternateUI,
    updateView
}: {
    opportunities: Array<OpportunityData>;
    lineItems: LineItemsData;
    inventory: Array<InventoryData>;
    agreementId?: string | null;
    isAuthorized: boolean;
    context: ComponentFramework.Context<IInputs>;
    hiddenFields: HiddenFields;
    alternateUI?: boolean;
    updateView: () => Promise<void>;
}) {

    return (
        <CartErrorBoundary>
            <AppStateProvider agreementId={agreementId} isAuthorized={isAuthorized} context={context}>
                <NotificationProvider>
                    <AgreementTable
                        opportunities={opportunities}
                        lineItems={lineItems}
                        inventory={inventory}
                        updateView={updateView}
                        hiddenFields={hiddenFields}
                        alternateUI={alternateUI}
                    />
                </NotificationProvider>
            </AppStateProvider>
        </CartErrorBoundary>
    );
}
