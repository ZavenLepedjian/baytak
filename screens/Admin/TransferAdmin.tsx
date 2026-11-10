import React, { useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useApi } from '../../api/useApi';
import { useAuth } from '../../auth/AuthProvider';
import { Card, ConfirmDialog, DoorPlate, EmptyState, ErrorState, InfoBanner, ListSkeleton, Screen, Text, TextField, chevronForward, showMessage } from '../../components/ui';
import { ordinalFloor } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import { colors, radius, spacing } from '../../theme';
import { t } from '../../i18n';
import { Apartment, selectApartments } from './Appartments';

/**
 * Hand the building to one of its residents. The choice takes effect at once: the new administrator manages the building
 * from now on and the person handing it over becomes a resident. Only the new administrator can hand it back.
 */
export default function TransferAdmin() {
  const api = useApi();
  const { phoneNumber, refreshSession } = useAuth();
  const { data, loading, error, refreshing, refetch } = useFetch<Apartment[]>('appartment/approved/get', { select: selectApartments });
  const [query, setQuery] = useState('');
  const [choice, setChoice] = useState<Apartment | null>(null);
  const [busy, setBusy] = useState(false);

  const members = useMemo(() => {
    const q = query.trim().toLowerCase();
    // Your own apartment is not a choice: you already manage the building.
    const others = (data ?? []).filter((a) => a.phoneNumber !== phoneNumber);
    if (!q) return others;
    return others.filter((a) => `${a.appartmentNumber} ${a.fullName} ${a.phoneNumber}`.toLowerCase().includes(q));
  }, [data, phoneNumber, query]);

  const transfer = async () => {
    if (!choice) return;
    setBusy(true);
    const res = await api.post<string>('building/transfer-admin', { apartmentId: Number(choice.id) });
    setBusy(false);
    setChoice(null);
    if (!res.ok) return;
    showMessage({ message: typeof res.data === 'string' && res.data ? res.data : t('The administrator role was transferred'), type: 'success' });
    // The role is written into the login token: fetch the new one, and the app turns into a resident's.
    await refreshSession(true);
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={4} rowHeight={80} />
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

  const nobody = (data ?? []).filter((a) => a.phoneNumber !== phoneNumber).length === 0;

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      <InfoBanner icon="swap-horizontal-outline">
        {t("The person you choose becomes the building's administrator at once. You become a resident, and only they can hand the building back.")}
      </InfoBanner>

      {nobody ? (
        <EmptyState icon="people-outline" title={t('No one to choose yet')} message={t('Residents whose apartment you have approved appear here.')} />
      ) : (
        <>
          <TextField icon="search-outline" placeholder={t('Search by number, name or phone')} value={query} onChangeText={setQuery} autoCorrect={false} />
          <View style={{ gap: spacing.md, marginTop: spacing.lg }}>
            {members.map((a) => (
              <Card key={String(a.id)} flush>
                <Pressable
                  accessibilityRole="button"
                  accessibilityLabel={t('Make {name} the administrator', { name: a.fullName || t('Apartment {number}', { number: a.appartmentNumber }) })}
                  onPress={() => setChoice(a)}
                  style={({ pressed }) => [styles.row, pressed && { backgroundColor: colors.primaryTint }]}
                >
                  <DoorPlate number={a.appartmentNumber} size="sm" />
                  <View style={{ flex: 1 }}>
                    <Text variant="bodyStrong" numberOfLines={1}>
                      {a.fullName || t('Unnamed resident')}
                    </Text>
                    <Text variant="caption" color={colors.textMuted}>
                      {ordinalFloor(a.floor)}
                    </Text>
                  </View>
                  <Ionicons name={chevronForward} size={18} color={colors.textFaint} />
                </Pressable>
              </Card>
            ))}
          </View>
          {members.length === 0 ? <EmptyState icon="search-outline" title={t('No matches')} message={t('Try a different search.')} /> : null}
        </>
      )}

      <ConfirmDialog
        visible={choice !== null}
        title={t('Make {name} the administrator?', { name: choice?.fullName || t('this resident') })}
        message={t('{name} in apartment {number} will manage the building from now on: receipts, payments and residents. You will become a resident.', {
          name: choice?.fullName || t('This resident'),
          number: choice?.appartmentNumber ?? '',
        })}
        confirmTitle={t('Transfer')}
        destructive
        loading={busy}
        onCancel={() => setChoice(null)}
        onConfirm={transfer}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, padding: spacing.lg, borderRadius: radius.lg },
});
