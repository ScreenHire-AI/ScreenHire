// The reader's language for dates and times on careers pages (design system section 13). Written on the server from the
// request's Accept-Language, so the pages need no script to convert anything. Pure: no API client, no environment.

/** The language used when the request names none the runtime supports. */
export const DEFAULT_LOCALE = 'en-GB';

/** A longer header is ignored: no browser sends one, and it keeps the parse cheap. */
const MAX_HEADER = 512;

const supported = (tag: string): boolean => {
  try {
    return Intl.DateTimeFormat.supportedLocalesOf(tag).length > 0;
  } catch {
    return false;
  }
};

/**
 * The first language of an `Accept-Language` header that `Intl` supports (highest `q` first, ties in the order sent), else en-GB.
 * `*` and tags that are not language tags are skipped.
 */
export function pickLocale(acceptLanguage: string | null | undefined): string {
  if (!acceptLanguage || acceptLanguage.length > MAX_HEADER) return DEFAULT_LOCALE;

  const ranked = acceptLanguage
    .split(',')
    .map((part, index) => {
      const [tag = '', ...params] = part.trim().split(';');
      const weight = params.map((p) => /^\s*q=([\d.]+)\s*$/i.exec(p)?.[1]).find((q) => q !== undefined);
      return { tag: tag.trim(), q: weight === undefined ? 1 : Number(weight), index };
    })
    .filter((entry) => entry.tag !== '' && entry.tag !== '*' && Number.isFinite(entry.q) && entry.q > 0)
    .sort((a, b) => b.q - a.q || a.index - b.index);

  return ranked.find((entry) => supported(entry.tag))?.tag ?? DEFAULT_LOCALE;
}

/** The language to write this request's dates in. The one thing a page calls. */
export const localeOf = (request: Request): string => pickLocale(request.headers.get('accept-language'));

/** The first language tag of a request's `Accept-Language`, as sent to the API to record with an application, or null when it names none. */
export function firstTag(acceptLanguage: string | null | undefined): string | null {
  if (!acceptLanguage || acceptLanguage.length > MAX_HEADER) return null;
  const tag = acceptLanguage.split(',')[0]?.split(';')[0]?.trim() ?? '';
  return /^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,3}$/.test(tag) ? tag : null;
}

/** A date as the reader writes it, such as "29 November 2026" or "November 29, 2026", in UTC so it matches the email. */
export const longDate = (iso: string, locale: string): string =>
  new Intl.DateTimeFormat(locale, { dateStyle: 'long', timeZone: 'UTC' }).format(new Date(iso));
