import type { DateFormat } from '@screenhire/api-client';

/**
 * The person's own way of reading dates, from Your account > Profile: the language (formatting locale), the time zone and
 * the date format. Unset values follow the browser. Reactive, so a date on screen rewrites itself when the person saves a
 * change: `#lib/dates.ts` reads `version` in every formatter.
 */
export const dateSettings = $state<{ language: string | null; timeZone: string | null; dateFormat: DateFormat; version: number }>({
  language: null,
  timeZone: null,
  dateFormat: 'Browser',
  version: 0,
});

/** Applies the person's saved settings; nothing changes (and nothing re-renders) when they are what is in place. */
export function configureDates(next: { language?: string | null; timeZone?: string | null; dateFormat?: DateFormat }): void {
  const language = next.language ?? null;
  const timeZone = next.timeZone ?? null;
  const dateFormat = next.dateFormat ?? 'Browser';
  if (language === dateSettings.language && timeZone === dateSettings.timeZone && dateFormat === dateSettings.dateFormat) return;
  dateSettings.language = language;
  dateSettings.timeZone = timeZone;
  dateSettings.dateFormat = dateFormat;
  dateSettings.version += 1;
}
