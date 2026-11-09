import type { CandidateEmailEntry, ScorecardEntry, TeamMember } from '@screenhire/api-client';
import { render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import LatestMessagePanel, { firstLine } from './latest-message-panel.svelte';
import ScorecardsSummary from './scorecards-summary.svelte';

afterEach(() => vi.unstubAllGlobals());

const members = [{ userId: 'u1', displayName: 'Avery Quinn' }, { userId: 'u2', displayName: 'Robin Ashby' }] as TeamMember[];

const card = (overrides: Partial<ScorecardEntry>): ScorecardEntry =>
  ({
    id: 'sc-1',
    stageId: 'screening',
    reviewerUserId: 'u2',
    answers: [],
    rating: 4,
    ratings: [],
    recommendation: 'Yes',
    note: null,
    workflowVersion: 1,
    timelineEventId: 'e1',
    submittedAt: '2026-10-28T10:00:00Z',
    scorecardTemplateId: null,
    scorecardName: 'Phone screen',
    withheld: false,
    ...overrides,
  }) as ScorecardEntry;

describe('scorecards on the Overview', () => {
  const show = (props: Record<string, unknown>) => render(WithTooltips, { props: { component: ScorecardsSummary, props: { members, currentUserId: 'u1', ...props } } });

  it('shows the rating and one line per person: name, scorecard, recommendation in words and the date', () => {
    show({
      rating: { state: 'Rated', value: 4.2, scorecards: 2 },
      scorecards: [card({}), card({ id: 'sc-2', reviewerUserId: 'u1', recommendation: 'StrongYes', scorecardName: 'Technical interview', submittedAt: '2026-10-29T10:00:00Z' })],
    });

    expect(screen.getByRole('link', { name: 'All scorecards' })).toHaveAttribute('href', '?tab=scorecards');
    expect(screen.getByText('4.2')).toBeInTheDocument();
    const lines = within(screen.getByRole('list', { name: 'Scorecards given' })).getAllByRole('listitem');
    expect(lines.map((line) => line.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Robin Ashby Phone screen Yes Oct 28, 2026',
      'You Technical interview Strong yes Oct 29, 2026',
    ]);
  });

  it('draws nothing while there are no scorecards and none are withheld', () => {
    const { container } = show({ rating: { state: 'NotRated', scorecards: 0 }, scorecards: [] });

    expect(container.querySelector('section')).toBeNull();
  });

  it('says the rating comes after your scorecard, and offers Fill in, while the others are withheld', () => {
    show({ rating: { state: 'AfterYourScorecard', scorecards: 0 }, scorecards: [], withheld: true, onfill: () => {} });

    expect(screen.getByText('Rating after your scorecard.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Fill in your scorecard' })).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Scorecards given' })).toBeNull();
  });

  it('lists who gave a withheld scorecard and when, with the recommendation blank for screen readers', () => {
    show({
      rating: { state: 'AfterYourScorecard', scorecards: 0 },
      scorecards: [card({ withheld: true, recommendation: null, rating: null })],
      withheld: true,
    });

    const [line] = within(screen.getByRole('list', { name: 'Scorecards given' })).getAllByRole('listitem');
    expect(line).toHaveTextContent('Robin Ashby');
    expect(line).toHaveTextContent('Oct 28, 2026');
    expect(within(line!).getByText('Hidden until you submit yours')).toHaveClass('sr-only');
    expect(line).not.toHaveTextContent('Yes');
  });

  it('shows no number or percentage that grades anyone beyond the people\'s own rating', () => {
    show({ rating: { state: 'Rated', value: 3.5, scorecards: 1 }, scorecards: [card({})] });

    expect(screen.queryByText(/%/)).toBeNull();
  });
});

