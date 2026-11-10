import React, { useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { ShareSheet } from '../components/ShareSheet';
import { Button, Card, EmptyState, ErrorState, ListSkeleton, Screen, Text, chevronBack, chevronForward } from '../components/ui';
import { SITE_URL } from '../config';
import { formatMoney, toYmd } from '../functions/format';
import { SummaryLabels, SummaryModel, summaryHtml, summaryMessage } from '../functions/summaryNotice';
import { useFetch } from '../hooks/useFetch';
import { isArabic, latinDigits, localeTag, t, tn } from '../i18n';
import { colors, HIT, radius, spacing } from '../theme';

type Currency = 'USD' | 'LBP';
type Block = { currency: Currency; billed: number; collected: number; outstanding: number; outstandingApartments: number };
type SummaryData = {
  hasBuilding: boolean;
  buildingName: string;
  isAdmin: boolean;
  month: string;
  earliest: string;
  receiptsIssued: number;
  currencies: Block[];
};

const asCurrency = (value: unknown): Currency => (String(value).toUpperCase() === 'LBP' ? 'LBP' : 'USD');
const list = <T,>(value: unknown): T[] => (Array.isArray(value) ? (value as T[]) : []);

function selectSummary(raw: unknown): SummaryData {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Record<string, any>;
  return {
    hasBuilding: !!r.hasBuilding,
    buildingName: String(r.buildingName ?? ''),
    isAdmin: !!r.isAdmin,
    month: String(r.month ?? ''),
    earliest: String(r.earliest ?? ''),
    receiptsIssued: Number(r.receiptsIssued ?? 0),
    currencies: list<any>(r.currencies).map((b) => ({
      currency: asCurrency(b.currency),
      billed: Number(b.billed ?? 0),
      collected: Number(b.collected ?? 0),
      outstanding: Number(b.outstanding ?? 0),
      outstandingApartments: Number(b.outstandingApartments ?? 0),
    })),
  };
}

const currentMonth = () => toYmd(new Date()).slice(0, 7);
const shiftMonth = (month: string, by: number) => {
  const [y, m] = month.split('-').map(Number);
  return toYmd(new Date(y, m - 1 + by, 1)).slice(0, 7);
};
const monthLabel = (month: string) => {
  const [y, m] = month.split('-').map(Number);
  return latinDigits(new Date(y, m - 1, 1).toLocaleDateString(localeTag(), { month: 'long', year: 'numeric' }));
};

const currencyName = (currency: Currency) => (currency === 'USD' ? t('US dollars') : t('Lebanese lira'));
const owedBy = (b: Block) => (b.outstandingApartments > 0 ? tn('{n} apartment|{n} apartments', b.outstandingApartments) : '');

function summaryLabels(): SummaryLabels {
  return {
    title: t('Monthly summary'),
    billed: t('Billed this month'),
    collected: t('Collected'),
    outstanding: t('Still owed at the end of the month'),
    noActivity: t('Nothing was collected or billed this month.'),
    footer: t('Issued with Baytak.'),
  };
}

function summaryModel(data: SummaryData): SummaryModel {
  return {
    buildingName: data.buildingName || t('Your building'),
    monthLabel: monthLabel(data.month),
    blocks: data.currencies.map((b) => ({
      currency: b.currency,
      billed: formatMoney(b.billed, b.currency),
      collected: formatMoney(b.collected, b.currency),
      outstanding: formatMoney(b.outstanding, b.currency),
      owedBy: owedBy(b),
    })),
  };
}

/** One month of collection on one page: what was collected, what was billed, and what is still owed. */
export default function Summary() {
  const [month, setMonth] = useState(currentMonth());
  const { data, loading, error, refreshing, refetch } = useFetch<SummaryData>(`building/summary?month=${month}`, { select: selectSummary });
  const [sharing, setSharing] = useState(false);

  const fresh = data !== null && data.month === month;
  const model = useMemo(() => (data && fresh ? summaryModel(data) : null), [data, fresh]);

  const atStart = !data || month <= data.earliest;
  const atEnd = month >= currentMonth();
  const nav = (
    <View style={styles.nav}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('Previous month')}
        disabled={atStart}
        onPress={() => setMonth(shiftMonth(month, -1))}
        style={[styles.navButton, atStart && styles.navOff]}
        hitSlop={8}
      >
        <Ionicons name={chevronBack} size={22} color={colors.primary} />
      </Pressable>
      <Text variant="heading" style={{ flex: 1, textAlign: 'center' }} accessibilityRole="header">
        {monthLabel(month)}
      </Text>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('Next month')}
        disabled={atEnd}
        onPress={() => setMonth(shiftMonth(month, 1))}
        style={[styles.navButton, atEnd && styles.navOff]}
        hitSlop={8}
      >
        <Ionicons name={chevronForward} size={22} color={colors.primary} />
      </Pressable>
    </View>
  );

  if (loading && !data) {
    return (
      <Screen>
        {nav}
        <ListSkeleton rows={2} rowHeight={150} />
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
  if (data && !data.hasBuilding) {
    return (
      <Screen scroll={false}>
        <EmptyState icon="business-outline" title={t('No building yet')} message={t('Join your building to see how its fees are collected.')} />
      </Screen>
    );
  }

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      {nav}

      {!data || !fresh ? (
        <ListSkeleton rows={2} rowHeight={150} />
      ) : (
        <>
          {data.receiptsIssued > 0 ? (
            <Text variant="caption" color={colors.textMuted} style={{ marginBottom: spacing.md }}>
              {tn('{n} receipt issued this month|{n} receipts issued this month', data.receiptsIssued)}
            </Text>
          ) : null}

          {data.currencies.length === 0 ? (
            <Card>
              <Text variant="body" color={colors.textMuted}>
                {t('Nothing was collected or billed this month.')}
              </Text>
            </Card>
          ) : (
            data.currencies.map((b) => <BlockCard key={b.currency} block={b} />)
          )}

          {model && data.currencies.length > 0 ? (
            <Button title={t('Share summary')} icon="share-outline" variant={data.isAdmin ? 'primary' : 'secondary'} onPress={() => setSharing(true)} style={{ marginTop: spacing.sm }} />
          ) : null}
        </>
      )}

      <ShareSheet
        visible={sharing && model !== null}
        title={t('Monthly summary')}
        intro={t("Send this month's summary to your building's WhatsApp group.")}
        dialogTitle={t('Share summary')}
        message={() => summaryMessage(model!, summaryLabels(), SITE_URL)}
        html={() => summaryHtml(model!, summaryLabels(), isArabic())}
        onClose={() => setSharing(false)}
      />
    </Screen>
  );
}

