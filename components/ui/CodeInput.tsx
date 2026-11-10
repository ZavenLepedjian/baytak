import React, { useEffect, useRef, useState } from 'react';
import { Keyboard, Platform, StyleSheet, TextInput, View } from 'react-native';
import { colors, radius, spacing } from '../../theme';
import { Text } from './Text';
import { t } from '../../i18n';

type Props = {
  value: string;
  onChange: (value: string) => void;
  length?: number;
  error?: boolean;
  editable?: boolean;
};

/**
 * A one-time-code field: one real TextInput laid over the visible cells.
 *
 * The keyboard's own backspace edits the text, nothing blurs the field when the last digit is typed, and tapping the cells
 * focuses it again. (The previous field cleared itself and dismissed the keyboard on completion, which on Android left no
 * way to correct a digit.)
 */
export function CodeInput({ value, onChange, length = 6, error = false, editable = true }: Props) {
  const ref = useRef<TextInput>(null);
  const [focused, setFocused] = useState(false);
  const active = Math.min(value.length, length - 1);

  // Android keeps the focus when the keyboard is closed with Back, so a tap would not reopen it: drop the focus instead.
  useEffect(() => {
    if (Platform.OS !== 'android') return;
    const sub = Keyboard.addListener('keyboardDidHide', () => ref.current?.blur());
    return () => sub.remove();
  }, []);

  return (
    <View style={styles.root}>
      <View style={styles.cells} pointerEvents="none">
        {Array.from({ length }, (_, i) => (
          <View key={i} style={[styles.cell, focused && i === active && styles.cellFocus, error && styles.cellError]}>
            <Text variant="title" style={{ fontSize: 24 }}>
              {value[i] ?? ''}
            </Text>
          </View>
        ))}
      </View>
      <TextInput
        ref={ref}
        value={value}
        onChangeText={(text) => onChange(text.replace(/\D/g, '').slice(0, length))}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        editable={editable}
        keyboardType="number-pad"
        maxLength={length}
        textContentType="oneTimeCode"
        autoComplete={Platform.OS === 'android' ? 'sms-otp' : 'one-time-code'}
        autoFocus
        caretHidden
        contextMenuHidden
        selectTextOnFocus={false}
        accessibilityLabel={t('Verification code, {n} digits', { n: length })}
        style={styles.input}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  // The six digits are read left to right in every language.
  root: { height: 60, direction: 'ltr' },
  cells: { flexDirection: 'row', gap: spacing.sm, justifyContent: 'space-between' },
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
  // Covers the cells and is invisible, so a tap anywhere on them lands on the real input.
  input: { position: 'absolute', top: 0, left: 0, right: 0, bottom: 0, opacity: 0.02, color: 'transparent', fontSize: 1 },
});
