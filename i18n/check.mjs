// Checks that every text passed to t() / tn() has an Arabic translation, with the same placeholders and the right number of plural forms.
// The messages the server writes in English (errors and confirmations) are translated as they arrive, so they need entries too.
//   npm run i18n:check        (Node 22.6+ reads the TypeScript dictionary itself)
// Exit code 1 when a text is missing or inconsistent; unused entries are only reported.
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ar } from './ar/all.ts';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const skip = new Set(['node_modules', '.git', 'android', 'ios', 'backend', 'website', 'marketing', 'play-store', 'chatgpt-plugin', 'assets', 'i18n']);

function* files(dir) {
  for (const name of readdirSync(dir)) {
    if (skip.has(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* files(path);
    else if (/\.(ts|tsx)$/.test(name) && !/\.d\.ts$/.test(name)) yield path;
  }
}

const call = /\b(?:t|tn)\(\s*(['"`])((?:\\.|(?!\1)[^\\])*)\1/g;
const unescape = (s) => s.replace(/\\(['"`\\])/g, '$1').replace(/\\n/g, '\n');
const placeholders = (s) => [...new Set([...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]))].sort().join(',');
/** Every {...} counts as one placeholder, so a C# interpolation ($"... {f.Name} ...") lines up with {name} in the dictionary. */
const shape = (s) => s.replace(/\{[^}]*\}/g, '{}');

const used = new Map(); // text -> first place
for (const file of files(root)) {
  const source = readFileSync(file, 'utf8');
  for (const m of source.matchAll(call)) {
    if (m[1] === '`' && m[2].includes('${')) continue; // built at run time: cannot be checked
    const text = unescape(m[2]);
    if (!used.has(text)) used.set(text, relative(root, file));
  }
}

const server = new Map(); // shape -> { text, place }
const serverRoot = join(root, 'backend', 'src', 'Baytak.Api');
const serverCall = /(?:BadRequest|NotFound|Conflict|Forbidden|Message|Fail)\(\s*(?:\w+ \?\? )?(\$?)"((?:\\.|[^"\\])*)"|\(null, (\$?)"((?:\\.|[^"\\])*)"\)/g;
function* csharp(dir) {
  for (const name of readdirSync(dir)) {
    if (['bin', 'obj', 'Migrations'].includes(name)) continue;
    const path = join(dir, name);
    if (statSync(path).isDirectory()) yield* csharp(path);
    else if (name.endsWith('.cs')) yield path;
  }
}
for (const file of csharp(serverRoot)) {
  for (const m of readFileSync(file, 'utf8').matchAll(serverCall)) {
    const text = (m[2] ?? m[4]).replace(/\\"/g, '"').replace(/\\n/g, '\n');
    if (!/\s/.test(text)) continue; // a code word the app understands ("SetupBuilding"), not a message
    if (!server.has(shape(text))) server.set(shape(text), { text, place: relative(root, file) });
  }
}
const arabicByShape = new Map(Object.entries(ar).map(([k, v]) => [shape(k), v]));

const problems = [];
for (const [key, { text, place }] of server) {
  if (!arabicByShape.has(key)) problems.push(`missing Arabic (server message)  ${JSON.stringify(text)}  (${place})`);
}
for (const [text, place] of used) {
  const arabic = ar[text];
  if (arabic === undefined) { problems.push(`missing Arabic  ${JSON.stringify(text)}  (${place})`); continue; }
  if (placeholders(text) !== placeholders(arabic)) problems.push(`placeholders differ  ${JSON.stringify(text)}`);
  const plural = text.includes('|');
  if (plural && arabic.split('|').length !== 6) problems.push(`needs 6 plural forms  ${JSON.stringify(text)}`);
  if (!plural && arabic.includes('|')) problems.push(`has plural forms but the English text has none  ${JSON.stringify(text)}`);
}
const unused = Object.keys(ar).filter((k) => !used.has(k) && !server.has(shape(k)));

console.log(`${used.size} texts used, ${server.size} server messages, ${Object.keys(ar).length} Arabic entries.`);
if (unused.length) {
  console.log(`${unused.length} Arabic entries are not used by any t() call or server message:`);
  for (const k of unused) console.log(`  unused  ${JSON.stringify(k)}`);
}
for (const p of problems) console.log(p);
if (problems.length) { console.log(`${problems.length} problem(s).`); process.exit(1); }
console.log('Translations are complete.');
