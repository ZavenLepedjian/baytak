import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { translateMessage, translatePlural } from './core.ts';
import { ar, server } from './ar/all.ts';

test('every text the app shows, and every message the server writes, has an Arabic entry', () => {
  const result = spawnSync(process.execPath, ['--experimental-strip-types', '--no-warnings', fileURLToPath(new URL('./check.mjs', import.meta.url))], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stdout + result.stderr);
});

test('messages from the server arrive in Arabic, with their numbers', () => {
  const arabic = (text) => translateMessage('ar', ar, text, server);
  assert.equal(arabic('Receipt not found'), 'الإيصال غير موجود');
  assert.equal(arabic('Template names can have at most 60 characters'), 'يمكن أن يحتوي اسم القالب على 60 حرفًا كحد أقصى');
  assert.equal(arabic('You can keep at most 20 templates. Delete one first.'), 'يمكنك الاحتفاظ بـ20 قالبًا كحد أقصى. احذف قالبًا أولًا.');
  assert.equal(arabic('"Water" is too long'), '«Water» طويل جدًا');
  // Text of the app itself is never taken for a server message with a placeholder.
  assert.equal(arabic('Something nobody wrote text'), 'Something nobody wrote text');
  assert.equal(translateMessage('en', ar, 'Receipt not found', server), 'Receipt not found');
});

test('counts read correctly in Arabic', () => {
  const key = '{n} receipt|{n} receipts';
  const say = (n) => translatePlural('ar', ar, key, n);
  assert.equal(say(1), 'إيصال واحد');
  assert.equal(say(2), 'إيصالان');
  assert.equal(say(5), '5 إيصالات');
  assert.equal(say(14), '14 إيصالًا');
  assert.equal(say(100), '100 إيصال');
  assert.equal(translatePlural('en', ar, key, 1), '1 receipt');
  assert.equal(translatePlural('en', ar, key, 3), '3 receipts');
});
