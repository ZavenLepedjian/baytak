import React, { useEffect, useMemo, useRef, useState } from 'react';
import { Keyboard, Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useApi } from '../../api/useApi';
import { PhotoPicker, MAX_RECEIPT_PHOTOS, usePhotoUploads } from '../../components/ReceiptPhotos';
import {
  Button,
  Card,
  ConfirmDialog,
  DateField,
  Divider,
  EmptyState,
  ErrorState,
  InfoBanner,
  ListRow,
  ListSkeleton,
  Screen,
  SectionHeader,
  Segmented,
  Sheet,
  Text,
  TextField,
  showMessage,
} from '../../components/ui';
import { amountValue, normalizeAmountInput } from '../../functions/amount';
import { currencyLabel, formatAmount, formatDate, fromYmd, toYmd } from '../../functions/format';
import { STARTER_TEMPLATES } from '../../functions/templates';
import { useFetch } from '../../hooks/useFetch';
import type { ScreenProps } from '../../navigation/types';
import { colors, HIT, radius, spacing } from '../../theme';

type Currency = 'USD' | 'LBP';
type Field = { fieldName: string; type: string; value: string };
type Line = { key: number; name: string; type: 'Amount' | 'Text'; value: string };

type FormatState =
  | { kind: 'ready'; currency: Currency; fields: Field[] }
  | { kind: 'register' }
  | { kind: 'building' }
  | { kind: 'setup' };

type SavedTemplate = { id: number; name: string; currency: Currency; customFields: Field[] };
type Starts = { templates: SavedTemplate[]; last: { currency: Currency; date: string; customFields: Field[] } | null };

const MAX_LINES = 30;
const isAmount = (type: string | undefined) => (type ?? '').toLowerCase() === 'amount';
const asCurrency = (value: unknown): Currency => (String(value).toUpperCase() === 'LBP' ? 'LBP' : 'USD');
const asField = (f: any): Field => ({ fieldName: String(f?.fieldName ?? ''), type: String(f?.type ?? 'Text'), value: String(f?.value ?? '') });

/** The endpoint answers with a JSON string, or one of two marker strings when prerequisites are missing. */
function selectFormat(raw: unknown): FormatState {
  if (raw === 'RegisterAppartment') return { kind: 'register' };
  if (raw === 'SetupBuilding') return { kind: 'building' };
  if (raw === 'SetupReceipt' || raw === null || raw === undefined || raw === '') return { kind: 'setup' };
  const parsed = typeof raw === 'string' ? JSON.parse(raw) : (raw as any);
  const list: any[] = Array.isArray(parsed?.customFields) ? parsed.customFields : [];
  if (list.length === 0) return { kind: 'setup' };
  return { kind: 'ready', currency: asCurrency(parsed.currency), fields: list.map(asField) };
}

function selectStarts(raw: unknown): Starts {
  const r = (raw && typeof raw === 'object' ? raw : {}) as any;
  const templates: SavedTemplate[] = (Array.isArray(r.templates) ? r.templates : []).map((t: any) => ({
    id: Number(t.id),
    name: String(t.name ?? ''),
    currency: asCurrency(t.currency),
    customFields: (Array.isArray(t.customFields) ? t.customFields : []).map(asField),
  }));
  const last = r.last && Array.isArray(r.last.customFields) ? { currency: asCurrency(r.last.currency), date: String(r.last.date ?? ''), customFields: r.last.customFields.map(asField) } : null;
  return { templates, last };
}

type StartOption = { id: string; title: string; subtitle: string; apply: () => void; remove?: () => void };

