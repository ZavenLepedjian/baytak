import React, { useEffect, useRef, useState } from 'react';
import { Pressable, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useApi } from '../../api/useApi';
import { Button, Card, ErrorState, InfoBanner, ListSkeleton, Screen, Segmented, SectionHeader, Text, TextField, showMessage } from '../../components/ui';
import { useFetch } from '../../hooks/useFetch';
import { colors, hitSlop, spacing } from '../../theme';

type FieldType = 'Amount' | 'Text';
type Row = { key: number; name: string; type: FieldType };
type Loaded = { currency: string; rows: Omit<Row, 'key'>[] };

const defaults: Loaded['rows'] = [
  { name: 'Maintenance', type: 'Amount' },
  { name: 'Electricity', type: 'Amount' },
  { name: 'Security', type: 'Amount' },
  { name: 'Elevator', type: 'Amount' },
  { name: 'Note', type: 'Text' },
];

/** Existing format (JSON string) or, when none is saved yet / the account isn't ready, the defaults. */
function selectFormat(raw: unknown): Loaded {
  try {
    const parsed = typeof raw === 'string' ? JSON.parse(raw) : (raw as any);
    const list: any[] = Array.isArray(parsed?.customFields) ? parsed.customFields : [];
    if (list.length) {
      return {
        currency: String(parsed.currency ?? ''),
        rows: list.map((f) => ({ name: String(f.fieldName ?? ''), type: String(f.type).toLowerCase() === 'amount' ? 'Amount' : 'Text' })),
      };
    }
  } catch {
    // "RegisterAppartment" / "SetupReceipt" markers are not JSON: use the defaults.
  }
  return { currency: '', rows: defaults };
}

export default function SetupReceipt() {
  const api = useApi();
  const { data, loading, error, refetch } = useFetch<Loaded>('get-receipt-format', { select: selectFormat });
  const nextKey = useRef(1);
  const [rows, setRows] = useState<Row[]>([]);
  const [currency, setCurrency] = useState('');
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const synced = useRef(false);

  useEffect(() => {
    if (data && !synced.current) {
      synced.current = true;
      setRows(data.rows.map((r) => ({ ...r, key: nextKey.current++ })));
      setCurrency(data.currency);
    }
  }, [data]);

  const update = (key: number, patch: Partial<Row>) => {
    setRows((list) => list.map((r) => (r.key === key ? { ...r, ...patch } : r)));
    setFormError(null);
  };
  const remove = (key: number) => setRows((list) => list.filter((r) => r.key !== key));
  const add = () => setRows((list) => [...list, { key: nextKey.current++, name: '', type: 'Text' }]);

  const validate = (): string | null => {
    if (!currency) return 'Please select a currency';
    const names = rows.map((r) => r.name.trim());
    if (names.length === 0) return 'Add at least one field';
    if (names.some((n) => !n)) return 'Every field needs a name';
    if (new Set(names.map((n) => n.toLowerCase())).size !== names.length) return 'Field names must be unique';
    if (!rows.some((r) => r.type === 'Amount')) return 'Add at least one Amount field';
    return null;
  };

  const save = async () => {
    const problem = validate();
    setFormError(problem);
    if (problem) return;

    setSaving(true);
    const res = await api.post('save-receipt-format', {
      currency,
      customFields: rows.map((r) => ({ fieldName: r.name.trim(), type: r.type, value: '' })),
      Date: '',
    });
    setSaving(false);
    if (res.ok) showMessage({ message: 'Receipt format saved successfully', type: 'success' });
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={5} rowHeight={72} />
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
    <Screen>
      <InfoBanner>Define the fields on your receipts. Changes apply to receipts you create from now on.</InfoBanner>

      <SectionHeader title="Currency" />
      <Segmented
        options={[
          { value: 'USD', label: 'USD' },
          { value: 'LBP', label: 'LBP' },
        ]}
        value={currency as 'USD' | 'LBP' | ''}
        onChange={(v) => {
          setCurrency(v);
          setFormError(null);
        }}
      />

      <SectionHeader title="Fields" />
      <View style={{ gap: spacing.md }}>
        {rows.map((r) => (
          <Card key={r.key}>
            <View style={{ flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm }}>
              <TextField containerStyle={{ flex: 1 }} label="Field name" placeholder="e.g. Water" value={r.name} onChangeText={(v) => update(r.key, { name: v })} />
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={`Remove ${r.name || 'field'}`}
                hitSlop={hitSlop}
                onPress={() => remove(r.key)}
                style={{ marginTop: 22, width: 44, height: 52, alignItems: 'center', justifyContent: 'center' }}
              >
                <Ionicons name="trash-outline" size={22} color={colors.danger} />
              </Pressable>
            </View>
            <View style={{ marginTop: spacing.md }}>
              <Segmented
                options={[
                  { value: 'Amount', label: 'Amount' },
                  { value: 'Text', label: 'Text / note' },
                ]}
                value={r.type}
                onChange={(v) => update(r.key, { type: v })}
              />
            </View>
          </Card>
        ))}
      </View>

      <Button title="Add field" icon="add" variant="secondary" onPress={add} style={{ marginTop: spacing.lg }} />

      {formError ? (
        <Text variant="caption" color={colors.danger} style={{ marginTop: spacing.lg }} accessibilityLiveRegion="polite">
          {formError}
        </Text>
      ) : null}

      <Button title="Save format" loading={saving} onPress={save} style={{ marginTop: spacing.xl }} />
    </Screen>
  );
}
