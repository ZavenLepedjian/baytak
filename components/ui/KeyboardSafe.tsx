import React, { useEffect, useRef, useState } from 'react';
import { Keyboard, KeyboardAvoidingView, Platform, StyleProp, StyleSheet, View, ViewStyle } from 'react-native';

type Props = {
  children: React.ReactNode;
  /** Style of the area that shrinks above the keyboard (the screen content). */
  style?: StyleProp<ViewStyle>;
  /** iOS: 'padding' uses KeyboardAvoidingView (for screens that do not scroll); 'none' leaves it to the ScrollView. */
  ios?: 'padding' | 'none';
};

/**
 * Keeps the content above the soft keyboard.
 *
 * On Android 15+ the app draws edge to edge and the system no longer resizes the window for the keyboard, so inputs sat
 * underneath it. This measures how far the keyboard overlaps this view and shrinks the content by that much. A shrinking
 * ScrollView scrolls the focused input into view by itself, so nothing else is needed. If the system already resized the
 * window (older Android) the overlap is 0 and nothing happens, so the two never add up.
 */
export function KeyboardSafe({ children, style, ios = 'none' }: Props) {
  const ref = useRef<View>(null);
  const [overlap, setOverlap] = useState(0);

  useEffect(() => {
    if (Platform.OS !== 'android') return;
    const show = Keyboard.addListener('keyboardDidShow', (e) => {
      const keyboardTop = e.endCoordinates.screenY;
      ref.current?.measureInWindow((_x, y, _w, h) => setOverlap(Math.max(0, Math.round(y + h - keyboardTop))));
    });
    const hide = Keyboard.addListener('keyboardDidHide', () => setOverlap(0));
    return () => {
      show.remove();
      hide.remove();
    };
  }, []);

  if (Platform.OS === 'ios') {
    return ios === 'padding' ? (
      <KeyboardAvoidingView style={[styles.flex, style]} behavior="padding">
        {children}
      </KeyboardAvoidingView>
    ) : (
      <View style={[styles.flex, style]}>{children}</View>
    );
  }

  return (
    <View ref={ref} style={styles.flex} collapsable={false}>
      <View style={[styles.flex, style, { paddingBottom: overlap }]}>{children}</View>
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
});
