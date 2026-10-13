import React, { useState } from 'react';
import { View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useApi } from '../../api/useApi';
import { Button, Card, EmptyState, ErrorState, ListSkeleton, Screen, Stamp, Text, showMessage, toPaymentState } from '../../components/ui';
import { formatDate, formatMoney } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import type { ScreenProps } from '../../navigation/types';
import { colors, spacing } from '../../theme';
import { ReceiptItem, selectReceipts } from '../Dashboard';

export default function ApartmentReceipts({ route }: ScreenProps<'ApartmentReceipts'>) {
  const { id } = route.params;
  const api = useApi();
  const { data, setData, loading, error, refreshing, refetch } = useFetch(`receipts/appartment/${id}`, { select: selectReceipts });
  const [busyKey, setBusyKey] = useState<string | null>(null);

  const keyOf = (item: ReceiptItem, i: number) => `${item.id ?? item.receiptId ?? i}`;

  const toggleStatus = async (item: ReceiptItem, key: string) => {
    if (busyKey) return;
    const previous = data;
    const status = item.status === 'Paid' ? 'Pending' : 'Paid';

    // Optimistic update; rolled back if the server refuses.
    setBusyKey(key);
    setData((d) => (d ? { ...d, items: d.items.map((x, i) => (keyOf(x, i) === key ? { ...x, status } : x)) } : d));
    const res = await api.post('receipt/appartment/save-status', { appartmentId: id, receiptId: item.receiptId, status });
    setBusyKey(null);

    if (res.ok) {
      showMessage({ message: `Receipt marked as ${status === 'Paid' ? 'paid' : 'unpaid'}`, type: 'success' });
      void refetch();
    } else {
      setData(previous);
    }
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={4} rowHeight={110} />
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

  const items = data?.items ?? [];

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      {items.length === 0 ? (
        <EmptyState icon="receipt-outline" title="No receipts yet" message="Receipts issued to this apartment will appear here." />
      ) : (
        <View style={{ gap: spacing.md }}>
          {items.map((item, i) => {
            const key = keyOf(item, i);
            const paid = item.status === 'Paid';
            return (
              <Card key={key}>
                <View style={{ flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between' }}>
                  <View>
                    <Text variant="caption" color={colors.textMuted}>
                      Issued {formatDate(item.parsed.date)}
                    </Text>
                    <Text variant="title" color={paid ? colors.success : colors.text} style={{ marginTop: 2 }}>
                      {formatMoney(item.totalPayment, item.currency)}
                    </Text>
                  </View>
                  <Stamp state={toPaymentState(item.status)} />
                </View>
                <Button
                  small
                  variant={paid ? 'secondary' : 'primary'}
                  icon={paid ? 'arrow-undo-outline' : 'checkmark-circle-outline'}
                  title={paid ? 'Mark as unpaid' : 'Mark as paid'}
                  loading={busyKey === key}
                  disabled={!!busyKey && busyKey !== key}
                  onPress={() => toggleStatus(item, key)}
                  style={{ marginTop: spacing.lg }}
                />
              </Card>
            );
          })}
        </View>
      )}
    </Screen>
  );
}
