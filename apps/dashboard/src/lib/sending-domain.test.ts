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

describe('the email domain mark on the Settings home and menu', () => {
  it('is a grey Not set up until a domain is added', () => {
    expect(sendingDomainMark(null)).toEqual({ status: 'waiting', label: 'Not set up' });
    expect(sendingDomainMark(undefined)).toEqual({ status: 'waiting', label: 'Not set up' });
  });

  it('is nothing once the email provider has verified the domain (T3)', () => {
    expect(sendingDomainMark({ status: 'Verified', records: [] })).toBeUndefined();
  });

  it("is the page's own status while something is left to do, in the same words (W6, T1)", () => {
    const notChecked = pending('NotChecked', 'NotChecked');
    const toAdd = pending('Matches', 'Missing');
    const waiting = pending('Matches', 'Matches');

    expect(sendingDomainMark(notChecked)).toEqual({ status: 'waiting', label: 'Not checked' });
    expect(sendingDomainMark(toAdd)).toEqual({ status: 'needsFixes', label: 'Records to add' });
    expect(sendingDomainMark(waiting)).toEqual({ status: 'waiting', label: 'Waiting for confirmation' });
    for (const domain of [notChecked, toAdd, waiting]) expect(sendingDomainMark(domain)).toEqual(domainStatus(domain));
  });
});