export default function CreateReceipt({ navigation, route }: ScreenProps<'CreateReceipt'>) {
  const api = useApi();
  const { data: format, loading, error, refetch } = useFetch<FormatState>('get-receipt-format', { select: selectFormat });
  const { data: starts, refetch: refetchStarts } = useFetch<Starts>('receipt/templates', { select: selectStarts });
  const uploads = usePhotoUploads(MAX_RECEIPT_PHOTOS);

  const nextKey = useRef(1);
  const [lines, setLines] = useState<Line[]>([]);
  const [currency, setCurrency] = useState<Currency>('USD');
  const [source, setSource] = useState('');
  const [date, setDate] = useState(new Date());
  const [confirm, setConfirm] = useState(false);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [startOpen, setStartOpen] = useState(false);
  const [pendingStart, setPendingStart] = useState<StartOption | null>(null);
  const [templateName, setTemplateName] = useState<string | null>(null);
  const [savingTemplate, setSavingTemplate] = useState(false);
  const [deleting, setDeleting] = useState<SavedTemplate | null>(null);
  const initialised = useRef(false);

  const apartments = route.params?.apartments ?? [];

  const toLines = (items: { name: string; type: string; value?: string }[], keepValues: boolean, forCurrency: Currency): Line[] =>
    items.map((f) => {
      const type = isAmount(f.type) ? 'Amount' : 'Text';
      const value = keepValues ? (type === 'Amount' ? normalizeAmountInput(f.value ?? '', forCurrency) : (f.value ?? '')) : '';
      return { key: nextKey.current++, name: f.name, type, value };
    });
  // Amount lines first, in the order they were set up.
  const amountsFirst = (items: Field[]) => [...items].sort((a, b) => Number(isAmount(b.type)) - Number(isAmount(a.type)));

  // Start from the building's receipt format; with none saved yet, from the monthly starter.
  useEffect(() => {
    if (initialised.current || !format) return;
    if (format.kind === 'ready') {
      initialised.current = true;
      setCurrency(format.currency);
      setLines(toLines(amountsFirst(format.fields).map((f) => ({ name: f.fieldName, type: f.type })), false, format.currency));
      setSource('Your building format');
    } else if (format.kind === 'setup') {
      initialised.current = true;
      setLines(toLines(STARTER_TEMPLATES[0].lines, false, 'USD'));
      setSource(STARTER_TEMPLATES[0].name);
    }
  }, [format]);

  const total = useMemo(() => lines.reduce((sum, l) => (l.type === 'Amount' ? sum + amountValue(l.value) : sum), 0), [lines]);
  const dirty = lines.some((l) => l.value.trim() !== '');
  const unit = currencyLabel(currency);

  const updateLine = (key: number, patch: Partial<Line>) => {
    setLines((list) => list.map((l) => (l.key === key ? { ...l, ...patch } : l)));
    setFormError(null);
  };
  const removeLine = (key: number) => setLines((list) => list.filter((l) => l.key !== key));
  const addLine = (type: Line['type']) => {
    if (lines.length >= MAX_LINES) {
      showMessage({ message: `A receipt can have at most ${MAX_LINES} lines`, type: 'warning' });
      return;
    }
    setLines((list) => [...list, { key: nextKey.current++, name: '', type, value: '' }]);
  };
  const changeCurrency = (next: Currency) => {
    setCurrency(next);
    // Lira has no cents: separators typed for dollars are removed, so nothing is silently misread.
    setLines((list) => list.map((l) => (l.type === 'Amount' ? { ...l, value: normalizeAmountInput(l.value, next) } : l)));
    setFormError(null);
  };

  // ----- where to start from
  const choose = (option: StartOption) => {
    setStartOpen(false);
    if (dirty) setPendingStart(option);
    else option.apply();
  };
  const startOptions = useMemo(() => {
    const groups: { title: string; items: StartOption[] }[] = [];
    if (format?.kind === 'ready') {
      groups.push({
        title: 'Your building',
        items: [
          {
            id: 'format',
            title: 'Building format',
            subtitle: format.fields.map((f) => f.fieldName).join(', '),
            apply: () => {
              setCurrency(format.currency);
              setLines(toLines(amountsFirst(format.fields).map((f) => ({ name: f.fieldName, type: f.type })), false, format.currency));
              setSource('Your building format');
            },
          },
        ],
      });
    }
    if (starts?.last) {
      const last = starts.last;
      groups.push({
        title: 'Repeat',
        items: [
          {
            id: 'last',
            title: 'Same as the last receipt',
            subtitle: `${formatDate(fromYmd(last.date))}, lines and amounts`,
            apply: () => {
              setCurrency(last.currency);
              setLines(toLines(last.customFields.map((f) => ({ name: f.fieldName, type: f.type, value: f.value })), true, last.currency));
              setSource(`Last receipt, ${formatDate(fromYmd(last.date))}`);
            },
          },
        ],
      });
    }
    if (starts && starts.templates.length > 0) {
      groups.push({
        title: 'Your templates',
        items: starts.templates.map((t) => ({
          id: `t${t.id}`,
          title: t.name,
          subtitle: `${t.currency} · ${t.customFields.map((f) => f.fieldName).join(', ')}`,
          apply: () => {
            setCurrency(t.currency);
            setLines(toLines(t.customFields.map((f) => ({ name: f.fieldName, type: f.type })), false, t.currency));
            setSource(`Template: ${t.name}`);
          },
          remove: () => setDeleting(t),
        })),
      });
    }
    groups.push({
      title: 'Starters',
      items: STARTER_TEMPLATES.map((s) => ({
        id: `s-${s.id}`,
        title: s.name,
        subtitle: s.description,
        apply: () => {
          setLines(toLines(s.lines, false, currency));
          setSource(s.name);
        },
      })),
    });
    return groups;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [format, starts, currency]);

  const saveTemplate = async () => {
    const name = (templateName ?? '').trim();
    if (!name) {
      showMessage({ message: 'Give the template a name', type: 'warning' });
      return;
    }
    const used = lines.filter((l) => l.name.trim() !== '');
    setSavingTemplate(true);
    const res = await api.post('receipt/templates/save', {
      name,
      currency,
      customFields: used.map((l) => ({ fieldName: l.name.trim(), type: l.type, value: '' })),
    });
    setSavingTemplate(false);
    if (res.ok) {
      setTemplateName(null);
      setSource(`Template: ${name}`);
      showMessage({ message: 'Template saved', type: 'success' });
      void refetchStarts();
    }
  };
  const deleteTemplate = async () => {
    if (!deleting) return;
    const res = await api.post(`receipt/templates/delete/${deleting.id}`, {});
    setDeleting(null);
    if (res.ok) void refetchStarts();
  };

  // ----- issuing
  /** Lines that will be on the receipt: blank amounts and empty notes are left off. */
  const sendLines = useMemo(
    () => lines.filter((l) => (l.type === 'Amount' ? amountValue(l.value) !== 0 : l.value.trim() !== '')),
    [lines],
  );

  const check = (): string | null => {
    if (total <= 0) return 'Enter at least one amount greater than 0';
    if (sendLines.some((l) => l.name.trim() === '')) return 'Every line with a value needs a name';
    const names = sendLines.map((l) => l.name.trim().toLowerCase());
    if (new Set(names).size !== names.length) return 'Line names must be different from each other';
    if (uploads.uploading) return 'Wait for the photos to finish uploading';
    if (uploads.failed) return 'Retry or remove the photo that did not upload';
    return null;
  };

  const requestPublish = () => {
    Keyboard.dismiss();
    const problem = check();
    setFormError(problem);
    if (!problem) setConfirm(true);
  };

  const summary = () => {
    const rows = sendLines.map((l) =>
      l.type === 'Amount' ? `${l.name.trim()}: ${formatAmount(amountValue(l.value), currency)} ${unit}` : `${l.name.trim()}: ${l.value.trim().slice(0, 60)}`,
    );
    const who = apartments.length ? `${apartments.length} selected apartment${apartments.length > 1 ? 's' : ''}` : 'the whole building';
    const photos = uploads.readyIds.length ? `\n${uploads.readyIds.length} photo${uploads.readyIds.length > 1 ? 's' : ''} attached.` : '';
    return `${rows.join('\n')}\n\nTotal ${formatAmount(total, currency)} ${unit} for ${who}.${photos}\nYou won't be able to edit or delete it afterwards.`;
  };

  const publish = async () => {
    setSaving(true);
    const customFields = sendLines.map((l) => ({ fieldName: l.name.trim(), type: l.type, value: l.type === 'Amount' ? String(amountValue(l.value)) : l.value.trim() }));
    const res = await api.post('receipt/add', {
      currency,
      receipt: { currency, customFields, Date: toYmd(date), date: toYmd(date) },
      total,
      appartmentIds: apartments.map((a) => a.id).join(','),
      photoIds: uploads.readyIds,
      version: 'v2.0',
    });
    setSaving(false);
    setConfirm(false);
    if (res.ok) {
      showMessage({ message: 'Receipt added successfully', type: 'success' });
      setLines((list) => list.map((l) => ({ ...l, value: '' })));
      uploads.clear();
      setDate(new Date());
      navigation.setParams({ apartments: undefined });
      void refetchStarts();
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
  if (!format) return null;

  const hasFormatCurrency = format.kind === 'ready' && format.currency !== currency;

  return (
    <Screen>
      <InfoBanner icon="warning-outline">Once a receipt is added it cannot be edited or deleted. Check the lines and amounts before you add it.</InfoBanner>

      <Card flush style={{ marginTop: spacing.lg }}>
        <ListRow icon="albums-outline" title="Start from" subtitle={source || 'Choose a starting point'} onPress={() => setStartOpen(true)} />
      </Card>

      <SectionHeader title="Currency" />
      <Segmented
        options={[
          { value: 'USD', label: 'USD' },
          { value: 'LBP', label: 'LBP' },
        ]}
        value={currency}
        onChange={changeCurrency}
      />
      {hasFormatCurrency ? (
        <Text variant="caption" color={colors.textMuted} style={{ marginTop: spacing.xs }}>
          Your building format uses {format.kind === 'ready' ? format.currency : ''}. This receipt only is in {currency}.
        </Text>
      ) : null}

      <SectionHeader
        title="Lines"
        action={
          <Pressable accessibilityRole="button" accessibilityLabel="Save these lines as a template" hitSlop={8} onPress={() => setTemplateName('')}>
            <Text variant="bodyStrong" color={colors.primary}>
              Save as template
            </Text>
          </Pressable>
        }
      />
      <Card>
        <View style={{ gap: spacing.lg }}>
          {lines.map((line) => (
            <LineRow key={line.key} line={line} currency={currency} onChange={(patch) => updateLine(line.key, patch)} onRemove={() => removeLine(line.key)} />
          ))}
          <View style={styles.addRow}>
            <Button small variant="secondary" icon="add" title="Add amount" onPress={() => addLine('Amount')} style={{ flex: 1 }} />
            <Button small variant="secondary" icon="create-outline" title="Add note" onPress={() => addLine('Text')} style={{ flex: 1 }} />
          </View>
          <DateField label="Receipt date" value={date} onChange={setDate} />
        </View>
      </Card>

      <SectionHeader title="Photos of the bill (optional)" />
      <Card>
        <PhotoPicker uploads={uploads} />
      </Card>

      <Card flush style={{ marginTop: spacing.lg }}>
        <ListRow
          icon="people-outline"
          title="Apartments"
          subtitle={apartments.length ? apartments.map((a) => a.appartmentNumber).join(' · ') : 'Everyone in the building'}
          onPress={() => navigation.navigate('SelectApartments', { selected: apartments })}
        />
      </Card>

      <View style={styles.total}>
        <Text variant="bodyStrong" color={colors.primaryDeep}>
          Total
        </Text>
        <Text variant="title" color={colors.primaryDeep}>
          {formatAmount(total, currency)} {unit}
        </Text>
      </View>
      {formError ? (
        <View style={styles.error}>
          <Ionicons name="alert-circle" size={16} color={colors.danger} />
          <Text variant="caption" color={colors.danger} style={{ flex: 1 }} accessibilityLiveRegion="polite">
            {formError}
          </Text>
        </View>
      ) : null}

      <Button title="Add receipt" icon="paper-plane-outline" onPress={requestPublish} style={{ marginTop: spacing.xl }} />

      <ConfirmDialog
        visible={confirm}
        title="Add this receipt?"
        message={summary()}
        confirmTitle="Add receipt"
        loading={saving}
        onCancel={() => setConfirm(false)}
        onConfirm={publish}
      />

      <ConfirmDialog
        visible={!!pendingStart}
        title="Replace what you typed?"
        message="The lines and amounts on this screen will be replaced."
        confirmTitle="Replace"
        onCancel={() => setPendingStart(null)}
        onConfirm={() => {
          pendingStart?.apply();
          setPendingStart(null);
        }}
      />

      <ConfirmDialog
        visible={!!deleting}
        title="Delete this template?"
        message={deleting ? `"${deleting.name}" will be removed. Receipts you already issued are not affected.` : undefined}
        confirmTitle="Delete"
        destructive
        onCancel={() => setDeleting(null)}
        onConfirm={deleteTemplate}
      />

      <Sheet visible={templateName !== null} onClose={() => setTemplateName(null)} title="Save as template">
        <View style={{ gap: spacing.lg }}>
          <TextField label="Template name" placeholder="e.g. Monthly fees" value={templateName ?? ''} onChangeText={setTemplateName} maxLength={60} autoFocus />
          <Text variant="caption" color={colors.textMuted}>
            Saves the {currency} currency and the line names. Amounts are not saved: you type them for each receipt.
          </Text>
          <Button title="Save template" loading={savingTemplate} onPress={saveTemplate} />
        </View>
      </Sheet>

      <Sheet visible={startOpen} onClose={() => setStartOpen(false)} title="Start from">
        <View>
          {startOptions.map((group) => (
            <View key={group.title}>
              <SectionHeader title={group.title} />
              <Card flush>
                {group.items.map((item, i) => (
                  <View key={item.id}>
                    {i > 0 ? <Divider /> : null}
                    <View style={styles.optionRow}>
                      <Pressable
                        style={({ pressed }) => [styles.optionMain, pressed && { backgroundColor: colors.primaryTint }]}
                        accessibilityRole="button"
                        accessibilityLabel={`${item.title}. ${item.subtitle}`}
                        onPress={() => choose(item)}
                      >
                        <Text variant="bodyStrong">{item.title}</Text>
                        <Text variant="caption" color={colors.textMuted} numberOfLines={2}>
                          {item.subtitle}
                        </Text>
                      </Pressable>
                      {item.remove ? (
                        <Pressable style={styles.trash} accessibilityRole="button" accessibilityLabel={`Delete template ${item.title}`} onPress={item.remove} hitSlop={6}>
                          <Ionicons name="trash-outline" size={20} color={colors.danger} />
                        </Pressable>
                      ) : null}
                    </View>
                  </View>
                ))}
              </Card>
            </View>
          ))}
        </View>
      </Sheet>
    </Screen>
  );
}

/** One line of the receipt: its name, and the amount (or the note) to go with it. Any line can be renamed or removed. */
function LineRow({ line, currency, onChange, onRemove }: { line: Line; currency: Currency; onChange: (patch: Partial<Line>) => void; onRemove: () => void }) {
  const amount = line.type === 'Amount';
  const typed = amount && line.value.replace(/\D/g, '').length >= 4 ? `${formatAmount(amountValue(line.value), currency)} ${currencyLabel(currency)}` : undefined;
  return (
    <View style={{ gap: spacing.sm }}>
      <View style={styles.lineTop}>
        <TextField
          containerStyle={{ flex: 1 }}
          placeholder={amount ? 'Line name, e.g. Water' : 'Note title'}
          accessibilityLabel={amount ? 'Amount line name' : 'Note title'}
          value={line.name}
          onChangeText={(name) => onChange({ name })}
          maxLength={60}
        />
        {amount ? (
          <TextField
            containerStyle={{ width: 132 }}
            placeholder={currency}
            accessibilityLabel={`${line.name || 'Amount'} in ${currency}`}
            keyboardType={currency === 'USD' ? 'decimal-pad' : 'number-pad'}
            value={line.value}
            onChangeText={(v) => onChange({ value: normalizeAmountInput(v, currency) })}
            helper={typed}
          />
        ) : null}
        <Pressable style={styles.removeLine} accessibilityRole="button" accessibilityLabel={`Remove ${line.name || 'this line'}`} onPress={onRemove} hitSlop={6}>
          <Ionicons name="close" size={22} color={colors.textMuted} />
        </Pressable>
      </View>
      {amount ? null : (
        <TextField
          placeholder="Optional note"
          accessibilityLabel={`${line.name || 'Note'} text`}
          multiline
          value={line.value}
          onChangeText={(value) => onChange({ value })}
          maxLength={500}
        />
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  lineTop: { flexDirection: 'row', alignItems: 'flex-start', gap: spacing.sm },
  removeLine: { width: HIT - 6, height: 52, alignItems: 'center', justifyContent: 'center' },
  addRow: { flexDirection: 'row', gap: spacing.sm },
  total: {
    marginTop: spacing.lg,
    padding: spacing.lg,
    borderRadius: radius.lg,
    backgroundColor: colors.primaryTint,
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  error: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs + 2, marginTop: spacing.sm },
  optionRow: { flexDirection: 'row', alignItems: 'center' },
  optionMain: { flex: 1, paddingVertical: spacing.md, paddingHorizontal: spacing.lg, minHeight: HIT + spacing.md },
  trash: { width: HIT, height: HIT, alignItems: 'center', justifyContent: 'center' },
});
