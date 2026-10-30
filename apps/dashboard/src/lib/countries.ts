// Countries are picked by their names and stored as ISO 3166-1 alpha-2 codes (design system W8). Intl has no list of
// regions to ask for, so the officially assigned codes live here, once; the names come from Intl.DisplayNames in the
// person's own languages.

/** Every officially assigned ISO 3166-1 alpha-2 code. */
export const countryCodes: readonly string[] = [
  'AD', 'AE', 'AF', 'AG', 'AI', 'AL', 'AM', 'AO', 'AQ', 'AR', 'AS', 'AT', 'AU', 'AW', 'AX', 'AZ',
  'BA', 'BB', 'BD', 'BE', 'BF', 'BG', 'BH', 'BI', 'BJ', 'BL', 'BM', 'BN', 'BO', 'BQ', 'BR', 'BS', 'BT', 'BV', 'BW', 'BY', 'BZ',
  'CA', 'CC', 'CD', 'CF', 'CG', 'CH', 'CI', 'CK', 'CL', 'CM', 'CN', 'CO', 'CR', 'CU', 'CV', 'CW', 'CX', 'CY', 'CZ',
  'DE', 'DJ', 'DK', 'DM', 'DO', 'DZ',
  'EC', 'EE', 'EG', 'EH', 'ER', 'ES', 'ET',
  'FI', 'FJ', 'FK', 'FM', 'FO', 'FR',
  'GA', 'GB', 'GD', 'GE', 'GF', 'GG', 'GH', 'GI', 'GL', 'GM', 'GN', 'GP', 'GQ', 'GR', 'GS', 'GT', 'GU', 'GW', 'GY',
  'HK', 'HM', 'HN', 'HR', 'HT', 'HU',
  'ID', 'IE', 'IL', 'IM', 'IN', 'IO', 'IQ', 'IR', 'IS', 'IT',
  'JE', 'JM', 'JO', 'JP',
  'KE', 'KG', 'KH', 'KI', 'KM', 'KN', 'KP', 'KR', 'KW', 'KY', 'KZ',
  'LA', 'LB', 'LC', 'LI', 'LK', 'LR', 'LS', 'LT', 'LU', 'LV', 'LY',
  'MA', 'MC', 'MD', 'ME', 'MF', 'MG', 'MH', 'MK', 'ML', 'MM', 'MN', 'MO', 'MP', 'MQ', 'MR', 'MS', 'MT', 'MU', 'MV', 'MW', 'MX', 'MY', 'MZ',
  'NA', 'NC', 'NE', 'NF', 'NG', 'NI', 'NL', 'NO', 'NP', 'NR', 'NU', 'NZ',
  'OM',
  'PA', 'PE', 'PF', 'PG', 'PH', 'PK', 'PL', 'PM', 'PN', 'PR', 'PS', 'PT', 'PW', 'PY',
  'QA',
  'RE', 'RO', 'RS', 'RU', 'RW',
  'SA', 'SB', 'SC', 'SD', 'SE', 'SG', 'SH', 'SI', 'SJ', 'SK', 'SL', 'SM', 'SN', 'SO', 'SR', 'SS', 'ST', 'SV', 'SX', 'SY', 'SZ',
  'TC', 'TD', 'TF', 'TG', 'TH', 'TJ', 'TK', 'TL', 'TM', 'TN', 'TO', 'TR', 'TT', 'TV', 'TW', 'TZ',
  'UA', 'UG', 'UM', 'US', 'UY', 'UZ',
  'VA', 'VC', 'VE', 'VG', 'VI', 'VN', 'VU',
  'WF', 'WS',
  'YE', 'YT',
  'ZA', 'ZM', 'ZW',
];

export interface CountryOption {
  value: string;
  label: string;
}

/** A country's name in the person's languages; the code itself when the browser has no name for it. */
export function countryName(locales: readonly string[], code: string): string {
  return new Intl.DisplayNames([...locales], { type: 'region', fallback: 'code' }).of(code) ?? code;
}

/**
 * Every country by name, sorted for the person's languages. `current` stays in the list even when it is not an
 * assigned code, so a job saved with one keeps it until someone changes it.
 */
export function countryOptions(locales: readonly string[], current: readonly string[] = []): CountryOption[] {
  const names = new Intl.DisplayNames([...locales], { type: 'region', fallback: 'code' });
  const codes = new Set(countryCodes);
  for (const code of current) if (code) codes.add(code.toUpperCase());
  return [...codes]
    .map((code) => ({ value: code, label: names.of(code) ?? code }))
    .toSorted((a, b) => a.label.localeCompare(b.label, [...locales]));
}

/** Lower case without accents, so "aland" finds Åland and "cote" finds Côte d'Ivoire. */
const folded = (text: string) => text.normalize('NFD').replace(/\p{M}/gu, '').toLocaleLowerCase();

/** The options whose names hold every word of `query`, in any order and ignoring case and accents. */
export function matchCountries(options: readonly CountryOption[], query: string): readonly CountryOption[] {
  const words = folded(query).split(/\s+/).filter(Boolean);
  if (words.length === 0) return options;
  return options.filter((option) => {
    const name = folded(option.label);
    return words.every((word) => name.includes(word));
  });
}
