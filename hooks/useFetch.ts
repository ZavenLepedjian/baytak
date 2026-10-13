import { useCallback, useEffect, useRef, useState } from 'react';
import { useFocusEffect } from '@react-navigation/native';
import { useApi } from '../api/useApi';

type Options<T> = {
  /** Skip fetching until true (e.g. waiting for a route param). */
  enabled?: boolean;
  /** Reload every time the screen regains focus. */
  refetchOnFocus?: boolean;
  /** Post-process the server response. May throw to signal an unreadable payload. */
  select?: (raw: unknown) => T;
};

/**
 * Loads a GET endpoint with race-safe loading / refreshing / error state.
 * - `loading`: first load with nothing to show yet
 * - `refreshing`: pull-to-refresh or background reload
 */
export function useFetch<T = unknown>(path: string, options: Options<T> = {}) {
  const { enabled = true, refetchOnFocus = false, select } = options;
  const api = useApi();
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(enabled);
  const [refreshing, setRefreshing] = useState(false);

  const requestId = useRef(0);
  const mounted = useRef(true);
  const hasData = useRef(false);
  const selectRef = useRef(select);
  selectRef.current = select;

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      requestId.current++;
    };
  }, []);

  const load = useCallback(
    async (mode: 'initial' | 'refresh' = 'initial') => {
      const id = ++requestId.current;
      if (mode === 'refresh' || hasData.current) setRefreshing(true);
      else setLoading(true);

      const res = await api.get<unknown>(path, { silent: true });
      if (!mounted.current || id !== requestId.current) return;

      if (res.ok) {
        try {
          setData(selectRef.current ? selectRef.current(res.data) : (res.data as T));
          hasData.current = true;
          setError(null);
        } catch {
          setError('We could not read the server response. Please try again.');
        }
      } else if (!res.aborted && !res.unauthorized) {
        setError(res.error);
      }
      setLoading(false);
      setRefreshing(false);
    },
    [api, path],
  );

  useEffect(() => {
    if (enabled && !refetchOnFocus) void load('initial');
  }, [enabled, refetchOnFocus, load]);

  useFocusEffect(
    useCallback(() => {
      if (enabled && refetchOnFocus) void load('initial');
    }, [enabled, refetchOnFocus, load]),
  );

  const refetch = useCallback(() => load('refresh'), [load]);

  return { data, setData, error, loading, refreshing, refetch };
}