/** One currency of the month: what came in, and the figures beside it. */
function BlockCard({ block: b }: { block: Block }) {
  return (
    <Card style={{ marginBottom: spacing.lg }}>
      <Text variant="label" color={colors.textMuted}>
        {currencyName(b.currency)}
      </Text>
      <Text variant="caption" color={colors.textMuted} style={{ marginTop: spacing.sm }}>
        {t('Collected')}
      </Text>
      <Text numeral variant="display" adjustsFontSizeToFit numberOfLines={1} style={{ fontSize: 32, lineHeight: 40, color: colors.success }}>
        {formatMoney(b.collected, b.currency)}
      </Text>

      <View style={{ marginTop: spacing.md }}>
        <Figure label={t('Billed this month')} value={formatMoney(b.billed, b.currency)} />
        <Figure label={t('Still owed at the end of the month')} value={formatMoney(b.outstanding, b.currency)} note={owedBy(b) || undefined} />
      </View>
    </Card>
  );
}

function Figure({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <View style={styles.figure} accessible accessibilityLabel={`${label}: ${value}${note ? `, ${note}` : ''}`}>
      <View style={{ flex: 1 }}>
        <Text variant="body" importantForAccessibility="no">
          {label}
        </Text>
        {note ? (
          <Text variant="caption" color={colors.textMuted} importantForAccessibility="no">
            {note}
          </Text>
        ) : null}
      </View>
      <Text numeral variant="bodyStrong" importantForAccessibility="no">
        {value}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  nav: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginBottom: spacing.lg },
  navButton: { width: HIT, height: HIT, borderRadius: radius.md, backgroundColor: colors.primarySoft, alignItems: 'center', justifyContent: 'center' },
  navOff: { opacity: 0.35 },
  figure: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.xs + 2 },
});
