import React from 'react';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { Badge, Card, Divider, Hero, ListRow, Screen, SectionHeader } from '../../components/ui';
import { useFetch } from '../../hooks/useFetch';
import type { RootStackParamList } from '../../navigation/types';
import { spacing } from '../../theme';
import { t } from '../../i18n';

const inset = spacing.lg + 38 + spacing.md;

/** Everything an administrator sets up and maintains. Collecting payments lives in its own tab. */
export default function Manage() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { data: pending, refreshing, refetch } = useFetch<number>('appartment/pending/get', {
    refetchOnFocus: true,
    select: (raw) => (Array.isArray(raw) ? raw.length : 0),
  });

  return (
    <Screen tab refreshing={refreshing} onRefresh={refetch} header={<Hero title={t('Manage building')} subtitle={t('Residents, receipts and settings')} />}>
      <SectionHeader title={t('Residents')} />
      <Card flush>
        <ListRow
          icon="person-add-outline"
          title={t('Join requests')}
          subtitle={t('Approve or reject new apartments')}
          right={pending ? <Badge label={t('{n} pending', { n: pending })} tone="warning" /> : undefined}
          onPress={() => navigation.navigate('PendingApartments')}
        />
        <Divider inset={inset} />
        <ListRow icon="business-outline" title={t('Apartments')} subtitle={t('Residents and contact details')} onPress={() => navigation.navigate('Apartments')} />
      </Card>

      <SectionHeader title={t('Receipts')} />
      <Card flush>
        <ListRow icon="add-circle-outline" title={t('New receipt')} subtitle={t('Issue a receipt to your residents')} onPress={() => navigation.navigate('CreateReceipt')} />
        <Divider inset={inset} />
        <ListRow icon="options-outline" title={t('Receipt format')} subtitle={t('Choose the fields and currency')} onPress={() => navigation.navigate('SetupReceipt')} />
      </Card>

      <SectionHeader title={t('Collection')} />
      <Card flush>
        <ListRow icon="bar-chart-outline" title={t('Monthly summary')} subtitle={t('Collected and still owed each month')} onPress={() => navigation.navigate('Summary')} />
      </Card>

      <SectionHeader title={t('Building')} />
      <Card flush>
        <ListRow icon="construct-outline" title={t('Building details')} subtitle={t('Name, address and location')} onPress={() => navigation.navigate('SetupBuilding')} />
        <Divider inset={inset} />
        <ListRow icon="swap-horizontal-outline" title={t('Transfer administrator')} subtitle={t('Hand the building to a resident')} onPress={() => navigation.navigate('TransferAdmin')} />
      </Card>
    </Screen>
  );
}
