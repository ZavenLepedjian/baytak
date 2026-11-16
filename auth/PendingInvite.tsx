import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { Linking } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import { inviteCodeFromUrl, normalizeInviteCode } from '../functions/inviteLink';

/**
 * The building invite code the app was opened with (from the administrator's WhatsApp message). It waits here while the
 * person signs in, even across a restart, until the join screen has used it. A code kept for more than a day is dropped:
 * the server stops accepting it after 8 hours anyway.
 */
const KEY = 'pendingInviteCode';
const KEEP_MS = 24 * 3600 * 1000;

type PendingInvite = { code: string | null; clear: () => void };

const Context = createContext<PendingInvite>({ code: null, clear: () => {} });

export const usePendingInvite = () => useContext(Context);

export function PendingInviteProvider({ children }: { children: React.ReactNode }) {
  const [code, setCode] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    const take = (url: string | null) => {
      const found = inviteCodeFromUrl(url);
      if (!found || !alive) return;
      setCode(found);
      SecureStore.setItemAsync(KEY, `${found}:${Date.now()}`).catch(() => {});
    };

    // Opened by the link, or already open when it was tapped.
    Linking.getInitialURL().then(take).catch(() => {});
    const subscription = Linking.addEventListener('url', ({ url }) => take(url));

    // Opened earlier and not used yet (the person had to sign in first).
    SecureStore.getItemAsync(KEY)
      .then((saved) => {
        if (!saved || !alive) return;
        const [savedCode, at] = saved.split(':');
        const fresh = Date.now() - Number(at) < KEEP_MS;
        if (fresh && normalizeInviteCode(savedCode)) setCode((current) => current ?? savedCode);
        else SecureStore.deleteItemAsync(KEY).catch(() => {});
      })
      .catch(() => {});

    return () => {
      alive = false;
      subscription.remove();
    };
  }, []);

  const clear = useCallback(() => {
    setCode(null);
    SecureStore.deleteItemAsync(KEY).catch(() => {});
  }, []);

  const value = useMemo(() => ({ code, clear }), [code, clear]);
  return <Context.Provider value={value}>{children}</Context.Provider>;
}
