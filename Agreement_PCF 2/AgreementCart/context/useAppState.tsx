import * as React from "react";
import { IInputs } from "../generated/ManifestTypes";

// -------------------------------------------------------------------------
// Phase D.2 — memoised value (plan: atomic-jumping-rabin.md §Phase D.2)
//
// The previous implementation re-allocated the `value` object on every
// provider render, which broadcast a fresh reference to React.Context
// consumers and re-rendered the entire tree (AgreementTable + every
// table cell) even when no app-state field had changed.
//
// useMemo with a stable dependency tuple keeps the same reference across
// renders unless one of the underlying values actually changes.
// -------------------------------------------------------------------------

interface IAppStateContext {
    agreementId?: string | null;
    isAuthorized: boolean;
    context: ComponentFramework.Context<IInputs>
}

const AppStateContext = React.createContext({} as IAppStateContext);

export function useAppState() {
    return React.useContext(AppStateContext);
}

export function AppStateProvider({
    children,
    agreementId,
    isAuthorized,
    context
}: {
    children: React.ReactNode;
    agreementId?: string | null;
    isAuthorized: boolean;
    context: ComponentFramework.Context<IInputs>
}) {
    const [id, setId] = React.useState(agreementId);

    React.useEffect(() => {
        setId(agreementId)
    }, [agreementId])

    const value = React.useMemo<IAppStateContext>(() => ({
        agreementId: id,
        isAuthorized,
        context,
    }), [id, isAuthorized, context]);

    return (
        <AppStateContext.Provider value={value}>
            {children}
        </AppStateContext.Provider>
    )
}
