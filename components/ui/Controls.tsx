import React, { useState } from 'react';
import { Platform, Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import DateTimePicker, { DateTimePickerAndroid, DateTimePickerEvent } from '@react-native-community/datetimepicker';
import * as Haptics from 'expo-haptics';
import { colors, HIT, radius, spacing } from '../../theme';
import { Text } from './Text';
import { Button } from './Button';
import { Sheet } from './Sheet';
import { formatDate } from '../../functions/format';
import { chevronForward } from './direction';

type Option<T extends string> = { value: T; label: string };

/** Pill-style single choice control (e.g. USD / LBP). */
export function Segmented<T extends string>({
  options,
  value,
  onChange,
  disabled,
}: {
  options: Option<T>[];
  value: T | '';
  onChange: (value: T) => void;
  disabled?: boolean;
}) {
  return (
    <View style={styles.segment} accessibilityRole="radiogroup">
      {options.map((o) => {
        const active = o.value === value;
        return (
          <Pressable
            key={o.value}
            disabled={disabled}
            accessibilityRole="radio"
            accessibilityState={{ selected: active, disabled }}
            onPress={() => {
              Haptics.selectionAsync().catch(() => {});
              onChange(o.value);
            }}
            style={[styles.segmentItem, active && styles.segmentActive]}
          >
            <Text variant="bodyStrong" color={active ? colors.onInk : colors.textMuted} adjustsFontSizeToFit numberOfLines={1} minimumFontScale={0.75}>
              {o.label}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}

/** Date input that opens the native Android dialog or an iOS bottom sheet. */
export function DateField({
  label,
  value,
  onChange,
  maximumDate,
}: {
  label?: string;
  value: Date;
  onChange: (date: Date) => void;
  maximumDate?: Date;
}) {
  const [iosOpen, setIosOpen] = useState(false);
  const [draft, setDraft] = useState(value);

  const open = () => {
    if (Platform.OS === 'android') {
      DateTimePickerAndroid.open({
        value,
        mode: 'date',
        maximumDate,
        onChange: (event: DateTimePickerEvent, date?: Date) => {
          if (event.type === 'set' && date) onChange(date);
        },
      });
    } else {
      setDraft(value);
      setIosOpen(true);
    }
  };

  return (
    <View>
      {label ? (
        <Text variant="bodyStrong" color={colors.textMuted} style={{ marginBottom: spacing.xs + 2 }}>
          {label}
        </Text>
      ) : null}
      <Pressable accessibilityRole="button" accessibilityLabel={`${label ?? 'Date'}: ${formatDate(value)}`} onPress={open} style={styles.dateBox}>
        <Ionicons name="calendar-outline" size={20} color={colors.textFaint} style={{ marginEnd: spacing.sm }} />
        <Text variant="body" style={{ flex: 1 }}>
          {formatDate(value)}
        </Text>
        <Ionicons name="chevron-down" size={18} color={colors.textFaint} />
      </Pressable>

      {Platform.OS === 'ios' ? (
        <Sheet visible={iosOpen} onClose={() => setIosOpen(false)} title={label ?? 'Select date'}>
          <DateTimePicker
            value={draft}
            mode="date"
            display="inline"
            maximumDate={maximumDate}
            themeVariant="light"
            accentColor={colors.primary}
            onChange={(_, date) => date && setDraft(date)}
          />
          <Button
            title="Done"
            onPress={() => {
              onChange(draft);
              setIosOpen(false);
            }}
            style={{ marginTop: spacing.md }}
          />
        </Sheet>
      ) : null}
    </View>
  );
}

type RowProps = {
  icon?: keyof typeof Ionicons.glyphMap;
  title: string;
  subtitle?: string;
  right?: React.ReactNode;
  onPress?: () => void;
  destructive?: boolean;
  chevron?: boolean;
};

/** Settings / menu style row. */
export function ListRow({ icon, title, subtitle, right, onPress, destructive, chevron = !!onPress }: RowProps) {
  const tint = destructive ? colors.danger : colors.primary;
  const content = (
    <View style={styles.row}>
      {icon ? (
        <View style={[styles.rowIcon, { backgroundColor: destructive ? colors.dangerSoft : colors.primarySoft }]}>
          <Ionicons name={icon} size={20} color={tint} />
        </View>
      ) : null}
      <View style={{ flex: 1 }}>
        <Text variant="bodyStrong" color={destructive ? colors.danger : colors.text}>
          {title}
        </Text>
        {subtitle ? (
          <Text variant="caption" color={colors.textMuted} style={{ marginTop: 1 }}>
            {subtitle}
          </Text>
        ) : null}
      </View>
      {right}
      {chevron ? <Ionicons name={chevronForward} size={18} color={colors.textFaint} /> : null}
    </View>
  );
  if (!onPress) return content;
  return (
    <Pressable accessibilityRole="button" accessibilityLabel={subtitle ? `${title}, ${subtitle}` : title} onPress={onPress} style={({ pressed }) => pressed && { backgroundColor: colors.primaryTint }}>
      {content}
    </Pressable>
  );
}

/** Tinted explanatory banner. */
export function InfoBanner({ icon = 'information-circle', children }: { icon?: keyof typeof Ionicons.glyphMap; children: React.ReactNode }) {
  return (
    <View style={styles.banner}>
      <Ionicons name={icon} size={20} color={colors.primaryDeep} style={{ marginTop: 1 }} />
      <Text variant="caption" color={colors.primaryDeep} style={{ flex: 1, fontSize: 14, lineHeight: 20 }}>
        {children}
      </Text>
    </View>
  );
}

const styles = StyleSheet.create({
  segment: { flexDirection: 'row', backgroundColor: colors.surfaceMuted, borderRadius: radius.md, padding: 4, gap: 4 },
  segmentItem: { flex: 1, minHeight: HIT, alignItems: 'center', justifyContent: 'center', borderRadius: radius.sm + 2 },
  segmentActive: { backgroundColor: colors.primary },
  dateBox: {
    flexDirection: 'row',
    alignItems: 'center',
    minHeight: 52,
    borderWidth: 1.5,
    borderColor: colors.borderInput,
    borderRadius: radius.md,
    backgroundColor: colors.surface,
    paddingHorizontal: spacing.md + 2,
  },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.md, paddingHorizontal: spacing.lg, minHeight: 60 },
  rowIcon: { width: 38, height: 38, borderRadius: radius.md - 2, alignItems: 'center', justifyContent: 'center' },
  banner: {
    flexDirection: 'row',
    gap: spacing.sm + 2,
    backgroundColor: colors.primarySoft,
    padding: spacing.md + 2,
    borderRadius: radius.md,
    marginBottom: spacing.lg,
  },
});
