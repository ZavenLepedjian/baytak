import React, { useState } from 'react';
import { Share, View } from 'react-native';
import * as Print from 'expo-print';
import * as Sharing from 'expo-sharing';
import { t } from '../i18n';
import { colors, spacing } from '../theme';
import { Button, Sheet, Text, showMessage } from './ui';

type Props = {
  visible: boolean;
  title: string;
  intro: string;
  /** Title of the share menu that opens for the PDF. */
  dialogTitle: string;
  /** The words to send, and the page to turn into a PDF. Built when the person taps, so they are never stale. */
  message: () => string;
  html: () => string;
  onClose: () => void;
};

/** Sends something the administrator made (a receipt, a monthly summary) to the building's WhatsApp group: as a message or as a PDF. */
export function ShareSheet({ visible, title, intro, dialogTitle, message, html, onClose }: Props) {
  const [busy, setBusy] = useState(false);

  const sendMessage = async () => {
    try {
      await Share.share({ message: message() });
    } catch {
      showMessage({ message: t('Could not open the share menu'), type: 'danger' });
    }
  };

  const sharePdf = async () => {
    setBusy(true);
    try {
      const { uri } = await Print.printToFileAsync({ html: html() });
      if (!(await Sharing.isAvailableAsync())) {
        showMessage({ message: t('Sharing is not available on this phone'), type: 'warning' });
        return;
      }
      await Sharing.shareAsync(uri, { mimeType: 'application/pdf', dialogTitle, UTI: 'com.adobe.pdf' });
    } catch {
      showMessage({ message: t('Could not create the PDF'), type: 'danger' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet visible={visible} onClose={onClose} title={title}>
      <View style={{ gap: spacing.lg }}>
        <Text variant="body" color={colors.textMuted}>
          {intro}
        </Text>
        <Button title={t('Send message')} icon="logo-whatsapp" onPress={sendMessage} />
        <Button title={t('Share as PDF')} icon="document-text-outline" variant="secondary" loading={busy} onPress={sharePdf} />
        <Button title={t('Done')} variant="ghost" onPress={onClose} />
      </View>
    </Sheet>
  );
}
