import React, { useState } from 'react';
import { ImageBackground, Keyboard, KeyboardAvoidingView, Platform, ScrollView, StyleSheet, TouchableWithoutFeedback, View } from 'react-native';
import { LinearGradient } from 'expo-linear-gradient';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { apiRequest } from '../../api/client';
import { Button, ConfirmDialog, FocusedStatusBar, Text, TextField, showMessage } from '../../components/ui';
import { useLocation } from '../../location/LocationProvider';
import type { ScreenProps } from '../../navigation/types';
import { colors, radius, spacing } from '../../theme';

export default function LoginScreen({ navigation }: ScreenProps<'Login'>) {
  const insets = useSafeAreaInsets();
  const { coords, refresh } = useLocation();
  const [phoneNumber, setPhoneNumber] = useState('');
  const [busy, setBusy] = useState<'login' | 'admin' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmAdmin, setConfirmAdmin] = useState(false);

  const requestCode = async (requestAdmin: '' | 'request-admin') => {
    const phone = phoneNumber.trim();
    if (!phone) {
      setError('Please enter your phone number');
      return;
    }
    setError(null);
    Keyboard.dismiss();
    setBusy(requestAdmin ? 'admin' : 'login');

    // Location is only sent when available; fall back to a fresh read if the first one hadn't finished.
    const position = coords ?? (await refresh());
    const res = await apiRequest('auth/Login', {
      method: 'POST',
      withKey: false,
      body: {
        phoneNumber: phone,
        status: requestAdmin,
        long: position ? String(position.long) : '',
        lat: position ? String(position.lat) : '',
      },
    });
    setBusy(null);

    if (!res.ok) {
      if (!res.aborted) {
        const message = res.status === 400 ? 'Please enter a valid phone number' : res.error;
        setError(message);
        showMessage({ message, type: 'danger' });
      }
      return;
    }
    navigation.navigate('LoginVerification', { phoneNumber: phone, requestAdmin: requestAdmin === 'request-admin' });
  };

  return (
    <ImageBackground source={require('../../assets/images/login-background.jpg')} resizeMode="cover" style={styles.flex}>
      <FocusedStatusBar style="light" />
      <LinearGradient colors={['rgba(20,26,68,0.9)', 'rgba(31,42,122,0.62)']} style={StyleSheet.absoluteFill} />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView
          contentContainerStyle={styles.scroll}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
          bounces={false}
        >
          <TouchableWithoutFeedback onPress={Keyboard.dismiss} accessible={false}>
            <View style={[styles.top, { paddingTop: insets.top + spacing.xxl }]}>
              <Text variant="display" color={colors.onInk} style={{ fontSize: 40, lineHeight: 48 }} accessibilityRole="header">
                Baytak
              </Text>
              <Text variant="body" color={colors.onInkMuted} style={{ marginTop: spacing.xs }}>
                Building fees, made clear.
              </Text>
            </View>
          </TouchableWithoutFeedback>

          <View style={[styles.sheet, { paddingBottom: insets.bottom + spacing.xl }]}>
            <Text variant="title">Welcome</Text>
            <Text variant="body" color={colors.textMuted} style={{ marginTop: spacing.xs, marginBottom: spacing.xl }}>
              Enter your phone number and we&apos;ll text you a verification code.
            </Text>

            <TextField
              label="Phone number"
              icon="call-outline"
              placeholder="e.g. 03 123 456"
              keyboardType="phone-pad"
              textContentType="telephoneNumber"
              autoComplete="tel"
              returnKeyType="go"
              value={phoneNumber}
              onChangeText={(v) => {
                setPhoneNumber(v);
                if (error) setError(null);
              }}
              onSubmitEditing={() => requestCode('')}
              error={error}
            />

            <Button title="Continue" loading={busy === 'login'} disabled={busy === 'admin'} onPress={() => requestCode('')} style={{ marginTop: spacing.xl }} />

            <Button
              title="Managing a building? Request admin access"
              variant="ghost"
              small
              disabled={busy !== null}
              onPress={() => setConfirmAdmin(true)}
              style={{ marginTop: spacing.md }}
            />

            <ConfirmDialog
              visible={confirmAdmin}
              title="Request administrator access?"
              message="We will text you a code. After you verify it, our team reviews your request before the building tools are switched on. Until then you can use Baytak as a resident."
              confirmTitle="Send code"
              loading={busy === 'admin'}
              onCancel={() => setConfirmAdmin(false)}
              onConfirm={() => {
                setConfirmAdmin(false);
                void requestCode('request-admin');
              }}
            />
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </ImageBackground>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  scroll: { flexGrow: 1, justifyContent: 'space-between' },
  top: { alignItems: 'center', paddingBottom: spacing.xxl },
  logo: {
    width: 76,
    height: 76,
    borderRadius: 24,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.xl + 4,
    borderTopRightRadius: radius.xl + 4,
    padding: spacing.xl,
  },
  divider: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginVertical: spacing.xl },
  line: { flex: 1, height: StyleSheet.hairlineWidth, backgroundColor: colors.border },
});
