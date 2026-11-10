import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeHtml, receiptNoticeHtml, receiptNoticeMessage } from './receiptNotice.ts';

const labels = {
  title: 'Building fees',
  date: 'Date',
  item: 'Item',
  amount: 'Amount',
  totalPerApartment: 'Total per apartment',
  billedTo: 'Billed to',
  footer: 'Issued with Baytak.',
};
const model = {
  buildingName: 'Cedar <Residence> & Co',
  date: '25 Oct 2026',
  lines: [
    { name: 'Maintenance', amount: '40 USD' },
    { name: 'Electricity', amount: '10 USD' },
    { name: 'Note', note: 'Pay by the 10th "please"' },
  ],
  total: '50 USD',
  billedTo: 'All 14 apartments',
};

test('every piece of text is escaped', () => {
  const html = receiptNoticeHtml(model, labels, false);
  assert.ok(html.includes('Cedar &lt;Residence&gt; &amp; Co'));
  assert.ok(!html.includes('<Residence>'));
  assert.ok(html.includes('Pay by the 10th &quot;please&quot;'));
  assert.equal(escapeHtml(`<a href="x">'&'</a>`), '&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;');
});

test('the page shows the lines, the total and who it is billed to', () => {
  const html = receiptNoticeHtml(model, labels, false);
  for (const text of ['Maintenance', '40 USD', 'Electricity', '10 USD', 'Total per apartment', '50 USD', 'All 14 apartments', '25 Oct 2026']) assert.ok(html.includes(text), text);
  assert.ok(html.includes('dir="ltr"') && html.includes('lang="en"'));
});

test('right-to-left pages say so', () => {
  const html = receiptNoticeHtml(model, labels, true);
  assert.ok(html.includes('dir="rtl"') && html.includes('lang="ar"'));
});

test('the message lists the lines and ends with the link', () => {
  const text = receiptNoticeMessage(model, labels, 'https://example.org/baytak');
  assert.ok(text.startsWith('Cedar <Residence> & Co\nBuilding fees, 25 Oct 2026'));
  assert.ok(text.includes('• Maintenance: 40 USD\n• Electricity: 10 USD\n• Note: Pay by the 10th "please"'));
  assert.ok(text.includes('Total per apartment: 50 USD'));
  assert.ok(text.trimEnd().endsWith('https://example.org/baytak'));
});
