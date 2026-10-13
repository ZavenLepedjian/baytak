import React, { useMemo, useState } from 'react';
import { Pressable, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { getDistance } from 'geolib';
import { Card, EmptyState, ErrorState, ListSkeleton, Screen, Text, TextField, chevronForward } from '../../components/ui';
import { NEARBY_BUILDING_RADIUS_M } from '../../config';
import { useFetch } from '../../hooks/useFetch';
import { useLocation } from '../../location/LocationProvider';
import type { ScreenProps } from '../../navigation/types';
import { colors, radius, spacing } from '../../theme';

type Building = { label: string; value: number | string; latitude?: number | string; longitude?: number | string };

const toNumber = (v: unknown) => {
  const n = Number(v);
  return v === null || v === undefined || v === '' || Number.isNaN(n) ? null : n;
};

export default function Buildings({ navigation }: ScreenProps<'Buildings'>) {
  const { coords } = useLocation();
  const { data, loading, error, refreshing, refetch } = useFetch<Building[]>('buildings/get', {
    select: (raw) => (Array.isArray(raw) ? (raw as Building[]).filter((b) => b && b.label) : []),
  });
  const [nearbyOnly, setNearbyOnly] = useState(true);
  const [query, setQuery] = useState('');

  const canFilter = !!coords;
  const filtering = nearbyOnly && canFilter;

  const buildings = useMemo(() => {
    let list = data ?? [];
    if (filtering && coords) {
      list = list.filter((b) => {
        const lat = toNumber(b.latitude);
        const lon = toNumber(b.longitude);
        if (lat === null || lon === null) return false;
        return getDistance({ latitude: coords.lat, longitude: coords.long }, { latitude: lat, longitude: lon }) < NEARBY_BUILDING_RADIUS_M;
      });
    }
    const q = query.trim().toLowerCase();
    if (q) list = list.filter((b) => b.label.toLowerCase().includes(q));
    return list;
  }, [data, filtering, coords, query]);

  const select = (b: Building) => navigation.popTo('JoinBuilding', { building: { id: b.value, label: b.label } }, { merge: true });

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={5} rowHeight={64} />
      </Screen>
    );
  }
  if (error && !data) {
    return (
      <Screen scroll={false}>
        <ErrorState message={error} onRetry={refetch} />
      </Screen>
    );
  }

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      <TextField icon="search-outline" placeholder="Search buildings" value={query} onChangeText={setQuery} returnKeyType="search" autoCorrect={false} />

      {filtering ? (
        <View style={{ flexDirection: 'row', marginTop: spacing.md }}>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Showing buildings near you. Tap to show all buildings"
            onPress={() => setNearbyOnly(false)}
            style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.xs + 2, backgroundColor: colors.primarySoft, paddingVertical: 6, paddingStart: spacing.md, paddingEnd: spacing.sm, minHeight: 44, borderRadius: radius.md }}
          >
            <Ionicons name="navigate" size={14} color={colors.primaryDeep} />
            <Text variant="label" color={colors.primaryDeep}>
              Near you
            </Text>
            <Ionicons name="close-circle" size={18} color={colors.primaryDeep} />
          </Pressable>
        </View>
      ) : null}

      <View style={{ gap: spacing.sm, marginTop: spacing.lg }}>
        {buildings.map((b) => (
          <Card key={String(b.value)} onPress={() => select(b)} style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.md }}>
            <View style={{ width: 40, height: 40, borderRadius: radius.md - 2, backgroundColor: colors.primarySoft, alignItems: 'center', justifyContent: 'center' }}>
              <Ionicons name="business-outline" size={20} color={colors.primary} />
            </View>
            <Text variant="bodyStrong" style={{ flex: 1 }}>
              {b.label}
            </Text>
            <Ionicons name={chevronForward} size={18} color={colors.textFaint} />
          </Card>
        ))}
      </View>

      {buildings.length === 0 ? (
        <EmptyState
          icon="business-outline"
          title={query ? 'No matches' : filtering ? 'No buildings near you' : 'No buildings yet'}
          message={
            query
              ? 'Try a different name.'
              : filtering
                ? `We could not find a registered building within ${NEARBY_BUILDING_RADIUS_M / 1000} km.`
                : 'Ask your building administrator to register the building first.'
          }
          actionTitle={filtering && !query ? 'Show all buildings' : undefined}
          onAction={filtering && !query ? () => setNearbyOnly(false) : undefined}
        />
      ) : null}
    </Screen>
  );
}
