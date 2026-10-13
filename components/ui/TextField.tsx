import React, { forwardRef, useState } from 'react';
import { StyleProp, StyleSheet, TextInput, TextInputProps, View, ViewStyle } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, family, MAX_FONT_SCALE, radius, spacing } from '../../theme';
import { Text } from './Text';

type Props = Omit<TextInputProps, 'style'> & {
  label?: string;
  helper?: string;
  error?: string | null;
  icon?: keyof typeof Ionicons.glyphMap;
  containerStyle?: StyleProp<ViewStyle>;
};

export const TextField = forwardRef<TextInput, Props>(function TextField(
  { label, helper, error, icon, containerStyle, editable = true, multiline, onFocus, onBlur, ...rest },
  ref,
) {
  const [focused, setFocused] = useState(false);
  const borderColor = error ? colors.danger : focused ? colors.primary : colors.borderInput;

  return (
    <View style={containerStyle}>
      {label ? (
        <Text variant="bodyStrong" color={error ? colors.danger : colors.textMuted} style={styles.label}>
          {label}
        </Text>
      ) : null}
      <View
        style={[
          styles.box,
          { borderColor, backgroundColor: editable ? colors.surface : colors.surfaceMuted },
          multiline && styles.multiline,
        ]}
      >
        {icon ? <Ionicons name={icon} size={20} color={focused ? colors.primary : colors.textFaint} style={styles.icon} /> : null}
        <TextInput
          ref={ref}
          editable={editable}
          multiline={multiline}
          placeholderTextColor={colors.textFaint}
          selectionColor={colors.primary}
          accessibilityLabel={label}
          maxFontSizeMultiplier={MAX_FONT_SCALE}
          onFocus={(e) => {
            setFocused(true);
            onFocus?.(e);
          }}
          onBlur={(e) => {
            setFocused(false);
            onBlur?.(e);
          }}
          style={[styles.input, multiline && styles.inputMultiline, !editable && { color: colors.textMuted }]}
          {...rest}
        />
      </View>
      {error ? (
        <Text variant="caption" color={colors.danger} style={styles.hint}>
          {error}
        </Text>
      ) : helper ? (
        <Text variant="caption" color={colors.textMuted} style={styles.hint}>
          {helper}
        </Text>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  label: { marginBottom: spacing.xs + 2 },
  box: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 52,
    borderWidth: 1.5,
    borderRadius: radius.md,
    paddingHorizontal: spacing.md + 2,
  },
  multiline: { alignItems: 'flex-start', paddingVertical: spacing.sm },
  icon: { marginEnd: spacing.sm },
  input: { flex: 1, fontFamily: family.regular, fontSize: 16, color: colors.text, paddingVertical: 12 },
  inputMultiline: { minHeight: 96, textAlignVertical: 'top' },
  hint: { marginTop: spacing.xs + 2 },
});
