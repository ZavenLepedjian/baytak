import React, { useMemo } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import {
  Button,
  Card,
  Divider,
  DoorPlate,
  EmptyState,
  ErrorState,
  FocusedStatusBar,
  chevronForward,
  ListSkeleton,
  Screen,
  Stamp,
  Text,
  toPaymentState,
} from '../components/ui';
import { currencyLabel, formatAmount, formatDate, formatMoney, ordinalFloor, parseReceipt, ParsedReceipt } from '../functions/format';
import { useFetch } from '../hooks/useFetch';
import type { RootStackParamList } from '../navigation/types';
import { colors, family, HIT, radius, spacing } from '../theme';

export type ReceiptItem = {
  id?: number | string;
  receiptId?: number | string;
  receipt: string;
  status?: string;
  totalPayment?: number;
  currency?: string;
  appartment?: { appartmentNumber?: string; floor?: number; appartmentDescription?: string } | null;
  parsed: ParsedReceipt;
};

type DashboardData = {
  apartment: NonNullable<ReceiptItem['appartment']> | null;
  items: ReceiptItem[];
};

/** Normalises the receipts endpoints, which answer with a plain string when no apartment is approved. */
export function selectReceipts(raw: unknown): DashboardData {
  if (!Array.isArray(raw) || raw.length === 0) return { apartment: null, items: [] };
  const apartment = raw[0]?.appartment ?? null;
  const items = raw
    .filter((x) => x && x.receipt)
    .map((x) => ({ ...x, parsed: parseReceipt(x.receipt) }) as ReceiptItem)
    .sort((a, b) => (b.parsed.date?.getTime() ?? 0) - (a.parsed.date?.getTime() ?? 0));
  return { apartment, items };
}

type MyApartment = { status: string; appartmentNumber: string; buildingName: string };

const selectApartment = (raw: unknown): MyApartment => {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Partial<MyApartment>;
  return { status: r.status ?? '', appartmentNumber: r.appartmentNumber ?? '', buildingName: r.buildingName ?? '' };
};

const fieldIcons: Record<string, keyof typeof Ionicons.glyphMap> = {
  maintenance: 'construct-outline',
  electricity: 'flash-outline',
  security: 'shield-checkmark-outline',
  elevator: 'swap-vertical-outline',
  water: 'water-outline',
  cleaning: 'sparkles-outline',
  generator: 'battery-charging-outline',
};
export const iconForField = (name: string): keyof typeof Ionicons.glyphMap => fieldIcons[name.trim().toLowerCase()] ?? 'receipt-outline';

/** What is owed, per currency. Currencies are never added together. */
function totalsByCurrency(unpaid: ReceiptItem[]): { currency: 'USD' | 'LBP'; amount: number }[] {
  const sums = new Map<'USD' | 'LBP', number>();
  for (const item of unpaid) {
    const currency = currencyLabel(item.currency);
    sums.set(currency, (sums.get(currency) ?? 0) + Number(item.totalPayment ?? 0));
  }
  return (['USD', 'LBP'] as const).filter((c) => sums.has(c)).map((c) => ({ currency: c, amount: sums.get(c)! }));
}

const MAX_SLIPS = 4;

