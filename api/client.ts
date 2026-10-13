import { API_BASE_URL, API_KEY, REQUEST_TIMEOUT_MS } from '../config';

export type ApiSuccess<T> = { ok: true; status: number; data: T };
export type ApiFailure = {
  ok: false;
  status: number;
  error: string;
  /** The server rejected the credentials (HTTP 401). */
  unauthorized: boolean;
  /** The caller cancelled the request (e.g. the screen unmounted). Callers should ignore it. */
  aborted: boolean;
};
export type ApiResult<T> = ApiSuccess<T> | ApiFailure;

export type RequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  body?: unknown;
  token?: string | null;
  signal?: AbortSignal;
  /** Append the Azure Functions key. Anonymous auth endpoints are called without it. */
  withKey?: boolean;
  timeoutMs?: number;
};

const NETWORK_ERROR = 'Unable to reach the server. Check your connection and try again.';
const TIMEOUT_ERROR = 'The request took too long. Please try again.';

/** The API replies with JSON strings or plain text; normalise both into a readable message. */
function toMessage(raw: string, fallback: string): string {
  const text = raw.trim();
  if (!text) return fallback;
  try {
    const parsed = JSON.parse(text);
    if (typeof parsed === 'string') return parsed || fallback;
    if (parsed && typeof parsed === 'object') {
      const candidate = (parsed as Record<string, unknown>).message ?? (parsed as Record<string, unknown>).title;
      if (typeof candidate === 'string' && candidate) return candidate;
    }
    return fallback;
  } catch {
    return text.length > 300 ? fallback : text;
  }
}

function fail(status: number, error: string, extra?: Partial<ApiFailure>): ApiFailure {
  return { ok: false, status, error, unauthorized: status === 401, aborted: false, ...extra };
}

export async function apiRequest<T = unknown>(path: string, options: RequestOptions = {}): Promise<ApiResult<T>> {
  const { method = 'GET', body, token, signal, withKey = true, timeoutMs = REQUEST_TIMEOUT_MS } = options;

  const url = `${API_BASE_URL}/${path.replace(/^\//, '')}${withKey ? `?code=${encodeURIComponent(API_KEY)}` : ''}`;

  const controller = new AbortController();
  let timedOut = false;
  const timer = setTimeout(() => {
    timedOut = true;
    controller.abort();
  }, timeoutMs);
  const onExternalAbort = () => controller.abort();
  if (signal) {
    if (signal.aborted) controller.abort();
    else signal.addEventListener('abort', onExternalAbort);
  }

  try {
    const headers: Record<string, string> = { Accept: 'application/json', 'Content-Type': 'application/json' };
    if (token) headers.Authorization = `Bearer ${token}`;

    const res = await fetch(url, {
      method,
      headers,
      body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body),
      signal: controller.signal,
    });

    const text = await res.text();

    if (!res.ok) {
      const fallback = res.status === 401 ? 'Unauthorized' : `Something went wrong (${res.status}). Please try again.`;
      return fail(res.status, toMessage(text, fallback));
    }

    if (!text) return { ok: true, status: res.status, data: null as T };
    try {
      return { ok: true, status: res.status, data: JSON.parse(text) as T };
    } catch {
      // Successful reply that is not JSON (plain text message).
      return { ok: true, status: res.status, data: text as unknown as T };
    }
  } catch (e) {
    if (controller.signal.aborted) {
      if (timedOut) return fail(0, TIMEOUT_ERROR);
      return fail(0, 'Request cancelled', { aborted: true });
    }
    return fail(0, NETWORK_ERROR);
  } finally {
    clearTimeout(timer);
    signal?.removeEventListener('abort', onExternalAbort);
  }
}
