import React, { useState } from 'react';
import { View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useApi } from '../../api/useApi';
import { Button, Card, ConfirmDialog, DoorPlate, EmptyState, ErrorState, ListSkeleton, Screen, Text, showMessage } from '../../components/ui';
import { ordinalFloor } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import { colors, spacing } from '../../theme';
import { Apartment, selectApartments } from './Appartments';

/** Pending join requests for the administrator's building. */
export default function PendingApartments() {
  const api = useApi();
  const { data, loading, error, refreshing, refetch } = useFetch<Apartment[]>('appartment/pending/get', { select: selectApartments });
  const [busyId, setBusyId] = useState<string | null>(null);
  const [rejecting, setRejecting] = useState<Apartment | null>(null);

  const decide = async (a: Apartment, action: 'approve' | 'reject') => {
    if (busyId) return;
    setBusyId(String(a.id));
    const res = await api.post<string>(`appartment/${action}`, { id: a.id });
    setBusyId(null);
    if (res.ok) {
      showMessage({
        message: action === 'approve' ? 'Apartment added to the building' : 'Request rejected',
        type: 'success',
      });
      void refetch();
    }
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={3} rowHeight={150} />
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
      {list.length === 0 ? (
        <EmptyState icon="checkmark-done-outline" title="All caught up" message="There are no pending join requests. Pull down to refresh." />
      ) : (
        <View style={{ gap: spacing.md }}>
          {list.map((a) => {
            const busy = busyId === String(a.id);
            return (
              <Card key={String(a.id)}>
                <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.md }}>
                  <DoorPlate number={a.appartmentNumber} size="sm" />
                  <View style={{ flex: 1 }}>
                    <Text variant="bodyStrong">{a.fullName || 'New resident'}</Text>
                    <Text variant="caption" color={colors.textMuted}>
                      {ordinalFloor(a.floor)}
                    </Text>
                  </View>
                </View>

                <View style={{ gap: spacing.xs + 2, marginTop: spacing.md }}>
                  <InfoLine icon="call-outline" text={a.phoneNumber} />
                  {a.emergencyContactPersonPhoneNumber ? <InfoLine icon="medkit-outline" text={`Emergency: ${a.emergencyContactPersonPhoneNumber}`} /> : null}
                  {a.appartmentDescription ? <InfoLine icon="document-text-outline" text={a.appartmentDescription} /> : null}
                </View>

                <View style={{ flexDirection: 'row', gap: spacing.md, marginTop: spacing.lg }}>
                  <Button title="Reject" variant="danger" small disabled={!!busyId && !busy} loading={busy} onPress={() => setRejecting(a)} style={{ flex: 1 }} />
                  <Button title="Approve" small disabled={!!busyId && !busy} loading={busy} onPress={() => decide(a, 'approve')} style={{ flex: 1 }} />
                </View>
              </Card>
            );
          })}
        </View>
      )}

      <ConfirmDialog
        visible={!!rejecting}
        title={rejecting ? `Reject apartment ${rejecting.appartmentNumber}?` : 'Reject request?'}
        message={`${rejecting?.fullName || 'This resident'} will be told the request was not approved. They can correct the details and send a new one.`}
        confirmTitle="Reject"
        destructive
        loading={!!busyId}
        onCancel={() => setRejecting(null)}
        onConfirm={async () => {
          const target = rejecting;
          if (!target) return;
          await decide(target, 'reject');
          setRejecting(null);
        }}
      />
    </Screen>
  );
}

function InfoLine({ icon, text }: { icon: keyof typeof Ionicons.glyphMap; text: string }) {
  return (
    <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.sm }}>
      <Ionicons name={icon} size={16} color={colors.textFaint} />
      <Text variant="caption" color={colors.textMuted} style={{ flex: 1, fontSize: 14 }}>
        {text}
      </Text>
    </View>
  );
}