export default function Dashboard() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const insets = useSafeAreaInsets();
  const receipts = useFetch<DashboardData>('appartment/receipts', { refetchOnFocus: true, select: selectReceipts });
  const mine = useFetch<MyApartment>('appartment/get', { refetchOnFocus: true, select: selectApartment });

  const apartment = receipts.data?.apartment ?? null;
  const items = receipts.data?.items ?? [];
  const unpaid = useMemo(() => items.filter((i) => toPaymentState(i.status) === 'unpaid'), [items]);
  const paid = useMemo(() => items.filter((i) => toPaymentState(i.status) === 'paid'), [items]);
  const owed = useMemo(() => totalsByCurrency(unpaid), [unpaid]);

  const refreshing = receipts.refreshing || mine.refreshing;
  const refetch = () => {
    void receipts.refetch();
    void mine.refetch();
  };

  const header = (
    <View style={[styles.band, { paddingTop: insets.top + spacing.md }]}>
      <FocusedStatusBar style="light" />
      {apartment ? (
        <View style={styles.bandRow}>
          <DoorPlate number={String(apartment.appartmentNumber ?? '')} size="lg" tone="paper" />
          <View style={{ flex: 1 }}>
            <Text variant="heading" color={colors.onInk} numberOfLines={1}>
              {mine.data?.buildingName || 'Your building'}
            </Text>
            <Text variant="caption" color={colors.onInkMuted}>
              {ordinalFloor(apartment.floor)}
            </Text>
          </View>
        </View>
      ) : (
        <Text variant="title" color={colors.onInk} accessibilityRole="header">
          Baytak
        </Text>
      )}
    </View>
  );

  if ((receipts.loading || mine.loading) && !receipts.data) {
    return (
      <Screen tab header={header}>
        <ListSkeleton rows={3} rowHeight={120} />
      </Screen>
    );
  }

  if (receipts.error && !receipts.data) {
    return (
      <Screen tab header={header} scroll={false}>
        <ErrorState message={receipts.error} onRetry={refetch} />
      </Screen>
    );
  }

  if (!apartment) {
    const status = mine.data?.status;
    return (
      <Screen tab header={header} refreshing={refreshing} onRefresh={refetch}>
        {status === 'Pending' ? (
          <EmptyState
            icon="hourglass-outline"
            title="Waiting for approval"
            message={`The administrator of ${mine.data?.buildingName || 'your building'} is reviewing apartment ${mine.data?.appartmentNumber}. We will notify you when it is approved.`}
            actionTitle="View my request"
            onAction={() => navigation.navigate('JoinBuilding')}
          />
        ) : status === 'Rejected' ? (
          <EmptyState
            icon="close-circle-outline"
            title="Your request was not approved"
            message="You can correct the details and send it again."
            actionTitle="Update my request"
            onAction={() => navigation.navigate('JoinBuilding')}
          />
        ) : (
          <EmptyState
            icon="home-outline"
            title="Register your apartment"
            message="Join your building to see what you owe and your receipts."
            actionTitle="Join a building"
            onAction={() => navigation.navigate('JoinBuilding')}
          />
        )}
      </Screen>
    );
  }

  const visible = unpaid.slice(0, MAX_SLIPS);
  const lastPaid = paid[0];

  return (
    <Screen tab header={header} refreshing={refreshing} onRefresh={refetch}>
      <Verdict owed={owed} unpaidCount={unpaid.length} hasReceipts={items.length > 0} lastPaidDate={lastPaid?.parsed.date ?? null} />

      {visible.length > 0 ? (
        <View style={{ gap: spacing.md, marginTop: spacing.lg }}>
          {visible.map((item, i) => (
            <Slip key={`${item.id ?? item.receiptId ?? i}`} item={item} onPress={() => navigation.navigate('Receipts')} />
          ))}
        </View>
      ) : null}

      {unpaid.length > MAX_SLIPS ? (
        <Button
          title={`See all ${unpaid.length} unpaid receipts`}
          variant="secondary"
          onPress={() => navigation.navigate('Receipts')}
          style={{ marginTop: spacing.md }}
        />
      ) : null}

      {paid.length > 0 ? (
        <View style={{ marginTop: spacing.xl }}>
          <Text variant="heading" style={{ marginBottom: spacing.sm }}>
            Paid recently
          </Text>
          <Card flush>
            {paid.slice(0, 3).map((item, i) => (
              <View key={`${item.id ?? item.receiptId ?? i}`}>
                {i > 0 ? <Divider /> : null}
                <ReceiptRow item={item} onPress={() => navigation.navigate('Receipts')} />
              </View>
            ))}
          </Card>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="See all receipts"
            onPress={() => navigation.navigate('Receipts')}
            style={styles.link}
          >
            <Text variant="bodyStrong" color={colors.primary}>
              See all receipts
            </Text>
            <Ionicons name={chevronForward} size={16} color={colors.primary} />
          </Pressable>
        </View>
      ) : null}
    </Screen>
  );
}

