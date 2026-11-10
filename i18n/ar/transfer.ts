import type { Dictionary } from '../core.ts';

/** Arabic texts for handing a building over to another administrator. Drafts for a native speaker to review before release. */
export const transfer: Dictionary = {
  'Transfer administrator': 'نقل صلاحية المسؤول',
  'Hand the building to a resident': 'سلّم المبنى إلى أحد السكان',
  "The person you choose becomes the building's administrator at once. You become a resident, and only they can hand the building back.": 'يصبح الشخص الذي تختاره مسؤول المبنى فورًا. وتصبح أنت ساكنًا، ولا يستطيع إعادة المبنى إليك إلا هو.',
  'No one to choose yet': 'لا أحد للاختيار بعد',
  'Residents whose apartment you have approved appear here.': 'يظهر هنا السكان الذين وافقت على شققهم.',
  'Make {name} the administrator': 'تعيين {name} مسؤولًا',
  'Make {name} the administrator?': 'تعيين {name} مسؤولًا؟',
  '{name} in apartment {number} will manage the building from now on: receipts, payments and residents. You will become a resident.': 'سيتولّى {name} في الشقة {number} إدارة المبنى من الآن: الإيصالات والدفعات والسكان. وستصبح أنت ساكنًا.',
  'this resident': 'هذا الساكن',
  'Transfer': 'نقل',
  'The administrator role was transferred': 'تم نقل صلاحية المسؤول',

  // From the server
  'Please choose who becomes the administrator': 'اختر من سيصبح مسؤولًا',
  'You already manage this building': 'أنت تدير هذا المبنى أصلًا',
  'This person cannot be made administrator': 'لا يمكن تعيين هذا الشخص مسؤولًا',
  'This person already manages another building': 'هذا الشخص يدير مبنى آخر',
};
