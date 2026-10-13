import React, { useEffect, useMemo, useState } from 'react';
import { Keyboard, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useApi } from '../../api/useApi';
import { Button, Card, ConfirmDialog, DateField, EmptyState, ErrorState, InfoBanner, ListRow, ListSkeleton, Screen, Text, TextField, showMessage } from '../../components/ui';
import { currencyLabel, formatAmount, toYmd } from '../../functions/format';
import { useFetch } from '../../hooks/useFetch';
import type { ScreenProps } from '../../navigation/types';
import { colors, radius, spacing } from '../../theme';

type Field = { fieldName: string; type: string; value: string };
type FormatState =
  | { kind: 'ready'; currency: string; fields: Field[] }
  | { kind: 'register' }
  | { kind: 'building' }
  | { kind: 'setup' };

const isAmount = (f: Field) => f.type?.toLowerCase() === 'amount';

/** The endpoint answers with a JSON string, or one of two marker strings when prerequisites are missing. */
function selectFormat(raw: unknown): FormatState {
  if (raw === 'RegisterAppartment') return { kind: 'register' };
  if (raw === 'SetupBuilding') return { kind: 'building' };
  if (raw === 'SetupReceipt' || raw === null || raw === undefined || raw === '') return { kind: 'setup' };
  const parsed = typeof raw === 'string' ? JSON.parse(raw) : (raw as any);
  const list: any[] = Array.isArray(parsed?.customFields) ? parsed.customFields : [];
  if (list.length === 0) return { kind: 'setup' };
  const fields: Field[] = list.map((f) => ({ fieldName: String(f.fieldName ?? ''), type: String(f.type ?? 'Text'), value: '' }));
  // Amount fields first, keeping the configured order within each group.
  fields.sort((a, b) => Number(isAmount(b)) - Number(isAmount(a)));
  return { kind: 'ready', currency: String(parsed.currency ?? ''), fields };
}

