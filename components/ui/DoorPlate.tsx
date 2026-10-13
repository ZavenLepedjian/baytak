import React from 'react';
import { StyleSheet, View, ViewStyle } from 'react-native';
import { colors, radius } from '../../theme';
import { Text } from './Text';

type Props = {
  number: string;
  size?: 'sm' | 'md' | 'lg';
  /**
   * `ink`: solid enamel plate with light numerals (default).
   * `paper`: a white plate with an ink outline, for settled / quiet states.
   */
  tone?: 'ink' | 'paper';
  style?: ViewStyle;
};

const sizes = {
  sm: { minWidth: 44, minHeight: 34, font: 17, pad: 8 },
  md: { minWidth: 64, minHeight: 48, font: 24, pad: 10 },
  lg: { minWidth: 76, minHeight: 58, font: 30, pad: 12 },
};

/**
 * An apartment number set as an enamel door plate with an engraved rim. Numerals are the heaviest weight with
 * fixed-width digits. Decorative container: the number is read as text by screen readers.
 */
export function DoorPlate({ number, size = 'md', tone = 'ink', style }: Props) {
  const s = sizes[size];
  const ink = tone === 'ink';
  return (
    <View
      style={[
        styles.plate,
        {
          minWidth: s.minWidth,
          minHeight: s.minHeight,
          paddingHorizontal: s.pad,
          paddingVertical: 4,
          backgroundColor: ink ? colors.primary : colors.surface,
          borderColor: ink ? colors.primaryDeep : colors.primary,
        },
        style,
      ]}
    >
      <View style={[styles.rim, { borderColor: ink ? colors.brass : colors.borderStrong }]} pointerEvents="none" />
      <Text
        numeral
        color={ink ? colors.plateText : colors.primaryDeep}
        numberOfLines={1}
        adjustsFontSizeToFit
        style={{ fontSize: s.font, lineHeight: s.font + 6 }}
      >
        {number}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  plate: {
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: radius.md,
    borderWidth: 1.5,
  },
  rim: {
    position: 'absolute',
    top: 3,
    bottom: 3,
    start: 3,
    end: 3,
    borderWidth: StyleSheet.hairlineWidth * 2,
    borderRadius: radius.sm,
  },
});
