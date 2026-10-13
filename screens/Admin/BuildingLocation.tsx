import React, { useEffect, useRef, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import MapView, { Marker, Region } from 'react-native-maps';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Button, Text } from '../../components/ui';
import { useLocation } from '../../location/LocationProvider';
import type { ScreenProps } from '../../navigation/types';
import { colors, radius, shadow, spacing } from '../../theme';

type Point = { latitude: number; longitude: number };

// Beirut: sensible default when neither a saved point nor the device location is available.
const FALLBACK: Point = { latitude: 33.8938, longitude: 35.5018 };

export default function BuildingLocation({ navigation, route }: ScreenProps<'BuildingLocation'>) {
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
        {point ? <Marker draggable coordinate={point} title="Building" onDragEnd={(e) => setPoint(e.nativeEvent.coordinate)} /> : null}
      </MapView>

      <View style={[styles.panel, shadow.md, { marginBottom: insets.bottom + spacing.lg }]}>
        <Text variant="body" color={colors.textMuted} style={{ marginBottom: spacing.md }}>
          {point ? 'Drag the pin or tap the map to adjust the location.' : 'Tap the map to place the building.'}
        </Text>
        <Button title="Use this location" icon="checkmark" disabled={!point} onPress={confirm} />
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
