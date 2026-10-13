import React, { useEffect, useRef, useState } from 'react';
import { Keyboard, View } from 'react-native';
import { useApi } from '../../api/useApi';
import { Badge, Button, Card, ErrorState, InfoBanner, ListSkeleton, Screen, Text, TextField, showMessage, statusTone } from '../../components/ui';
import { useFetch } from '../../hooks/useFetch';
import type { ScreenProps } from '../../navigation/types';
import { colors, spacing } from '../../theme';

type ApartmentForm = {
  appartmentNumber: string;
  appartmentDescription: string;
  floor: number;
  buildingId: number | string;
  buildingName: string;
  status: string;
};

const emptyForm: ApartmentForm = { appartmentNumber: '', appartmentDescription: '', floor: 0, buildingId: 0, buildingName: '', status: '' };

const randomId = (length = 8) => {
  const chars = 'abcdefghijklmnopqrstuvwxyz0123456789';
  let out = '';
  for (let i = 0; i < length; i++) out += chars[Math.floor(Math.random() * chars.length)];
  return out;
};

export default function JoinBuilding({ navigation, route }: ScreenProps<'JoinBuilding'>) {
  const api = useApi();
  const { data, loading, error, refreshing, refetch } = useFetch<ApartmentForm>('appartment/get', {
    select: (raw) => ({ ...emptyForm, ...((raw && typeof raw === 'object' ? raw : {}) as Partial<ApartmentForm>) }),
  });

  const [form, setForm] = useState<ApartmentForm>(emptyForm);
  const [errors, setErrors] = useState<{ floor?: string; building?: string; description?: string }>({});
  const [saving, setSaving] = useState(false);
  const syncedFrom = useRef<ApartmentForm | null>(null);

  // Load the server state into the form, but don't clobber edits when the same data re-renders.
  useEffect(() => {
    if (data && syncedFrom.current !== data) {
      syncedFrom.current = data;
      setForm(data);
    }
  }, [data]);

  // A building picked on the Buildings screen arrives as a route param.
  const picked = route.params?.building;
  useEffect(() => {
    if (picked) {
      setForm((f) => ({ ...f, buildingId: picked.id, buildingName: picked.label }));
      setErrors((e) => ({ ...e, building: undefined }));
    }
  }, [picked]);

  const editable = !form.status || form.status === 'Rejected';
  const update = (patch: Partial<ApartmentForm>) => setForm((f) => ({ ...f, ...patch }));

  const submit = async () => {
    Keyboard.dismiss();
    const next: typeof errors = {};
    if (!form.floor) next.floor = 'Please set the floor your apartment is on';
    if (!form.buildingId) next.building = 'Please select the building you want to join';
    if (!form.appartmentDescription.trim()) next.description = 'Please enter an apartment description';
    setErrors(next);
    if (Object.keys(next).length) return;

    setSaving(true);
    const res = await api.post<string>('appartment/request', {
      ...form,
      appartmentDescription: form.appartmentDescription.trim(),
      appartmentNumber: form.appartmentNumber.trim() || `${form.floor}-${randomId()}`,
    });
    setSaving(false);
    if (res.ok) {
      showMessage({ message: typeof res.data === 'string' && res.data ? res.data : 'Request sent', type: 'success' });
      syncedFrom.current = null;
      void refetch();
    }
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={3} rowHeight={90} />
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

  const statusMessage: Record<string, string> = {
    Pending: 'Your request is waiting for the building administration to approve it.',
    Approved: 'You are part of this building.',
    Rejected: 'Your request was rejected. You can update the details and try again.',
  };

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      {form.status ? (
        <Card style={{ marginBottom: spacing.lg, gap: spacing.sm }}>
          <Badge label={form.status} tone={statusTone(form.status)} />
          <Text variant="body" color={colors.textMuted}>
            {statusMessage[form.status] ?? ''}
          </Text>
        </Card>
      ) : (
        <InfoBanner>
          Registering your apartment adds you to the selected building. The building administration will verify your request before you can join.
        </InfoBanner>
      )}

      <Card>
        <View style={{ gap: spacing.lg }}>
          <TextField
            label="Building *"
            icon="business-outline"
            value={form.buildingName}
            placeholder="Choose a building"
            editable={false}
            error={errors.building}
          />
          {editable ? <Button title={form.buildingId ? 'Change building' : 'Choose building'} variant="secondary" small icon="search-outline" onPress={() => navigation.navigate('Buildings')} /> : null}

          <TextField
            label="Apartment number"
            icon="key-outline"
            placeholder="Apartment number"
            helper="Generated automatically if left empty"
            value={form.appartmentNumber}
            editable={editable}
            onChangeText={(v) => update({ appartmentNumber: v })}
          />
          <TextField
            label="Floor *"
            icon="layers-outline"
            placeholder="Floor"
            keyboardType="number-pad"
            value={form.floor ? String(form.floor) : ''}
            editable={editable}
            onChangeText={(v) => {
              update({ floor: Number(v.replace(/\D/g, '')) || 0 });
              if (errors.floor) setErrors((e) => ({ ...e, floor: undefined }));
            }}
            error={errors.floor}
          />
          <TextField
            label="Description *"
            placeholder="e.g. Corner apartment, facing the sea"
            multiline
            value={form.appartmentDescription}
            editable={editable}
            onChangeText={(v) => {
              update({ appartmentDescription: v });
              if (errors.description) setErrors((e) => ({ ...e, description: undefined }));
            }}
            error={errors.description}
          />
        </View>
      </Card>

      {editable ? (
        <Button title={form.status === 'Rejected' ? 'Request again' : 'Send request'} loading={saving} onPress={submit} style={{ marginTop: spacing.xl }} />
      ) : null}
    </Screen>
  );
}
