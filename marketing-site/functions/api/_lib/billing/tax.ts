// Tax resolution — a hardcoded country table, reviewed quarterly (see
// BILLING_SETUP.md § Tax). Planscape Ltd is established in Uganda, so:
//   UG               → 18% VAT, shown as an inclusive line on the invoice.
//   KE/TZ/RW/NG/ZA   → B2B reverse charge: no tax collected, customer self-accounts.
//   EU-27 + GB       → VAT MOSS at the customer-country rate (inclusive).
//   everywhere else  → 0% (no tax line).
//
// Prices are treated as TAX-INCLUSIVE: the total a customer pays never changes,
// we only break out the embedded tax component. So for an inclusive rate r,
//   subtotal = round(total / (1 + r)),  tax = total - subtotal.
// That keeps subtotal + tax === total exactly and never silently inflates a
// charge to "add" tax after the fact.

export interface TaxResult {
  subtotalCents: number;
  taxCents: number;
  taxLabel: string | null; // null = no tax line shown
}

// Customer-country VAT rates applied inclusively. Uganda is domestic; the rest
// are EU MOSS + GB. Keep in sync with the quarterly review note in BILLING_SETUP.md.
const INCLUSIVE_VAT_RATES: Record<string, { rate: number; label: string }> = {
  UG: { rate: 0.18, label: "UG VAT 18%" },
  // United Kingdom + EU-27 (standard rates).
  GB: { rate: 0.20, label: "UK VAT 20%" },
  AT: { rate: 0.20, label: "AT VAT 20%" },
  BE: { rate: 0.21, label: "BE VAT 21%" },
  BG: { rate: 0.20, label: "BG VAT 20%" },
  HR: { rate: 0.25, label: "HR VAT 25%" },
  CY: { rate: 0.19, label: "CY VAT 19%" },
  CZ: { rate: 0.21, label: "CZ VAT 21%" },
  DK: { rate: 0.25, label: "DK VAT 25%" },
  EE: { rate: 0.22, label: "EE VAT 22%" },
  FI: { rate: 0.255, label: "FI VAT 25.5%" },
  FR: { rate: 0.20, label: "FR VAT 20%" },
  DE: { rate: 0.19, label: "DE VAT 19%" },
  GR: { rate: 0.24, label: "GR VAT 24%" },
  HU: { rate: 0.27, label: "HU VAT 27%" },
  IE: { rate: 0.23, label: "IE VAT 23%" },
  IT: { rate: 0.22, label: "IT VAT 22%" },
  LV: { rate: 0.21, label: "LV VAT 21%" },
  LT: { rate: 0.21, label: "LT VAT 21%" },
  LU: { rate: 0.17, label: "LU VAT 17%" },
  MT: { rate: 0.18, label: "MT VAT 18%" },
  NL: { rate: 0.21, label: "NL VAT 21%" },
  PL: { rate: 0.23, label: "PL VAT 23%" },
  PT: { rate: 0.23, label: "PT VAT 23%" },
  RO: { rate: 0.19, label: "RO VAT 19%" },
  SK: { rate: 0.23, label: "SK VAT 23%" },
  SI: { rate: 0.22, label: "SI VAT 22%" },
  ES: { rate: 0.21, label: "ES VAT 21%" },
  SE: { rate: 0.25, label: "SE VAT 25%" },
};

// B2B reverse-charge jurisdictions: no tax cents, but the invoice carries a line
// instructing the customer to self-account to their revenue authority.
const REVERSE_CHARGE_AUTHORITY: Record<string, string> = {
  KE: "KRA",
  TZ: "TRA",
  RW: "RRA",
  NG: "FIRS",
  ZA: "SARS",
};

// Resolve the tax breakdown for a tax-inclusive `totalCents` charged to a tenant
// in `country` (ISO 3166-1 alpha-2). `country` may be null (treated as 0%).
export function resolveTax(country: string | null, totalCents: number): TaxResult {
  const cc = (country || "").trim().toUpperCase();

  const vat = INCLUSIVE_VAT_RATES[cc];
  if (vat) {
    const subtotal = Math.round(totalCents / (1 + vat.rate));
    return {
      subtotalCents: subtotal,
      taxCents: totalCents - subtotal,
      taxLabel: vat.label,
    };
  }

  const authority = REVERSE_CHARGE_AUTHORITY[cc];
  if (authority) {
    return {
      subtotalCents: totalCents,
      taxCents: 0,
      taxLabel: `Reverse charge — declare to ${authority}`,
    };
  }

  // US, CA, AU, and the rest: no tax line.
  return { subtotalCents: totalCents, taxCents: 0, taxLabel: null };
}
