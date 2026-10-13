import React, { useState } from 'react';
import { View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useApi } from '../api/useApi';
import { Button, Card, ConfirmDialog, DateField, EmptyState, ErrorState, ListSkeleton, Screen, Segmented, Sheet, Text, TextField, showMessage } from '../components/ui';
import { formatAmount, formatDate, fromYmd, toYmd } from '../functions/format';
import { newId } from '../functions/id';
import { useFetch } from '../hooks/useFetch';
import { colors, radius, spacing } from '../theme';

type Currency = 'USD' | 'LBP';
type TransactionType = 'income' | 'expense';

type Transaction = {
  id: string;
  description: string;
  amount: number;
  currency: Currency;
  type: TransactionType;
  /** "yyyy-MM-dd" */
  date: string;
};

type WalletData = {
  balances: Record<Currency, number>;
  count: number;
  hasMore: boolean;
  transactions: Transaction[];
};

const PAGE = 100;

function selectWallet(raw: unknown): WalletData {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Partial<WalletData>;
  return {
    balances: { USD: Number(r.balances?.USD ?? 0), LBP: Number(r.balances?.LBP ?? 0) },
    count: Number(r.count ?? 0),
    hasMore: !!r.hasMore,
    transactions: Array.isArray(r.transactions) ? r.transactions : [],
  };
}

const money = (value: number, currency: Currency) => `${value < 0 ? '-' : ''}${formatAmount(Math.abs(value), currency)} ${currency}`;

