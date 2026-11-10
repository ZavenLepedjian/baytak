import React, { useEffect, useRef, useState } from 'react';
import { Platform, StyleSheet, View } from 'react-native';
import Constants from 'expo-constants';
import MapView, { Marker, Region } from 'react-native-maps';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Button, Text } from '../../components/ui';
import { useLocation } from '../../location/LocationProvider';
import type { ScreenProps } from '../../navigation/types';
import { colors, radius, shadow, spacing } from '../../theme';
import { t } from '../../i18n';

type Point = { latitude: number; longitude: number };

// Beirut: sensible default when neither a saved point nor the device location is available.
const FALLBACK: Point = { latitude: 33.8938, longitude: 35.5018 };

/** Android needs a Google Maps key baked into the build (see app.config.js). Without one a map view would crash the app. */
const MAPS_AVAILABLE = Platform.OS !== 'android' || Constants.expoConfig?.extra?.mapsEnabled === true;

export default function BuildingLocation(props: ScreenProps<'BuildingLocation'>) {
  return MAPS_AVAILABLE ? <MapPicker {...props} /> : <CurrentLocationPicker {...props} />;
}

/** Used when the map is not available: the building's location is the device's current location. */
function CurrentLocationPicker({ navigation }: ScreenProps<'BuildingLocation'>) {
  const insets = useSafeAreaInsets();
  const { coords, refresh } = useLocation();

  const confirm = async () => {
    const here = coords ?? (await refresh());
    if (!here) return;
    navigation.popTo('SetupBuilding', { latitude: here.lat, longitude: here.long }, { merge: true });
  };

  return (
    <View style={[styles.flex, { justifyContent: 'center', padding: spacing.lg, paddingBottom: insets.bottom + spacing.lg }]}>
      <Text variant="title" style={{ marginBottom: spacing.sm }}>{t('Use your current location')}</Text>
      <Text variant="body" color={colors.textMuted} style={{ marginBottom: spacing.xl }}>
        {t('The map is not available in this version. Stand at the building and Baytak will use your current location for it.')}
      </Text>
      <Button title={t('Use my current location')} icon="locate" onPress={confirm} />
    </View>
  );
}

function MapPicker({ navigation, route }: ScreenProps<'BuildingLocation'>) {
  const insets = useSafeAreaInsets();
  const { coords } = useLocation();
  const initial = route.params && route.params.latitude !== undefined && route.params.longitude !== undefined
    ? { latitude: route.params.latitude, longitude: route.params.longitude }
    : null;

  const [point, setPoint] = useState<Point | null>(initial);
  const mapRef = useRef<MapView>(null);
  const centered = useRef(!!initial);

  // Center on the device location once it's known, if nothing was chosen before.
  useEffect(() => {
    if (!centered.current && coords) {
      centered.current = true;
      const here = { latitude: coords.lat, longitude: coords.long };
      setPoint(here);
      mapRef.current?.animateToRegion({ ...here, latitudeDelta: 0.005, longitudeDelta: 0.005 }, 400);
    }
  }, [coords]);

  const start = point ?? (coords ? { latitude: coords.lat, longitude: coords.long } : FALLBACK);
  const region: Region = { ...start, latitudeDelta: point || coords ? 0.005 : 0.2, longitudeDelta: point || coords ? 0.005 : 0.2 };

  const confirm = () => {
    if (!point) return;
    navigation.popTo('SetupBuilding', { latitude: point.latitude, longitude: point.longitude }, { merge: true });
  };

  return (
    <View style={styles.flex}>
      <MapView ref={mapRef} style={styles.flex} initialRegion={region} showsUserLocation onPress={(e) => setPoint(e.nativeEvent.coordinate)}>
        {point ? <Marker draggable coordinate={point} title={t('Building')} onDragEnd={(e) => setPoint(e.nativeEvent.coordinate)} /> : null}
      </MapView>

      <View style={[styles.panel, shadow.md, { marginBottom: insets.bottom + spacing.lg }]}>
        <Text variant="body" color={colors.textMuted} style={{ marginBottom: spacing.md }}>
          {point ? t('Drag the pin or tap the map to adjust the location.') : t('Tap the map to place the building.')}
        </Text>
        <Button title={t('Use this location')} icon="checkmark" disabled={!point} onPress={confirm} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  panel: {
    position: 'absolute',
    left: spacing.lg,
    right: spacing.lg,
    bottom: 0,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
});
