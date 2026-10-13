import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import * as Location from 'expo-location';

type Coords = { lat: number; long: number };

type LocationContextValue = {
  coords: Coords | null;
  permission: 'unknown' | 'granted' | 'denied';
  /** Asks for permission if needed and reads the current position. Never throws. */
  refresh: () => Promise<Coords | null>;
};

const LocationContext = createContext<LocationContextValue>({
  coords: null,
  permission: 'unknown',
  refresh: async () => null,
});

export function LocationProvider({ children }: { children: React.ReactNode }) {
  const [coords, setCoords] = useState<Coords | null>(null);
  const [permission, setPermission] = useState<LocationContextValue['permission']>('unknown');
  const inFlight = useRef<Promise<Coords | null> | null>(null);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const refresh = useCallback(() => {
    if (inFlight.current) return inFlight.current;
    const run = (async (): Promise<Coords | null> => {
      try {
        let { status } = await Location.getForegroundPermissionsAsync();
        if (status !== 'granted') {
          ({ status } = await Location.requestForegroundPermissionsAsync());
        }
        if (status !== 'granted') {
          if (mounted.current) setPermission('denied');
          return null;
        }
        if (mounted.current) setPermission('granted');

        const position =
          (await Location.getLastKnownPositionAsync()) ??
          (await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced }));
        if (!position) return null;
        const next = { lat: position.coords.latitude, long: position.coords.longitude };
        if (mounted.current) setCoords(next);
        return next;
      } catch {
        // Location services off, timeout, etc. The app works without it.
        return null;
      } finally {
        inFlight.current = null;
      }
    })();
    inFlight.current = run;
    return run;
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo(() => ({ coords, permission, refresh }), [coords, permission, refresh]);
  return <LocationContext.Provider value={value}>{children}</LocationContext.Provider>;
}

export const useLocation = () => useContext(LocationContext);