export default function Wallet() {
  const api = useApi();
  const [limit, setLimit] = useState(PAGE);
  const { data, loading, error, refreshing, refetch } = useFetch<WalletData>(`wallet?limit=${limit}`, { refetchOnFocus: true, select: selectWallet });

  const [editing, setEditing] = useState<Transaction | null>(null);
  const [sheetOpen, setSheetOpen] = useState(false);
  const [type, setType] = useState<TransactionType>('expense');
  const [currency, setCurrency] = useState<Currency>('USD');
  const [amount, setAmount] = useState('');
  const [description, setDescription] = useState('');
  const [date, setDate] = useState(new Date());
  const [errors, setErrors] = useState<{ amount?: string; description?: string }>({});
  const [saving, setSaving] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  // One id per "new entry" form, reused on retries so a lost response can never create a duplicate.
  const [draftId, setDraftId] = useState(newId);

  const balances = data?.balances ?? { USD: 0, LBP: 0 };
  const primaryCurrency: Currency = balances.USD === 0 && balances.LBP !== 0 ? 'LBP' : 'USD';
  const otherCurrency: Currency = primaryCurrency === 'USD' ? 'LBP' : 'USD';
  const hasOther = balances[otherCurrency] !== 0;

  const openNew = () => {
    setEditing(null);
    setType('expense');
    setCurrency('USD');
    setAmount('');
    setDescription('');
    setDate(new Date());
    setErrors({});
    setDraftId(newId());
    setSheetOpen(true);
  };

  const openEdit = (t: Transaction) => {
    setEditing(t);
    setType(t.type);
    setCurrency(t.currency);
    setAmount(String(t.amount));
    setDescription(t.description);
    setDate(fromYmd(t.date));
    setErrors({});
    setSheetOpen(true);
  };

  const save = async () => {
    const value = Number(amount.replace(',', '.'));
    const next: typeof errors = {};
    if (!amount || Number.isNaN(value) || value <= 0) next.amount = 'Enter an amount greater than 0';
    else if (Math.round(value * 100) / 100 !== value) next.amount = 'Use at most 2 decimal places';
    if (!description.trim()) next.description = 'Add a short description';
    setErrors(next);
    if (Object.keys(next).length) return;

    setSaving(true);
    const body = { description: description.trim(), amount: value, currency, type, date: toYmd(date) };
    const res = editing
      ? await api.post(`wallet/update/${editing.id}`, body)
      : await api.post('wallet/add', { ...body, id: draftId });
    setSaving(false);

    if (res.ok) {
      setSheetOpen(false);
      showMessage({ message: editing ? 'Entry updated' : 'Entry added', type: 'success' });
      void refetch();
    }
  };

  const remove = async () => {
    if (!editing) return;
    setSaving(true);
    const res = await api.post(`wallet/delete/${editing.id}`, {});
    setSaving(false);
    setConfirmDelete(false);
    if (res.ok) {
      setSheetOpen(false);
      showMessage({ message: 'Entry deleted', type: 'success' });
      void refetch();
    }
  };

  const list = data?.transactions ?? [];

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={4} rowHeight={72} />
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

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      <Card>
        <Text variant="caption" color={colors.textMuted}>
          Your wallet balance
        </Text>
        <Text numeral variant="display" adjustsFontSizeToFit numberOfLines={1} style={{ fontSize: 32, lineHeight: 40 }}>
          {money(balances[primaryCurrency], primaryCurrency)}
        </Text>
        {hasOther ? (
          <Text numeral variant="bodyStrong" color={colors.textMuted} style={{ marginTop: spacing.xs }}>
            {money(balances[otherCurrency], otherCurrency)}
          </Text>
        ) : null}
      </Card>

      <Button title="Add entry" icon="add" onPress={openNew} style={{ marginTop: spacing.lg }} />

      <View style={{ gap: spacing.sm, marginTop: spacing.lg }}>
        {list.length === 0 ? (
          <EmptyState icon="wallet-outline" title="No entries yet" message="Track your income and expenses. Add your first entry above." />
        ) : (
          list.map((t) => (
            <Card key={t.id} onPress={() => openEdit(t)} accessibilityLabel={`${t.type === 'income' ? 'Income' : 'Expense'}: ${t.description}, ${money(t.amount, t.currency)}. Tap to edit`} style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.md }}>
              <View
                style={{
                  width: 40,
                  height: 40,
                  borderRadius: radius.md,
                  alignItems: 'center',
                  justifyContent: 'center',
                  backgroundColor: t.type === 'income' ? colors.primarySoft : colors.surfaceMuted,
                }}
              >
                <Ionicons name={t.type === 'income' ? 'arrow-down' : 'arrow-up'} size={18} color={t.type === 'income' ? colors.primary : colors.textMuted} />
              </View>
              <View style={{ flex: 1 }}>
                <Text variant="bodyStrong" numberOfLines={1}>
                  {t.description}
                </Text>
                <Text variant="caption" color={colors.textMuted}>
                  {formatDate(fromYmd(t.date))}
                </Text>
              </View>
              <Text numeral variant="bodyStrong" color={t.type === 'income' ? colors.primaryDeep : colors.text}>
                {t.type === 'income' ? '+' : '-'}
                {money(t.amount, t.currency)}
              </Text>
            </Card>
          ))
        )}
      </View>

      {data?.hasMore && limit < 500 ? (
        <Button title="Show more" variant="secondary" onPress={() => setLimit((l) => Math.min(l + PAGE, 500))} style={{ marginTop: spacing.lg }} />
      ) : null}

      <Sheet visible={sheetOpen} onClose={() => !saving && setSheetOpen(false)} title={editing ? 'Edit entry' : 'New entry'}>
        <View style={{ gap: spacing.lg }}>
          <Segmented
            options={[
              { value: 'expense', label: 'Expense' },
              { value: 'income', label: 'Income' },
            ]}
            value={type}
            onChange={setType}
          />
          <Segmented
            options={[
              { value: 'USD', label: 'USD' },
              { value: 'LBP', label: 'LBP' },
            ]}
            value={currency}
            onChange={setCurrency}
          />
          <TextField
            label={`Amount (${currency})`}
            icon="cash-outline"
            placeholder={currency === 'USD' ? '0.00' : '0'}
            keyboardType="decimal-pad"
            value={amount}
            onChangeText={(v) => setAmount(v.replace(/[^0-9.,]/g, ''))}
            error={errors.amount}
          />
          <TextField label="Description" icon="create-outline" placeholder="What was it for?" maxLength={200} value={description} onChangeText={setDescription} error={errors.description} />
          <DateField label="Date" value={date} onChange={setDate} />
          <Button title={editing ? 'Save changes' : 'Add entry'} loading={saving} onPress={save} />
          {editing ? <Button title="Delete entry" variant="danger" disabled={saving} onPress={() => setConfirmDelete(true)} /> : null}
        </View>
      </Sheet>

      <ConfirmDialog
        visible={confirmDelete}
        title="Delete this entry?"
        message={editing ? `"${editing.description}" will be removed from your wallet.` : undefined}
        confirmTitle="Delete"
        destructive
        loading={saving}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={remove}
      />
    </Screen>
  );
}
