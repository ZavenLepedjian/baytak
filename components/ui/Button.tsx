import React from 'react';
import { ActivityIndicator, Pressable, StyleProp, StyleSheet, View, ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import * as Haptics from 'expo-haptics';
import { colors, fonts, HIT, radius, spacing } from '../../theme';
import { Text } from './Text';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'destructive';

type Props = {
  title: string;
  onPress?: () => void | Promise<void>;
  variant?: Variant;
  icon?: keyof typeof Ionicons.glyphMap;
  loading?: boolean;
  disabled?: boolean;
  /** Compact height for inline actions. */
  small?: boolean;
  style?: StyleProp<ViewStyle>;
  accessibilityLabel?: string;
};

const palette: Record<Variant, { bg: string; fg: string; border: string; pressed: string }> = {
  primary: { bg: colors.primary, fg: colors.textOnPrimary, border: colors.primary, pressed: colors.primaryDark },
  secondary: { bg: colors.primarySoft, fg: colors.primaryDeep, border: colors.primarySoft, pressed: colors.pressedSoft },
  ghost: { bg: 'transparent', fg: colors.primary, border: 'transparent', pressed: colors.primaryTint },
  destructive: { bg: colors.danger, fg: colors.onInk, border: colors.danger, pressed: colors.dangerDeep },
  danger: { bg: colors.dangerSoft, fg: colors.danger, border: colors.dangerSoft, pressed: colors.pressedDanger },
};

export function Button({
  title,
  onPress,
  variant = 'primary',
  icon,
  loading = false,
  disabled = false,
  small = false,
  style,
  accessibilityLabel,
}: Props) {
  const p = palette[variant];
  const inactive = disabled || loading;

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={accessibilityLabel ?? title}
      accessibilityState={{ disabled: inactive, busy: loading }}
      disabled={inactive}
      onPress={() => {
        if (variant === 'primary') Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light).catch(() => {});
        void onPress?.();
      }}
      style={({ pressed }) => [
        styles.base,
        { minHeight: small ? HIT : 52, backgroundColor: pressed ? p.pressed : p.bg, borderColor: p.border },
        inactive && !loading && styles.disabled,
        style,
      ]}
    >
      {loading ? (
        <ActivityIndicator color={p.fg} />
      ) : (
        <View style={styles.content}>
          {icon ? <Ionicons name={icon} size={small ? 18 : 20} color={p.fg} /> : null}
          <Text variant="bodyStrong" color={p.fg} style={small ? styles.smallText : undefined}>
            {title}
          </Text>
        </View>
      )}
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    borderRadius: radius.md,
    borderWidth: 1,
    paddingHorizontal: spacing.lg,
    alignItems: 'center',
    justifyContent: 'center',
  },
  content: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  disabled: { opacity: 0.45 },
  smallText: { fontSize: fonts.caption.fontSize },
});
