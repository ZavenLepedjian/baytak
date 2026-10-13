import React, { useEffect, useState } from 'react';
import { Pressable, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { Button, DoorPlate, EmptyState, ErrorState, ListSkeleton, Screen, Text } from '../../components/ui';
import { ordinalFloor } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import type { ApartmentChoice, ScreenProps } from '../../navigation/types';
import { colors, radius, spacing } from '../../theme';
import { Apartment, selectApartments } from './Appartments';

/** Pick the apartments a custom receipt applies to. The selection returns to the receipt screen on "Done". */
export default function SelectApartments({ navigation, route }: ScreenProps<'SelectApartments'>) {
  const { data, loading, error, refreshing, refetch } = useFetch<Apartment[]>('appartment/approved/get', { select: selectApartments });
  const [selected, setSelected] = useState<Map<string, ApartmentChoice>>(
    () => new Map(route.params.selected.map((a) => [String(a.id), a])),
  );

  useEffect(() => {
    navigation.setOptions({ title: selected.size ? `Select apartments (${selected.size})` : 'Select apartments' });
  }, [navigation, selected.size]);

  const toggle = (a: Apartment) =>
    setSelected((prev) => {
      const next = new Map(prev);
      const key = String(a.id);
      if (next.has(key)) next.delete(key);
      else next.set(key, { id: a.id, appartmentNumber: a.appartmentNumber });
      return next;
    });

  const done = () => navigation.popTo('CreateReceipt', { apartments: Array.from(selected.values()) }, { merge: true });

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={5} rowHeight={72} />
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

  const list = data ?? [];

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      <Text variant="body" color={colors.textMuted} style={{ marginBottom: spacing.lg }}>
        Choose the apartments this receipt is for. Leave everything unselected to issue it to the whole building.
      </Text>

      {list.length === 0 ? (
        <EmptyState icon="business-outline" title="No apartments yet" message="Approved apartments will be listed here." />
      ) : (
        <View style={{ gap: spacing.sm }}>
          {list.map((a) => {
            const on = selected.has(String(a.id));
            return (
              <Pressable
                key={String(a.id)}
                accessibilityRole="checkbox"
                accessibilityState={{ checked: on }}
                accessibilityLabel={`Apartment ${a.appartmentNumber}, ${a.fullName}`}
                onPress={() => toggle(a)}
                style={{
                  flexDirection: 'row',
                  alignItems: 'center',
                  gap: spacing.md,
                  padding: spacing.md + 2,
                  borderRadius: radius.lg,
                  backgroundColor: on ? colors.primaryTint : colors.surface,
                  borderWidth: 1.5,
                  borderColor: on ? colors.primary : 'transparent',
                }}
              >
                <DoorPlate number={a.appartmentNumber} size="sm" />
                <View style={{ flex: 1 }}>
                  <Text variant="bodyStrong">{a.fullName || 'Unnamed resident'}</Text>
                  <Text variant="caption" color={colors.textMuted}>
                    {ordinalFloor(a.floor)}
                  </Text>
                </View>
                <Ionicons name={on ? 'checkbox' : 'square-outline'} size={26} color={on ? colors.primary : colors.borderInput} />
              </Pressable>
            );
          })}
        </View>
      )}

      <Button title="Done" onPress={done} style={{ marginTop: spacing.xl }} />
    </Screen>
  );
}
