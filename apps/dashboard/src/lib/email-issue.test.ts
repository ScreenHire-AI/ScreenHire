import { describe, expect, it } from 'vitest';
import { emailIssueNotice, emailIssueStatus } from './email-issue.js';
import { replyHtml } from './received-email.js';
import { domainStatus, recordStatus } from './sending-domain.js';

describe('email issue', () => {
  it('has a status with its icon, colour and label for a bounce and for a complaint, and none otherwise', () => {
    expect(emailIssueStatus('Bounced')).toEqual({ status: 'needsFixes', label: 'Email bounced' });
    expect(emailIssueStatus('SpamComplaint')).toEqual({ status: 'needsFixes', label: 'Marked as spam' });
    expect(emailIssueStatus('None')).toBeUndefined();
    expect(emailIssueStatus(undefined)).toBeUndefined();
  });

  it('explains what the flag means and that it says nothing else about the candidate', () => {
    expect(emailIssueNotice('Bounced', '550 mailbox not found')).toContain('(550 mailbox not found)');
    expect(emailIssueNotice('Bounced')).not.toContain('()');
    expect(emailIssueNotice('Bounced')).toContain('allow email again');
    expect(emailIssueNotice('Bounced')).toContain('says nothing else about the candidate');
    expect(emailIssueNotice('SpamComplaint')).toContain('says nothing else about the candidate');
    expect(emailIssueNotice('None')).toBeUndefined();
  });
});

describe('reply html', () => {
  it('shows the sanitized html when the reply has some', () => {
    expect(replyHtml({ html: '<p>Hello</p>', text: 'Hello' })).toBe('<p>Hello</p>');
  });

  it('shows a text-only reply as written, with nothing read as markup', () => {
    const html = replyHtml({ html: '  ', text: 'a <b>bold</b> & "plain"\nline two' });

    expect(html).toContain('a &lt;b&gt;bold&lt;/b&gt; &amp; &quot;plain&quot;\nline two');
    expect(html).not.toContain('<b>');
  });
});

describe('sending domain status', () => {
  it('is verified only when the domain is', () => {
    expect(domainStatus({ status: 'Verified', records: [] })).toEqual({ status: 'done', label: 'Verified' });
  });

  it('says not checked before any lookup, records to add after one that found a gap, and waiting once all are in place', () => {
    const record = (state: 'NotChecked' | 'Matches' | 'Missing' | 'Differs') =>
      ({ purpose: 'Dkim', type: 'CNAME', host: 'h', value: 'v', priority: null, state, reason: '' }) as const;

    expect(domainStatus({ status: 'Pending', records: [record('NotChecked'), record('NotChecked')] }).label).toBe('Not checked');
    expect(domainStatus({ status: 'Pending', records: [record('Matches'), record('Missing')] })).toEqual({ status: 'needsFixes', label: 'Records to add' });
    expect(domainStatus({ status: 'Pending', records: [record('Matches'), record('Differs')] }).label).toBe('Records to add');
    expect(domainStatus({ status: 'Pending', records: [record('Matches'), record('Matches')] })).toEqual({ status: 'waiting', label: 'Waiting for confirmation' });
  });

  it('words each record state, calm when found and attention when it is not', () => {
    expect(recordStatus('Matches')).toEqual({ status: 'done', label: 'Found' });
    expect(recordStatus('Missing')).toEqual({ status: 'needsFixes', label: 'Not found' });
    expect(recordStatus('Differs')).toEqual({ status: 'needsFixes', label: 'Different value' });
    expect(recordStatus('NotChecked')).toEqual({ status: 'waiting', label: 'Not checked' });
  });
});
