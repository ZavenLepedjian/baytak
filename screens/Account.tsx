import React, { useState } from 'react';
import { View } from 'react-native';
import Constants from 'expo-constants';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { useApi } from '../api/useApi';
import { useAuth } from '../auth/AuthProvider';
import { Avatar, Badge, Card, ConfirmDialog, Divider, Hero, ListRow, Screen, Text, showMessage } from '../components/ui';
import { FEATURES } from '../config';
import type { RootStackParamList } from '../navigation/types';
import { colors, spacing } from '../theme';

export default function Account() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { fullName, phoneNumber, isAdmin, signOut } = useAuth();
  const [confirmOut, setConfirmOut] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const api = useApi();

  const deleteAccount = async () => {
    setDeleting(true);
    const res = await api.post<string>('user/delete', {});
    setDeleting(false);
    setConfirmDelete(false);
    if (res.ok) {
      showMessage({ message: 'Your account has been deleted', type: 'success' });
      void signOut({ remote: false });
    }
  };

  return (
    <Screen
      tab
      header={
        <Hero title="Account" subtitle="Your profile and settings">
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginTop: spacing.lg }}>
            <Avatar name={fullName || phoneNumber} size={56} color={colors.onInkWash} textColor={colors.onInk} />
            <View style={{ flex: 1 }}>
              <Text variant="heading" color={colors.onInk} numberOfLines={1}>
                {fullName || phoneNumber}
              </Text>
              {fullName ? (
                <Text variant="caption" color={colors.onInkMuted}>
                  {phoneNumber}
                </Text>
              ) : null}
            </View>
            {isAdmin ? <Badge label="Admin" tone="brand" /> : null}
          </View>
        </Hero>
      }
    >
      <Card flush style={{ marginTop: spacing.sm }}>
        <ListRow icon="person-outline" title="Edit profile" subtitle="Name and emergency contact" onPress={() => navigation.navigate('Profile')} />
        <Divider inset={spacing.lg + 38 + spacing.md} />
        <ListRow icon="home-outline" title="My apartment" subtitle="Join or view your building" onPress={() => navigation.navigate('JoinBuilding')} />
        {FEATURES.wallet ? (
          <>
            <Divider inset={spacing.lg + 38 + spacing.md} />
            <ListRow icon="wallet-outline" title="Wallet" subtitle="Income and expenses" onPress={() => navigation.navigate('Wallet')} />
          </>
        ) : null}
      </Card>

      <Card flush style={{ marginTop: spacing.lg }}>
        <ListRow icon="log-out-outline" title="Sign out" destructive onPress={() => setConfirmOut(true)} />
        <Divider inset={spacing.lg + 38 + spacing.md} />
        <ListRow icon="trash-outline" title="Delete account" subtitle="Permanently remove your data" destructive onPress={() => setConfirmDelete(true)} />
      </Card>

      <Text variant="caption" color={colors.textFaint} align="center" style={{ marginTop: spacing.xl }}>
        Baytak · v{Constants.expoConfig?.version ?? '—'}
      </Text>

      <ConfirmDialog
        visible={confirmDelete}
        title="Delete your account?"
        message="Your profile, apartment, receipts and wallet will be permanently deleted. This cannot be undone."
        confirmTitle="Delete account"
        destructive
        loading={deleting}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={deleteAccount}
      />

      <ConfirmDialog
        visible={confirmOut}
        title="Sign out?"
        message="You will need to verify your phone number again to sign back in."
        confirmTitle="Sign out"
        destructive
        onCancel={() => setConfirmOut(false)}
        onConfirm={() => {
          setConfirmOut(false);
          void signOut();
        }}
      />
    </Screen>
  );
}
