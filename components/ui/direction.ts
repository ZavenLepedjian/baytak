import { I18nManager } from 'react-native';

/**
 * Direction-aware chevrons. Ionicons glyphs don't mirror on their own, so in a right-to-left layout "forward"
 * must point left. (React Native already flips `flexDirection: 'row'` and start/end styles.)
 */
export const chevronForward = I18nManager.isRTL ? 'chevron-back' : 'chevron-forward';
export const chevronBack = I18nManager.isRTL ? 'chevron-forward' : 'chevron-back';
