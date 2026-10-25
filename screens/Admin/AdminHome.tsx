import React from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import {
  Badge,
  Button,
  Card,
  Divider,
  DoorPlate,
  EmptyState,
  ErrorState,
  Hero,
  ListSkeleton,
  Screen,
  SectionHeader,
  Text,
  chevronForward,
  toPaymentState,
} from '../../components/ui';
import { currencyLabel, formatDate, formatMoney, fromYmd, ordinalFloor, parseDate } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import type { RootStackParamList } from '../../navigation/types';
import { colors, HIT, radius, spacing } from '../../theme';
import { selectReceipts, totalsByCurrency } from '../Dashboard';

type Owed = { currency: string; amount: number };
type Outstanding = Owed & { apartments: number; receipts: number };
type Attention = {
  apartmentId: number;
  apartmentNumber: string;
  floor: number;
  residentName: string;
  unpaidCount: number;
  overdueCount: number;
  oldestUnpaidDays: number;
  latePaidCount: number;
  owed: Owed[];
};
type Payment = { apartmentNumber: string; floor: number; residentName: string; currency: string; amount: number; receiptDate: string; paidAt: string | null };
type ReceiptOption = { id: number; date: string; currency: string; total: number; count: number; paidCount: number };
type Latest = ReceiptOption & { unpaidCount: number; collected: number; expected: number };
type Overview = {
  hasBuilding: boolean;
  buildingName: string;
  apartmentCount: number;
  pendingRequests: number;
  lateAfterDays: number;
  latest: Latest | null;
  outstanding: Outstanding[];
  needsAttention: Attention[];
  recentPayments: Payment[];
  receipts: ReceiptOption[];
};

const list = <T,>(value: unknown): T[] => (Array.isArray(value) ? (value as T[]) : []);

function selectOverview(raw: unknown): Overview {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Partial<Overview>;
  return {
    hasBuilding: !!r.hasBuilding,
    buildingName: r.buildingName ?? '',
    apartmentCount: Number(r.apartmentCount ?? 0),
    pendingRequests: Number(r.pendingRequests ?? 0),
    lateAfterDays: Number(r.lateAfterDays ?? 14),
    latest: r.latest ?? null,
    outstanding: list<Outstanding>(r.outstanding),
    needsAttention: list<Attention>(r.needsAttention),
    recentPayments: list<Payment>(r.recentPayments),
    receipts: list<ReceiptOption>(r.receipts),
  };
}

const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;

/** Why an apartment is on the list, in words. */
function reasonFor(a: Attention, lateAfterDays: number): string {
  const parts: string[] = [];
  if (a.overdueCount > 0) {
    parts.push(`${plural(a.overdueCount, 'receipt')} unpaid for over ${lateAfterDays} days (oldest ${a.oldestUnpaidDays} days)`);
  }
  if (a.latePaidCount > 0) {
    parts.push(`paid ${plural(a.latePaidCount, 'receipt')} late`);
  }
  const text = parts.join(', ');
  return text.charAt(0).toUpperCase() + text.slice(1);
}

