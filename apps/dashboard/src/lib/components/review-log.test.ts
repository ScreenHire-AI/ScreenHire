import type { VerificationReviewEntry } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, user } from '../../test-support/fixtures.js';
import { botVerification, verificationApplicationId } from '../../test-support/verification-fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import ReviewLog from './review-log.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));

afterEach(() => vi.unstubAllGlobals());

const evidenceChanged = 'Evidence changed after a re-check: a hosting-provider network was no longer seen.';

function logEntry(overrides: Partial<VerificationReviewEntry>): VerificationReviewEntry {
  return {
    id: 'l1',
    type: 'NoteAdded',
    assessmentKind: null,
    assessmentId: null,
    dismissReason: null,
    note: null,
    actorUserId: user.id,
    actorName: 'Avery Quinn',
    occurredAt: '2026-10-20T10:00:00Z',
    signalId: null,
    signalTitle: null,
    explainReason: null,
    apiKeyId: null,
    apiKeyName: null,
    keyCreatedByName: null,
    ...overrides,
  };
}

function renderLog(reviewLog: VerificationReviewEntry[]) {
  vi.stubGlobal(
    'fetch',
    vi.fn(apiStub({ [`GET /v1/applications/${verificationApplicationId}/verification`]: () => Response.json(botVerification({ reviewLog })) })),
  );
  return render(WithQueryClient, { props: { component: ReviewLog, props: { applicationId: verificationApplicationId } } });
}

describe('review log', () => {
  it('shows an evidence change as ScreenHire\'s own line: its reason is the event, by ScreenHire, with the scan icon and no quotation marks', async () => {
    renderLog([logEntry({ id: 'e1', type: 'EvidenceChanged', actorUserId: null, actorName: null, note: evidenceChanged })]);

    const log = await screen.findByRole('list', { name: 'Review actions' });
    const line = within(log).getAllByRole('listitem');

    expect(line).toHaveLength(1);
    expect(line[0]).toHaveTextContent(`${evidenceChanged} by ScreenHire`);
    expect(line[0]!.querySelector('[data-icon="scan-search"]')).not.toBeNull();
    // A reviewer's note is quoted under its event; the system's reason is the event itself, said once.
    expect(line[0]).not.toHaveTextContent('"');
    expect(line[0]!.textContent!.split(evidenceChanged)).toHaveLength(2);
  });

  it('says Evidence changed when the entry carries no reason', async () => {
    renderLog([logEntry({ id: 'e1', type: 'EvidenceChanged', actorUserId: null, actorName: null, note: null })]);

    const log = await screen.findByRole('list', { name: 'Review actions' });

    expect(within(log).getByText('Evidence changed')).toBeInTheDocument();
    expect(within(log).getByText('by ScreenHire')).toBeInTheDocument();
  });

  it('still quotes a reviewer\'s note and names the reviewer, newest entry first', async () => {
    renderLog([
      logEntry({ id: 'n1', type: 'NoteAdded', note: 'Asked for references.', occurredAt: '2026-10-20T10:00:00Z' }),
      logEntry({ id: 'e1', type: 'EvidenceChanged', actorUserId: null, actorName: null, note: evidenceChanged, occurredAt: '2026-10-20T11:00:00Z' }),
    ]);

    const log = await screen.findByRole('list', { name: 'Review actions' });
    const lines = within(log).getAllByRole('listitem');

    expect(lines[0]).toHaveTextContent('by ScreenHire');
    expect(lines[1]).toHaveTextContent('Note by you');
    expect(within(lines[1]!).getByText('"Asked for references."')).toBeInTheDocument();
    expect(lines[1]!.querySelector('[data-icon="scan-search"]')).toBeNull();
  });

  it('has no axe violations with an evidence change in the log', async () => {
    renderLog([logEntry({ id: 'e1', type: 'EvidenceChanged', actorUserId: null, actorName: null, note: evidenceChanged })]);
    await screen.findByRole('list', { name: 'Review actions' });

    await expectNoAxeViolations();
  });
});
