import React, { useMemo, useState } from 'react';
import { View } from 'react-native';
import { Card, EmptyState, ErrorState, ListSkeleton, Screen, Segmented, Text } from '../components/ui';
import { ordinalFloor } from '../functions/format';
import { useFetch } from '../hooks/useFetch';
import { colors, spacing } from '../theme';
import { ReceiptBreakdown, ReceiptRow, selectReceipts } from './Dashboard';

type Sort = 'newest' | 'oldest' | 'highest' | 'lowest';

const sortOptions: { value: Sort; label: string }[] = [
  { value: 'newest', label: 'Newest' },
  { value: 'oldest', label: 'Oldest' },
  { value: 'highest', label: 'Highest' },
  { value: 'lowest', label: 'Lowest' },
];

export default function Receipts() {
  const { data, loading, error, refreshing, refetch } = useFetch('appartment/receipts', { select: selectReceipts });
  const [sort, setSort] = useState<Sort>('newest');
  const [openKey, setOpenKey] = useState<string | null>(null);

  const items = useMemo(() => {
    const list = [...(data?.items ?? [])];
    const time = (x: (typeof list)[number]) => x.parsed.date?.getTime() ?? 0;
    const total = (x: (typeof list)[number]) => Number(x.totalPayment ?? 0);
    switch (sort) {
      case 'oldest':
        return list.sort((a, b) => time(a) - time(b));
      case 'highest':
        return list.sort((a, b) => total(b) - total(a));
      case 'lowest':
        return list.sort((a, b) => total(a) - total(b));
      default:
        return list.sort((a, b) => time(b) - time(a));
    }
  }, [data, sort]);

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={5} rowHeight={76} />
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

  const apartment = data?.apartment;

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      {apartment ? (
        <Card style={{ marginBottom: spacing.lg }}>
          <Text variant="heading">
            {ordinalFloor(apartment.floor)} · Apartment {apartment.appartmentNumber}
          </Text>
          {apartment.appartmentDescription ? (
            <Text variant="body" color={colors.textMuted} style={{ marginTop: 2 }}>
              {apartment.appartmentDescription}
            </Text>
          ) : null}
        </Card>
      ) : null}

      {items.length === 0 ? (
        <EmptyState icon="receipt-outline" title="No receipts yet" message="There are no receipts posted for your apartment. Pull down to refresh." />
      ) : (
        <>
          <Segmented options={sortOptions} value={sort} onChange={setSort} />
          <View style={{ gap: spacing.sm, marginTop: spacing.lg }}>
            {items.map((item, i) => {
              const key = `${item.id ?? item.receiptId ?? i}`;
              const open = openKey === key;
              return (
                <View key={key}>
                  <ReceiptRow item={item} expanded={open} onPress={() => setOpenKey(open ? null : key)} />
                  {open ? (
                    <Card style={{ marginTop: spacing.xs, backgroundColor: colors.primaryTint }}>
                      <ReceiptBreakdown item={item} />
                    </Card>
                  ) : null}
                </View>
              );
            })}
          </View>
        </>
      )}
    </Screen>
  );
}