describe('first line of an email', () => {
  it('is the first line with words in it, trimmed', () => {
    // A lone greeting says who it is to, not what it says, so the line after it is the one shown.
    expect(firstLine('\n\n  Hi Avery,\nThanks for the call.\n')).toBe('Thanks for the call.');
    expect(firstLine('Dear Ms Quinn\n\nWe would like to book a time.')).toBe('We would like to book a time.');
    expect(firstLine('Hello, can we move the call to Friday?')).toBe('Hello, can we move the call to Friday?');
    expect(firstLine('Highlights from the call: three.')).toBe('Highlights from the call: three.');
    expect(firstLine('Hi Avery,')).toBe('Hi Avery,');
    expect(firstLine('Hello all,\nThe offer letter is attached.')).toBe('The offer letter is attached.');
    expect(firstLine('Good morning Ms Quinn,\nI can start in March.')).toBe('I can start in March.');
    // A short reply that opens with a greeting is what was said, not a greeting to skip.
    expect(firstLine('Hi Avery, I can do Friday\n\nThanks,\nJordan')).toBe('Hi Avery, I can do Friday');
    expect(firstLine('Hey, accepting the offer\nJordan')).toBe('Hey, accepting the offer');
    expect(firstLine('Hello I accept\nJordan')).toBe('Hello I accept');
    expect(firstLine('   \n  ')).toBe('');
    expect(firstLine(undefined)).toBe('');
  });
});

describe('latest message on the Overview', () => {
  const entry = (overrides: Partial<CandidateEmailEntry>): CandidateEmailEntry =>
    ({ id: 'm1', applicationId: 'app-1', jobTitle: 'Plumber', templateId: null, subject: 'Re: Your application', actorUserId: null, automationRunId: null, sentAt: new Date(Date.now() - 86_400_000).toISOString(), kept: true, direction: 'Received', fromAddress: 'sam@example.com', attachmentCount: 0, ...overrides }) as CandidateEmailEntry;

  const stub = (path: string, body: unknown) => vi.stubGlobal('fetch', vi.fn(async (url: RequestInfo | URL) => (String(url).includes(path) ? Response.json(body) : new Response('{}', { status: 404 }))));
  const show = (emails: CandidateEmailEntry[] | undefined) =>
    render(WithQueryClient, { props: { component: LatestMessagePanel, props: { applicationId: 'app-1', emails, members, currentUserId: 'u1' } } });

  it('shows who replied, when and the first line of what they said, and a link to Messages', async () => {
    stub('/v1/replies/m1', { id: 'm1', applicationId: 'app-1', fromAddress: 'sam@example.com', fromName: 'Sam Taylor', subject: 'Re', html: '', text: '\nThanks, Thursday works for me.\nBest, Sam', receivedAt: '2026-10-28T10:00:00Z', autoReply: false, attachments: [] });
    show([entry({})]);

    const panel = screen.getByRole('region', { name: 'Latest message' });
    await waitFor(() => expect(within(panel).getByText('Sam Taylor')).toBeInTheDocument());
    expect(panel).toHaveTextContent(/replied/);
    await waitFor(() => expect(within(panel).getByText('Thanks, Thursday works for me.')).toBeInTheDocument());
    expect(within(panel).getByRole('link', { name: 'All messages' })).toHaveAttribute('href', '?tab=messages');
  });

  it("names a teammate who emailed, 'You' for the reader, and ignores another application's email", async () => {
    stub('/v1/emails/m2', { id: 'm2', applicationId: 'app-1', templateId: null, to: 'sam@example.com', subject: 'Interview', html: '', text: 'We would like to talk.', sentAt: '2026-10-28T10:00:00Z' });
    show([entry({ id: 'other', applicationId: 'app-2' }), entry({ id: 'm2', direction: 'Sent', actorUserId: 'u1', fromAddress: null })]);

    const panel = screen.getByRole('region', { name: 'Latest message' });
    expect(within(panel).getByText('You')).toBeInTheDocument();
    expect(panel).toHaveTextContent(/emailed/);
    await waitFor(() => expect(within(panel).getByText('We would like to talk.')).toBeInTheDocument());
  });

  it('draws nothing without an email for this application, or while they load', () => {
    expect(show([entry({ applicationId: 'app-2' })]).container.querySelector('section')).toBeNull();
    expect(show(undefined).container.querySelector('section')).toBeNull();
  });
});
