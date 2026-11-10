import { I18nManager } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import { ar, server } from './ar';
import { Language, Params, translate, translateMessage, translatePlural } from './core';

export type { Language } from './core';
export { latinDigits } from './core';
/** What the person chose: follow the phone, or a fixed language. */
export type LanguagePreference = 'auto' | Language;

const KEY = 'languagePreference';

let current: Language = 'en';
let preference: LanguagePreference = 'auto';

/** The phone's language, as far as this app supports one (Arabic or English). */
function deviceLanguage(): Language {
  const id = String(I18nManager.getConstants?.().localeIdentifier ?? '').toLowerCase();
  return id.startsWith('ar') ? 'ar' : 'en';
}

/**
 * Decides the language once, when the app starts and before any screen is loaded (so even a text kept in a module-level
 * constant is already in the right language). The language and the layout direction change together on the next launch
 * (React Native reads the direction when the app starts), never half way through a session, so text and layout agree.
 */
function decide(): void {
  try {
    const stored = SecureStore.getItem(KEY);
    if (stored === 'auto' || stored === 'en' || stored === 'ar') preference = stored;
  } catch {
    // The preference is a convenience; without it the phone's language is used.
  }
  const wantsArabic = preference === 'ar' || (preference === 'auto' && deviceLanguage() === 'ar');
  // These apply from the next launch. Only Arabic is a right-to-left language here, so RTL stays off for everything else.
  I18nManager.allowRTL(wantsArabic);
  I18nManager.forceRTL(preference === 'ar');
  current = wantsArabic && I18nManager.isRTL ? 'ar' : 'en';
}
decide();

export function currentLanguage(): Language {
  return current;
}
export function isArabic(): boolean {
  return current === 'ar';
}
export function getLanguagePreference(): LanguagePreference {
  return preference;
}

/** Saves the choice. It takes effect the next time the app is opened. */
export async function setLanguagePreference(next: LanguagePreference): Promise<void> {
  preference = next;
  try {
    await SecureStore.setItemAsync(KEY, next);
  } catch {
    // Not saved: the choice is lost on the next launch.
  }
  const wantsArabic = next === 'ar' || (next === 'auto' && deviceLanguage() === 'ar');
  I18nManager.allowRTL(wantsArabic);
  I18nManager.forceRTL(next === 'ar');
}

/** True when the saved choice would give a different language than the one running now (a restart is needed). */
export function languageChangePending(): boolean {
  const wantsArabic = preference === 'ar' || (preference === 'auto' && deviceLanguage() === 'ar');
  return wantsArabic !== (current === 'ar');
}

/** Translates an English text. Safe to call anywhere (screens, toasts, API errors): the language does not change while the app runs. */
export function t(key: string, params?: Params): string {
  return translate(current, ar, key, params);
}

/** Translates a message the server wrote in English (errors and confirmations); unknown messages stay as they are. */
export function tServer(text: string): string {
  return translateMessage(current, ar, text, server);
}

/** Translates a plural text: forms separated by `|`, with `{n}` for the count. */
export function tn(key: string, count: number, params?: Params): string {
  return translatePlural(current, ar, key, count, params);
}

/** Locale for dates and numbers: Arabic names for months and days, Latin digits (handled by the callers). */
export function localeTag(): string | undefined {
  return current === 'ar' ? 'ar' : undefined;
}

/**
 * Keeps a phone number, an amount or a code reading left to right inside Arabic text (an "isolate"), so "+961 70 000 001" or
 * "1,250 USD" is never reordered. Nothing changes in English.
 */
export function ltr(text: string): string {
  return current === 'ar' && text ? `\u2066${text}\u2069` : text;
}
