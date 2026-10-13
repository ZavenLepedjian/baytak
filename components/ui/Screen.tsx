import React from 'react';
import { Platform, RefreshControl, ScrollView, StyleProp, StyleSheet, View, ViewStyle } from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { useIsFocused } from '@react-navigation/native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, spacing } from '../../theme';
import { Text } from './Text';

/** Sets the status bar style only while the screen is focused, so stacked screens don't fight over it. */
export function FocusedStatusBar({ style }: { style: 'light' | 'dark' }) {
  const focused = useIsFocused();
  return focused ? <StatusBar style={style} /> : null;
}

type ScreenProps = {
  children: React.ReactNode;
  /** Scrollable content (default). Pass false when the child manages its own scrolling (FlatList). */
  scroll?: boolean;
  refreshing?: boolean;
  onRefresh?: () => void;
  /** Horizontal / vertical padding around content (default true). */
  padded?: boolean;
  /** The screen sits above a tab bar, which already handles the bottom safe area. */
  tab?: boolean;
  /** Fixed content above the scroll area, e.g. a <Hero />. */
  header?: React.ReactNode;
  contentStyle?: StyleProp<ViewStyle>;
};

export function Screen({
  children,
  scroll = true,
  refreshing = false,
  onRefresh,
  padded = true,
  tab = false,
  header,
  contentStyle,
}: ScreenProps) {
  const insets = useSafeAreaInsets();
  const bottom = tab ? spacing.xl : insets.bottom + spacing.xl;

  const body = scroll ? (
    <ScrollView
      style={styles.flex}
      contentContainerStyle={[padded && styles.padded, { paddingBottom: bottom }, contentStyle]}
      keyboardShouldPersistTaps="handled"
      keyboardDismissMode={Platform.OS === 'ios' ? 'interactive' : 'on-drag'}
      automaticallyAdjustKeyboardInsets
      showsVerticalScrollIndicator={false}
      refreshControl={
        onRefresh ? <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor={colors.primary} colors={[colors.primary]} /> : undefined
      }
    >
      {children}
    </ScrollView>
  ) : (
    <View style={[styles.flex, padded && styles.padded, contentStyle]}>{children}</View>
  );

  return (
    <View style={styles.root}>
      {header}
      {body}
    </View>
  );
}

type HeroProps = {
  title: string;
  subtitle?: string;
  right?: React.ReactNode;
  children?: React.ReactNode;
};

/** Flat ink page header for the main tab screens. */
export function Hero({ title, subtitle, right, children }: HeroProps) {
  const insets = useSafeAreaInsets();
  return (
    <View style={[styles.hero, { paddingTop: insets.top + spacing.md }]}>
      <FocusedStatusBar style="light" />
      <View style={styles.heroRow}>
        <View style={styles.flex}>
          <Text variant="title" color={colors.onInk} numberOfLines={1} accessibilityRole="header">
            {title}
          </Text>
          {subtitle ? (
            <Text variant="body" color={colors.onInkMuted} numberOfLines={2} style={{ marginTop: 2 }}>
              {subtitle}
            </Text>
          ) : null}
        </View>
        {right}
      </View>
      {children}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  root: { flex: 1, backgroundColor: colors.bg },
  padded: { paddingHorizontal: spacing.lg, paddingTop: spacing.lg },
  hero: {
    backgroundColor: colors.primary,
    paddingHorizontal: spacing.lg,
    paddingBottom: spacing.lg,
  },
  heroRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
});
