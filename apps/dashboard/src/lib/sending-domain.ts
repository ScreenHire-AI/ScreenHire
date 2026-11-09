import type { SendingDomainDetail, SendingDomainRecordPurpose, SendingDomainRecordState } from '@screenhire/api-client';
import type { StatusName } from '#lib/status.js';

/** The name each record goes by on the Sending domain page. */
export const recordPurposes: Record<SendingDomainRecordPurpose, string> = {
  Dkim: 'DKIM key',
  ReturnPath: 'Return path',
  Replies: 'Replies',
};

/** One record's heading. The provider's two DKIM records would read the same, so records of one purpose are numbered. */
export function recordName(records: readonly SendingDomainDetail['records'][number][], record: SendingDomainDetail['records'][number]): string {
  const same = records.filter((other) => other.purpose === record.purpose);
  return same.length > 1 ? `${recordPurposes[record.purpose]} ${same.indexOf(record) + 1}` : recordPurposes[record.purpose];
}

/** What the last lookup found for one record: found is calm, a record to add or fix needs attention (T1, T3). */
export function recordStatus(state: SendingDomainRecordState): { status: StatusName; label: string } {
  switch (state) {
    case 'Matches':
      return { status: 'done', label: 'Found' };
    case 'Differs':
      return { status: 'needsFixes', label: 'Different value' };
    case 'Missing':
      return { status: 'needsFixes', label: 'Not found' };
    default:
      return { status: 'waiting', label: 'Not checked' };
  }
}

/**
 * The domain's status beside the title (T2). Verified is the only state mail comes from the domain in; every other state
 * says what is left: records not looked up yet, records to add, or the email provider's confirmation.
 */
export function domainStatus(domain: Pick<SendingDomainDetail, 'status' | 'records'>): { status: StatusName; label: string } {
  if (domain.status === 'Verified') return { status: 'done', label: 'Verified' };
  if (domain.records.every((record) => record.state === 'NotChecked')) return { status: 'waiting', label: 'Not checked' };
  if (domain.records.some((record) => record.state === 'Differs' || record.state === 'Missing')) return { status: 'needsFixes', label: 'Records to add' };
  return { status: 'waiting', label: 'Waiting for confirmation' };
}

/** A status the places that point at the email domain show beside its name. */
export type SendingDomainMark = { status: StatusName; label: string };

/**
 * What the Settings home and menu show beside Email domain: only while something is left to do, and in the page's own
 * words (W6, T1). No domain yet is a grey Not set up. A verified domain shows nothing (T3). Any other state is the status
 * the page shows beside its title (`domainStatus`): grey while it waits, filled for Records to add.
 */
export function sendingDomainMark(domain: Pick<SendingDomainDetail, 'status' | 'records'> | null | undefined): SendingDomainMark | undefined {
  if (!domain) return { status: 'waiting', label: 'Not set up' };
  return domain.status === 'Verified' ? undefined : domainStatus(domain);
}
