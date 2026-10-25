/**
 * Builds the full request URL. The Azure Functions key goes in the `code` query parameter, so it must be joined with
 * `&` when the path already carries a query string (e.g. `collection/summary?receiptId=5`); joining with a second `?`
 * hides the key inside the previous value and the host answers 401, which the app reports as an expired session.
 */
export function buildUrl(baseUrl: string, path: string, key?: string): string {
  const trimmed = path.replace(/^\//, '');
  const url = `${baseUrl}/${trimmed}`;
  if (key === undefined) return url;
  return `${url}${trimmed.includes('?') ? '&' : '?'}code=${encodeURIComponent(key)}`;
}
