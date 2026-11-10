/**
 * What an administrator sends to the building's WhatsApp group after issuing a receipt: a one-page PDF and a short message.
 * Pure functions (no React Native), so the layout and the escaping can be tested on their own.
 */
export type NoticeLine = { name: string; /** Formatted with its currency, e.g. "40 USD". */ amount?: string; note?: string };

export type NoticeModel = {
  buildingName: string;
  /** Already formatted for the reader, e.g. "25 Oct 2026". */
  date: string;
  lines: NoticeLine[];
  /** Total per apartment, formatted with its currency. */
  total: string;
  /** "All 14 apartments" or "Apartments 3A, 3B". */
  billedTo: string;
};

export type NoticeLabels = {
  title: string;
  date: string;
  item: string;
  amount: string;
  totalPerApartment: string;
  billedTo: string;
  footer: string;
};

const entities: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
export const escapeHtml = (text: string) => text.replace(/[&<>"']/g, (c) => entities[c]);

/** An A4 page in the Baytak look. Text direction follows the language; every piece of text is escaped. */
export function receiptNoticeHtml(model: NoticeModel, labels: NoticeLabels, rtl: boolean): string {
  const rows = model.lines
    .map((l) =>
      l.amount !== undefined
        ? `<tr><td>${escapeHtml(l.name)}</td><td class="amount">${escapeHtml(l.amount)}</td></tr>`
        : `<tr><td colspan="2"><strong>${escapeHtml(l.name)}</strong><div class="note">${escapeHtml(l.note ?? '')}</div></td></tr>`,
    )
    .join('');
  return `<!doctype html>
<html lang="${rtl ? 'ar' : 'en'}" dir="${rtl ? 'rtl' : 'ltr'}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
  body { font-family: -apple-system, 'Helvetica Neue', Arial, 'Noto Sans Arabic', 'Geeza Pro', sans-serif; color: #0B2625; margin: 0; padding: 36px; }
  .band { background: #00847C; color: #fff; padding: 26px 30px; border-radius: 12px; }
  .band .title { font-size: 15px; opacity: .85; margin: 0 0 6px; }
  .band .building { font-size: 28px; font-weight: 700; margin: 0; }
  .meta { margin: 26px 2px 0; font-size: 15px; color: #3F5654; }
  .meta strong { color: #0B2625; }
  table { width: 100%; border-collapse: collapse; margin-top: 22px; }
  th { text-align: start; font-size: 12px; letter-spacing: .04em; color: #3F5654; padding: 0 10px 8px; border-bottom: 2px solid #D0DDDC; }
  th.amount, td.amount { text-align: end; }
  td { padding: 14px 10px; border-bottom: 1px solid #D0DDDC; font-size: 18px; vertical-align: top; }
  td.amount { font-weight: 600; white-space: nowrap; }
  .note { font-size: 15px; color: #3F5654; margin-top: 4px; font-weight: 400; }
  .total { margin-top: 24px; background: #EAF6F5; border-radius: 12px; padding: 20px 24px; display: flex; justify-content: space-between; align-items: center; }
  .total .label { font-size: 16px; font-weight: 600; color: #05302E; }
  .total .value { font-size: 30px; font-weight: 700; color: #05302E; }
  .footer { margin-top: 36px; font-size: 13px; color: #3F5654; border-top: 1px solid #D0DDDC; padding-top: 14px; }
</style>
</head>
<body>
  <div class="band"><p class="title">${escapeHtml(labels.title)}</p><p class="building">${escapeHtml(model.buildingName)}</p></div>
  <p class="meta"><strong>${escapeHtml(labels.date)}:</strong> ${escapeHtml(model.date)}<br><strong>${escapeHtml(labels.billedTo)}:</strong> ${escapeHtml(model.billedTo)}</p>
  <table>
    <thead><tr><th>${escapeHtml(labels.item)}</th><th class="amount">${escapeHtml(labels.amount)}</th></tr></thead>
    <tbody>${rows}</tbody>
  </table>
  <div class="total"><span class="label">${escapeHtml(labels.totalPerApartment)}</span><span class="value">${escapeHtml(model.total)}</span></div>
  <p class="footer">${escapeHtml(labels.footer)}</p>
</body>
</html>`;
}

/** The text that goes with the PDF, or alone: short enough to read in a group chat. */
export function receiptNoticeMessage(model: NoticeModel, labels: NoticeLabels, link: string): string {
  const lines = model.lines.map((l) => (l.amount !== undefined ? `• ${l.name}: ${l.amount}` : `• ${l.name}: ${l.note ?? ''}`)).join('\n');
  return `${model.buildingName}\n${labels.title}, ${model.date}\n\n${lines}\n\n${labels.totalPerApartment}: ${model.total}\n${labels.billedTo}: ${model.billedTo}\n\n${labels.footer}\n${link}`;
}
