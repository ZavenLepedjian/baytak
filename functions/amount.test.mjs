// Run with: npm run test:unit  (Node 22.6+ strips the TypeScript types itself)
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { amountValue, normalizeAmountInput as n } from './amount.ts';

test('lira has no cents: every separator is a thousands separator', () => {
  assert.equal(n('1,500,000', 'LBP'), '1500000');
  assert.equal(n('1.500.000', 'LBP'), '1500000');
  assert.equal(n('1 500 000', 'LBP'), '1500000');
  assert.equal(n('2225000', 'LBP'), '2225000');
  assert.equal(n('', 'LBP'), '');
});

test('dollars: three digits after a separator are thousands, one or two are cents', () => {
  assert.equal(n('1,500', 'USD'), '1500');
  assert.equal(n('1,500,000', 'USD'), '1500000');
  assert.equal(n('1,500.50', 'USD'), '1500.50');
  assert.equal(n('1.500,50', 'USD'), '1500.50');
  assert.equal(n('12,5', 'USD'), '12.5');
  assert.equal(n('12.5', 'USD'), '12.5');
  assert.equal(n('0,75', 'USD'), '0.75');
  assert.equal(n('100', 'USD'), '100');
});

test('while typing, a trailing separator is kept as a decimal point', () => {
  assert.equal(n('12,', 'USD'), '12.');
  assert.equal(n('.5', 'USD'), '.5');
});

test('letters are dropped and the value is read safely', () => {
  assert.equal(n('abc', 'USD'), '');
  assert.equal(amountValue('1500.50'), 1500.5);
  assert.equal(amountValue(''), 0);
  assert.equal(amountValue('12.'), 12);
  assert.equal(amountValue('x'), 0);
});
