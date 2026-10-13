import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  AccessibilityInfo,
  Animated,
  Easing,
  KeyboardAvoidingView,
  Modal,
  PanResponder,
  Platform,
  Pressable,
  ScrollView,
  StyleSheet,
  useWindowDimensions,
  View,
} from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { colors, HIT, radius, spacing } from '../../theme';
import { Text } from './Text';
import { Button } from './Button';
import { ToastHost } from './Toast';

type SheetProps = {
  visible: boolean;
  onClose: () => void;
  title?: string;
  children: React.ReactNode;
};

/** How far down a drag on the handle must travel before the sheet is dismissed. */
const DISMISS_DRAG = 80;

/**
 * Bottom sheet. It rises from the bottom edge over a dimming backdrop, and leaves the way it came: drag the handle
 * down, tap outside, or press the system back button. With Reduce Motion on it just appears and disappears.
 * Tall content scrolls inside the sheet and the sheet lifts above the keyboard, so every action stays reachable at
 * large text sizes. It hosts its own toast layer because a Modal covers the one at the app root.
 */
export function Sheet({ visible, onClose, title, children }: SheetProps) {
  const insets = useSafeAreaInsets();
  const { height } = useWindowDimensions();
  const [mounted, setMounted] = useState(visible);
  const [reduceMotion, setReduceMotion] = useState(false);
  const progress = useRef(new Animated.Value(visible ? 1 : 0)).current;
  const drag = useRef(new Animated.Value(0)).current;
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  // While the sheet animates out, callers have already cleared their content; keep showing the last real content.
  const last = useRef({ title, children });
  if (visible) last.current = { title, children };
  const shown = visible ? { title, children } : last.current;

  useEffect(() => {
    let alive = true;
    AccessibilityInfo.isReduceMotionEnabled().then((v) => alive && setReduceMotion(v)).catch(() => {});
    const sub = AccessibilityInfo.addEventListener('reduceMotionChanged', setReduceMotion);
    return () => {
      alive = false;
      sub.remove();
    };
  }, []);

  useEffect(() => {
    if (visible) {
      setMounted(true);
      drag.setValue(0);
      Animated.timing(progress, {
        toValue: 1,
        duration: reduceMotion ? 0 : 280,
        easing: Easing.out(Easing.exp),
        useNativeDriver: true,
      }).start();
    } else {
      Animated.timing(progress, {
        toValue: 0,
        duration: reduceMotion ? 0 : 180,
        easing: Easing.in(Easing.quad),
        useNativeDriver: true,
      }).start(({ finished }) => {
        if (finished) setMounted(false);
      });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visible]);

  const pan = useMemo(
    () =>
      PanResponder.create({
        onMoveShouldSetPanResponder: (_, g) => Math.abs(g.dy) > 6 && Math.abs(g.dy) > Math.abs(g.dx),
        onPanResponderMove: (_, g) => drag.setValue(Math.max(0, g.dy)),
        onPanResponderRelease: (_, g) => {
          if (g.dy > DISMISS_DRAG || g.vy > 1.2) onCloseRef.current();
          else Animated.spring(drag, { toValue: 0, useNativeDriver: true, friction: 8 }).start();
        },
        onPanResponderTerminate: () => Animated.spring(drag, { toValue: 0, useNativeDriver: true }).start(),
      }),
    [drag],
  );

  if (!mounted) return null;

  const translateY = Animated.add(progress.interpolate({ inputRange: [0, 1], outputRange: [height * 0.6, 0] }), drag);

  return (
    <Modal visible transparent animationType="none" statusBarTranslucent navigationBarTranslucent onRequestClose={onClose}>
      <KeyboardAvoidingView style={styles.root} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <Animated.View style={[StyleSheet.absoluteFill, { opacity: progress }]}>
          <Pressable style={StyleSheet.absoluteFill} onPress={onClose} accessibilityLabel="Close" accessibilityRole="button">
            <View style={styles.backdrop} />
          </Pressable>
        </Animated.View>
        <Animated.View
          style={[
            styles.sheet,
            { maxHeight: height - insets.top - spacing.xl, paddingBottom: insets.bottom + spacing.lg, transform: [{ translateY }] },
          ]}
        >
          <View {...pan.panHandlers} style={styles.grab}>
            <Pressable accessibilityRole="button" accessibilityLabel="Close" onPress={onClose} style={styles.grabTarget}>
              <View style={styles.handle} />
            </Pressable>
          </View>
          {shown.title ? (
            <Text variant="heading" accessibilityRole="header" style={{ marginBottom: spacing.md }}>
              {shown.title}
            </Text>
          ) : null}
          <ScrollView
            style={styles.scroll}
            keyboardShouldPersistTaps="handled"
            showsVerticalScrollIndicator={false}
            bounces={false}
          >
            {shown.children}
          </ScrollView>
        </Animated.View>
        <ToastHost />
      </KeyboardAvoidingView>
    </Modal>
  );
}

type ConfirmProps = {
  visible: boolean;
  title: string;
  message?: string;
  confirmTitle?: string;
  cancelTitle?: string;
  destructive?: boolean;
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
};

export function ConfirmDialog({
  visible,
  title,
  message,
  confirmTitle = 'Confirm',
  cancelTitle = 'Cancel',
  destructive,
  loading,
  onConfirm,
  onCancel,
}: ConfirmProps) {
  return (
    <Sheet visible={visible} onClose={onCancel} title={title}>
      {message ? (
        <Text variant="body" color={colors.textMuted} style={{ marginBottom: spacing.xl }}>
          {message}
        </Text>
      ) : null}
      <View style={styles.actions}>
        <Button title={cancelTitle} variant="secondary" onPress={onCancel} style={styles.action} />
        <Button title={confirmTitle} variant={destructive ? 'destructive' : 'primary'} loading={loading} onPress={onConfirm} style={styles.action} />
      </View>
    </Sheet>
  );
}

const styles = StyleSheet.create({
  root: { flex: 1, justifyContent: 'flex-end' },
  backdrop: { flex: 1, backgroundColor: colors.overlay },
  sheet: {
    backgroundColor: colors.surface,
    borderTopLeftRadius: radius.xl,
    borderTopRightRadius: radius.xl,
    paddingHorizontal: spacing.lg,
  },
  grab: { alignItems: 'center', marginBottom: spacing.xs },
  grabTarget: { minHeight: HIT, minWidth: HIT * 2, alignItems: 'center', justifyContent: 'center' },
  handle: { width: 40, height: 4, borderRadius: 2, backgroundColor: colors.borderStrong },
  scroll: { flexGrow: 0, flexShrink: 1 },
  actions: { flexDirection: 'row', gap: spacing.md },
  action: { flex: 1 },
});
