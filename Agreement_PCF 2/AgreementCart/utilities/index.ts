export async function sleep(msec) {
    return new Promise((resolve) => setTimeout(resolve, msec));
}


export function formatNumberValue(n: number, decimals?: number) {
    try {
        if (n === undefined || n === null) return "!N/A";
        if (isNaN(n)) throw new Error("Input is not a number.");
        const num = +n

        const value = num.toLocaleString('en-US', { style: 'decimal', minimumFractionDigits: decimals || 0, maximumFractionDigits: 2 })
        return value
    } catch (error: any) {
        console.log("formatNumberValue", JSON.stringify(error.message || "Error"));
        return "!N/A"
    }
}


// Format function
export function formatDollarValue(n: number, currencyIsoCode = 'USD') {
    try {
        if (n === undefined || n === null) return "!N/A";
        if (isNaN(n)) throw new Error("Input is not a number.");
        const num = +n;

        // Resolve locale from currencyIsoCode
        const locale = currencyToLocale[currencyIsoCode] || 'en-US'; // Default to 'en-US' if no match

        // Format the number
        return num.toLocaleString(locale, {
            style: 'currency',
            currency: currencyIsoCode, // Use ISO code directly for currency
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    } catch (error: any) {
        console.error("formatDollarValue error:", error.message || "Error");
        return "!N/A";
    }
}

export function likeMatch(terms, pattern) {
    try {
        // Check if all terms are present in the pattern, in any order
        return terms.some(term => pattern.includes(term));
    } catch (error) {
        return false;
    }
}


const currencyToLocale = {
    USD: 'en-US', // United States Dollar
    GBP: 'en-GB', // British Pound Sterling
    EUR: 'fr-FR', // Euro (default to France)
    AUD: 'en-AU', // Australian Dollar
    CAD: 'en-CA', // Canadian Dollar
    JPY: 'ja-JP', // Japanese Yen
    CNY: 'zh-CN', // Chinese Yuan
    CHF: 'de-CH', // Swiss Franc
    INR: 'hi-IN', // Indian Rupee
    NZD: 'en-NZ', // New Zealand Dollar
    SEK: 'sv-SE', // Swedish Krona
    NOK: 'nb-NO', // Norwegian Krone
    DKK: 'da-DK', // Danish Krone
    SGD: 'en-SG', // Singapore Dollar
    HKD: 'zh-HK', // Hong Kong Dollar
    KRW: 'ko-KR', // South Korean Won
    ZAR: 'en-ZA', // South African Rand
    RUB: 'ru-RU', // Russian Ruble
    MXN: 'es-MX', // Mexican Peso
    BRL: 'pt-BR', // Brazilian Real
    ARS: 'es-AR', // Argentine Peso
    THB: 'th-TH', // Thai Baht
    MYR: 'ms-MY', // Malaysian Ringgit
    IDR: 'id-ID', // Indonesian Rupiah
    PLN: 'pl-PL', // Polish Zloty
    TRY: 'tr-TR', // Turkish Lira
    ILS: 'he-IL', // Israeli New Shekel
    SAR: 'ar-SA', // Saudi Riyal
    AED: 'ar-AE', // United Arab Emirates Dirham
};