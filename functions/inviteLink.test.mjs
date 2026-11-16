import assert from 'node:assert/strict';
import { test } from 'node:test';
import { formatInviteCode, inviteAppLink, inviteCodeFromUrl, inviteLink, inviteMessage, normalizeInviteCode, timeLeft } from './inviteLink.ts';

test('a code is read as it was typed, copied or spoken', () => {
  assert.equal(normalizeInviteCode('abcd-2345'), 'ABCD2345');
  assert.equal(normalizeInviteCode(' ABCD 2345 '), 'ABCD2345');
  assert.equal(normalizeInviteCode('ABCD2345'), 'ABCD2345');
  assert.equal(formatInviteCode('ABCD2345'), 'ABCD-2345');
});

test('anything that cannot be a code is refused', () => {
  for (const bad of ['', '   ', 'ABCD234', 'ABCD23456', 'ABCD2340', 'ABCDO345', 'ABCD1345', 'ABCDL345', 'ABCDI345', null, undefined]) {
    assert.equal(normalizeInviteCode(bad), null, String(bad));
  }
});

test('the code is found in every kind of link', () => {
  for (const url of [
    'baytak://join?code=ABCD2345',
    'baytak://join/?code=abcd2345',
    'baytak://join/ABCD2345',
    'baytak://join?code=ABCD-2345',
    'https://baytakapi8803.z16.web.core.windows.net/join/?code=ABCD2345',
    'https://baytakapi8803.z16.web.core.windows.net/join?code=ABCD2345#top',
    'https://example.com/join/?utm=x&code=ABCD2345',
    ' baytak://join?code=ABCD2345 ',
  ]) {
    assert.equal(inviteCodeFromUrl(url), 'ABCD2345', url);
  }
});

test('other links, bad codes and malformed text give no code', () => {
  for (const url of [
    '',
    null,
    undefined,
    'baytak://receipts?code=ABCD2345',
    'https://example.com/privacy/?code=ABCD2345',
    'baytak://join',
    'baytak://join?code=',
    'baytak://join?code=ABCD234',
    'baytak://join?code=%E0%A4%A',
    'javascript:alert(1)',
  ]) {
    assert.equal(inviteCodeFromUrl(url), null, String(url));
  }
});

test('the links round-trip through the parser', () => {
  assert.equal(inviteAppLink('ABCD2345'), 'baytak://join?code=ABCD2345');
  assert.equal(inviteLink('https://site.example/', 'ABCD2345'), 'https://site.example/join/?code=ABCD2345');
  assert.equal(inviteCodeFromUrl(inviteAppLink('ABCD2345')), 'ABCD2345');
  assert.equal(inviteCodeFromUrl(inviteLink('https://site.example', 'ABCD2345')), 'ABCD2345');
});

test('the WhatsApp message keeps the link alone on its line and shows the code in groups', () => {
  const text = inviteMessage({
    headline: "You're invited to join Tower on Baytak.",
    linkLabel: 'Open this link on your phone:',
    link: 'https://site.example/join/?code=ABCD2345',
    codeLabel: 'Your building code:',
    code: 'ABCD2345',
    validity: 'Valid for 8 hours, until 18:30.',
    footer: 'Sign in with your phone number, then enter your apartment.',
  });
  const lines = text.split('\n');
  assert.equal(lines[0], "You're invited to join Tower on Baytak.");
  assert.ok(lines.includes('https://site.example/join/?code=ABCD2345'));
  assert.ok(lines.includes('Your building code: ABCD-2345'));
  assert.equal(lines.at(-1), 'Sign in with your phone number, then enter your apartment.');
});

test('the time left rounds up to the minute and ends at zero', () => {
  const now = Date.parse('2026-11-16T10:00:00Z');
  assert.deepEqual(timeLeft('2026-11-16T18:00:00Z', now), { hours: 8, minutes: 0, expired: false });
  assert.deepEqual(timeLeft('2026-11-16T17:59:30Z', now), { hours: 8, minutes: 0, expired: false });
  assert.deepEqual(timeLeft('2026-11-16T12:42:10Z', now), { hours: 2, minutes: 43, expired: false });
  assert.deepEqual(timeLeft('2026-11-16T10:00:20Z', now), { hours: 0, minutes: 1, expired: false });
  assert.deepEqual(timeLeft('2026-11-16T10:00:00Z', now), { hours: 0, minutes: 0, expired: true });
  assert.deepEqual(timeLeft('2026-11-16T09:00:00Z', now), { hours: 0, minutes: 0, expired: true });
  assert.deepEqual(timeLeft('not a date', now), { hours: 0, minutes: 0, expired: true });
});
