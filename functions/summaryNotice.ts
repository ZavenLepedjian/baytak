import { escapeHtml } from './receiptNotice.ts';

/**
 * The one-page monthly summary an administrator shares with the building: a PDF and a short message.
 * Pure functions (no React Native), so the layout and the escaping can be tested on their own. Amounts and dates arrive
 * already formatted for the reader, with their currency.
 */
export type SummaryBlock = {
  /** "USD" or "LBP": every currency has its own block and they are never added together. */
  currency: string;
  /** What the month's receipts asked for. */
  billed: string;
  /** What came in during the month. */
  collected: string;
  /** Still unpaid at the end of the month. */
  outstanding: string;
  /** e.g. "2 apartments"; empty when nothing is owed. */
  owedBy: string;
};

export type SummaryModel = {
  buildingName: string;
  /** e.g. "October 2026". */
  monthLabel: string;
  blocks: SummaryBlock[];
};

export type SummaryLabels = {
  title: string;
  billed: string;
  collected: string;
  outstanding: string;
  noActivity: string;
  footer: string;
};

/** An A4 page in the Baytak look. Text direction follows the language; every piece of text is escaped. */
export function summaryHtml(model: SummaryModel, labels: SummaryLabels, rtl: boolean): string {
  const blocks = model.blocks
    .map(
      (b) => `<section>
    <h2>${escapeHtml(b.currency)}</h2>
    <div class="figures">
      <div class="figure main"><span class="label">${escapeHtml(labels.collected)}</span><span class="value">${escapeHtml(b.collected)}</span></div>
      <div class="figure"><span class="label">${escapeHtml(labels.billed)}</span><span class="value">${escapeHtml(b.billed)}</span></div>
      <div class="figure"><span class="label">${escapeHtml(labels.outstanding)}</span><span class="value">${escapeHtml(b.outstanding)}</span>${b.owedBy ? `<span class="note">${escapeHtml(b.owedBy)}</span>` : ''}</div>
    </div>
  </section>`,
    )
    .join('');

  return `<!doctype html>
<html lang="${rtl ? 'ar' : 'en'}" dir="${rtl ? 'rtl' : 'ltr'}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>
  body { font-family: -apple-system, 'Helvetica Neue', Arial, 'Noto Sans Arabic', 'Geeza Pro', sans-serif; color: #0B2625; margin: 0; padding: 32px; }
  .band { background: #00847C; color: #fff; padding: 22px 26px; border-radius: 12px; }
  .band .title { font-size: 14px; opacity: .85; margin: 0 0 4px; }
  .band .building { font-size: 26px; font-weight: 700; margin: 0; }
  .band .month { font-size: 16px; margin: 6px 0 0; opacity: .95; }
  section { margin-top: 22px; }
  h2 { font-size: 15px; letter-spacing: .06em; color: #3F5654; margin: 0 0 8px; }
  .figures { display: flex; gap: 10px; }
  .figure { flex: 1; background: #EAF6F5; border-radius: 10px; padding: 12px 14px; }
  .figure.main { background: #00847C; color: #fff; }
  .figure .label { display: block; font-size: 12px; opacity: .8; }
  .figure .value { display: block; font-size: 20px; font-weight: 700; margin-top: 4px; }
  .figure .note { display: block; font-size: 12px; margin-top: 2px; opacity: .8; }
  .empty { margin-top: 24px; font-size: 15px; color: #3F5654; }
  .footer { margin-top: 28px; font-size: 12px; color: #3F5654; border-top: 1px solid #D0DDDC; padding-top: 12px; }
</style>
</head>
<body>
  <div class="band"><p class="title">${escapeHtml(labels.title)}</p><p class="building">${escapeHtml(model.buildingName)}</p><p class="month">${escapeHtml(model.monthLabel)}</p></div>
  ${blocks || `<p class="empty">${escapeHtml(labels.noActivity)}</p>`}
  <p class="footer">${escapeHtml(labels.footer)}</p>
</body>
</html>`;
}

/** The text that goes with the PDF, or alone: short enough to read in a group chat. */
export function summaryMessage(model: SummaryModel, labels: SummaryLabels, link: string): string {
  const blocks = model.blocks
    .map((b) =>
      [
        b.currency,
        `• ${labels.collected}: ${b.collected}`,
        `• ${labels.billed}: ${b.billed}`,
        `• ${labels.outstanding}: ${b.outstanding}${b.owedBy ? ` (${b.owedBy})` : ''}`,
      ].join('\n'),
    )
    .join('\n\n');
  return `${model.buildingName}\n${labels.title}, ${model.monthLabel}\n\n${blocks || labels.noActivity}\n\n${labels.footer}\n${link}`;
}
