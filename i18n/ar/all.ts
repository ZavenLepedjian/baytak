import type { Dictionary } from '../core.ts';
import { admin } from './admin.ts';
import { common } from './common.ts';
import { summary } from './summary.ts';
import { server } from './server.ts';
import { transfer } from './transfer.ts';

/**
 * The Arabic dictionary: English text -> Arabic. English is the key (see core.ts). Split by area so no file grows too long;
 * `npm run i18n:check` compares it with every t() / tn() call and with the messages the server writes.
 */
export const ar: Dictionary = { ...common, ...admin, ...summary, ...transfer, ...server };

/** Only the server's messages; the app matches the server's text with numbers filled in against these. */
export { server };
