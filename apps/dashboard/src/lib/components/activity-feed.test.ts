import type { ActivityEntry, ActivityPage, ApplicationTimelineEntry, TeamMember } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import type { ActivityFeed as Feed } from '#lib/activity-feed.svelte.js';
import ActivityFeed from './activity-feed.svelte';

const now = Date.parse('2026-11-21T12:00:00Z');
const me = '01a157d4-0000-7000-8000-000000000001';
const riley = '01a157d4-0000-7000-8000-000000000002';
const members = [{ id: 'm2', userId: riley, displayName: 'Riley Harper' }] as TeamMember[];

const timeline = (patch: Partial<ApplicationTimelineEntry>): ApplicationTimelineEntry => ({
  id: 'e',
  type: 'StageChanged',
  occurredAt: '2026-11-21T11:59:30Z',
  actorUserId: null,
  fromStageId: null,
  toStageId: null,
  note: null,
  tag: null,
  automationRunId: null,
  actorApiKeyId: null,
  scorecardId: null,
  stageId: null,
  recommendation: null,
  ...patch,
});

const entry = (id: string, at: string, patch: Partial<ActivityEntry>, event?: Partial<ApplicationTimelineEntry>): ActivityEntry => ({
  id,
  kind: 'Moves',
  occurredAt: at,
  by: 'Member',
  actorUserId: riley,
  actorApiKeyName: null,
  jobId: 'job-1',
  jobTitle: 'Senior .NET Engineer',
  applicationId: 'app-1',
  candidateName: 'Taylor Brook',
  event: event ? timeline({ id, occurredAt: at, ...event }) : null,
  fromStageName: null,
  toStageName: null,
  stageName: null,
  followUpName: null,
  jobChange: null,
  ...patch,
});

const moved = entry('a', '2026-11-21T11:59:30Z', { toStageName: 'Interview' }, { type: 'StageChanged', toStageId: 'interview' });
const scored = entry('b', '2026-11-21T11:50:00Z', { kind: 'Scorecards', by: 'ApiKey', actorUserId: null, actorApiKeyName: 'ATS sync' }, { type: 'ScorecardSubmitted', scorecardId: 'card-1' });
const checked = entry('c', '2026-11-21T09:00:00Z', { kind: 'Applications', by: 'ScreenHire', actorUserId: null }, { type: 'VerificationAssessed' });
const published = entry('d', '2026-11-20T09:00:00Z', {
  kind: 'Jobs',
  applicationId: null,
  candidateName: null,
  jobChange: { created: false, line: { text: 'published the job', parts: [{ text: 'published the job', strong: false }], details: [] }, moreLines: 0 },
});

function feedOf(entries: ActivityEntry[], patch: Partial<Feed> = {}): Feed {
  const first: ActivityPage = { entries, nextBefore: null, seenAt: null, unseenCount: null, scope: 'MyJobs' };
  return {
    entries,
    first,
    error: undefined,
    hasMore: false,
    loadingMore: false,
    scope: 'MyJobs',
    kinds: [],
    setScope: vi.fn(),
    setKinds: vi.fn(),
    more: vi.fn(),
    refetch: vi.fn(),
    ...patch,
  };
}

function renderFeed(feed: Feed, since?: { at: string; count: number }, onnavigate = vi.fn()) {
  return render(WithTooltips, { props: { component: ActivityFeed, props: { feed, members, currentUserId: me, since, onnavigate } } });
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] });
  vi.setSystemTime(now);
});

afterEach(() => {
  vi.useRealTimers();
});

describe('activity feed', () => {
  it('says who did what with the record as a link to where the change is, the job underneath and how long ago', async () => {
    const onnavigate = vi.fn();
    const { container } = renderFeed(feedOf([moved, scored, checked, published]), undefined, onnavigate);
    const rows = within(screen.getByRole('list', { name: 'Activity' })).getAllByRole('listitem');

    expect(rows[0]).toHaveTextContent('Riley Harper moved Taylor Brook to Interview');
    expect(within(rows[0]!).getByRole('link', { name: 'Taylor Brook' })).toHaveAttribute('href', '/applications/app-1');
    expect(rows[0]).toHaveTextContent('Senior .NET Engineer');
    expect(rows[0]).toHaveTextContent('now');

    // A key's change names the key; a scorecard opens its own sheet.
    expect(rows[1]).toHaveTextContent('Submitted a scorecard for Taylor Brook by ATS sync, via an API key');
    expect(within(rows[1]!).getByRole('link', { name: 'Taylor Brook' })).toHaveAttribute('href', '/applications/app-1?tab=scorecards#scorecard-card-1');

    // The checks say only that they finished (non-negotiable 1).
    expect(rows[2]).toHaveTextContent(/^Checks finished for Taylor Brook/);
    expect(within(rows[2]!).getByRole('link', { name: 'Taylor Brook' })).toHaveAttribute('href', '/applications/app-1?tab=verification');

    expect(rows[3]).toHaveTextContent('Riley Harper changed Senior .NET Engineer: published the job');
    expect(within(rows[3]!).getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', '/jobs/job-1');

    await fireEvent.click(within(rows[0]!).getByRole('link', { name: 'Taylor Brook' }));
    expect(onnavigate).toHaveBeenCalled();
    await expectNoAxeViolations(container);
  });

  it('moves each time ago on by the minute while it is open', async () => {
    renderFeed(feedOf([moved]));
    const row = within(screen.getByRole('list', { name: 'Activity' })).getAllByRole('listitem')[0]!;
    expect(row.querySelector('time')).toHaveTextContent('now');

    await vi.advanceTimersByTimeAsync(2 * 60_000);

    expect(row.querySelector('time')).toHaveTextContent('2 minutes ago');
  });

  it('counts what came since the person last looked and marks where that ends', () => {
    renderFeed(feedOf([moved, scored, checked]), { at: '2026-11-21T10:00:00Z', count: 2 });

    expect(screen.getByText(/Since you were last here: 2 changes/)).toBeInTheDocument();
    const rows = within(screen.getByRole('list', { name: 'Activity' })).getAllByRole('listitem');
    expect(rows.map((row) => row.textContent?.trim().slice(0, 12))).toEqual(['Riley Harper', 'Submitted a ', 'Seen before', 'Checks finis']);
  });

  it('says nothing about last time when nothing came since', () => {
    renderFeed(feedOf([checked]), { at: '2026-11-21T10:00:00Z', count: 0 });

    expect(screen.queryByText(/Since you were last here/)).toBeNull();
    expect(screen.queryByText('Seen before')).toBeNull();
  });

  it('switches between My jobs and Everything I can see, and loads more on request', async () => {
    const feed = feedOf([moved], { hasMore: true });
    renderFeed(feed);

    expect(screen.getByRole('radio', { name: 'My jobs' })).toHaveAttribute('aria-checked', 'true');
    await fireEvent.click(screen.getByRole('radio', { name: 'Everything I can see' }));
    expect(feed.setScope).toHaveBeenCalledWith('Everything');

    await fireEvent.click(screen.getByRole('button', { name: 'Show more' }));
    expect(feed.more).toHaveBeenCalled();
  });

  it('says why it is empty', () => {
    renderFeed(feedOf([], { kinds: ['Moves'] }));
    expect(screen.getByText('Nothing of these kinds yet.')).toBeInTheDocument();
  });
});
