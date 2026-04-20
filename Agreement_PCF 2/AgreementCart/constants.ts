// -------------------------------------------------------------------------
// Phase E — central constants for the Agreement Cart PCF.
// (plan: atomic-jumping-rabin.md §Phase E)
//
// Goal: replace scattered magic numbers with named constants so future
// tuning happens in one place. These mirror the values the legacy code
// hard-coded inline. Importing this file is intentionally cheap (no
// side-effects, no dependencies on React or PCF).
// -------------------------------------------------------------------------

/** D365 option-set values for ats_rate.ats_ratetype. */
export const RATE_TYPE = {
    SEASON: 114300000,
    INDIVIDUAL: 114300001,
} as const;

/** D365 option-set values for opportunity.ats_pricingmode. */
export const PRICING_MODE = {
    AUTOMATIC: 559240000,
    MANUAL: 559240001,
} as const;

/** D365 option-set values for opportunity.statuscode (relevant transitions). */
export const OPP_STATUS_REASON = {
    OPPORTUNITY: 114300008,
    PROPOSAL: 114300009,
    CONTRACT: 114300010,
} as const;

/** Pagination defaults used by the AgreementTable / InventoryTable. */
export const PAGINATION = {
    DEFAULT_PAGE_SIZE: 50,
    PAGE_SIZE_OPTIONS: [20, 50, 100],
} as const;

/** Search-input debounce in ms. */
export const SEARCH_DEBOUNCE_MS = 1000;

/** Cache TTL for the per-product available-seasons lookup (ms). */
export const SEASON_LOOKUP_TTL_MS = 2 * 60 * 1000;

/** Maximum concurrent in-flight HTTP calls during bulk operations. */
export const BULK_CONCURRENCY_LIMIT = 4;
