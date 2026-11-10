import React, { useState } from 'react';
import { Linking, Pressable, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import * as Updates from 'expo-updates';
import Constants from 'expo-constants';
import { useNavigation } from '@react-navigation/native';
import type { NativeStackNavigationProp } from '@react-navigation/native-stack';
import { useApi } from '../api/useApi';
import { useAuth } from '../auth/AuthProvider';
import { Avatar, Badge, Button, Card, ConfirmDialog, Divider, Hero, InfoBanner, ListRow, Screen, Sheet, Text, showMessage } from '../components/ui';
import { FEATURES, PRIVACY_POLICY_URL, TERMS_URL } from '../config';
import type { RootStackParamList } from '../navigation/types';
import { colors, spacing } from '../theme';
import { LanguagePreference, getLanguagePreference, languageChangePending, ltr, setLanguagePreference, t } from '../i18n';

export default function Account() {
  const navigation = useNavigation<NativeStackNavigationProp<RootStackParamList>>();
  const { fullName, phoneNumber, isAdmin, signOut } = useAuth();
  const [confirmOut, setConfirmOut] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [languageOpen, setLanguageOpen] = useState(false);
  const [preference, setPreference] = useState<LanguagePreference>(getLanguagePreference());
  const [restartNeeded, setRestartNeeded] = useState(languageChangePending());
  const api = useApi();

  // Language and layout direction are decided when the app starts, so a change waits for a restart.
  const chooseLanguage = async (next: LanguagePreference) => {
    await setLanguagePreference(next);
    setPreference(next);
    setRestartNeeded(languageChangePending());
  };
  const restart = async () => {
    try {
      await Updates.reloadAsync();
    } catch {
      setLanguageOpen(false);
      showMessage({ message: t('Close Baytak and open it again to change the language'), type: 'info' });
    }
  };
  const languageName = (p: LanguagePreference) => (p === 'ar' ? 'العربية' : p === 'en' ? 'English' : t('Automatic'));

  const deleteAccount = async () => {
    setDeleting(true);
    const res = await api.post<string>('user/delete', {});
    setDeleting(false);
    setConfirmDelete(false);
    if (res.ok) {
      showMessage({ message: t('Your account has been deleted'), type: 'success' });
      void signOut({ remote: false });
    }
  };

  const openPage = (url: string) =>
    Linking.openURL(url).catch(() => showMessage({ message: t('Unable to open the page'), type: 'danger' }));

  return (
    <Screen
      tab
      header={
        <Hero title={t('Account')} subtitle={t('Your profile and settings')}>
          <View style={{ flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginTop: spacing.lg }}>
            <Avatar name={fullName || phoneNumber} size={56} color={colors.onInkWash} textColor={colors.onInk} />
            <View style={{ flex: 1 }}>
              <Text variant="heading" color={colors.onInk} numberOfLines={1}>
                {fullName || phoneNumber}
              </Text>
              {fullName ? (
                <Text variant="caption" color={colors.onInkMuted}>
                  {ltr(phoneNumber)}
                </Text>
              ) : null}
            </View>
            {isAdmin ? <Badge label={t('Admin')} tone="brand" /> : null}
          </View>
        </Hero>
      }
    >
      <Card flush style={{ marginTop: spacing.sm }}>
        <ListRow icon="person-outline" title={t('Edit profile')} subtitle={t('Name and emergency contact')} onPress={() => navigation.navigate('Profile')} />
        <Divider inset={spacing.lg + 38 + spacing.md} />
        <ListRow icon="home-outline" title={t('My apartment')} subtitle={t('Join or view your building')} onPress={() => navigation.navigate('JoinBuilding')} />
        <Divider inset={spacing.lg + 38 + spacing.md} />
        <ListRow icon="bar-chart-outline" title={t('Building summary')} subtitle={t('What was collected and what is still owed')} onPress={() => navigation.navigate('Summary')} />
        {FEATURES.wallet ? (
          <>
            <Divider inset={spacing.lg + 38 + spacing.md} />
            <ListRow icon="wallet-outline" title={t('Wallet')} subtitle={t('Income and expenses')} onPress={() => navigation.navigate('Wallet')} />
          </>
        ) : null}
        <Divider inset={spacing.lg + 38 + spacing.md} />
        <ListRow icon="language-outline" title={t('Language')} subtitle={languageName(preference)} onPress={() => setLanguageOpen(true)} />
      </Card>

      <Card flush style={{ marginTop: spacing.lg }}>
        <ListRow icon="shield-checkmark-outline" title={t('Privacy policy')} subtitle={t('How your data is used')} onPress={() => openPage(PRIVACY_POLICY_URL)} />
        <Divider inset={spacing.lg + 38 + spacing.md} />
        <ListRow icon="document-text-outline" title={t('Terms of service')} onPress={() => openPage(TERMS_URL)} />
      </Card>

      <Card flush style={{ marginTop: spacing.lg }}>
        <ListRow icon="log-out-outline" title={t('Sign out')} destructive onPress={() => setConfirmOut(true)} />
        <Divider inset={spacing.lg + 38 + spacing.md} />
        <ListRow icon="trash-outline" title={t('Delete account')} subtitle={t('Permanently remove your data')} destructive onPress={() => setConfirmDelete(true)} />
      </Card>

      <Text variant="caption" color={colors.textFaint} align="center" style={{ marginTop: spacing.xl }}>
        Baytak · v{Constants.expoConfig?.version ?? '—'}
      </Text>

      <Sheet visible={languageOpen} onClose={() => setLanguageOpen(false)} title={t('Language')}>
        <View style={{ gap: spacing.sm }}>
          {(['auto', 'en', 'ar'] as const).map((option) => (
            <Pressable
              key={option}
              accessibilityRole="radio"
              accessibilityState={{ selected: preference === option }}
              onPress={() => void chooseLanguage(option)}
              style={{ flexDirection: 'row', alignItems: 'center', minHeight: 52, paddingHorizontal: spacing.md, borderRadius: 8, backgroundColor: preference === option ? colors.primaryTint : undefined }}
            >
              <Text variant="bodyStrong" style={{ flex: 1 }}>
                {languageName(option)}
              </Text>
              {preference === option ? <Ionicons name="checkmark-circle" size={22} color={colors.primary} /> : null}
            </Pressable>
          ))}
          {restartNeeded ? (
            <View style={{ marginTop: spacing.md, gap: spacing.md }}>
              <InfoBanner>{t('The language changes after Baytak restarts.')}</InfoBanner>
              <Button title={t('Restart now')} icon="refresh" onPress={() => void restart()} />
            </View>
          ) : null}
        </View>
      </Sheet>

      <ConfirmDialog
        visible={confirmDelete}
        title={t('Delete your account?')}
        message={t('Your profile, apartment, receipts and wallet will be permanently deleted. This cannot be undone.')}
        confirmTitle={t('Delete account')}
        destructive
        loading={deleting}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={deleteAccount}
      />

      <ConfirmDialog
        visible={confirmOut}
        title={t('Sign out?')}
        message={t('You will need to verify your phone number again to sign back in.')}
        confirmTitle={t('Sign out')}
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
