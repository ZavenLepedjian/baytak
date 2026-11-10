import assert from 'node:assert/strict';
import { test } from 'node:test';
import { summaryHtml, summaryMessage } from './summaryNotice.ts';

const labels = {
  title: 'Monthly summary',
  billed: 'Billed',
  collected: 'Collected',
  outstanding: 'Still owed',
  noActivity: 'Nothing was collected or billed this month.',
  footer: 'Issued with Baytak.',
};
const model = {
  buildingName: 'Cedar <Residence> & Co',
  monthLabel: 'October 2026',
  blocks: [
    { currency: 'USD', billed: '300 USD', collected: '100 USD', outstanding: '200 USD', owedBy: '2 apartments' },
    { currency: 'LBP', billed: '6,000,000 LBP', collected: '0 LBP', outstanding: '6,000,000 LBP', owedBy: '3 apartments' },
  ],
};

test('the page shows each currency on its own, with the figures', () => {
  const html = summaryHtml(model, labels, false);
  for (const text of ['October 2026', '100 USD', '300 USD', '200 USD', '2 apartments', 'LBP', '6,000,000 LBP', 'Still owed']) {
    assert.ok(html.includes(text), text);
  }
  assert.ok(html.includes('dir="ltr"') && html.includes('lang="en"'));
});

test('every piece of text is escaped', () => {
  const html = summaryHtml({ ...model, blocks: [{ ...model.blocks[0], owedBy: '<b>2</b>' }] }, labels, false);
  assert.ok(html.includes('Cedar &lt;Residence&gt; &amp; Co'));
  assert.ok(!html.includes('<Residence>'));
  assert.ok(html.includes('&lt;b&gt;2&lt;/b&gt;'));
});

test('right-to-left pages say so, and an empty month says so in words', () => {
  assert.ok(summaryHtml(model, labels, true).includes('dir="rtl"'));
  const empty = summaryHtml({ ...model, blocks: [] }, labels, false);
  assert.ok(empty.includes('Nothing was collected or billed this month.'));
});

test('the message lists the figures per currency and ends with the link', () => {
  const text = summaryMessage(model, labels, 'https://example.org/baytak');
  assert.ok(text.startsWith('Cedar <Residence> & Co\nMonthly summary, October 2026\n\nUSD\n• Collected: 100 USD\n• Billed: 300 USD\n• Still owed: 200 USD (2 apartments)'));
  assert.ok(text.includes('\n\nLBP\n'));
  assert.ok(text.trimEnd().endsWith('https://example.org/baytak'));
  assert.ok(summaryMessage({ ...model, blocks: [] }, labels, 'x').includes('Nothing was collected or billed this month.'));
});
