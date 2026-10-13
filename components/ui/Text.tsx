import React from 'react';
import { Text as RNText, TextProps, TextStyle } from 'react-native';
import { colors, fonts, MAX_FONT_SCALE } from '../../theme';

export type Variant = 'display' | 'title' | 'heading' | 'body' | 'bodyStrong' | 'caption' | 'label';

type Props = TextProps & {
  variant?: Variant;
  color?: string;
  align?: TextStyle['textAlign'];
  /** Money and numerals: heaviest weight, fixed-width digits. */
  numeral?: boolean;
};

/** Themed text. Font scaling is capped at MAX_FONT_SCALE so the largest accessibility sizes don't break layouts. */
export function Text({ variant = 'body', color = colors.text, align, numeral, style, ...rest }: Props) {
  return (
    <RNText
      maxFontSizeMultiplier={MAX_FONT_SCALE}
      {...rest}
      style={[fonts[variant], numeral ? fonts.numeral : null, { color }, align ? { textAlign: align } : null, style]}
    />
  );
}
