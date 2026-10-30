import { describe, expect, it } from 'vitest';
import { countryCodes, countryName, countryOptions, matchCountries } from './countries.js';

describe('countries', () => {
  it('lists the 249 assigned ISO 3166-1 alpha-2 codes once each, each with a name the browser knows', () => {
    expect(countryCodes).toHaveLength(249);
    expect(new Set(countryCodes).size).toBe(249);
    const names = new Intl.DisplayNames(['en'], { type: 'region' });
    for (const code of countryCodes) {
      expect(code).toMatch(/^[A-Z]{2}$/);
      expect(names.of(code), code).not.toBe(code);
    }
  });

  it('names countries in the languages given, sorted by name', () => {
    const options = countryOptions(['en-GB']);
    expect(options).toHaveLength(249);
    expect(options.find((option) => option.value === 'GB')?.label).toBe('United Kingdom');
    expect(options.map((option) => option.label)).toEqual(options.map((option) => option.label).toSorted((a, b) => a.localeCompare(b, 'en-GB')));
    expect(countryName(['de'], 'IE')).toBe('Irland');
  });

  it("keeps a job's own code that is not on the list", () => {
    expect(countryOptions(['en'], ['xk']).some((option) => option.value === 'XK')).toBe(true);
    expect(countryName(['en'], 'QM')).toBe('QM');
  });

  it('matches every word of a search, ignoring case and accents', () => {
    const options = countryOptions(['en']);
    const labels = (query: string) => matchCountries(options, query).map((option) => option.label);
    expect(labels('aland')).toContain('Åland Islands');
    expect(labels('COTE')).toEqual(['Côte d’Ivoire']);
    expect(labels('kingdom united')).toEqual(['United Kingdom']);
    expect(labels('  ')).toHaveLength(249);
  });
});
