import { Platform, TextStyle, ViewStyle } from 'react-native';
import { isArabic } from './i18n';

/**
 * The Baytak world: a building's receipt book. The logo's teal carries the shell, buttons and door plates;
 * the ground is cool teal-tinted paper; two stamp inks do the state jobs (green = Paid, vermilion = Unpaid).
 * Token names are stable so screens re-skin by changing values here.
 */
export const colors = {
  // Teal (primary, the logo's bowl): shell, primary buttons, door plates
  primary: '#00847C',
  primaryDark: '#006B65',
  primaryDeep: '#05302E',
  primarySoft: '#D3EFEC',
  primaryTint: '#EAF6F5',

  // Paper
  bg: '#EEF4F3',
  surface: '#FFFFFF',
  surfaceMuted: '#E1EAE9',
  border: '#D0DDDC',
  borderStrong: '#A9BDBB',
  /** Borders of inputs and unchecked boxes: 3:1 against white and paper (WCAG 1.4.11). */
  borderInput: '#6F8583',

  // Text (tinted from the teal, never neutral gray)
  text: '#0B2625',
  textMuted: '#3F5654',
  textFaint: '#5B706E',
  textOnPrimary: '#FFFFFF',

  // Stamp inks: each has a text/ink colour (>= 6:1 on white) and a soft wash.
  // Paid is a true green so it never reads as the teal of the shell.
  success: '#2E6B1F',
  successSoft: '#E3F1DB',
  warning: '#8A5A00',
  warningSoft: '#FBEFD0',
  danger: '#B3321B',
  dangerDeep: '#8F2815',
  dangerSoft: '#FBE3DC',
  info: '#00847C',
  infoSoft: '#D3EFEC',

  /** Engraved rim of a door plate. Decoration only, never text. */
  brass: '#B8913F',
  /** Light text on a teal plate. */
  plateText: '#F2FBFA',

  /** Text and washes laid over the teal shell. */
  onInk: '#FFFFFF',
  onInkMuted: 'rgba(255,255,255,0.85)',
  onInkWash: 'rgba(255,255,255,0.18)',

  /** Pressed states of the soft buttons. */
  pressedSoft: '#BFE3DF',
  pressedDanger: '#F6D0C6',

  overlay: 'rgba(5, 48, 46, 0.5)',
} as const;

export const spacing = { xs: 4, sm: 8, md: 12, lg: 16, xl: 24, xxl: 32 } as const;

/** Small, crisp corners: slips and plates, not pills. */
export const radius = { sm: 4, md: 8, lg: 10, xl: 14, pill: 999 } as const;

/** One family, one file per weight (a custom face has no fontWeight on Android). Latin + Arabic, fixed-width digits. */
export const family = {
  regular: 'IBMPlexSansArabic_400Regular',
  medium: 'IBMPlexSansArabic_500Medium',
  semibold: 'IBMPlexSansArabic_600SemiBold',
  bold: 'IBMPlexSansArabic_700Bold',
} as const;

export const fonts = {
  display: { fontFamily: family.bold, fontSize: 30, lineHeight: 38 } as TextStyle,
  title: { fontFamily: family.bold, fontSize: 22, lineHeight: 30 } as TextStyle,
  heading: { fontFamily: family.semibold, fontSize: 17, lineHeight: 24 } as TextStyle,
  body: { fontFamily: family.regular, fontSize: 16, lineHeight: 23 } as TextStyle,
  bodyStrong: { fontFamily: family.semibold, fontSize: 16, lineHeight: 23 } as TextStyle,
  caption: { fontFamily: family.regular, fontSize: 14, lineHeight: 20 } as TextStyle,
  /** Letter-spacing would pull apart the joined letters of Arabic, so it is only for Latin text. */
  label: { fontFamily: family.semibold, fontSize: 12, lineHeight: 16, letterSpacing: isArabic() ? 0 : 0.4 } as TextStyle,
  /** Door-plate numerals and money: heaviest weight, fixed-width digits so columns align. */
  numeral: { fontFamily: family.bold, fontVariant: ['tabular-nums'] } as TextStyle,
};

export const shadow = {
  sm: Platform.select<ViewStyle>({
    ios: { shadowColor: '#05302E', shadowOpacity: 0.08, shadowRadius: 5, shadowOffset: { width: 0, height: 2 } },
    default: { elevation: 2 },
  }) as ViewStyle,
  md: Platform.select<ViewStyle>({
    ios: { shadowColor: '#05302E', shadowOpacity: 0.16, shadowRadius: 12, shadowOffset: { width: 0, height: 6 } },
    default: { elevation: 6 },
  }) as ViewStyle,
};

/** Minimum touch target recommended by iOS/Android guidelines. */
export const HIT = Platform.OS === 'android' ? 48 : 44;

export const hitSlop = { top: 8, bottom: 8, left: 8, right: 8 };

/** Largest text scale the layouts are built for. */
export const MAX_FONT_SCALE = 1.6;
