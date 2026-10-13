import React, { useEffect, useMemo, useRef, useState } from 'react';
import { AccessibilityInfo, Animated, Linking, Pressable, StyleSheet, useWindowDimensions, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import * as Haptics from 'expo-haptics';
import { useApi } from '../../api/useApi';
import {
  Button,
  ConfirmDialog,
  DoorPlate,
  EmptyState,
  ErrorState,
  FocusedStatusBar,
  ListSkeleton,
  Screen,
  Sheet,
  Stamp,
  Text,
  showMessage,
  toPaymentState,
} from '../../components/ui';
import { currencyLabel, formatAmount, formatDate, fromYmd, ordinalFloor } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import type { RootStackParamList } from '../../navigation/types';
import { colors, family, HIT, radius, spacing } from '../../theme';

type Plate = {
  apartmentId: number;
  apartmentNumber: string;
  floor: number;
  residentName: string;
  phoneNumber: string;
  status: string;
  paidAt: string | null;
};

type ReceiptOption = { id: number; date: string; currency: string; total: number; count: number; paidCount: number };

type Selected = ReceiptOption & {
  unpaidCount: number;
  collected: number;
  expected: number;
  lastRemindedAt: string | null;
  canRemind: boolean;
  apartments: Plate[];
};

type Summary = { hasBuilding: boolean; receipts: ReceiptOption[]; selected: Selected | null };

const COLUMNS = 4;
const GAP = spacing.sm;

function selectSummary(raw: unknown): Summary {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Partial<Summary>;
  return { hasBuilding: !!r.hasBuilding, receipts: Array.isArray(r.receipts) ? r.receipts : [], selected: r.selected ?? null };
}

const money = (value: number, currency: string) => `${formatAmount(value, currency)} ${currencyLabel(currency)}`;

export default function Collection() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const api = useApi();
  const { width } = useWindowDimensions();
  const [receiptId, setReceiptId] = useState<number | null>(null);
  const path = receiptId ? `collection/summary?receiptId=${receiptId}` : 'collection/summary';
  const { data, setData, loading, error, refreshing, refetch } = useFetch<Summary>(path, { refetchOnFocus: true, select: selectSummary });

  const [unpaidOnly, setUnpaidOnly] = useState(false);
  const [plate, setPlate] = useState<Plate | null>(null);
  const [switcher, setSwitcher] = useState(false);
  const [confirmRemind, setConfirmRemind] = useState(false);
  const [reminding, setReminding] = useState(false);
  // Stamping several apartments in a row must not wait on each request, so pending state is per apartment.
  const [pendingIds, setPendingIds] = useState<ReadonlySet<number>>(new Set());
  const inFlight = useRef(0);

  const selected = data?.selected ?? null;
  const counts = useMemo(() => {
    const apartments = selected?.apartments ?? [];
    const paidCount = apartments.filter((a) => toPaymentState(a.status) === 'paid').length;
    return { paidCount, unpaidCount: apartments.length - paidCount };
  }, [selected]);
  const cell = Math.floor((width - spacing.lg * 2 - GAP * (COLUMNS - 1)) / COLUMNS);

  const floors = useMemo(() => {
    const list = (selected?.apartments ?? []).filter((a) => !unpaidOnly || toPaymentState(a.status) === 'unpaid');
    const byFloor = new Map<number, Plate[]>();
    for (const a of list) byFloor.set(a.floor, [...(byFloor.get(a.floor) ?? []), a]);
    return Array.from(byFloor.entries()).sort(([a], [b]) => a - b);
  }, [selected, unpaidOnly]);

  // The sheet shows live data, so marking paid updates it in place.
  const livePlate = plate ? (selected?.apartments.find((a) => a.apartmentId === plate.apartmentId) ?? plate) : null;

  const flip = (apartmentId: number, status: string) =>
    setData((d) =>
      d && d.selected
        ? { ...d, selected: { ...d.selected, apartments: d.selected.apartments.map((a) => (a.apartmentId === apartmentId ? { ...a, status } : a)) } }
        : d,
    );

  const setStatus = async (target: Plate, status: 'Paid' | 'Pending') => {
    if (!selected || pendingIds.has(target.apartmentId)) return;
    const before = target.status;
    inFlight.current += 1;
    setPendingIds((s) => new Set(s).add(target.apartmentId));
    // Optimistic: the plate flips at once; only this plate is rolled back if the server refuses.
    flip(target.apartmentId, status);
    const res = await api.post('receipt/appartment/save-status', { appartmentId: target.apartmentId, receiptId: selected.id, status });
    inFlight.current -= 1;
    setPendingIds((s) => {
      const next = new Set(s);
      next.delete(target.apartmentId);
      return next;
    });
    if (!res.ok) flip(target.apartmentId, before);
    // Re-sync with the server only once the last request has landed, so a late response can't undo a newer tap.
    if (inFlight.current === 0) void refetch();
  };

  /** One tap stamps an unpaid apartment paid. Undo is a tap away for six seconds. */
  const stamp = (target: Plate) => {
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success).catch(() => {});
    void setStatus(target, 'Paid');
    showMessage({
      message: `Apartment ${target.apartmentNumber} marked as paid`,
      type: 'success',
      action: { label: 'Undo', onPress: () => void setStatus({ ...target, status: 'Paid' }, 'Pending') },
    });
  };

  const sendReminder = async () => {
    if (!selected) return;
    setReminding(true);
    const res = await api.post<{ sent: number }>('collection/remind', { receiptId: selected.id });
    setReminding(false);
    setConfirmRemind(false);
    if (res.ok) {
      const sent = res.data?.sent ?? counts.unpaidCount;
      showMessage({ message: `Reminder sent to ${sent} ${sent === 1 ? 'resident' : 'residents'}`, type: 'success' });
      void refetch();
    }
  };

  if (loading && !data) {
    return (
      <Screen tab>
        <ListSkeleton rows={4} rowHeight={72} />
      </Screen>
    );
  }
  if (error && !data) {
    return (
      <Screen tab scroll={false}>
        <ErrorState message={error} onRetry={refetch} />
      </Screen>
    );
  }
  if (data && !data.hasBuilding) {
    return (
      <Screen tab scroll={false}>
        <EmptyState
          icon="business-outline"
          title="Set up your building first"
          message="Add your building so residents can join it. Collection starts once you have issued a receipt."
          actionTitle="Set up building"
          onAction={() => navigation.navigate('SetupBuilding')}
        />
      </Screen>
    );
  }
  if (!selected) {
    return (
      <Screen tab scroll={false}>
        <EmptyState
          icon="receipt-outline"
          title="No receipts yet"
          message="Issue your first receipt and every apartment appears here as a door plate you can stamp as paid."
          actionTitle="New receipt"
          onAction={() => navigation.navigate('CreateReceipt')}
        />
      </Screen>
    );
  }

  const allPaid = selected.count > 0 && counts.unpaidCount === 0;
  const collected = counts.paidCount * selected.total;
  const headline =
    selected.count === 0
      ? 'No apartments were billed'
      : allPaid
        ? 'Everyone has paid'
        : `${counts.paidCount} of ${selected.count} apartments have paid`;

  return (
    <Screen tab refreshing={refreshing} onRefresh={refetch}>
      <FocusedStatusBar style="light" />
      <View accessible accessibilityRole="header" accessibilityLabel={`${headline}. Collected ${money(collected, selected.currency)} of ${money(selected.expected, selected.currency)}`}>
        <Text variant="title" importantForAccessibility="no">
          {headline}
        </Text>
        <Text numeral variant="body" color={colors.textMuted} style={{ marginTop: 2 }} importantForAccessibility="no">
          Collected {money(collected, selected.currency)} of {money(selected.expected, selected.currency)}
        </Text>
      </View>

      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`Receipt of ${formatDate(fromYmd(selected.date))}, ${money(selected.total, selected.currency)}. Change receipt`}
        onPress={() => setSwitcher(true)}
        style={styles.switcher}
      >
        <View style={{ flex: 1 }}>
          <Text variant="caption" color={colors.textMuted}>
            Receipt
          </Text>
          <Text variant="bodyStrong">
            {formatDate(fromYmd(selected.date))} · <Text numeral variant="bodyStrong">{money(selected.total, selected.currency)}</Text>
          </Text>
        </View>
        <Text variant="bodyStrong" color={colors.primary}>
          Change
        </Text>
        <Ionicons name="chevron-down" size={18} color={colors.primary} />
      </Pressable>

      <View style={styles.actions}>
        <Pressable
          accessibilityRole="switch"
          accessibilityState={{ checked: unpaidOnly }}
          accessibilityLabel="Show unpaid only"
          onPress={() => setUnpaidOnly((v) => !v)}
          style={[styles.chip, unpaidOnly && styles.chipOn]}
        >
          <Ionicons name={unpaidOnly ? 'checkmark' : 'funnel-outline'} size={16} color={unpaidOnly ? colors.plateText : colors.primary} />
          <Text variant="bodyStrong" color={unpaidOnly ? colors.plateText : colors.primary}>
            Unpaid only
          </Text>
        </Pressable>
        <Button
          small
          icon="notifications-outline"
          variant="secondary"
          title={counts.unpaidCount > 0 ? `Remind ${counts.unpaidCount} unpaid` : 'Remind'}
          disabled={!selected.canRemind || counts.unpaidCount === 0}
          onPress={() => setConfirmRemind(true)}
          style={{ flex: 1 }}
        />
      </View>
      {counts.unpaidCount > 0 && !selected.canRemind ? (
        <Text variant="caption" color={colors.textMuted} style={{ marginTop: spacing.xs }}>
          You already sent a reminder for this receipt today.
        </Text>
      ) : null}

      {floors.length === 0 ? (
        <View style={{ marginTop: spacing.xl }}>
          <EmptyState icon="checkmark-done-outline" title="Nothing unpaid" message="Every apartment has paid this receipt." />
        </View>
      ) : (
        floors.map(([floor, plates]) => (
          <View key={floor} style={{ marginTop: spacing.xl }}>
            <Text variant="heading" accessibilityRole="header" style={{ marginBottom: spacing.sm }}>
              {ordinalFloor(floor)}
            </Text>
            <View style={styles.grid}>
              {plates.map((a) => (
                <PlateCell
                  key={a.apartmentId}
                  plate={a}
                  width={cell}
                  currency={selected.currency}
                  total={selected.total}
                  onStamp={() => stamp(a)}
                  onOpen={() => setPlate(a)}
                />
              ))}
            </View>
          </View>
        ))
      )}

      {/* One plate: who, how much, and the stamp. */}
      <Sheet visible={!!livePlate} onClose={() => setPlate(null)} title={livePlate ? `Apartment ${livePlate.apartmentNumber}` : undefined}>
        {livePlate ? (
          <View style={{ gap: spacing.lg }}>
            <View style={styles.detailTop}>
              <View style={{ flex: 1 }}>
                <Text variant="bodyStrong">{livePlate.residentName || 'Unnamed resident'}</Text>
                <Text variant="caption" color={colors.textMuted}>
                  {ordinalFloor(livePlate.floor)}
                </Text>
              </View>
              <Stamp state={toPaymentState(livePlate.status)} settle />
            </View>

            <View style={styles.detailAmount}>
              <Text variant="body" color={colors.textMuted}>
                Receipt of {formatDate(fromYmd(selected.date))}
              </Text>
              <Text numeral variant="heading" style={{ fontSize: 22 }}>
                {money(selected.total, selected.currency)}
              </Text>
            </View>

            {toPaymentState(livePlate.status) === 'paid' ? (
              <Button title="Mark as unpaid" variant="secondary" loading={pendingIds.has(livePlate.apartmentId)} onPress={() => setStatus(livePlate, 'Pending')} />
            ) : (
              <Button title="Mark as paid" icon="checkmark-done" loading={pendingIds.has(livePlate.apartmentId)} onPress={() => setStatus(livePlate, 'Paid')} />
            )}
            {livePlate.phoneNumber ? (
              <Button
                title={`Call ${livePlate.residentName || livePlate.phoneNumber}`}
                icon="call-outline"
                variant="ghost"
                onPress={() => Linking.openURL(`tel:${livePlate.phoneNumber}`).catch(() => showMessage({ message: 'Unable to open the phone app', type: 'danger' }))}
              />
            ) : null}
          </View>
        ) : null}
      </Sheet>

      {/* Receipt switcher */}
      <Sheet visible={switcher} onClose={() => setSwitcher(false)} title="Choose a receipt">
        <View style={{ gap: spacing.sm }}>
          {(data?.receipts ?? []).map((r) => {
            const on = r.id === selected.id;
            return (
              <Pressable
                key={r.id}
                accessibilityRole="radio"
                accessibilityState={{ selected: on }}
                accessibilityLabel={`Receipt of ${formatDate(fromYmd(r.date))}, ${money(r.total, r.currency)}, ${r.paidCount} of ${r.count} paid`}
                onPress={() => {
                  setReceiptId(r.id);
                  setSwitcher(false);
                }}
                style={[styles.option, on && styles.optionOn]}
              >
                <View style={{ flex: 1 }}>
                  <Text variant="bodyStrong">{formatDate(fromYmd(r.date))}</Text>
                  <Text numeral variant="caption" color={colors.textMuted}>
                    {money(r.total, r.currency)} · {r.paidCount} of {r.count} paid
                  </Text>
                </View>
                {on ? <Ionicons name="checkmark-circle" size={22} color={colors.primary} /> : null}
              </Pressable>
            );
          })}
        </View>
      </Sheet>

      <ConfirmDialog
        visible={confirmRemind}
        title="Send a reminder?"
        message={`We will send a push notification to the ${counts.unpaidCount} ${counts.unpaidCount === 1 ? 'resident who has' : 'residents who have'} not paid the receipt of ${formatDate(fromYmd(selected.date))}. You can send one reminder per receipt per day.`}
        confirmTitle="Send reminder"
        loading={reminding}
        onCancel={() => setConfirmRemind(false)}
        onConfirm={sendReminder}
      />
    </Screen>
  );
}

