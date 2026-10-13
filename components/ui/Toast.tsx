import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, Animated, Easing, Pressable, StyleSheet, Text, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { Ionicons } from '@expo/vector-icons';
import { colors, fonts, HIT, MAX_FONT_SCALE, radius, shadow, spacing } from '../../theme';

export type ToastType = 'success' | 'danger' | 'info' | 'warning';
export type ToastAction = { label: string; onPress: () => void };
type ToastPayload = { id: number; message: string; type: ToastType; action?: ToastAction };

type Listener = (toast: ToastPayload) => void;
// More than one host can be mounted: the app root, and one inside each open Sheet (a Modal covers the root host).
const listeners = new Set<Listener>();
let counter = 0;

/** Show a toast from anywhere (screens, API layer). An action (e.g. Undo) keeps the toast up longer. */
export function showMessage({ message, type = 'info', action }: { message: string; type?: ToastType | 'default'; action?: ToastAction }) {
  if (!message) return;
  const payload: ToastPayload = { id: ++counter, message, type: type === 'default' ? 'info' : type, action };
  // Screen readers get the message once, whichever host happens to be on screen.
  AccessibilityInfo.announceForAccessibility(action ? `${message}. ${action.label} available` : message);
  listeners.forEach((l) => l(payload));
}

const tone: Record<ToastType, { bg: string; fg: string; icon: keyof typeof Ionicons.glyphMap }> = {
  success: { bg: colors.success, fg: colors.onInk, icon: 'checkmark-circle' },
  danger: { bg: colors.danger, fg: colors.onInk, icon: 'alert-circle' },
  warning: { bg: colors.warning, fg: colors.onInk, icon: 'warning' },
  info: { bg: colors.text, fg: colors.onInk, icon: 'information-circle' },
};

const VISIBLE_MS = 3600;
/** A toast with an action (Undo) stays long enough to be reached one-handed. */
const VISIBLE_WITH_ACTION_MS = 6000;

export function ToastHost() {
  const insets = useSafeAreaInsets();
  const [toast, setToast] = useState<ToastPayload | null>(null);
  const [screenReader, setScreenReader] = useState(false);
  const progress = useRef(new Animated.Value(0)).current;
  const hideTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    let alive = true;
    AccessibilityInfo.isScreenReaderEnabled().then((v) => alive && setScreenReader(v)).catch(() => {});
    const sub = AccessibilityInfo.addEventListener('screenReaderChanged', setScreenReader);
    return () => {
      alive = false;
      sub.remove();
    };
  }, []);

  const hide = useCallback(() => {
    if (hideTimer.current) clearTimeout(hideTimer.current);
    Animated.timing(progress, { toValue: 0, duration: 180, easing: Easing.in(Easing.quad), useNativeDriver: true }).start(
      ({ finished }) => {
        if (finished) setToast(null);
      },
    );
  }, [progress]);

  useEffect(() => {
    const listener: Listener = (next) => {
      if (hideTimer.current) clearTimeout(hideTimer.current);
      setToast(next);
      progress.setValue(0);
      Animated.timing(progress, { toValue: 1, duration: 220, easing: Easing.out(Easing.cubic), useNativeDriver: true }).start();
      // Someone using a screen reader needs more time to hear the message and find the action.
      const base = next.action ? VISIBLE_WITH_ACTION_MS : VISIBLE_MS;
      hideTimer.current = setTimeout(hide, screenReader ? base * 2 : base);
    };
    listeners.add(listener);
    return () => {
      listeners.delete(listener);
      if (hideTimer.current) clearTimeout(hideTimer.current);
    };
  }, [hide, progress, screenReader]);

  if (!toast) return null;
  const t = tone[toast.type];

  return (
    <View pointerEvents="box-none" style={[StyleSheet.absoluteFill, { paddingTop: insets.top + spacing.sm }]}>
      <Animated.View
        style={{
          opacity: progress,
          transform: [{ translateY: progress.interpolate({ inputRange: [0, 1], outputRange: [-24, 0] }) }],
          marginHorizontal: spacing.lg,
        }}
      >
        {/* The container is not itself a control, so the message and the action are separate focus targets. */}
        <View style={[styles.toast, shadow.md, { backgroundColor: t.bg }]}>
          <Pressable
            onPress={hide}
            accessibilityRole="alert"
            accessibilityLabel={toast.message}
            accessibilityHint="Double tap to dismiss"
            style={styles.message}
          >
            <Ionicons name={t.icon} size={22} color={t.fg} />
            <Text maxFontSizeMultiplier={MAX_FONT_SCALE} style={[styles.text, { color: t.fg }]} numberOfLines={4}>
              {toast.message}
            </Text>
          </Pressable>
          {toast.action ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={toast.action.label}
              hitSlop={8}
              onPress={() => {
                const run = toast.action!.onPress;
                hide();
                run();
              }}
              style={[styles.action, { borderColor: t.fg }]}
            >
              <Text maxFontSizeMultiplier={MAX_FONT_SCALE} style={[styles.actionText, { color: t.fg }]}>
                {toast.action.label}
              </Text>
            </Pressable>
          ) : null}
        </View>
      </Animated.View>
    </View>
  );
}

const styles = StyleSheet.create({
  toast: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.sm,
    paddingStart: spacing.lg,
    paddingEnd: spacing.md,
    borderRadius: radius.lg,
  },
  message: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: HIT, paddingVertical: spacing.xs },
  text: { ...fonts.bodyStrong, flex: 1 },
  action: { minHeight: HIT, justifyContent: 'center', paddingHorizontal: spacing.md, borderRadius: radius.sm, borderWidth: 1.5 },
  actionText: { ...fonts.bodyStrong, textTransform: 'uppercase', letterSpacing: 0.6 },
});
