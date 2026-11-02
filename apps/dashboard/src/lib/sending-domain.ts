import type { SendingDomainDetail, SendingDomainRecordPurpose, SendingDomainRecordState } from '@screenhire/api-client';
import type { StatusName } from '#lib/status.js';

/** The name each record goes by on the Sending domain page. */
export const recordPurposes: Record<SendingDomainRecordPurpose, string> = {
  Dkim: 'DKIM key',
  ReturnPath: 'Return path',
  Replies: 'Replies',
};

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
