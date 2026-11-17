import { describe, expect, it } from 'vitest';
import { domainStatus, sendingDomainMark, tidyDomain } from './sending-domain.js';

describe('tidying a pasted domain', () => {
  it.each([
    ['example.com', 'example.com'],
    ['  Example.com  ', 'Example.com'],
    ['https://www.example.com/careers?x=1', 'example.com'],
    ['http://example.com:8080/', 'example.com'],
    ['hiring@example.com', 'example.com'],
    ['www.example.co.uk', 'example.co.uk'],
    ['mailto://jo@example.org', 'example.org'],
  ])('reads %s as %s', (pasted, domain) => {
    expect(tidyDomain(pasted)).toBe(domain);
  });

  it('leaves what is not a domain for the API to refuse in its own words', () => {
    expect(tidyDomain('example')).toBe('example');
    expect(tidyDomain('www.com')).toBe('www.com');
    expect(tidyDomain('')).toBe('');
  });
});

const record = (state: 'Matches' | 'Differs' | 'Missing' | 'NotChecked') => ({ state }) as never;
const pending = (...states: ('Matches' | 'Differs' | 'Missing' | 'NotChecked')[]) => ({ status: 'Pending' as const, records: states.map(record) });

describe('the email domain mark in the Settings menu', () => {
  it('shows nothing until a domain is added, because the setting is optional', () => {
    expect(sendingDomainMark(null)).toBeUndefined();
    expect(sendingDomainMark(undefined)).toBeUndefined();
  });

  it('is a check once the email provider has verified the domain', () => {
    expect(sendingDomainMark({ status: 'Verified', records: [] })).toEqual({ status: 'done', label: 'Verified' });
  });

  it('is a cross while records need fixing, and nothing while a lookup or the provider is still on its way', () => {
    expect(sendingDomainMark(pending('Matches', 'Missing'))).toEqual({ status: 'broken', label: 'Records to add' });
    expect(sendingDomainMark(pending('NotChecked', 'NotChecked'))).toBeUndefined();
    expect(sendingDomainMark(pending('Matches', 'Matches'))).toBeUndefined();
  });
});
