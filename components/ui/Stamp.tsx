import React, { useEffect, useRef, useState } from 'react';
import { AccessibilityInfo, Animated, Easing, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, family, radius, spacing } from '../../theme';
import { Text } from './Text';

export type PaymentState = 'paid' | 'unpaid';

/** Receipt statuses come from the API as Pending / Paid; people read them as Unpaid / Paid. */
export const toPaymentState = (status: string | null | undefined): PaymentState => (status === 'Paid' ? 'paid' : 'unpaid');
export const paymentLabel = (state: PaymentState) => (state === 'paid' ? 'Paid' : 'Unpaid');

const ink: Record<PaymentState, { color: string; wash: string; icon: keyof typeof Ionicons.glyphMap }> = {
  paid: { color: colors.success, wash: colors.successSoft, icon: 'checkmark-done' },
  unpaid: { color: colors.danger, wash: colors.dangerSoft, icon: 'ellipse-outline' },
};

type Props = {
  state: PaymentState;
  size?: 'sm' | 'md';
  /** Play the settle motion when the state turns to Paid after first render (the one authored moment). */
  settle?: boolean;
};

/**
 * The signature move: payment status as a rubber stamp. Word plus icon, never colour alone, set at a slight
 * angle. Turning Paid, it lands with a short spring (skipped when Reduce Motion is on).
 */
export function Stamp({ state, size = 'md', settle = false }: Props) {
  const tone = ink[state];
  const scale = useRef(new Animated.Value(1)).current;
  const opacity = useRef(new Animated.Value(1)).current;
  const previous = useRef(state);
  const [reduceMotion, setReduceMotion] = useState(false);

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
    const turnedPaid = previous.current !== 'paid' && state === 'paid';
    previous.current = state;
    if (!settle || !turnedPaid || reduceMotion) return;

    scale.setValue(1.45);
    opacity.setValue(0);
    Animated.parallel([
      Animated.timing(opacity, { toValue: 1, duration: 120, easing: Easing.out(Easing.exp), useNativeDriver: true }),
      Animated.spring(scale, { toValue: 1, friction: 6, tension: 220, useNativeDriver: true }),
    ]).start();
  }, [state, settle, reduceMotion, scale, opacity]);

  const small = size === 'sm';

  return (
    <Animated.View
      accessible
      accessibilityRole="text"
      accessibilityLabel={paymentLabel(state)}
      style={[
        styles.stamp,
        small && styles.small,
        { borderColor: tone.color, backgroundColor: tone.wash, opacity, transform: [{ rotate: '-3deg' }, { scale }] },
      ]}
    >
      <View style={styles.row} importantForAccessibility="no-hide-descendants">
        <Ionicons name={tone.icon} size={small ? 13 : 16} color={tone.color} />
        <Text
          variant="label"
          color={tone.color}
          style={{ fontFamily: family.bold, fontSize: small ? 12 : 13, letterSpacing: 1, textTransform: 'uppercase' }}
        >
          {paymentLabel(state)}
        </Text>
      </View>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  stamp: {
    alignSelf: 'flex-start',
    borderWidth: 2,
    borderRadius: radius.sm,
    paddingVertical: 3,
    paddingHorizontal: spacing.sm,
  },
  small: { borderWidth: 1.5, paddingVertical: 2, paddingHorizontal: 6 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 4 },
});
