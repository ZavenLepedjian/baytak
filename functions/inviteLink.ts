/**
 * The building's invite code and the links that carry it. The administrator shares them (in the building's WhatsApp group);
 * the code only names the building, and every request to join still waits for the administrator's approval.
 * Pure functions, so they can be tested without the app (see inviteLink.test.mjs).
 */

export const INVITE_CODE_LENGTH = 8;

/** Letters and digits that cannot be mistaken for one another on a phone screen (no 0, O, 1, I or L). Same as the server's. */
const ALPHABET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';

/** The code as the server stores it: upper case, without spaces or dashes. Null when it cannot be a code. */
export function normalizeInviteCode(input: string | null | undefined): string | null {
  const code = (input ?? '').replace(/[^a-zA-Z0-9]/g, '').toUpperCase();
  return code.length === INVITE_CODE_LENGTH && [...code].every((c) => ALPHABET.includes(c)) ? code : null;
}

/** "ABCD2345" as it is read aloud and typed: "ABCD-2345". */
export const formatInviteCode = (code: string) => `${code.slice(0, 4)}-${code.slice(4)}`;

/** The page of the site that opens the app (or the store) for a code. It is a web link because WhatsApp only makes web links tappable. */
export const inviteLink = (siteUrl: string, code: string) => `${siteUrl.replace(/\/+$/, '')}/join/?code=${code}`;

/** The link that opens the installed app directly. */
export const inviteAppLink = (code: string) => `baytak://join?code=${code}`;

/**
 * The code in a link the app was opened with: baytak://join?code=ABCD2345, baytak://join/ABCD2345, or the site's page
 * (https://.../join/?code=ABCD2345). Null for any other link, or a code that cannot be right.
 */
export function inviteCodeFromUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  const match = /^(?:baytak:\/\/|https?:\/\/[^/?#]+\/)join(?:\/([^/?#]+))?\/?(?:\?([^#]*))?(?:#.*)?$/i.exec(url.trim());
  if (!match) return null;
  const [, fromPath, query = ''] = match;
  try {
    const fromQuery = query.split('&').map((pair) => pair.split('=')).find(([key]) => key === 'code')?.[1];
    return normalizeInviteCode(decodeURIComponent(fromQuery ?? fromPath ?? ''));
  } catch {
    return null; // a malformed % sequence
  }
}

export type InviteMessageParts = {
  /** "You're invited to join Tower on Baytak". */
  headline: string;
  linkLabel: string;
  link: string;
  codeLabel: string;
  code: string;
  /** "Valid for 8 hours, until 18:30." */
  validity: string;
  /** What happens next. */
  footer: string;
};

/**
 * The WhatsApp message. The link sits on a line of its own so it stays tappable, and the code is repeated in words for
 * anyone who has to type it.
 */
export const inviteMessage = (p: InviteMessageParts) =>
  [p.headline, '', p.linkLabel, p.link, '', `${p.codeLabel} ${formatInviteCode(p.code)}`, p.validity, '', p.footer].join('\n');

/** How long a code still works, from its expiry (an ISO time) and the current time in milliseconds. */
export function timeLeft(expiresAt: string, now: number): { hours: number; minutes: number; expired: boolean } {
  const ms = Date.parse(expiresAt) - now;
  if (!Number.isFinite(ms) || ms <= 0) return { hours: 0, minutes: 0, expired: true };
  const totalMinutes = Math.ceil(ms / 60000);
  return { hours: Math.floor(totalMinutes / 60), minutes: totalMinutes % 60, expired: false };
}
