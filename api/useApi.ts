import { useMemo } from 'react';
import { apiRequest, ApiResult } from './client';
import { useAuth } from '../auth/AuthProvider';
import { showMessage } from '../components/ui/Toast';
import { t } from '../i18n';

type CallOptions = {
  /** Skip the error toast; the caller renders its own error state. */
  silent?: boolean;
  signal?: AbortSignal;
  /** For binary bodies (photos). */
  contentType?: string;
  timeoutMs?: number;
};

/**
 * Authenticated API access for screens. Adds the bearer token, signs the user out when
 * the server rejects it, and surfaces errors as toasts (unless `silent`).
 */
export function useApi() {
  const { token, signOut } = useAuth();

  return useMemo(() => {
    async function call<T>(
      path: string,
      method: 'GET' | 'POST',
      body: unknown,
      opts: CallOptions = {},
    ): Promise<ApiResult<T>> {
      const res = await apiRequest<T>(path, { method, body, token, signal: opts.signal, contentType: opts.contentType, timeoutMs: opts.timeoutMs });
      if (!res.ok && !res.aborted) {
        if (res.unauthorized && token) {
          showMessage({ message: t('Your session has expired. Please sign in again.'), type: 'warning' });
          void signOut({ remote: false });
        } else if (!opts.silent) {
          showMessage({ message: res.error, type: 'danger' });
        }
      }
      return res;
    }

    return {
      get: <T>(path: string, opts?: CallOptions) => call<T>(path, 'GET', undefined, opts),
      post: <T>(path: string, body?: unknown, opts?: CallOptions) => call<T>(path, 'POST', body ?? {}, opts),
      /** Sends the bytes as they are (a photo), with a longer timeout than a JSON call. */
      upload: <T>(path: string, body: Blob, contentType: string, opts?: CallOptions) =>
        call<T>(path, 'POST', body, { timeoutMs: 60000, ...opts, contentType }),
    };
  }, [token, signOut]);
}

export type Api = ReturnType<typeof useApi>;
