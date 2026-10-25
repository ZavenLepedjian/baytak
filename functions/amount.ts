/**
 * Turns what an administrator types into a plain amount ("1500000", "12.50") that the server accepts.
 *
 * People type thousands separators ("1,500,000", "1.500.000", "1 500") and decimal commas ("12,5"). Reading the first
 * comma as a decimal point turned "1,500,000" into 1.5, and a receipt cannot be edited once issued. The rules:
 *  - LBP has no cents: every separator is dropped.
 *  - USD: three digits after the last separator are a thousands group ("1,500" is 1500); one or two digits after it
 *    are cents, whichever of "." and "," is used ("12,5" is 12.5, "1.500,50" is 1500.50).
 * It is applied on every keystroke, so a trailing separator ("12,") is kept as "12." while the cents are being typed.
 */
export function normalizeAmountInput(text: string, currency: string): string {
  const s = text.replace(/[^\d.,]/g, '');
  if (currency.toUpperCase() === 'LBP') return s.replace(/[.,]/g, '');

  const last = Math.max(s.lastIndexOf('.'), s.lastIndexOf(','));
  if (last === -1) return s;

  const before = s.slice(0, last).replace(/[.,]/g, '');
  const after = s.slice(last + 1);
  if (after.length >= 3) return before + after; // a thousands group (or a digit run with stray separators)
  return `${before}.${after}`;
}

/** The number behind a normalised input; blank or unreadable input is 0. */
export function amountValue(text: string): number {
  const n = Number(text);
  return Number.isFinite(n) ? n : 0;
}
