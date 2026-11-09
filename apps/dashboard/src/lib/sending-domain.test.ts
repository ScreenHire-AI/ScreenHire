import { describe, expect, it } from 'vitest';
import { domainStatus, sendingDomainMark } from './sending-domain.js';

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
