import React, { useEffect, useRef, useState } from 'react';
import { View } from 'react-native';
import { useApi } from '../../api/useApi';
import { useAuth } from '../../auth/AuthProvider';
import { Button, Card, ErrorState, InfoBanner, ListSkeleton, Screen, TextField, showMessage } from '../../components/ui';
import { useFetch } from '../../hooks/useFetch';
import { spacing } from '../../theme';
import { t } from '../../i18n';

type UserProfile = {
  firstName: string;
  lastName: string;
  emergencyContactPersonPhoneNumber: string;
  emergencyContactPersonFullName: string;
  [key: string]: unknown;
};

const empty: UserProfile = { firstName: '', lastName: '', emergencyContactPersonPhoneNumber: '', emergencyContactPersonFullName: '' };

export default function Profile() {
  const api = useApi();
  const { phoneNumber, setFullName } = useAuth();
  const { data, loading, error, refetch } = useFetch<UserProfile>('user/get/profile', {
    select: (raw) => ({ ...empty, ...((raw && typeof raw === 'object' ? raw : {}) as Partial<UserProfile>) }),
  });

  const [form, setForm] = useState<UserProfile>(empty);
  const [saving, setSaving] = useState(false);
  const [errors, setErrors] = useState<{ firstName?: string; lastName?: string }>({});
  const loaded = useRef(false);

  useEffect(() => {
    if (data && !loaded.current) {
      loaded.current = true;
      setForm(data);
      setFullName(data.firstName ? `${data.firstName} ${data.lastName ?? ''}`.trim() : '');
    }
  }, [data, setFullName]);

  const update = (patch: Partial<UserProfile>) => setForm((f) => ({ ...f, ...patch }));

  const save = async () => {
    const next: typeof errors = {};
    if (!form.firstName.trim()) next.firstName = t('First name is required');
    if (!form.lastName.trim()) next.lastName = t('Last name is required');
    setErrors(next);
    if (Object.keys(next).length) return;

    setSaving(true);
    const body = { ...form, firstName: form.firstName.trim(), lastName: form.lastName.trim(), phoneNumber };
    const res = await api.post<string>('user/save/profile', body);
    setSaving(false);
    if (res.ok) {
      setFullName(`${body.firstName} ${body.lastName}`);
      showMessage({ message: typeof res.data === 'string' && res.data ? res.data : t('Profile saved'), type: 'success' });
    }
  };

  if (loading) {
    return (
      <Screen>
        <ListSkeleton rows={3} rowHeight={80} />
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
      <InfoBanner icon="people-outline">{t('Your name and an emergency contact help your building reach you when it matters.')}</InfoBanner>

      <Card>
        <View style={{ gap: spacing.lg }}>
          <TextField label={t('Phone number')} icon="call-outline" keyboardType="phone-pad" value={phoneNumber} editable={false} />
          <TextField
            label={t('First name *')}
            icon="person-outline"
            placeholder={t('First name')}
            value={form.firstName}
            autoCapitalize="words"
            textContentType="givenName"
            onChangeText={(v) => update({ firstName: v })}
            error={errors.firstName}
          />
          <TextField
            label={t('Last name *')}
            icon="person-outline"
            placeholder={t('Last name')}
            value={form.lastName}
            autoCapitalize="words"
            textContentType="familyName"
            onChangeText={(v) => update({ lastName: v })}
            error={errors.lastName}
          />
        </View>
      </Card>

      <Card style={{ marginTop: spacing.lg }}>
        <View style={{ gap: spacing.lg }}>
          <TextField
            label={t('Emergency contact name')}
            icon="medkit-outline"
            placeholder={t('Full name')}
            value={form.emergencyContactPersonFullName}
            autoCapitalize="words"
            onChangeText={(v) => update({ emergencyContactPersonFullName: v })}
          />
          <TextField
            label={t('Emergency contact number')}
            icon="call-outline"
            placeholder={t('Phone number')}
            keyboardType="phone-pad"
            value={form.emergencyContactPersonPhoneNumber}
            onChangeText={(v) => update({ emergencyContactPersonPhoneNumber: v })}
          />
        </View>
      </Card>

      <Button title={t('Save changes')} loading={saving} onPress={save} style={{ marginTop: spacing.xl }} />
    </Screen>
  );
}