/** The administrator's home: how collection is going across receipts, who needs a nudge, and what came in. */
export default function AdminHome() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const overview = useFetch<Overview>('building/overview', { refetchOnFocus: true, select: selectOverview });
  const mine = useFetch('appartment/receipts', { refetchOnFocus: true, select: selectReceipts });

  const data = overview.data;
  const refreshing = overview.refreshing || mine.refreshing;
  const refetch = () => {
    void overview.refetch();
    void mine.refetch();
  };

  const openCollect = (receiptId?: number) => navigation.navigate('Tabs', { screen: 'Collect', params: receiptId ? { receiptId, at: Date.now() } : undefined });

  const header = <Hero title={data?.hasBuilding && data.buildingName ? data.buildingName : 'Your building'} subtitle={subtitleFor(data)} />;

  if (overview.loading && !data) {
    return (
      <Screen tab header={header}>
        <ListSkeleton rows={4} rowHeight={96} />
      </Screen>
    );
  }
  if (overview.error && !data) {
    return (
      <Screen tab header={header} scroll={false}>
        <ErrorState message={overview.error} onRetry={refetch} />
      </Screen>
    );
  }
  if (data && !data.hasBuilding) {
    return (
      <Screen tab header={header} scroll={false}>
        <EmptyState
          icon="business-outline"
          title="Set up your building first"
          message="Add your building so residents can join it. Your dashboard fills in once you have issued a receipt."
          actionTitle="Set up building"
          onAction={() => navigation.navigate('SetupBuilding')}
        />
      </Screen>
    );
  }
  if (!data) return <Screen tab header={header}><View /></Screen>;

  const joinRequests =
    data.pendingRequests > 0 ? (
      <Card
        flush
        onPress={() => navigation.navigate('PendingApartments')}
        accessibilityLabel={`${plural(data.pendingRequests, 'join request')} waiting for your approval`}
        style={{ marginBottom: spacing.lg }}
      >
        <View style={styles.row}>
          <View style={styles.rowIcon}>
            <Ionicons name="person-add-outline" size={20} color={colors.primary} />
          </View>
          <View style={{ flex: 1 }}>
            <Text variant="bodyStrong">Join requests</Text>
            <Text variant="caption" color={colors.textMuted}>
              Approve or reject new apartments
            </Text>
          </View>
          <Badge label={`${data.pendingRequests} pending`} tone="warning" />
          <Ionicons name={chevronForward} size={18} color={colors.textFaint} />
        </View>
      </Card>
    ) : null;

  const yours = mine.data?.apartment ? <YourApartment data={mine.data} onPress={() => navigation.navigate('Receipts')} /> : null;

  if (!data.latest) {
    return (
      <Screen tab header={header} refreshing={refreshing} onRefresh={refetch}>
        {joinRequests}
        <EmptyState
          icon="receipt-outline"
          title="No receipts yet"
          message="Issue your first receipt and this page shows who has paid, what is still owed and who needs a reminder."
          actionTitle="New receipt"
          onAction={() => navigation.navigate('CreateReceipt')}
        />
        {yours}
      </Screen>
    );
  }

  const latest = data.latest;
  const allPaid = latest.count > 0 && latest.unpaidCount === 0;
  const headline = latest.count === 0 ? 'No apartments were billed' : allPaid ? 'Everyone has paid' : `${latest.paidCount} of ${latest.count} apartments have paid`;
  const share = latest.count > 0 ? latest.paidCount / latest.count : 0;

  return (
    <Screen tab header={header} refreshing={refreshing} onRefresh={refetch}>
      {joinRequests}

      {/* The newest receipt: one sentence of truth, the money, and how far along it is. */}
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`${headline}. Collected ${formatMoney(latest.collected, latest.currency)} of ${formatMoney(latest.expected, latest.currency)}, receipt of ${formatDate(fromYmd(latest.date))}. Opens collection`}
        onPress={() => openCollect(latest.id)}
      >
        <Text variant="title" importantForAccessibility="no">
          {headline}
        </Text>
        <Text numeral variant="body" color={colors.textMuted} style={{ marginTop: 2 }} importantForAccessibility="no">
          Collected {formatMoney(latest.collected, latest.currency)} of {formatMoney(latest.expected, latest.currency)}
        </Text>
        <Text variant="caption" color={colors.textMuted} style={{ marginTop: 2 }} importantForAccessibility="no">
          Latest receipt, {formatDate(fromYmd(latest.date))}
        </Text>
        <ProgressBar share={share} />
      </Pressable>
      <Button title="New receipt" icon="add-circle-outline" onPress={() => navigation.navigate('CreateReceipt')} style={{ marginTop: spacing.lg }} />

      <SectionHeader title="Still owed" />
      {data.outstanding.length === 0 ? (
        <Card>
          <View style={styles.inline}>
            <Ionicons name="checkmark-circle-outline" size={22} color={colors.success} />
            <Text variant="body" style={{ flex: 1 }}>
              Nothing is owed. Every receipt is settled.
            </Text>
          </View>
        </Card>
      ) : (
        <Card flush>
          {data.outstanding.map((o, i) => (
            <View key={o.currency}>
              {i > 0 ? <Divider /> : null}
              <View style={styles.owedRow} accessible accessibilityLabel={`${formatMoney(o.amount, o.currency)} still owed by ${plural(o.apartments, 'apartment')}, across ${plural(o.receipts, 'receipt')}`}>
                <View style={{ flex: 1 }}>
                  <Text numeral variant="title" style={{ fontSize: 24, lineHeight: 30 }} importantForAccessibility="no">
                    {formatMoney(o.amount, o.currency)}
                  </Text>
                  <Text variant="caption" color={colors.textMuted} importantForAccessibility="no">
                    {plural(o.apartments, 'apartment')} · {plural(o.receipts, 'receipt')} unpaid, in {currencyLabel(o.currency) === 'USD' ? 'dollars' : 'lira'}
                  </Text>
                </View>
              </View>
            </View>
          ))}
        </Card>
      )}

      <SectionHeader title="Needs attention" />
      {data.needsAttention.length === 0 ? (
        <Card>
          <View style={styles.inline}>
            <Ionicons name="checkmark-circle-outline" size={22} color={colors.success} />
            <Text variant="body" style={{ flex: 1 }}>
              Nobody is late. Residents are paying within {data.lateAfterDays} days of the receipt date.
            </Text>
          </View>
        </Card>
      ) : (
        <Card flush>
          {data.needsAttention.map((a, i) => {
            const owed = a.owed.map((o) => formatMoney(o.amount, o.currency)).join(' and ');
            return (
              <View key={a.apartmentId}>
                {i > 0 ? <Divider inset={spacing.lg + 44 + spacing.md} /> : null}
                <PersonRow
                  number={a.apartmentNumber}
                  title={a.residentName || `Apartment ${a.apartmentNumber}`}
                  subtitle={reasonFor(a, data.lateAfterDays)}
                  right={owed ? <Text numeral variant="bodyStrong">{owed}</Text> : undefined}
                  label={`Apartment ${a.apartmentNumber}, ${a.residentName || 'unnamed resident'}. ${reasonFor(a, data.lateAfterDays)}${owed ? `. Owes ${owed}` : ''}. Opens their receipts`}
                  onPress={() => navigation.navigate('ApartmentReceipts', { id: a.apartmentId, appartmentNumber: a.apartmentNumber })}
                />
              </View>
            );
          })}
        </Card>
      )}

      {data.recentPayments.length > 0 ? (
        <>
          <SectionHeader title="Recent payments" />
          <Card flush>
            {data.recentPayments.map((p, i) => (
              <View key={`${p.apartmentNumber}-${p.paidAt}-${i}`}>
                {i > 0 ? <Divider inset={spacing.lg + 44 + spacing.md} /> : null}
                <PersonRow
                  number={p.apartmentNumber}
                  title={p.residentName || `Apartment ${p.apartmentNumber}`}
                  subtitle={`Paid ${formatDate(parseDate(p.paidAt))} · receipt of ${formatDate(fromYmd(p.receiptDate))}`}
                  right={<Text numeral variant="bodyStrong">{formatMoney(p.amount, p.currency)}</Text>}
                  label={`Apartment ${p.apartmentNumber}, ${p.residentName || 'unnamed resident'} paid ${formatMoney(p.amount, p.currency)} on ${formatDate(parseDate(p.paidAt))}`}
                />
              </View>
            ))}
          </Card>
        </>
      ) : null}

      <SectionHeader title="Receipts you issued" />
      <Card flush>
        {data.receipts.map((r, i) => (
          <View key={r.id}>
            {i > 0 ? <Divider /> : null}
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={`Receipt of ${formatDate(fromYmd(r.date))}, ${formatMoney(r.total, r.currency)} each, ${r.paidCount} of ${r.count} paid. Opens collection`}
              onPress={() => openCollect(r.id)}
              style={({ pressed }) => pressed && { backgroundColor: colors.primaryTint }}
            >
              <View style={styles.receiptRow}>
                <View style={{ flex: 1 }}>
                  <Text variant="bodyStrong">{formatDate(fromYmd(r.date))}</Text>
                  <Text numeral variant="caption" color={colors.textMuted}>
                    {formatMoney(r.total, r.currency)} each · {r.paidCount} of {r.count} paid
                  </Text>
                  <ProgressBar share={r.count > 0 ? r.paidCount / r.count : 0} thin />
                </View>
                <Ionicons name={chevronForward} size={18} color={colors.textFaint} />
              </View>
            </Pressable>
          </View>
        ))}
      </Card>

      {yours}
    </Screen>
  );
}

