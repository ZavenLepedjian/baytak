import { Platform, TextStyle, ViewStyle } from 'react-native';

/**
 * The Baytak world: a building's receipt book. Carbon-copy ink blue carries the shell, buttons and door plates;
 * the ground is cool carbon-blue paper; two stamp inks do the state jobs (verdigris = Paid, vermilion = Unpaid).
 * Token names are stable so screens re-skin by changing values here.
 */
export const colors = {
  // Ink (primary): shell, primary buttons, door plates
  primary: '#1F2A7A',
  primaryDark: '#18205F',
  primaryDeep: '#141A44',
  primarySoft: '#DDE2F5',
  primaryTint: '#EEF0FA',

  // Paper
  bg: '#EDF0F7',
  surface: '#FFFFFF',
  surfaceMuted: '#E3E7F2',
  border: '#D3D9EA',
  borderStrong: '#AEB6D2',
  /** Borders of inputs and unchecked boxes: 3:1 against white and paper (WCAG 1.4.11). */
  borderInput: '#7A81A3',

  // Text (tinted from the ink, never neutral gray)
  text: '#141A44',
  textMuted: '#4B5273',
  textFaint: '#666C8A',
  textOnPrimary: '#FFFFFF',

  // Stamp inks: each has a text/ink colour (>= 6:1 on white) and a soft wash
  success: '#0B6B4D',
  successSoft: '#DDF1E8',
  warning: '#8A5A00',
  warningSoft: '#FBEFD0',
  danger: '#B3321B',
  dangerDeep: '#8F2815',
  dangerSoft: '#FBE3DC',
  info: '#1F2A7A',
  infoSoft: '#DDE2F5',

  /** Engraved rim of a door plate. Decoration only, never text. */
  brass: '#B8913F',
  /** Light text on an ink plate. */
  plateText: '#F4F6FF',

  /** Text and washes laid over the ink shell. */
  onInk: '#FFFFFF',
  onInkMuted: 'rgba(255,255,255,0.85)',
  onInkWash: 'rgba(255,255,255,0.18)',

  /** Pressed states of the soft buttons. */
  pressedSoft: '#CBD2EE',
  pressedDanger: '#F6D0C6',

  overlay: 'rgba(20, 26, 68, 0.5)',
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
  label: { fontFamily: family.semibold, fontSize: 12, lineHeight: 16, letterSpacing: 0.4 } as TextStyle,
  /** Door-plate numerals and money: heaviest weight, fixed-width digits so columns align. */
  numeral: { fontFamily: family.bold, fontVariant: ['tabular-nums'] } as TextStyle,
};

export const shadow = {
  sm: Platform.select<ViewStyle>({
    ios: { shadowColor: '#141A44', shadowOpacity: 0.08, shadowRadius: 5, shadowOffset: { width: 0, height: 2 } },
    default: { elevation: 2 },
  }) as ViewStyle,
  md: Platform.select<ViewStyle>({
    ios: { shadowColor: '#141A44', shadowOpacity: 0.16, shadowRadius: 12, shadowOffset: { width: 0, height: 6 } },
    default: { elevation: 6 },
  }) as ViewStyle,
};

/** Minimum touch target recommended by iOS/Android guidelines. */
export const HIT = Platform.OS === 'android' ? 48 : 44;

export const hitSlop = { top: 8, bottom: 8, left: 8, right: 8 };

/** Largest text scale the layouts are built for. */
export const MAX_FONT_SCALE = 1.6;
