import type { DateFormat, EmailChoice, NoticeKind, NoticeScope } from '@screenhire/api-client';

/**
 * Your account (design system section 21, "Account pages"): what belongs to the person, whatever organization they work in.
 * Its pages share one menu, as Settings' do, and none needs a permission. Connected accounts shows only where this
 * ScreenHire has a calendar to connect or a Slack workspace connected (A3).
 */
export interface AccountSection {
  label: string;
  href: string;
  /** Everyday words that find it in Ctrl K. */
  keywords: readonly string[];
  /** Shown only where something can be connected. */
  connected?: true;
}

export const accountHome = '/account';

export const accountSections: readonly AccountSection[] = [
  { label: 'Profile', href: '/account/profile', keywords: ['name', 'photo', 'picture', 'time zone', 'language', 'date format', 'email'] },
  { label: 'Notifications', href: '/account/notifications', keywords: ['email', 'digest', 'toasts', 'alerts', 'slack', 'mentions'] },
  { label: 'Connected accounts', href: '/account/connected', keywords: ['calendar', 'google', 'outlook', 'slack'], connected: true },
  { label: 'Sign-in and security', href: '/account/security', keywords: ['passkeys', 'sign in', 'sign out everywhere', 'sessions', 'security'] },
  { label: 'Preferences', href: '/account/preferences', keywords: ['theme', 'dark', 'shortcuts', 'confirm moves', 'motion'] },
];

/** The account pages this person sees: Connected accounts only when there is something to connect. */
export function visibleAccountSections(hasConnections: boolean): readonly AccountSection[] {
  return accountSections.filter((section) => !section.connected || hasConnections);
}

/** The breadcrumb of an account page: Account, then the page. */
export function accountCrumbs(href: string): { label: string; href?: string }[] {
  const section = accountSections.find((entry) => entry.href === href);
  return [{ label: 'Account', href: accountHome }, { label: section?.label ?? 'Account' }];
}

/** Each row of the notification grid, in the API's order; the words are the dashboard's. */
export const noticeRows: Readonly<Record<NoticeKind, { label: string; hint: string }>> = {
  Mentions: { label: 'Mentions', hint: 'A teammate mentions you in a note' },
  Replies: { label: 'Candidate replies', hint: 'A candidate answers an email you sent' },
  Interviews: { label: 'Interviews booked or changed', hint: 'An interview you are on is booked, moved or cancelled' },
  Tasks: { label: 'Tasks assigned to you', hint: 'Someone gives you a task about a candidate' },
  NewApplications: { label: 'New applications on your jobs', hint: 'A candidate applies' },
  TeammateChanges: { label: "Teammates' moves and notes", hint: 'A teammate moves a candidate or adds a note' },
  VerificationSuggested: { label: 'Verification suggested', hint: 'An application needs a closer look' },
  AiUseAndBilling: { label: 'AI use and billing', hint: 'AI use passes the budget, or the trial ends' },
};

export const emailChoices: readonly { value: EmailChoice; label: string }[] = [
  { value: 'Instantly', label: 'Instantly' },
  { value: 'Digest', label: 'Daily digest' },
  { value: 'Off', label: 'Off' },
];

export const scopeChoices: readonly { value: NoticeScope; label: string }[] = [
  { value: 'MyJobs', label: 'My jobs' },
  { value: 'Everything', label: 'Everything I can see' },
];

export const dateFormats: readonly { value: DateFormat; label: string; example: string }[] = [
  { value: 'Browser', label: 'As your language writes it', example: '' },
  { value: 'DayMonthYear', label: 'Day, month, year', example: '21 Nov 2026' },
  { value: 'MonthDayYear', label: 'Month, day, year', example: 'Nov 21, 2026' },
  { value: 'Iso', label: 'Year, month, day', example: '2026-11-21' },
];

/**
 * The languages dates and numbers can be written in. The interface itself is in English; this sets how a date or a number
 * reads, so it lists the English-speaking regions ScreenHire is sold in.
 */
export const languages: readonly { value: string; label: string }[] = [
  { value: 'en-US', label: 'English (United States)' },
  { value: 'en-GB', label: 'English (United Kingdom)' },
  { value: 'en-CA', label: 'English (Canada)' },
  { value: 'en-AU', label: 'English (Australia)' },
  { value: 'en-IE', label: 'English (Ireland)' },
  { value: 'en-NZ', label: 'English (New Zealand)' },
  { value: 'en-IN', label: 'English (India)' },
];

/** Crops a picture to a centred square and scales it to `side` pixels, as WebP (or PNG where WebP is not drawn), in base64. */
export async function squarePhoto(file: Blob, side = 256): Promise<string> {
  const bitmap = await createImageBitmap(file);
  const edge = Math.min(bitmap.width, bitmap.height);
  const canvas = document.createElement('canvas');
  canvas.width = side;
  canvas.height = side;
  const context = canvas.getContext('2d');
  if (!context) throw new Error('This browser cannot draw the picture.');
  context.drawImage(bitmap, (bitmap.width - edge) / 2, (bitmap.height - edge) / 2, edge, edge, 0, 0, side, side);
  bitmap.close();
  const dataUrl = canvas.toDataURL('image/webp', 0.9);
  const png = dataUrl.startsWith('data:image/webp') ? dataUrl : canvas.toDataURL('image/png');
  return png.slice(png.indexOf(',') + 1);
}
