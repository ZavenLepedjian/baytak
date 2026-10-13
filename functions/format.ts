export function formatNumber(value: unknown): string {
  if (value === null || value === undefined || value === '') return '0';
  const num = Number(value);
  if (Number.isNaN(num)) return String(value);
  const [int, dec] = String(num).split('.');
  return int.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + (dec ? `.${dec}` : '');
}

export function currencyLabel(currency: unknown): 'USD' | 'LBP' {
  return typeof currency === 'string' && currency.toLowerCase() === 'usd' ? 'USD' : 'LBP';
}

/** Amount with thousands separators; USD shows cents whenever there are any ("1,250.50"). */
export function formatAmount(value: unknown, currency?: unknown): string {
  // Round to cents first: sums of decimals ("0.1 + 0.2") must never show as 0.30000000000000004.
  const num = Math.round(Number(value ?? 0) * 100) / 100;
  if (!Number.isFinite(num)) return String(value ?? '');
  const fixed = currencyLabel(currency) === 'USD' && !Number.isInteger(num) ? num.toFixed(2) : String(num);
  const [int, dec] = fixed.split('.');
  return int.replace(/\B(?=(\d{3})+(?!\d))/g, ',') + (dec ? `.${dec}` : '');
}

export function formatMoney(value: unknown, currency: unknown): string {
  return `${formatAmount(value, currency)} ${currencyLabel(currency)}`;
}

export function parseDate(value: unknown): Date | null {
  if (!value) return null;
  const d = value instanceof Date ? value : new Date(value as string);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** Local calendar date as "yyyy-MM-dd". Unlike toISOString() it never shifts the day across time zones. */
export function toYmd(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Parses "yyyy-MM-dd" as a local date (not UTC midnight). */
export function fromYmd(value: string): Date {
  const [y, m, d] = value.split('-').map(Number);
  return new Date(y, (m || 1) - 1, d || 1);
}

export function formatDate(value: unknown): string {
  const d = parseDate(value);
  return d ? d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' }) : '—';
}

export function ordinalFloor(floor: number | string | null | undefined): string {
  const n = Number(floor);
  if (!Number.isFinite(n)) return 'Floor';
  const mod100 = n % 100;
  const mod10 = n % 10;
  const suffix = mod100 >= 11 && mod100 <= 13 ? 'th' : mod10 === 1 ? 'st' : mod10 === 2 ? 'nd' : mod10 === 3 ? 'rd' : 'th';
  return `${n}${suffix} floor`;
}

export function initials(name: string | null | undefined): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return '?';
  return (parts[0][0] + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase();
}

export type ReceiptField = { name: string; type: 'amount' | 'text'; value: string };
export type ParsedReceipt = { date: Date | null; fields: ReceiptField[] };

/**
 * The backend stores receipts as a JSON string whose casing differs between endpoints
 * (`CustomFields`/`FieldName` vs `customFields`/`fieldName`). Accept both and never throw.
 */
export function parseReceipt(raw: unknown): ParsedReceipt {
  let obj: any = raw;
  if (typeof raw === 'string') {
    try {
      obj = JSON.parse(raw);
    } catch {
      return { date: null, fields: [] };
    }
  }
  if (!obj || typeof obj !== 'object') return { date: null, fields: [] };
  const list: any[] = obj.CustomFields ?? obj.customFields ?? [];
  const fields: ReceiptField[] = Array.isArray(list)
    ? list.map((f) => ({
        name: String(f?.FieldName ?? f?.fieldName ?? ''),
        type: String(f?.Type ?? f?.type ?? '').toLowerCase() === 'amount' ? 'amount' : 'text',
        value: String(f?.Value ?? f?.value ?? ''),
      }))
    : [];
  return { date: parseDate(obj.Date ?? obj.date), fields };
}
