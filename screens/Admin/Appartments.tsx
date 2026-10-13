import React, { useMemo, useState } from 'react';
import { Linking, Pressable, StyleSheet, View } from 'react-native';
import * as Clipboard from 'expo-clipboard';
import { Ionicons } from '@expo/vector-icons';
import { Card, DoorPlate, EmptyState, ErrorState, ListSkeleton, Screen, Text, TextField, chevronForward, showMessage } from '../../components/ui';
import { ordinalFloor } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import type { ScreenProps } from '../../navigation/types';
import { colors, HIT, hitSlop, radius, spacing } from '../../theme';

export type Apartment = {
  id: number | string;
  fullName: string;
  floor: number;
  phoneNumber: string;
  emergencyContactPersonFullName?: string;
  emergencyContactPersonPhoneNumber?: string;
  appartmentNumber: string;
  appartmentDescription?: string;
};

/** The endpoint answers with the string "NoAppartments" when the building has none yet. */
export const selectApartments = (raw: unknown): Apartment[] => (Array.isArray(raw) ? (raw as Apartment[]) : []);

export default function Apartments({ navigation }: ScreenProps<'Apartments'>) {
  const { data, loading, error, refreshing, refetch } = useFetch<Apartment[]>('appartment/approved/get', { select: selectApartments });
  const [query, setQuery] = useState('');

  const list = useMemo(() => {
    const q = query.trim().toLowerCase();
    const all = data ?? [];
    if (!q) return all;
    return all.filter((a) => `${a.appartmentNumber} ${a.fullName} ${a.phoneNumber}`.toLowerCase().includes(q));
  }, [data, query]);

  const call = (phone: string) => {
    Linking.openURL(`tel:${phone}`).catch(() => showMessage({ message: 'Unable to open the phone app', type: 'danger' }));
  };

  const copy = async (a: Apartment) => {
    await Clipboard.setStringAsync(`primary:${a.phoneNumber};emergency:${a.emergencyContactPersonPhoneNumber ?? ''}`);
    showMessage({ message: 'Phone numbers copied', type: 'success' });
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={5} rowHeight={92} />
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
      {(data?.length ?? 0) > 0 ? (
        <TextField icon="search-outline" placeholder="Search by number, name or phone" value={query} onChangeText={setQuery} autoCorrect={false} />
      ) : null}

      <View style={{ gap: spacing.md, marginTop: spacing.lg }}>
        {list.map((a) => (
          <Card key={String(a.id)} flush>
            <View style={{ flexDirection: 'row', alignItems: 'center' }}>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={`Apartment ${a.appartmentNumber}, ${a.fullName || 'unnamed resident'}. Opens payments`}
                accessibilityActions={[{ name: 'copy', label: 'Copy phone numbers' }]}
                onAccessibilityAction={(e) => e.nativeEvent.actionName === 'copy' && void copy(a)}
                onPress={() => navigation.navigate('ApartmentReceipts', { id: a.id, appartmentNumber: `Apartment ${a.appartmentNumber}` })}
                onLongPress={() => void copy(a)}
                style={({ pressed }) => [styles.rowMain, pressed && { backgroundColor: colors.primaryTint }]}
              >
                <DoorPlate number={a.appartmentNumber} size="sm" />
                <View style={{ flex: 1 }}>
                  <Text variant="bodyStrong" numberOfLines={1}>
                    {a.fullName || 'Unnamed resident'}
                  </Text>
                  <Text variant="caption" color={colors.textMuted}>
                    {ordinalFloor(a.floor)}
                  </Text>
                  {a.emergencyContactPersonPhoneNumber ? (
                    <Text variant="caption" color={colors.textMuted} numberOfLines={1}>
                      Emergency: {a.emergencyContactPersonFullName ? `${a.emergencyContactPersonFullName} · ` : ''}
                      {a.emergencyContactPersonPhoneNumber}
                    </Text>
                  ) : null}
                </View>
                <Ionicons name={chevronForward} size={18} color={colors.textFaint} />
              </Pressable>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={`Call ${a.fullName || a.phoneNumber}`}
                hitSlop={hitSlop}
                onPress={() => call(a.phoneNumber)}
                style={styles.callTile}
              >
                <Ionicons name="call-outline" size={20} color={colors.primary} />
              </Pressable>
            </View>
          </Card>
        ))}
      </View>

      {list.length === 0 ? (
        <EmptyState
          icon="business-outline"
          title={query ? 'No matches' : 'No apartments yet'}
          message={query ? 'Try a different search.' : 'Approved apartments will appear here. Pull down to refresh.'}
          actionTitle={query ? undefined : 'Review join requests'}
          onAction={query ? undefined : () => navigation.navigate('PendingApartments')}
        />
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  rowMain: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: spacing.md, padding: spacing.lg, borderRadius: radius.lg },
  callTile: {
    width: HIT + 4,
    height: HIT + 4,
    marginEnd: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
});