/** One sentence of truth, then the slips that make it up. Deliberately not a stat block. */
function Verdict({
  owed,
  unpaidCount,
  hasReceipts,
  lastPaidDate,
}: {
  owed: { currency: 'USD' | 'LBP'; amount: number }[];
  unpaidCount: number;
  hasReceipts: boolean;
  lastPaidDate: Date | null;
}) {
  if (owed.length === 0) {
    return (
      <View accessible accessibilityRole="header" accessibilityLabel={hasReceipts ? "You're all paid up" : 'No receipts yet'}>
        <Text variant="display" style={{ fontSize: 32, lineHeight: 40 }} importantForAccessibility="no">
          {hasReceipts ? "You're all paid up" : 'No receipts yet'}
        </Text>
        <Text variant="body" color={colors.textMuted} style={{ marginTop: spacing.xs }} importantForAccessibility="no">
          {hasReceipts
            ? lastPaidDate
              ? `Nothing is due. Your last payment was recorded on ${formatDate(lastPaidDate)}.`
              : 'Nothing is due.'
            : 'When your building administrator issues a receipt, it appears here and you get a notification.'}
        </Text>
      </View>
    );
  }

  const spoken = `You owe ${owed.map((o) => formatMoney(o.amount, o.currency)).join(' and ')}, across ${unpaidCount} unpaid ${unpaidCount === 1 ? 'receipt' : 'receipts'}`;
  return (
    <View accessible accessibilityRole="header" accessibilityLabel={spoken}>
      {owed.map((o, i) => (
        <Text
          key={o.currency}
          adjustsFontSizeToFit
          numberOfLines={2}
          importantForAccessibility="no"
          style={{ fontFamily: family.regular, fontSize: 30, lineHeight: 40, color: colors.textMuted }}
        >
          {i === 0 ? 'You owe ' : 'and '}
          <Text numeral style={{ fontSize: 34, color: colors.text }}>
            {formatAmount(o.amount, o.currency)}
          </Text>
          <Text style={{ fontFamily: family.semibold }}> {o.currency}</Text>
        </Text>
      ))}
    </View>
  );
}

/** An unpaid receipt, shown as the slip it is: date and stamp, its lines, the total. */
function Slip({ item, onPress }: { item: ReceiptItem; onPress: () => void }) {
  const amounts = item.parsed.fields.filter((f) => f.type === 'amount' && Number(f.value) !== 0);
  const shown = amounts.slice(0, 3);
  const hidden = amounts.length - shown.length;
  const note = item.parsed.fields.find((f) => f.type !== 'amount' && f.value.trim() !== '');
  const state = toPaymentState(item.status);

  return (
    <Card
      flush
      onPress={onPress}
      accessibilityLabel={`Receipt of ${formatDate(item.parsed.date)}, ${formatMoney(item.totalPayment, item.currency)}, ${state === 'paid' ? 'paid' : 'unpaid'}. Opens all receipts`}
    >
      <View style={styles.slipTop}>
        <Text variant="bodyStrong">{formatDate(item.parsed.date)}</Text>
        <Stamp state={state} />
      </View>
      <Divider />
      <View style={styles.slipBody}>
        {shown.map((f, i) => (
          <View key={`${f.name}-${i}`} style={styles.line}>
            <Text variant="body" color={colors.textMuted} style={{ flex: 1 }} numberOfLines={1}>
              {f.name}
            </Text>
            <Text numeral variant="body">
              {formatAmount(f.value, item.currency)}
            </Text>
          </View>
        ))}
        {hidden > 0 ? (
          <Text variant="caption" color={colors.textMuted}>
            + {hidden} more {hidden === 1 ? 'line' : 'lines'}
          </Text>
        ) : null}
        {note ? (
          <Text variant="caption" color={colors.textMuted} numberOfLines={2} style={{ marginTop: spacing.xs }}>
            {note.name}: {note.value}
          </Text>
        ) : null}
      </View>
      <Divider />
      <View style={styles.slipTotal}>
        <Text variant="bodyStrong">Total</Text>
        <Text numeral variant="heading" style={{ fontSize: 20 }}>
          {formatMoney(item.totalPayment, item.currency)}
        </Text>
      </View>
    </Card>
  );
}

