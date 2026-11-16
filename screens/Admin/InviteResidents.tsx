import React, { useEffect, useState } from 'react';
import { Linking, Share, StyleSheet, View } from 'react-native';
import * as Clipboard from 'expo-clipboard';
import { useApi } from '../../api/useApi';
import { Button, Card, ConfirmDialog, EmptyState, ErrorState, InfoBanner, ListSkeleton, Screen, Text, showMessage } from '../../components/ui';
import { SITE_URL } from '../../config';
import { formatInviteCode, inviteLink, inviteMessage, timeLeft } from '../../functions/inviteLink';
import { useFetch } from '../../hooks/useFetch';
import { latinDigits, localeTag, t } from '../../i18n';
import { colors, spacing } from '../../theme';

type Invite = { active: boolean; buildingName: string; code: string | null; expiresAt: string | null };

const selectInvite = (raw: unknown): Invite => {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
  return {
    active: !!r.active,
    buildingName: String(r.buildingName ?? ''),
    code: typeof r.code === 'string' ? r.code : null,
    expiresAt: typeof r.expiresAt === 'string' ? r.expiresAt : null,
  };
};

const clock = (iso: string) => latinDigits(new Date(iso).toLocaleTimeString(localeTag(), { hour: '2-digit', minute: '2-digit' }));

/**
 * The building's invite code, to share in its WhatsApp group. It works for 8 hours, names the building and nothing more:
 * the administrator still approves every request that comes in with it.
 */
export default function InviteResidents() {
  const api = useApi();
  const { data, loading, error, refreshing, refetch } = useFetch<Invite>('building/invite', { select: selectInvite });
  const [now, setNow] = useState(Date.now());
  const [asking, setAsking] = useState<'new' | 'stop' | null>(null);
  const [busy, setBusy] = useState(false);

  // The time left counts down while the screen is open.
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(timer);
  }, []);

  const left = data?.active && data.expiresAt ? timeLeft(data.expiresAt, now) : null;
  const code = data?.code && left && !left.expired ? data.code : null;

  const make = async () => {
    setBusy(true);
    const res = await api.post<unknown>('building/invite');
    setBusy(false);
    setAsking(null);
    if (res.ok) await refetch();
  };

  const stop = async () => {
    setBusy(true);
    const res = await api.post<string>('building/invite/revoke');
    setBusy(false);
    setAsking(null);
    if (!res.ok) return;
    showMessage({ message: typeof res.data === 'string' && res.data ? res.data : t('The invite code was stopped'), type: 'success' });
    await refetch();
  };

  const share = async () => {
    if (!code || !data?.expiresAt) return;
    const message = inviteMessage({
      headline: t("You're invited to join {building} on Baytak, the app for building fees.", { building: data.buildingName }),
      linkLabel: t('Open this link on your phone:'),
      link: inviteLink(SITE_URL, code),
      codeLabel: t('Your building code:'),
      code,
      validity: t('Valid for 8 hours, until {time}.', { time: clock(data.expiresAt) }),
      footer: t('Sign in with your phone number, then enter your apartment. The building administrator approves your request.'),
    });
    // Straight to WhatsApp, where the building's group is; any other app through the phone's share menu when it is not installed.
    try {
      await Linking.openURL(`whatsapp://send?text=${encodeURIComponent(message)}`);
    } catch {
      Share.share({ message }, { dialogTitle: t('Invite residents') }).catch(() => showMessage({ message: t('Unable to open WhatsApp'), type: 'danger' }));
    }
  };

  const copy = async () => {
    if (!code) return;
    await Clipboard.setStringAsync(formatInviteCode(code));
    showMessage({ message: t('Code copied'), type: 'success' });
  };

  if (loading && !data) {
    return (
      <Screen>
        <ListSkeleton rows={2} rowHeight={140} />
      </Screen>
    );
  }
  if (error && !data) {
    return (
      <Screen scroll={false}>
        <ErrorState message={error} onRetry={refetch} />
      </Screen>
    );
  }

  const validity = left
    ? left.hours > 0
      ? t('Valid for {hours} h {minutes} min', { hours: left.hours, minutes: left.minutes })
      : t('Valid for {minutes} min', { minutes: left.minutes })
    : '';

  return (
    <Screen refreshing={refreshing} onRefresh={refetch}>
      <InfoBanner icon="logo-whatsapp">
        {t("Share this code in your building's WhatsApp group. Residents open the link, sign in with their phone number and enter their apartment. You still approve every request.")}
      </InfoBanner>

      {code ? (
        <>
          <Card style={styles.codeCard}>
            <Text variant="label" color={colors.textMuted}>
              {t('Invite code')}
            </Text>
            <Text numeral variant="display" adjustsFontSizeToFit numberOfLines={1} style={styles.code}>
              {formatInviteCode(code)}
            </Text>
            <Text variant="caption" color={colors.textMuted}>
              {validity}
            </Text>
          </Card>

          <View style={{ gap: spacing.md, marginTop: spacing.lg }}>
            <Button title={t('Share on WhatsApp')} icon="logo-whatsapp" onPress={share} />
            <Button title={t('Copy code')} icon="copy-outline" variant="secondary" onPress={copy} />
            <Button title={t('Make a new code')} icon="refresh-outline" variant="secondary" onPress={() => setAsking('new')} />
            <Button title={t('Stop sharing')} icon="close-circle-outline" variant="danger" onPress={() => setAsking('stop')} />
          </View>
        </>
      ) : (
        <>
          <EmptyState icon="ticket-outline" title={t('No active code')} message={t('Make a code and share it with your residents. It works for 8 hours.')} />
          <Button title={t('Make a code')} icon="add-circle-outline" loading={busy} onPress={make} style={{ marginTop: spacing.md }} />
        </>
      )}

      <ConfirmDialog
        visible={asking === 'new'}
        title={t('Make a new code?')}
        message={t('The current code stops working at once. Share the new one with the residents.')}
        confirmTitle={t('Make new code')}
        loading={busy}
        onCancel={() => setAsking(null)}
        onConfirm={make}
      />
      <ConfirmDialog
        visible={asking === 'stop'}
        title={t('Stop this code?')}
        message={t('Nobody will be able to use it to join. You can make a new one at any time.')}
        confirmTitle={t('Stop')}
        destructive
        loading={busy}
        onCancel={() => setAsking(null)}
        onConfirm={stop}
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  codeCard: { alignItems: 'center', gap: spacing.sm, marginTop: spacing.lg, paddingVertical: spacing.xl },
  code: { fontSize: 38, lineHeight: 46, letterSpacing: 4, textAlign: 'center', color: colors.primaryDeep },
});