export default function CreateReceipt({ navigation, route }: ScreenProps<'CreateReceipt'>) {
  const api = useApi();
  const { data: format, loading, error, refreshing, refetch } = useFetch<FormatState>('get-receipt-format', {
    refetchOnFocus: true,
    select: selectFormat,
  });

  const [values, setValues] = useState<string[]>([]);
  const [date, setDate] = useState(new Date());
  const [confirm, setConfirm] = useState(false);
  const [saving, setSaving] = useState(false);
  const [amountError, setAmountError] = useState<string | null>(null);

  const apartments = route.params?.apartments ?? [];
  const fields = format?.kind === 'ready' ? format.fields : [];

  // Reset the inputs only when the set of fields really changes (first load or after editing the format),
  // so refetching on focus (e.g. coming back from the apartment picker) keeps what was typed.
  const signature = fields.map((f) => `${f.fieldName}:${f.type}`).join("|");
  useEffect(() => {
    setValues(new Array(fields.length).fill(""));
  }, [signature]);

  const total = useMemo(
    () =>
      fields.reduce((sum, f, i) => {
        if (!isAmount(f)) return sum;
        const n = Number(values[i]);
        return sum + (Number.isFinite(n) ? n : 0);
      }, 0),
    [fields, values],
  );

  const setValue = (i: number, v: string, amount: boolean) => {
    const next = amount ? v.replace(',', '.').replace(/[^0-9.]/g, '') : v;
    setValues((vals) => vals.map((x, idx) => (idx === i ? next : x)));
    if (amountError) setAmountError(null);
  };

  const requestPublish = () => {
    Keyboard.dismiss();
    if (total <= 0) {
      setAmountError('Enter at least one amount greater than 0');
      return;
    }
    setConfirm(true);
  };

  const publish = async () => {
    if (format?.kind !== 'ready') return;
    setSaving(true);
    const customFields = format.fields.map((f, i) => ({ fieldName: f.fieldName, type: f.type, value: values[i] ?? '' }));
    const res = await api.post('receipt/add', {
      currency: format.currency,
      receipt: { currency: format.currency, customFields, Date: toYmd(date), date: toYmd(date) },
      total,
      appartmentIds: apartments.map((a) => a.id).join(','),
      version: 'v2.0',
    });
    setSaving(false);
    setConfirm(false);
    if (res.ok) {
      showMessage({ message: 'Receipt added successfully', type: 'success' });
      setValues(fields.map(() => ''));
      setDate(new Date());
      navigation.setParams({ apartments: undefined });
    }
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={4} rowHeight={70} />
      </Screen>
    );
  }
  if (error && !format) {
    return (
      <Screen scroll={false}>
        <ErrorState message={error} onRetry={refetch} />
      </Screen>
    );
  }
  if (format?.kind === 'register') {
    return (
      <Screen scroll={false}>
        <EmptyState icon="home-outline" title="Register your apartment first" message="You need to be part of a building before you can issue receipts." actionTitle="Join a building" onAction={() => navigation.navigate('JoinBuilding')} />
      </Screen>
    );
  }
  if (format?.kind === 'building') {
    return (
      <Screen scroll={false}>
        <EmptyState icon="business-outline" title="Set up your building first" message="Add your building details so residents can join it, then come back to issue receipts." actionTitle="Set up building" onAction={() => navigation.navigate('SetupBuilding')} />
      </Screen>
    );
  }
  if (format?.kind === 'setup') {
    return (
      <Screen scroll={false}>
        <EmptyState icon="options-outline" title="Set up your receipt format" message="Choose the fields and currency your receipts use, then come back here." actionTitle="Set up receipt format" onAction={() => navigation.navigate('SetupReceipt')} />
      </Screen>
    );
  }
  if (format?.kind !== 'ready') return null;

  const unit = currencyLabel(format.currency);

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      <InfoBanner icon="warning-outline">Once a receipt is added it cannot be edited or deleted.</InfoBanner>

      <Card>
        <View style={{ gap: spacing.lg }}>
          {fields.map((f, i) => {
            const amount = isAmount(f);
            return (
              <TextField
                key={`${f.fieldName}-${i}`}
                label={f.fieldName}
                icon={amount ? 'cash-outline' : 'create-outline'}
                placeholder={amount ? `Amount in ${unit}` : 'Optional note'}
                keyboardType={amount ? 'decimal-pad' : 'default'}
                multiline={!amount}
                value={values[i] ?? ''}
                onChangeText={(v) => setValue(i, v, amount)}
              />
            );
          })}
          <DateField label="Receipt date" value={date} onChange={setDate} />
        </View>
      </Card>

      <Card flush style={{ marginTop: spacing.lg }}>
        <ListRow
          icon="people-outline"
          title="Apartments"
          subtitle={apartments.length ? apartments.map((a) => a.appartmentNumber).join(' · ') : 'Everyone in the building'}
          onPress={() => navigation.navigate('SelectApartments', { selected: apartments })}
        />
      </Card>

      <View
        style={{
          marginTop: spacing.lg,
          padding: spacing.lg,
          borderRadius: radius.lg,
          backgroundColor: colors.primaryTint,
          flexDirection: 'row',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <Text variant="bodyStrong" color={colors.primaryDeep}>
          Total
        </Text>
        <Text variant="title" color={colors.primaryDeep}>
          {formatAmount(total, format.currency)} {unit}
        </Text>
      </View>
      {amountError ? (
        <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.xs + 2, marginTop: spacing.sm }}>
          <Ionicons name="alert-circle" size={16} color={colors.danger} />
          <Text variant="caption" color={colors.danger}>
            {amountError}
          </Text>
        </View>
      ) : null}

      <Button title="Add receipt" icon="paper-plane-outline" onPress={requestPublish} style={{ marginTop: spacing.xl }} />

      <ConfirmDialog
        visible={confirm}
        title="Add this receipt?"
        message={`Total ${formatAmount(total, format.currency)} ${unit} for ${apartments.length ? `${apartments.length} selected apartment${apartments.length > 1 ? 's' : ''}` : 'the whole building'}. You won't be able to edit or delete it afterwards.`}
        confirmTitle="Add receipt"
        loading={saving}
        onCancel={() => setConfirm(false)}
        onConfirm={publish}
      />
    </Screen>
  );
}