/**
 * One apartment: its door plate, with the status written out (never colour alone).
 * Tap an unpaid plate to stamp it paid (the plate itself settles); a paid plate, or a long press, opens the details.
 */
function PlateCell({
  plate,
  width,
  currency,
  total,
  onStamp,
  onOpen,
}: {
  plate: Plate;
  width: number;
  currency: string;
  total: number;
  onStamp: () => void;
  onOpen: () => void;
}) {
  const state = toPaymentState(plate.status);
  const paid = state === 'paid';
  const scale = useRef(new Animated.Value(1)).current;
  const previous = useRef(state);
  const [reduceMotion, setReduceMotion] = useState(false);

  useEffect(() => {
    let alive = true;
    AccessibilityInfo.isReduceMotionEnabled().then((v) => alive && setReduceMotion(v)).catch(() => {});
    const sub = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduceMotion);
    return () => {
      alive = false;
      sub.remove();
    };
  }, []);

  // The signature moment, on the plate: it lands with a short spring when it turns Paid.
  useEffect(() => {
    const turnedPaid = previous.current !== 'paid' && state === 'paid';
    previous.current = state;
    if (!turnedPaid || reduceMotion) return;
    scale.setValue(1.18);
    Animated.spring(scale, { toValue: 1, friction: 5, tension: 240, useNativeDriver: true }).start();
  }, [state, reduceMotion, scale]);

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`Apartment ${plate.apartmentNumber}, ${paid ? 'paid' : 'unpaid'}, ${money(total, currency)}`}
      accessibilityHint={paid ? 'Opens details' : 'Marks as paid. Long press for details'}
      accessibilityActions={[{ name: 'details', label: 'Show details' }]}
      onAccessibilityAction={(e) => e.nativeEvent.actionName === 'details' && onOpen()}
      onPress={paid ? onOpen : onStamp}
      onLongPress={onOpen}
      style={({ pressed }) => [{ width }, pressed && { opacity: 0.7 }]}
    >
      <Animated.View style={{ transform: [{ scale }] }}>
        <DoorPlate number={plate.apartmentNumber} size="md" tone={paid ? 'paper' : 'ink'} style={{ width, borderColor: paid ? colors.success : colors.primaryDeep }} />
      </Animated.View>
      <View style={styles.caption}>
        <Ionicons name={paid ? 'checkmark-done' : 'ellipse-outline'} size={15} color={paid ? colors.success : colors.danger} />
        <Text variant="label" color={paid ? colors.success : colors.danger} style={{ fontFamily: family.bold, fontSize: 13, lineHeight: 18 }}>
          {paid ? 'Paid' : 'Unpaid'}
        </Text>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  switcher: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    minHeight: HIT + spacing.md,
    marginTop: spacing.lg,
    paddingHorizontal: spacing.md,
    paddingVertical: spacing.sm,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    borderWidth: 1,
    borderColor: colors.border,
  },
  actions: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginTop: spacing.md },
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs + 2,
    minHeight: HIT,
    paddingHorizontal: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderColor: colors.primary,
  },
  chipOn: { backgroundColor: colors.primary },
  grid: { flexDirection: 'row', flexWrap: 'wrap', gap: GAP },
  caption: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'center', gap: 4, marginTop: 4, minHeight: 22 },
  detailTop: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  detailAmount: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: spacing.md,
    backgroundColor: colors.bg,
    borderRadius: radius.md,
  },
  option: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    minHeight: HIT + spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderColor: colors.border,
  },
  optionOn: { borderColor: colors.primary, backgroundColor: colors.primaryTint },
});
