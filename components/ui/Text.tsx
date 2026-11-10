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

/** Fills the `{name}` holes of a translated sentence with elements (a link, a bold phone number), wherever the language puts them. */
export function fillNodes(template: string, nodes: Record<string, React.ReactNode>): React.ReactNode[] {
  return template.split(/(\{\w+\})/).map((part, i) => {
    const name = /^\{(\w+)\}$/.exec(part)?.[1];
    return name !== undefined && name in nodes ? <React.Fragment key={i}>{nodes[name]}</React.Fragment> : part;
  });
}
