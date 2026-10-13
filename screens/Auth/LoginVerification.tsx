import React, { useCallback, useEffect, useRef, useState } from 'react';
import { Keyboard, Platform, Pressable, StyleSheet, View } from 'react-native';
import { CodeField, Cursor, useBlurOnFulfill, useClearByFocusCell } from 'react-native-confirmation-code-field';
import { Ionicons } from '@expo/vector-icons';
import { apiRequest } from '../../api/client';
import { useAuth } from '../../auth/AuthProvider';
import { Button, Screen, Text, chevronBack, showMessage } from '../../components/ui';
import type { ScreenProps } from '../../navigation/types';
import { colors, HIT, radius, spacing } from '../../theme';

const CELL_COUNT = 6;
const RESEND_SECONDS = 30;

type ValidateResponse = { auth?: string; userStatus?: string };

export default function LoginVerification({ navigation, route }: ScreenProps<'LoginVerification'>) {
  const { phoneNumber, requestAdmin } = route.params;
  const { signIn } = useAuth();
  const [value, setValue] = useState('');
  const [verifying, setVerifying] = useState(false);
  const [resending, setResending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cooldown, setCooldown] = useState(RESEND_SECONDS);
  const submitting = useRef(false);

  const ref = useBlurOnFulfill({ value, cellCount: CELL_COUNT });
  const [cellProps, getCellOnLayoutHandler] = useClearByFocusCell({ value, setValue });

  useEffect(() => {
    if (cooldown <= 0) return;
    const t = setTimeout(() => setCooldown((c) => c - 1), 1000);
    return () => clearTimeout(t);
  }, [cooldown]);

  const verify = useCallback(
    async (code: string) => {
      if (submitting.current) return;
      if (code.length < CELL_COUNT) {
        setError('Enter the 6-digit code');
        return;
      }
      submitting.current = true;
      setVerifying(true);
      setError(null);
      Keyboard.dismiss();

      const res = await apiRequest<ValidateResponse>('auth/validate', {
        method: 'POST',
        withKey: false,
        body: { phoneNumber, pincode: code },
      });

      if (!res.ok) {
        submitting.current = false;
        setVerifying(false);
        if (!res.aborted) {
          setError(res.status === 400 ? 'That code is not correct. Please try again.' : res.error);
          setValue('');
          setTimeout(() => ref.current?.focus(), 100);
        }
        return;
      }
      const token = res.data?.auth;
      if (!token) {
        submitting.current = false;
        setVerifying(false);
        setError('We could not sign you in. Please try again.');
        return;
      }
      // Signing in swaps the navigator to the signed-in screens, which unmounts this screen.
      await signIn(token, res.data?.userStatus ?? null);
    },
    [phoneNumber, signIn, ref],
  );

  // Submit automatically as soon as the last digit is entered.
  useEffect(() => {
    if (value.length === CELL_COUNT) void verify(value);
  }, [value, verify]);

  const resend = async () => {
    setResending(true);
    const res = await apiRequest('auth/login', { method: 'POST', withKey: false, body: { phoneNumber, status: requestAdmin ? 'request-admin' : '' } });
    setResending(false);
    if (res.ok) {
      setCooldown(RESEND_SECONDS);
      showMessage({ message: 'A new code is on its way', type: 'success' });
    } else if (!res.aborted) {
      showMessage({ message: res.error, type: 'danger' });
    }
  };

  return (
    <Screen contentStyle={{ paddingTop: spacing.xl }}>
      <Pressable onPress={() => navigation.goBack()} accessibilityRole="button" accessibilityLabel="Change number" hitSlop={12} style={styles.back}>
        <Ionicons name={chevronBack} size={24} color={colors.primary} />
        <Text variant="bodyStrong" color={colors.primary}>
          Change number
        </Text>
      </Pressable>

      <View style={styles.iconCircle}>
        <Ionicons name="chatbubble-ellipses-outline" size={34} color={colors.primary} />
      </View>
      <Text variant="title" style={{ marginTop: spacing.lg }}>
        Enter verification code
      </Text>
      <Text variant="body" color={colors.textMuted} style={{ marginTop: spacing.xs + 2, marginBottom: spacing.xl }}>
        We sent a 6-digit code to <Text variant="bodyStrong">{phoneNumber}</Text>.
      </Text>

      <CodeField
        ref={ref}
        {...cellProps}
        value={value}
        onChangeText={(text) => {
          setValue(text.replace(/\D/g, ''));
          if (error) setError(null);
        }}
        cellCount={CELL_COUNT}
        rootStyle={styles.codeRoot}
        keyboardType="number-pad"
        textContentType="oneTimeCode"
        autoComplete={Platform.OS === 'android' ? 'sms-otp' : 'one-time-code'}
        autoFocus
        renderCell={({ index, symbol, isFocused }) => (
          <View
            key={index}
            onLayout={getCellOnLayoutHandler(index)}
            style={[styles.cell, isFocused && styles.cellFocus, !!error && styles.cellError]}
          >
            <Text variant="title" style={{ fontSize: 24 }}>
              {symbol || (isFocused ? <Cursor /> : null)}
            </Text>
          </View>
        )}
      />
      {error ? (
        <Text variant="caption" color={colors.danger} style={{ marginTop: spacing.md }} accessibilityLiveRegion="polite">
          {error}
        </Text>
      ) : null}

      <Button title="Verify" loading={verifying} disabled={value.length < CELL_COUNT} onPress={() => verify(value)} style={{ marginTop: spacing.xl }} />

      <View style={styles.resendRow}>
        <Text variant="body" color={colors.textMuted}>
          Didn&apos;t get a code?
        </Text>
        <Button
          small
          variant="ghost"
          title={cooldown > 0 ? `Resend in ${cooldown}s` : 'Resend code'}
          disabled={cooldown > 0 || verifying}
          loading={resending}
          onPress={resend}
        />
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  back: { flexDirection: 'row', alignItems: 'center', alignSelf: 'flex-start', minHeight: HIT, marginBottom: spacing.lg, marginStart: -spacing.xs },
  iconCircle: {
    width: 72,
    height: 72,
    borderRadius: radius.xl,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  codeRoot: { gap: spacing.sm, justifyContent: 'space-between' },
  cell: {
    flex: 1,
    maxWidth: 52,
    height: 60,
    borderRadius: radius.md,
    borderWidth: 1.5,
    borderColor: colors.border,
    backgroundColor: colors.surface,
    alignItems: 'center',
    justifyContent: 'center',
  },
  cellFocus: { borderColor: colors.primary, backgroundColor: colors.primaryTint },
  cellError: { borderColor: colors.danger },
  resendRow: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'center', gap: spacing.xs, marginTop: spacing.lg },
});
