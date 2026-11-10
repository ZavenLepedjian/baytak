import assert from 'node:assert/strict';
import { test } from 'node:test';
import { interpolate, latinDigits, pluralIndex, translate, translateMessage, translatePlural } from './core.ts';

const dict = {
  'Sign in': 'تسجيل الدخول',
  'Hello {name}': 'مرحبا {name}',
  '{n} apartment|{n} apartments': 'لا شقق|شقة واحدة|شقتان|{n} شقق|{n} شقة|{n} شقة',
};

test('English is the key and is returned as it is', () => {
  assert.equal(translate('en', dict, 'Sign in'), 'Sign in');
  assert.equal(translate('en', dict, 'Hello {name}', { name: 'Rania' }), 'Hello Rania');
});

test('Arabic uses the dictionary, falls back to English, and fills placeholders', () => {
  assert.equal(translate('ar', dict, 'Sign in'), 'تسجيل الدخول');
  assert.equal(translate('ar', dict, 'Hello {name}', { name: 'Rania' }), 'مرحبا Rania');
  assert.equal(translate('ar', dict, 'Not translated yet'), 'Not translated yet');
  assert.equal(interpolate('Keep {unknown}', { other: 1 }), 'Keep {unknown}');
});

test('English plurals: one, then other', () => {
  assert.equal(translatePlural('en', {}, '{n} apartment|{n} apartments', 1), '1 apartment');
  assert.equal(translatePlural('en', {}, '{n} apartment|{n} apartments', 0), '0 apartments');
  assert.equal(translatePlural('en', {}, '{n} apartment|{n} apartments', 14), '14 apartments');
});

test('Arabic plurals use the six forms', () => {
  const key = '{n} apartment|{n} apartments';
  assert.equal(translatePlural('ar', dict, key, 0), 'لا شقق');
  assert.equal(translatePlural('ar', dict, key, 1), 'شقة واحدة');
  assert.equal(translatePlural('ar', dict, key, 2), 'شقتان');
  assert.equal(translatePlural('ar', dict, key, 7), '7 شقق');
  assert.equal(translatePlural('ar', dict, key, 14), '14 شقة');
  assert.equal(translatePlural('ar', dict, key, 100), '100 شقة');
  assert.equal([0, 1, 2, 3, 10, 11, 99, 100, 102, 103, 111].map((n) => pluralIndex('ar', n)).join(''), '01233445534'.replace(/(.)$/, '$1'));
});

test('an untranslated plural falls back to the English forms, not the Arabic rule', () => {
  assert.equal(translatePlural('ar', {}, '{n} receipt|{n} receipts', 2), '2 receipts');
});

test('Arabic-Indic and Persian digits become Latin digits', () => {
  assert.equal(latinDigits('١٢٣٤٥٦٧٨٩٠ and ۱۲۳'), '1234567890 and 123');
  assert.equal(latinDigits('25 Oct 2026'), '25 Oct 2026');
});

test('server messages translate exactly, by pattern, and otherwise stay as written', () => {
  const dictionary = {
    'Profile saved successfully': 'تم حفظ الملف الشخصي',
    'Template names can have at most {max} characters': 'يمكن أن يحتوي اسم القالب على {max} حرفًا كحد أقصى',
    'A month can hold at most {max} expenses (a.k.a. "limit")': 'الحد {max}',
  };
  assert.equal(translateMessage('ar', dictionary, 'Profile saved successfully'), 'تم حفظ الملف الشخصي');
  assert.equal(translateMessage('ar', dictionary, 'Template names can have at most 60 characters'), 'يمكن أن يحتوي اسم القالب على 60 حرفًا كحد أقصى');
  assert.equal(translateMessage('ar', dictionary, 'A month can hold at most 500 expenses (a.k.a. "limit")'), 'الحد 500');
  assert.equal(translateMessage('ar', dictionary, 'Something unknown'), 'Something unknown');
  assert.equal(translateMessage('en', dictionary, 'Profile saved successfully'), 'Profile saved successfully');
});
