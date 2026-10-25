export type TemplateLine = { name: string; type: 'Amount' | 'Text' };
export type StarterTemplate = { id: string; name: string; description: string; lines: TemplateLine[] };

const amount = (name: string): TemplateLine => ({ name, type: 'Amount' });
const note = (name = 'Note'): TemplateLine => ({ name, type: 'Text' });

/**
 * Starting points that need no setup. They only name the lines: amounts and the currency are the administrator's.
 * Names are plain words the residents read on their receipt.
 */
export const STARTER_TEMPLATES: StarterTemplate[] = [
  {
    id: 'monthly',
    name: 'Monthly building fees',
    description: 'Maintenance, electricity, elevator, security, cleaning',
    lines: [amount('Maintenance'), amount('Electricity'), amount('Elevator'), amount('Security'), amount('Cleaning'), note()],
  },
  { id: 'generator', name: 'Generator', description: 'Subscription or fuel top-up', lines: [amount('Generator'), note()] },
  { id: 'water', name: 'Water', description: 'Water delivery or the network bill', lines: [amount('Water'), note()] },
  { id: 'repairs', name: 'Repairs and one-offs', description: 'A repair or any single cost', lines: [amount('Repair'), note()] },
  { id: 'cleaning', name: 'Cleaning and garden', description: 'Stairs, entrance and garden', lines: [amount('Cleaning'), amount('Garden'), note()] },
];
