import React from 'react';
import { SITE_URL } from '../config';
import { NoticeLabels, NoticeModel, receiptNoticeHtml, receiptNoticeMessage } from '../functions/receiptNotice';
import { isArabic, t, tn } from '../i18n';
import { ShareSheet } from './ShareSheet';

/** The words on the PDF and in the message, in the app's language. */
export function noticeLabels(): NoticeLabels {
  return {
    title: t('Building fees'),
    date: t('Date'),
    item: t('Item'),
    amount: t('Amount'),
    totalPerApartment: t('Total per apartment'),
    billedTo: t('Billed to'),
    footer: t('Issued with Baytak. See what you owe in the app.'),
  };
}

/** "The whole building (14 apartments)" or "Apartments 3A, 3B". */
export function billedToText(selectedNumbers: string[], count: number): string {
  if (selectedNumbers.length > 0) return t('Apartments {list}', { list: selectedNumbers.join(', ') });
  return count > 0 ? `${t('The whole building')} (${tn('{n} apartment|{n} apartments', count)})` : t('The whole building');
}

/** After a receipt is issued (or from the collection screen): send it to the building's WhatsApp group as a message or a PDF. */
export function ShareReceiptSheet({ visible, model, first, onClose }: { visible: boolean; model: NoticeModel | null; first?: boolean; onClose: () => void }) {
  return (
    <ShareSheet
      visible={visible && model !== null}
      title={first ? t('Receipt added') : t('Share this receipt')}
      intro={
        first
          ? t("Send it to your building's WhatsApp group so residents see what is due and can join Baytak.")
          : t("Send the receipt to your building's WhatsApp group.")
      }
      dialogTitle={t('Share receipt')}
      message={() => receiptNoticeMessage(model!, noticeLabels(), SITE_URL)}
      html={() => receiptNoticeHtml(model!, noticeLabels(), isArabic())}
      onClose={onClose}
    />
  );
}
