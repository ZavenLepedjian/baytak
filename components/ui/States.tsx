import React, { useEffect, useRef } from 'react';
import { ActivityIndicator, Animated, StyleProp, StyleSheet, View, ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Text } from './Text';
import { Button } from './Button';

export function Loading({ label }: { label?: string }) {
  return (
    <View style={styles.center} accessibilityRole="progressbar" accessibilityLabel={label ?? 'Loading'}>
      <ActivityIndicator size="large" color={colors.primary} />
      {label ? (
        <Text variant="caption" color={colors.textMuted} style={{ marginTop: spacing.md }}>
          {label}
        </Text>
      ) : null}
    </View>
  );
}

type EmptyProps = {
  icon?: keyof typeof Ionicons.glyphMap;
  title: string;
  message?: string;
  actionTitle?: string;
  onAction?: () => void;
};

export function EmptyState({ icon = 'file-tray-outline', title, message, actionTitle, onAction }: EmptyProps) {
  return (
    <View style={styles.center}>
      <View style={styles.iconCircle}>
        <Ionicons name={icon} size={34} color={colors.primary} />
      </View>
      <Text variant="heading" align="center" style={{ marginTop: spacing.lg }}>
        {title}
      </Text>
      {message ? (
        <Text variant="body" color={colors.textMuted} align="center" style={{ marginTop: spacing.xs + 2 }}>
          {message}
        </Text>
      ) : null}
      {actionTitle && onAction ? <Button title={actionTitle} onPress={onAction} style={{ marginTop: spacing.xl, alignSelf: 'stretch' }} /> : null}
    </View>
  );
}

export function ErrorState({ message, onRetry }: { message?: string | null; onRetry?: () => void }) {
  return (
    <View style={styles.center}>
      <View style={[styles.iconCircle, { backgroundColor: colors.dangerSoft }]}>
        <Ionicons name="cloud-offline-outline" size={34} color={colors.danger} />
      </View>
      <Text variant="heading" align="center" style={{ marginTop: spacing.lg }}>
        Something went wrong
      </Text>
      <Text variant="body" color={colors.textMuted} align="center" style={{ marginTop: spacing.xs + 2 }}>
        {message ?? 'Please check your connection and try again.'}
      </Text>
      {onRetry ? <Button title="Try again" icon="refresh" variant="secondary" onPress={onRetry} style={{ marginTop: spacing.xl, alignSelf: 'stretch' }} /> : null}
    </View>
  );
}

/** Pulsing placeholder block shown while content loads. */
export function Skeleton({ height = 16, width = '100%', style }: { height?: number; width?: number | `${number}%`; style?: StyleProp<ViewStyle> }) {
  const opacity = useRef(new Animated.Value(0.55)).current;
  useEffect(() => {
    const loop = Animated.loop(
      Animated.sequence([
        Animated.timing(opacity, { toValue: 1, duration: 700, useNativeDriver: true }),
        Animated.timing(opacity, { toValue: 0.55, duration: 700, useNativeDriver: true }),
      ]),
    );
    loop.start();
    return () => loop.stop();
  }, [opacity]);
  return <Animated.View style={[{ height, width, opacity, backgroundColor: colors.border, borderRadius: radius.sm }, style]} />;
}

export function ListSkeleton({ rows = 4, rowHeight = 88 }: { rows?: number; rowHeight?: number }) {
  return (
    <View accessibilityRole="progressbar" accessibilityLabel="Loading" style={{ gap: spacing.md }}>
      {Array.from({ length: rows }).map((_, i) => (
        <View key={i} style={[styles.skelCard, { height: rowHeight }]}>
          <Skeleton height={44} width={44} style={{ borderRadius: radius.md }} />
          <View style={{ flex: 1, gap: spacing.sm }}>
            <Skeleton height={14} width="60%" />
            <Skeleton height={12} width="85%" />
          </View>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  center: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.xl, minHeight: 280 },
  iconCircle: {
    width: 76,
    height: 76,
    borderRadius: radius.xl,
    backgroundColor: colors.primarySoft,
    alignItems: 'center',
    justifyContent: 'center',
  },
  skelCard: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
  },
});