function subtitleFor(data: Overview | null): string | undefined {
  if (!data || !data.hasBuilding) return undefined;
  const apartments = plural(data.apartmentCount, 'apartment');
  return data.pendingRequests > 0 ? `${apartments} · ${plural(data.pendingRequests, 'join request')} waiting` : apartments;
}

/** The part paid, in the Paid green. The words next to it carry the meaning; the bar is only the picture. */
function ProgressBar({ share, thin }: { share: number; thin?: boolean }) {
  const pct = Math.max(0, Math.min(1, share));
  return (
    <View style={[styles.track, thin && styles.trackThin]} importantForAccessibility="no-hide-descendants" accessibilityElementsHidden>
      <View style={[styles.fill, { width: `${Math.round(pct * 100)}%` }]} />
    </View>
  );
}

function PersonRow({
  number,
  title,
  subtitle,
  right,
  label,
  onPress,
}: {
  number: string;
  title: string;
  subtitle: string;
  right?: React.ReactNode;
  label: string;
  onPress?: () => void;
}) {
  const content = (
    <View style={styles.personRow}>
      <DoorPlate number={number} size="sm" />
      <View style={{ flex: 1 }}>
        <Text variant="bodyStrong" numberOfLines={1}>
          {title}
        </Text>
        <Text variant="caption" color={colors.textMuted} numberOfLines={3}>
          {subtitle}
        </Text>
      </View>
      {right}
      {onPress ? <Ionicons name={chevronForward} size={18} color={colors.textFaint} /> : null}
    </View>
  );
  if (!onPress) {
    return (
      <View accessible accessibilityLabel={label}>
        {content}
      </View>
    );
  }
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={label} onPress={onPress} style={({ pressed }) => pressed && { backgroundColor: colors.primaryTint }}>
      {content}
    </Pressable>
  );
}

