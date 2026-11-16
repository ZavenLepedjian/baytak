import type { Dictionary } from '../core.ts';

/** Arabic texts for the building's invite code, shared over WhatsApp. Drafts for a native speaker to review before release. */
export const invite: Dictionary = {
  // Administrator: the screen
  'Invite residents': 'دعوة السكان',
  'Share a code on WhatsApp': 'شارك رمزًا عبر واتساب',
  "Share this code in your building's WhatsApp group. Residents open the link, sign in with their phone number and enter their apartment. You still approve every request.":
    'شارك هذا الرمز في مجموعة واتساب الخاصة بالمبنى. يفتح السكان الرابط، ويسجّلون الدخول برقم هاتفهم ثم يدخلون شقتهم. وتبقى أنت من يوافق على كل طلب.',
  'Invite code': 'رمز الدعوة',
  'Valid for {hours} h {minutes} min': 'صالح لمدة {hours} س و{minutes} د',
  'Valid for {minutes} min': 'صالح لمدة {minutes} د',
  'Share on WhatsApp': 'مشاركة عبر واتساب',
  'Copy code': 'نسخ الرمز',
  'Code copied': 'تم نسخ الرمز',
  'Make a new code': 'إنشاء رمز جديد',
  'Stop sharing': 'إيقاف المشاركة',
  'Make a new code?': 'إنشاء رمز جديد؟',
  'The current code stops working at once. Share the new one with the residents.': 'يتوقف الرمز الحالي عن العمل فورًا. شارك الرمز الجديد مع السكان.',
  'Make new code': 'إنشاء الرمز',
  'Stop this code?': 'إيقاف هذا الرمز؟',
  'Nobody will be able to use it to join. You can make a new one at any time.': 'لن يتمكن أحد من استخدامه للانضمام. يمكنك إنشاء رمز جديد في أي وقت.',
  'Stop': 'إيقاف',
  'No active code': 'لا يوجد رمز فعّال',
  'Make a code and share it with your residents. It works for 8 hours.': 'أنشئ رمزًا وشاركه مع سكان المبنى. يعمل لمدة 8 ساعات.',
  'Make a code': 'إنشاء رمز',
  'Unable to open WhatsApp': 'تعذّر فتح واتساب',

  // The WhatsApp message
  "You're invited to join {building} on Baytak, the app for building fees.": 'أنت مدعو للانضمام إلى {building} على بيتك، تطبيق رسوم المباني.',
  'Open this link on your phone:': 'افتح هذا الرابط على هاتفك:',
  'Your building code:': 'رمز المبنى:',
  'Valid for 8 hours, until {time}.': 'صالح لمدة 8 ساعات، حتى {time}.',
  'Sign in with your phone number, then enter your apartment. The building administrator approves your request.':
    'سجّل الدخول برقم هاتفك ثم أدخل شقتك. يوافق مسؤول المبنى على طلبك.',

  // Resident: the join screen
  'Have an invite code?': 'هل لديك رمز دعوة؟',
  'Use code': 'استخدام الرمز',
  'Enter the 8-character code from the invitation': 'أدخل الرمز المؤلف من 8 خانات من الدعوة',
  'You were invited to join {building}. Fill in your apartment and send your request. The building administrator approves it.':
    'تمت دعوتك للانضمام إلى {building}. أدخل بيانات شقتك وأرسل طلبك. يوافق مسؤول المبنى عليه.',

  // Messages from the server
  'This invite code is not valid or has expired': 'رمز الدعوة هذا غير صالح أو انتهت صلاحيته',
  'The invite code was stopped': 'تم إيقاف رمز الدعوة',
};
