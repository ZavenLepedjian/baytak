import React from 'react';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { Badge, Card, Divider, Hero, ListRow, Screen, SectionHeader } from '../../components/ui';
import { useFetch } from '../../hooks/useFetch';
import type { RootStackParamList } from '../../navigation/types';
import { spacing } from '../../theme';

const inset = spacing.lg + 38 + spacing.md;

/** Everything an administrator sets up and maintains. Collecting payments lives in its own tab. */
export default function Manage() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { data: pending, refreshing, refetch } = useFetch<number>('appartment/pending/get', {
    refetchOnFocus: true,
    select: (raw) => (Array.isArray(raw) ? raw.length : 0),
  });

  return (
    <Screen tab refreshing={refreshing} onRefresh={refetch} header={<Hero title="Manage building" subtitle="Residents, receipts and settings" />}>
      <SectionHeader title="Residents" />
      <Card flush>
        <ListRow
          icon="person-add-outline"
          title="Join requests"
          subtitle="Approve or reject new apartments"
          right={pending ? <Badge label={`${pending} pending`} tone="warning" /> : undefined}
          onPress={() => navigation.navigate('PendingApartments')}
        />
        <Divider inset={inset} />
        <ListRow icon="business-outline" title="Apartments" subtitle="Residents and contact details" onPress={() => navigation.navigate('Apartments')} />
      </Card>

      <SectionHeader title="Receipts" />
      <Card flush>
        <ListRow icon="add-circle-outline" title="New receipt" subtitle="Issue a receipt to your residents" onPress={() => navigation.navigate('CreateReceipt')} />
        <Divider inset={inset} />
        <ListRow icon="options-outline" title="Receipt format" subtitle="Choose the fields and currency" onPress={() => navigation.navigate('SetupReceipt')} />
      </Card>

      <SectionHeader title="Building" />
      <Card flush>
        <ListRow icon="construct-outline" title="Building details" subtitle="Name, address and location" onPress={() => navigation.navigate('SetupBuilding')} />
      </Card>
    </Screen>
  );
}