/** Administrators who also live in the building still see their own dues, in one quiet line. */
function YourApartment({ data, onPress }: { data: NonNullable<ReturnType<typeof selectReceipts>>; onPress: () => void }) {
  const apartment = data.apartment;
  if (!apartment) return null;
  const unpaid = data.items.filter((i) => toPaymentState(i.status) === 'unpaid');
  const owed = totalsByCurrency(unpaid);
  const summary =
    owed.length > 0
      ? `You owe ${owed.map((o) => formatMoney(o.amount, o.currency)).join(' and ')}`
      : data.items.length > 0
        ? 'Nothing is due'
        : 'No receipts for your apartment yet';
  return (
    <View style={{ marginTop: spacing.xl }}>
      <SectionHeader title="Your apartment" />
      <Card flush onPress={onPress} accessibilityLabel={`Your apartment ${apartment.appartmentNumber}. ${summary}. Opens your receipts`}>
        <View style={styles.personRow}>
          <DoorPlate number={String(apartment.appartmentNumber ?? '')} size="sm" />
          <View style={{ flex: 1 }}>
            <Text variant="bodyStrong">{ordinalFloor(apartment.floor)}</Text>
            <Text variant="caption" color={colors.textMuted}>
              {summary}
            </Text>
          </View>
          <Ionicons name={chevronForward} size={18} color={colors.textFaint} />
        </View>
      </Card>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.md, paddingHorizontal: spacing.lg, minHeight: HIT + spacing.lg },
  rowIcon: { width: 38, height: 38, borderRadius: radius.md - 2, backgroundColor: colors.primarySoft, alignItems: 'center', justifyContent: 'center' },
  inline: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  owedRow: { flexDirection: 'row', alignItems: 'center', paddingHorizontal: spacing.lg, paddingVertical: spacing.md },
  personRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: spacing.lg, paddingVertical: spacing.md, minHeight: HIT + spacing.lg },
  receiptRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingHorizontal: spacing.lg, paddingVertical: spacing.md },
  track: { height: 10, borderRadius: radius.sm, backgroundColor: colors.surfaceMuted, overflow: 'hidden', marginTop: spacing.md },
  trackThin: { height: 6, marginTop: spacing.sm },
  fill: { height: '100%', backgroundColor: colors.success },
});