/** A compact receipt line: date and amount with its stamp. */
export function ReceiptRow({ item, onPress, expanded }: { item: ReceiptItem; onPress?: () => void; expanded?: boolean }) {
  const state = toPaymentState(item.status);
  const content = (
    <View style={styles.row}>
      <View style={{ flex: 1 }}>
        <Text variant="bodyStrong">{formatDate(item.parsed.date)}</Text>
        <Text numeral variant="caption" color={colors.textMuted}>
          {formatMoney(item.totalPayment, item.currency)}
        </Text>
      </View>
      <Stamp state={state} size="sm" />
    </View>
  );
  if (!onPress) return content;
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`Receipt of ${formatDate(item.parsed.date)}, ${formatMoney(item.totalPayment, item.currency)}, ${state === 'paid' ? 'paid' : 'unpaid'}`}
      accessibilityState={expanded === undefined ? undefined : { expanded }}
      onPress={onPress}
      style={({ pressed }) => pressed && { backgroundColor: colors.primaryTint }}
    >
      {content}
    </Pressable>
  );
}

export function ReceiptBreakdown({ item }: { item: ReceiptItem }) {
  const amounts = item.parsed.fields.filter((f) => f.type === 'amount');
  const notes = item.parsed.fields.filter((f) => f.type !== 'amount' && f.value.trim() !== '');
  const unit = currencyLabel(item.currency);

  return (
    <View>
      {amounts.map((f, i) => (
        <View key={`${f.name}-${i}`} style={styles.breakdownLine}>
          <View style={styles.lineIcon}>
            <Ionicons name={iconForField(f.name)} size={18} color={colors.primary} />
          </View>
          <Text variant="body" style={{ flex: 1 }}>
            {f.name}
          </Text>
          <Text numeral variant="bodyStrong">
            {formatAmount(f.value, item.currency)} <Text variant="caption" color={colors.textMuted}>{unit}</Text>
          </Text>
        </View>
      ))}
      {notes.map((f, i) => (
        <View key={`${f.name}-note-${i}`} style={styles.note}>
          <Text variant="label" color={colors.textMuted}>
            {f.name}
          </Text>
          <Text variant="body" style={{ marginTop: 2 }}>
            {f.value}
          </Text>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  band: { backgroundColor: colors.primary, paddingHorizontal: spacing.lg, paddingBottom: spacing.lg },
  bandRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  slipTop: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingHorizontal: spacing.lg, paddingVertical: spacing.md },
  slipBody: { paddingHorizontal: spacing.lg, paddingVertical: spacing.md, gap: spacing.xs },
  slipTotal: { flexDirection: 'row', alignItems: 'center', justifyContent: 'space-between', paddingHorizontal: spacing.lg, paddingVertical: spacing.md },
  line: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: spacing.lg, paddingVertical: spacing.md, minHeight: HIT + spacing.md },
  link: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, minHeight: HIT, alignSelf: 'flex-start' },
  breakdownLine: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.sm + 2 },
  lineIcon: {
    width: 34,
    height: 34,
    borderRadius: radius.md,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  note: { marginTop: spacing.sm, padding: spacing.md, backgroundColor: colors.surfaceMuted, borderRadius: radius.md },
});
