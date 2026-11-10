/**
 * Translation logic with no React Native in it, so it can be tested on its own.
 *
 * English is the key: `t('Sign in')` returns "Sign in" in English and looks the same text up in the Arabic dictionary otherwise.
 * A text with no Arabic entry shows in English rather than breaking. Placeholders are `{name}`. Plural texts put their forms in
 * one string separated by `|`: English `"{n} apartment|{n} apartments"` (one|other), Arabic six forms (zero|one|two|few|many|other).
 */
export type Language = 'en' | 'ar';
export type Dictionary = Record<string, string>;
export type Params = Record<string, string | number>;

export function interpolate(text: string, params?: Params): string {
  if (!params) return text;
  return text.replace(/\{(\w+)\}/g, (whole, name: string) => (name in params ? String(params[name]) : whole));
}

/** Which plural form a count takes: English has two, Arabic six. */
export function pluralIndex(language: Language, count: number): number {
  const n = Math.abs(count);
  if (language === 'ar') {
    const last2 = n % 100;
    if (n === 0) return 0;
    if (n === 1) return 1;
    if (n === 2) return 2;
    if (last2 >= 3 && last2 <= 10) return 3;
    if (last2 >= 11 && last2 <= 99) return 4;
    return 5;
  }
  return n === 1 ? 0 : 1;
}

export function translate(language: Language, dictionary: Dictionary, key: string, params?: Params): string {
  const text = language === 'ar' ? (dictionary[key] ?? key) : key;
  return interpolate(text, params);
}

export function translatePlural(language: Language, dictionary: Dictionary, key: string, count: number, params?: Params): string {
  const translated = language === 'ar' ? dictionary[key] : undefined;
  const forms = (translated ?? key).split('|');
  const index = pluralIndex(translated ? 'ar' : 'en', count);
  return interpolate(forms[Math.min(index, forms.length - 1)], { n: count, ...params });
}

/** Arabic-Indic digits to the Latin digits the app uses for every amount, so money always reads the same. */
export function latinDigits(text: string): string {
  return text.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660)).replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0));
}

type Pattern = { regex: RegExp; names: string[]; arabic: string };
const patterns = new WeakMap<Dictionary, Pattern[]>();

function patternsOf(dictionary: Dictionary): Pattern[] {
  let list = patterns.get(dictionary);
  if (!list) {
    list = [];
    for (const [english, arabic] of Object.entries(dictionary)) {
      if (!/\{\w+\}/.test(english) || english.includes('|')) continue;
      const names: string[] = [];
      const source = english
        .split(/(\{\w+\})/)
        .map((part) => {
          const name = /^\{(\w+)\}$/.exec(part)?.[1];
          if (!name) return part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
          names.push(name);
          return '(.+?)';
        })
        .join('');
      list.push({ regex: new RegExp(`^${source}$`), names, arabic });
    }
    patterns.set(dictionary, list);
  }
  return list;
}

/**
 * Translates a message the server wrote in English. An exact entry wins; otherwise an entry with placeholders is matched
 * against the text ("Template names can have at most {max} characters" matches "...at most 60 characters"), so messages
 * with a number in them translate too. Anything unknown stays as the server wrote it.
 * Patterns come from `patternSource` (the server's own list), so an app text can never be mistaken for a server message.
 */
export function translateMessage(language: Language, dictionary: Dictionary, text: string, patternSource: Dictionary = dictionary): string {
  if (language !== 'ar') return text;
  const exact = dictionary[text];
  if (exact !== undefined) return exact;
  for (const { regex, names, arabic } of patternsOf(patternSource)) {
    const match = regex.exec(text);
    if (match) return interpolate(arabic, Object.fromEntries(names.map((name, i) => [name, match[i + 1]])));
  }
  return text;
}
