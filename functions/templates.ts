import { t } from '../i18n';
export type TemplateLine = { name: string; type: 'Amount' | 'Text' };
export type StarterTemplate = { id: string; name: string; description: string; lines: TemplateLine[] };

const amount = (name: string): TemplateLine => ({ name, type: 'Amount' });
const note = (name = t('Note')): TemplateLine => ({ name, type: 'Text' });

/**
 * Starting points that need no setup. They only name the lines: amounts and the currency are the administrator's.
 * Names are plain words the residents read on their receipt.
 */
export const STARTER_TEMPLATES: StarterTemplate[] = [
  {
    id: 'monthly',
    name: t('Monthly building fees'),
    description: t('Maintenance, electricity, elevator, security, cleaning'),
    lines: [amount(t('Maintenance')), amount(t('Electricity')), amount(t('Elevator')), amount(t('Security')), amount(t('Cleaning')), note()],
  },
  { id: 'generator', name: t('Generator'), description: t('Subscription or fuel top-up'), lines: [amount(t('Generator')), note()] },
  { id: 'water', name: t('Water'), description: t('Water delivery or the network bill'), lines: [amount(t('Water')), note()] },
  { id: 'repairs', name: t('Repairs and one-offs'), description: t('A repair or any single cost'), lines: [amount(t('Repair')), note()] },
  { id: 'cleaning', name: t('Cleaning and garden'), description: t('Stairs, entrance and garden'), lines: [amount(t('Cleaning')), amount(t('Garden')), note()] },
];
