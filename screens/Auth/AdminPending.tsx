import React, { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useAuth } from '../../auth/AuthProvider';
import { Button, Card, Text, showMessage } from '../../components/ui';
import { colors, radius, spacing } from '../../theme';
import { t } from '../../i18n';

/** Shown to administrator accounts until their request has been approved. */
export default function AdminPending() {
  const insets = useSafeAreaInsets();
  const { phoneNumber, userStatus, refreshUserStatus, signOut } = useAuth();
  const [checking, setChecking] = useState(false);

  const check = async () => {
    setChecking(true);
    const status = await refreshUserStatus();
    setChecking(false);
    if (status === null) {
      showMessage({ message: t('Could not check your status. Please try again.'), type: "danger" });
    } else if (status !== "Approved") {
      showMessage({ message: t('Your request is still being reviewed.'), type: "info" });
    }
  };

  return (
    <View style={[styles.root, { paddingTop: insets.top + spacing.xxl, paddingBottom: insets.bottom + spacing.xl }]}>
      <View style={[styles.iconCircle, userStatus === 'Rejected' && { backgroundColor: colors.dangerSoft }]}>
        <Ionicons name={userStatus === 'Rejected' ? 'close-circle-outline' : 'hourglass-outline'} size={40} color={userStatus === 'Rejected' ? colors.danger : colors.warning} />
      </View>
      <Text variant="title" align="center" style={{ marginTop: spacing.xl }}>
        {userStatus === 'Rejected' ? t('Request not approved') : t('Request received')}
      </Text>
      <Text variant="body" color={colors.textMuted} align="center" style={{ marginTop: spacing.sm }}>
        {userStatus === 'Rejected'
          ? t('Your administrator request was not approved. Please contact support if you think this is a mistake.')
          : t('Your administrator access is being reviewed.')}
      </Text>

      <Card style={{ marginTop: spacing.xl, alignSelf: 'stretch' }}>
        <Text variant="body">
          {userStatus === 'Rejected'
            ? t('You can still sign out and use Baytak as a resident with another number.')
            : t('You will be able to manage your building as soon as the request is approved. Tap Check status to see where it stands.')}
        </Text>
      </Card>

      <View style={styles.actions}>
        <Button title={t('Check status')} icon="refresh" loading={checking} onPress={check} />
        <Button title={t('Sign out')} variant="ghost" onPress={() => void signOut()} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, backgroundColor: colors.bg, alignItems: 'center', paddingHorizontal: spacing.xl },
  iconCircle: {
    width: 88,
    height: 88,
    borderRadius: radius.xl,
    backgroundColor: colors.warningSoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  actions: { alignSelf: 'stretch', gap: spacing.sm, marginTop: 'auto' },
});
