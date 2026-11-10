import React, { useEffect, useRef, useState } from 'react';
import { Keyboard, View } from 'react-native';
import { useApi } from '../../api/useApi';
import { Button, Card, ErrorState, InfoBanner, ListRow, ListSkeleton, Screen, TextField, showMessage } from '../../components/ui';
import { useFetch } from '../../hooks/useFetch';
import { useLocation } from '../../location/LocationProvider';
import type { ScreenProps } from '../../navigation/types';
import { spacing } from '../../theme';
import { t } from '../../i18n';

type Building = {
  buildingNumber: string;
  buildingName: string;
  city: string;
  country: string;
  street: string;
  numberOfFloors: number;
  linkedAppartments: number;
  longitude: number;
  latitude: number;
};

const emptyBuilding: Building = {
  buildingNumber: '',
  buildingName: '',
  city: '',
  country: '',
  street: '',
  numberOfFloors: 0,
  linkedAppartments: 0,
  longitude: 0,
  latitude: 0,
};

type Errors = Partial<Record<'buildingNumber' | 'buildingName' | 'city' | 'country' | 'street' | 'numberOfFloors', string>>;

const hasCoords = (b: { latitude?: number; longitude?: number }) => !!b.latitude || !!b.longitude;

export default function SetupBuilding({ navigation, route }: ScreenProps<'SetupBuilding'>) {
  const api = useApi();
  const { coords } = useLocation();
  const { data, loading, error, refetch } = useFetch<Building>('building/get', {
    select: (raw) => ({ ...emptyBuilding, ...((raw && typeof raw === 'object' ? raw : {}) as Partial<Building>) }),
  });

  const [form, setForm] = useState<Building>(emptyBuilding);
  const [point, setPoint] = useState<{ latitude: number; longitude: number } | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  const [saving, setSaving] = useState(false);
  const synced = useRef(false);

  useEffect(() => {
    if (data && !synced.current) {
      synced.current = true;
      setForm(data);
      if (hasCoords(data)) setPoint({ latitude: Number(data.latitude), longitude: Number(data.longitude) });
    }
  }, [data]);

  // Coordinates chosen on the map screen come back as route params.
  const pickedLat = route.params?.latitude;
  const pickedLon = route.params?.longitude;
  useEffect(() => {
    if (pickedLat !== undefined && pickedLon !== undefined) setPoint({ latitude: pickedLat, longitude: pickedLon });
  }, [pickedLat, pickedLon]);

  const locked = form.linkedAppartments > 0;
  const set = <K extends keyof Building>(key: K, value: Building[K]) => {
    setForm((f) => ({ ...f, [key]: value }));
    if (key in errors) setErrors((e) => ({ ...e, [key]: undefined }));
  };

  const save = async () => {
    Keyboard.dismiss();
    const required = t('This field is required');
    const next: Errors = {};
    (['buildingNumber', 'buildingName', 'city', 'country', 'street'] as const).forEach((k) => {
      if (!String(form[k]).trim()) next[k] = required;
    });
    if (!form.numberOfFloors) next.numberOfFloors = required;
    setErrors(next);
    if (Object.keys(next).length) return;

    const target = point ?? (coords ? { latitude: coords.lat, longitude: coords.long } : { latitude: Number(form.latitude) || 0, longitude: Number(form.longitude) || 0 });

    setSaving(true);
    const res = await api.post<string>('building/save', {
      ...form,
      buildingNumber: form.buildingNumber.trim(),
      buildingName: form.buildingName.trim(),
      city: form.city.trim(),
      country: form.country.trim(),
      street: form.street.trim(),
      latitude: target.latitude,
      longitude: target.longitude,
    });
    setSaving(false);
    if (res.ok) showMessage({ message: typeof res.data === 'string' && res.data ? res.data : t('Building saved'), type: 'success' });
  };

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

  const field = (key: keyof Errors) => ({
    value: String(form[key] ?? ''),
    editable: !locked,
    error: errors[key],
  });

  return (
    <Screen>
      <InfoBanner icon={locked ? 'lock-closed' : 'information-circle'}>
        {locked
          ? t('Apartments have already joined this building, so its details can no longer be changed.')
          : t('Set up your building so apartments can join it. Once apartments register, these details are locked.')}
      </InfoBanner>

      <Card>
        <View style={{ gap: spacing.lg }}>
          <TextField label={t('Building number *')} icon="pricetag-outline" placeholder={t('Building number')} {...field('buildingNumber')} onChangeText={(v) => set('buildingNumber', v)} />
          <TextField label={t('Building name *')} icon="business-outline" placeholder={t('Building name')} {...field('buildingName')} onChangeText={(v) => set('buildingName', v)} />
          <TextField label={t('Street *')} icon="trail-sign-outline" placeholder={t('Street')} {...field('street')} onChangeText={(v) => set('street', v)} />
          <TextField label={t('City *')} icon="location-outline" placeholder={t('City')} {...field('city')} onChangeText={(v) => set('city', v)} />
          <TextField label={t('Country *')} icon="flag-outline" placeholder={t('Country')} {...field('country')} onChangeText={(v) => set('country', v)} />
          <TextField
            label={t('Number of floors *')}
            icon="layers-outline"
            placeholder={t('Number of floors')}
            keyboardType="number-pad"
            value={form.numberOfFloors ? String(form.numberOfFloors) : ''}
            editable={!locked}
            error={errors.numberOfFloors}
            onChangeText={(v) => set('numberOfFloors', Number(v.replace(/\D/g, '')) || 0)}
          />
        </View>
      </Card>

      <Card flush style={{ marginTop: spacing.lg }}>
        <ListRow
          icon="map-outline"
          title={t('Building location')}
          subtitle={point ? `${point.latitude.toFixed(5)}, ${point.longitude.toFixed(5)}` : coords ? t('Not set · your current location will be used') : t('Not set')}
          onPress={locked ? undefined : () => navigation.navigate('BuildingLocation', point ?? undefined)}
        />
      </Card>

      {!locked ? <Button title={t('Save building')} loading={saving} onPress={save} style={{ marginTop: spacing.xl }} /> : null}
    </Screen>
  );
}
