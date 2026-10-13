import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import * as SecureStore from 'expo-secure-store';
import { apiRequest } from '../api/client';
import { getPhoneNumber, getRoles, isAdmin, isTokenExpired } from './jwt';

// Storage keys are unchanged so existing installs stay signed in after the upgrade.
const TOKEN_KEY = 'userToken';
const NAME_KEY = 'fullName';
const STATUS_KEY = 'userStatus';

export type AuthStatus = 'loading' | 'signedOut' | 'signedIn';

type AuthState = {
  status: AuthStatus;
  token: string | null;
  roles: string;
  phoneNumber: string;
  /** Administrator approval state: 'Approved' | 'Pending' | ... | null when unknown. */
  userStatus: string | null;
  fullName: string;
};

type AuthContextValue = AuthState & {
  isAdmin: boolean;
  /** Admin accounts must be approved before they can use the app. */
  needsAdminApproval: boolean;
  signIn: (token: string, userStatus?: string | null) => Promise<void>;
  /**
   * Ends the session. By default it also revokes the session on the server (all tokens issued so far stop
   * working and push notifications stop). Pass { remote: false } when the server already rejected the token.
   */
  signOut: (options?: { remote?: boolean }) => Promise<void>;
  /** Returns the fresh status, or null when it could not be fetched. */
  refreshUserStatus: () => Promise<string | null>;
  setFullName: (name: string) => void;
};

const initialState: AuthState = {
  status: 'loading',
  token: null,
  roles: '',
  phoneNumber: '',
  userStatus: null,
  fullName: '',
};

const AuthContext = createContext<AuthContextValue | null>(null);

async function safeGet(key: string): Promise<string | null> {
  try {
    return await SecureStore.getItemAsync(key, STORE_OPTIONS);
  } catch {
    return null;
  }
}

// The session token never leaves this device (no iCloud Keychain sync / device-to-device migration)
// and is readable only while the device is unlocked.
const STORE_OPTIONS: SecureStore.SecureStoreOptions = { keychainAccessible: SecureStore.WHEN_UNLOCKED_THIS_DEVICE_ONLY };

async function safeSet(key: string, value: string | null | undefined) {
  try {
    if (value) await SecureStore.setItemAsync(key, value, STORE_OPTIONS);
    else await SecureStore.deleteItemAsync(key);
  } catch {
    // Secure storage can fail on some devices; the in-memory session still works.
  }
}

/** Names saved by older versions may contain "null": treat those as empty. */
const cleanName = (name: string | null) => (name && !name.includes('null') ? name.trim() : '');

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<AuthState>(initialState);
  const tokenRef = useRef<string | null>(null);
  tokenRef.current = state.token;

  const signOut = useCallback(async (options?: { remote?: boolean }) => {
    const token = tokenRef.current;
    tokenRef.current = null;
    if (token && options?.remote !== false) {
      // Best effort: the local session is cleared even if the server can't be reached.
      void apiRequest('user/logout', { method: 'POST', token, body: {}, timeoutMs: 5000 });
    }
    setState({ ...initialState, status: 'signedOut' });
    await Promise.all([safeSet(TOKEN_KEY, null), safeSet(STATUS_KEY, null), safeSet(NAME_KEY, null)]);
  }, []);

  /** Returns the status string, null when unavailable (offline), or throws when the token is rejected. */
  const fetchUserStatus = useCallback(async (token: string): Promise<string | null> => {
    const res = await apiRequest<{ userStatus?: string }>('user/status', { token, timeoutMs: 10000 });
    if (res.ok) return res.data?.userStatus ?? null;
    if (res.unauthorized) throw new Error('unauthorized');
    return null;
  }, []);

  // Restore the session once on launch.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const token = await safeGet(TOKEN_KEY);
      if (cancelled) return;
      if (!token || isTokenExpired(token)) {
        if (token) await Promise.all([safeSet(TOKEN_KEY, null), safeSet(STATUS_KEY, null)]);
        if (!cancelled) setState({ ...initialState, status: 'signedOut' });
        return;
      }

      const roles = getRoles(token);
      const storedName = cleanName(await safeGet(NAME_KEY));
      let userStatus = await safeGet(STATUS_KEY);

      if (isAdmin(roles)) {
        try {
          const fresh = await fetchUserStatus(token);
          if (fresh) {
            userStatus = fresh;
            void safeSet(STATUS_KEY, fresh);
          }
        } catch {
          if (!cancelled) await signOut({ remote: false });
          return;
        }
      }
      if (cancelled) return;
      setState({
        status: 'signedIn',
        token,
        roles,
        phoneNumber: getPhoneNumber(token),
        userStatus,
        fullName: storedName,
      });
    })();
    return () => {
      cancelled = true;
    };
  }, [fetchUserStatus, signOut]);

  const signIn = useCallback(async (token: string, userStatus?: string | null) => {
    await Promise.all([safeSet(TOKEN_KEY, token), safeSet(STATUS_KEY, userStatus ?? null)]);
    const storedName = cleanName(await safeGet(NAME_KEY));
    setState({
      status: 'signedIn',
      token,
      roles: getRoles(token),
      phoneNumber: getPhoneNumber(token),
      userStatus: userStatus ?? null,
      fullName: storedName,
    });
  }, []);

  const refreshUserStatus = useCallback(async (): Promise<string | null> => {
    const token = tokenRef.current;
    if (!token) return null;
    try {
      const fresh = await fetchUserStatus(token);
      if (!fresh) return null;
      await safeSet(STATUS_KEY, fresh);
      setState((s) => ({ ...s, userStatus: fresh }));
      return fresh;
    } catch {
      await signOut({ remote: false });
      return null;
    }
  }, [fetchUserStatus, signOut]);

  const setFullName = useCallback((name: string) => {
    const clean = cleanName(name);
    void safeSet(NAME_KEY, clean);
    setState((s) => (s.fullName === clean ? s : { ...s, fullName: clean }));
  }, []);

  const value = useMemo<AuthContextValue>(() => {
    const admin = isAdmin(state.roles);
    return {
      ...state,
      isAdmin: admin,
      needsAdminApproval: state.status === 'signedIn' && admin && state.userStatus !== 'Approved',
      signIn,
      signOut,
      refreshUserStatus,
      setFullName,
    };
  }, [state, signIn, signOut, refreshUserStatus, setFullName]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>');
  return ctx;
}
