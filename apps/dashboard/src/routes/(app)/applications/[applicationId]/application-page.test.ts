import type {
  ApplicationDetail,
  ApplicationInterview,
  ApplicationTaskEntry,
  ApplicationVerification,
  VerificationReviewEntry,
  CandidateEmailEntry,
  ApplicationTimelineEntry,
  JobDetail,
  ScorecardEntry,
  ScorecardTemplateSummary,
  TeamMember,
} from '@screenhire/api-client';
import { QueryClient } from '@tanstack/svelte-query';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub, interviewer, jobId, roleCatalogue, stages, user, userAs } from '../../../../test-support/fixtures.js';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { stubViewport } from '../../../../test-support/viewport.js';
import { formatDateTime } from '#lib/dates.js';
import { botVerification, pendingVerification } from '../../../../test-support/verification-fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
// After the fixtures: the mock below reads `user`.
import { page } from '$app/state';
import Page from './+page.svelte';

// superforms (the verification panel's dialogs) reads `navigating` too.
vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));

const applicationId = '01a157d4-0000-7000-8000-000000000300';
const candidateId = '01a157d4-0000-7000-8000-000000000304';
const resumeId = '01a157d4-0000-7000-8000-000000000301';
const resumeUpload = '01a157d4-0000-7000-8000-000000000302';
const portfolioUpload = '01a157d4-0000-7000-8000-000000000303';
const teammate = '01a157d4-0000-7000-8000-0000000000cc';
const taskId = '01a157d4-0000-7000-8000-000000000307';

const members: TeamMember[] = [
  { id: '01a157d4-0000-7000-8000-000000000308', userId: user.id, email: user.email, displayName: user.displayName, role: 'Owner', customRoleId: null, roleName: 'Owner', joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
  { id: '01a157d4-0000-7000-8000-000000000309', userId: teammate, email: 'robin@brightwater.example.com', displayName: 'Robin Ashby', role: 'Custom', customRoleId: '01a157d4-0000-7000-8000-0000000000cd', roleName: 'Interviewer', joinedAt: '2026-10-02T09:00:00Z', jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
];

const task: ApplicationTaskEntry = {
  id: taskId,
  applicationId,
  kind: 'Task',
  note: 'Call the references.',
  assigneeUserId: null,
  automationRunId: '01a157d4-0000-7000-8000-000000000306',
  createdAt: '2026-10-20T10:05:00Z',
  completedAt: null,
  completedByUserId: null,
};

// Only what the page reads from the job: its stages and its questions.
const job = {
  id: jobId,
  title: 'Senior .NET Engineer',
  workflow: {
    stages,
    questionnaire: {
      questions: [
        { id: 'work_authorization', type: 'YesNo', label: 'Do you have the right to work in the UK?' },
        {
          id: 'experience',
          type: 'SingleChoice',
          label: 'Years of experience',
          options: [{ id: 'three_to_five', label: '3 to 5 years' }],
        },
        { id: 'portfolio', type: 'File', label: 'Portfolio' },
        { id: 'why', type: 'LongText', label: 'Why this role?' },
      ],
    },
    automations: [
      { id: 'ask-references', name: 'Ask for references', enabled: true, trigger: { type: 'Manual' }, actions: [{ type: 'CreateTask', note: 'Call the references.' }] },
      { id: 'on-arrival', name: 'On arrival', enabled: true, trigger: { type: 'ApplicationSubmitted' }, actions: [{ type: 'AddTag', tag: 'new' }] },
    ],
  },
} as unknown as JobDetail;

const application: ApplicationDetail = {
  id: applicationId,
  jobId,
  jobTitle: 'Senior .NET Engineer',
  candidate: { id: candidateId, fullName: 'Casey Morgan', firstName: 'Casey', lastName: 'Morgan', email: 'casey.morgan@example.com', phone: null, createdAt: '2026-10-20T09:00:00Z' },
  stage: { id: 'screening', name: 'Screening', kind: 'Screening' },
  workflowVersion: 1,
  accommodationRequested: true,
  aiUseDeclaration: 'Used',
  aiUseDetails: 'Grammar checks only.',
  submittedAt: '2026-10-20T09:00:00Z',
  stageEnteredAt: '2026-10-20T10:00:00Z',
  tags: [],
  source: 'CareersSite',
  answers: [
    { questionId: 'resume', value: resumeUpload },
    { questionId: 'work_authorization', value: 'yes' },
    { questionId: 'experience', value: 'three_to_five' },
    { questionId: 'portfolio', value: portfolioUpload },
    { questionId: 'why', value: 'Small teams.\nReal problems.' },
  ],
  files: [
    { id: resumeId, questionId: 'resume', uploadId: resumeUpload, fileName: 'Casey Morgan CV.pdf', contentType: 'application/pdf', sizeBytes: 48_000, scanStatus: 'NotScanned', uploadedAt: '2026-10-20T09:00:00Z' },
    { id: '01a157d4-0000-7000-8000-000000000305', questionId: 'portfolio', uploadId: portfolioUpload, fileName: 'portfolio.pdf', contentType: 'application/pdf', sizeBytes: 90_000, scanStatus: 'Infected', uploadedAt: '2026-10-20T09:00:00Z' },
  ],
  followUps: [
    {
      id: '01a157d4-0000-7000-8000-000000000310',
      questionnaireId: 'availability',
      name: 'Availability',
      firstSentAt: '2026-10-20T10:00:00Z',
      lastSentAt: '2026-10-20T10:00:00Z',
      expiresAt: '2026-11-03T10:00:00Z',
      timesSent: 1,
      completedAt: '2026-10-20T12:00:00Z',
      // The questions as the candidate saw them: the job's workflow may have changed since.
      questions: [
        { id: 'start_date', type: 'ShortText', label: 'When could you start?', required: true },
        { id: 'forklift_licence', type: 'YesNo', label: 'Do you hold a forklift licence?', required: true },
      ],
      answers: [
        { questionId: 'start_date', value: 'Next month' },
        { questionId: 'forklift_licence', value: 'no' },
      ],
    },
    {
      id: '01a157d4-0000-7000-8000-000000000311',
      questionnaireId: 'references',
      name: 'References',
      firstSentAt: '2026-10-20T10:00:00Z',
      lastSentAt: '2026-10-21T10:00:00Z',
      expiresAt: '2026-11-04T10:00:00Z',
      timesSent: 2,
      completedAt: null,
      questions: [],
      answers: [],
    },
  ],
};

const entry = (overrides: Partial<ApplicationTimelineEntry>): ApplicationTimelineEntry => ({
  id: crypto.randomUUID(),
  type: 'Submitted',
  occurredAt: '2026-10-20T09:00:00Z',
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
  ...overrides,
});

const timeline: ApplicationTimelineEntry[] = [
  entry({ type: 'Submitted', toStageId: 'applied' }),
  entry({ type: 'VerificationAssessed', occurredAt: '2026-10-20T09:01:00Z' }),
  entry({ type: 'StageChanged', occurredAt: '2026-10-20T10:00:00Z', actorUserId: teammate, fromStageId: 'applied', toStageId: 'screening', note: 'Strong portfolio.' }),
  entry({ type: 'TagAdded', occurredAt: '2026-10-20T10:05:00Z', tag: 'fast-track', automationRunId: '01a157d4-0000-7000-8000-000000000306' }),
  entry({ type: 'StageChanged', occurredAt: '2026-10-20T11:00:00Z', actorUserId: user.id, fromStageId: 'screening', toStageId: 'interview' }),
  entry({ type: 'NoteAdded', occurredAt: '2026-10-20T11:30:00Z', actorUserId: '01a157d4-0000-7000-8000-0000000000dd', note: 'Ask about the notice period.', noteId: '01a157d4-0000-7000-8000-0000000003a1' }),
];

// Notes for the menu tests: one of the signed-in person's, one of a teammate's, and one deleted since.
const ownNote = '01a157d4-0000-7000-8000-0000000003a2';
const teammatesNote = '01a157d4-0000-7000-8000-0000000003a3';
const deletedNote = '01a157d4-0000-7000-8000-0000000003a4';
const notes: ApplicationTimelineEntry[] = [
  entry({ type: 'NoteAdded', occurredAt: '2026-10-20T12:00:00Z', actorUserId: user.id, noteId: ownNote, note: `Ask @[${teammate}] about the call.`, noteEditedAt: '2026-10-20T12:30:00Z' }),
  entry({ type: 'NoteEdited', occurredAt: '2026-10-20T12:30:00Z', actorUserId: user.id, noteId: ownNote }),
  entry({ type: 'NoteAdded', occurredAt: '2026-10-20T12:05:00Z', actorUserId: teammate, noteId: teammatesNote, note: 'Strong portfolio.' }),
  entry({ type: 'NoteAdded', occurredAt: '2026-10-20T12:06:00Z', actorUserId: teammate, noteId: deletedNote, note: null }),
  entry({ type: 'NoteDeleted', occurredAt: '2026-10-20T12:40:00Z', actorUserId: teammate, noteId: deletedNote }),
];

// The routes every render reads; a test adds the writes it makes.
const reads = {
  [`GET /v1/applications/${applicationId}`]: () => Response.json(application),
  [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json(timeline),
  [`GET /v1/applications/${applicationId}/tasks`]: () => Response.json([task]),
  [`GET /v1/applications/${applicationId}/automation-runs`]: () => Response.json([]),
  [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([]),
  [`GET /v1/applications/${applicationId}/interviews`]: () => Response.json([]),
  [`GET /v1/jobs/${jobId}`]: () => Response.json(job),
  [`GET /v1/applications/${applicationId}/verification`]: () => Response.json({ ...pendingVerification(), applicationId }),
  [`GET /v1/candidates/${candidateId}/emails`]: () => Response.json([]),
  'GET /v1/organization/members': () => Response.json(members),
};

function stubApi(writes: Parameters<typeof apiStub>[0] = {}) {
  const fetch = vi.fn(apiStub({ ...reads, ...writes }));
  vi.stubGlobal('fetch', fetch);
  return fetch;
}

beforeEach(() => {
  stubApi();
});

afterEach(() => {
  vi.unstubAllGlobals();
  page.data.user = user;
  page.url = new URL('http://dashboard.test/');
});

// The page's own query cache, kept so a test can wait until every request the page made has been answered.
let client: QueryClient;

/** Renders the page on `tab`, as its address (?tab=) says; Playwright switches tabs by clicking. */
function renderPage(tab?: string) {
  page.url = new URL(`http://dashboard.test/applications/${applicationId}${tab ? `?tab=${tab}` : ''}`);
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(WithQueryClient, { props: { component: Page, props: { params: { applicationId } }, client } });
}

/**
 * Every request the page made has been answered. Call it once something that only shows after the page has loaded is on the
 * screen, so a panel that should be there by now is really absent and not still on its way.
 */
const settled = () => waitFor(() => expect(client.isFetching()).toBe(0));

const asRole = (role: 'Owner' | 'Admin' | 'Recruiter' | 'Viewer') => (page.data.user = userAs(role));

async function openMenu(item: HTMLElement) {
  const trigger = within(item).getByRole('button', { name: 'More actions for this note' });
  await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

/** A note's line in Activity, once the timeline is in. */
const noteItem = (noteId: string) =>
  waitFor(() => {
    const item = document.getElementById(`note-${noteId}`);
    if (!item) throw new Error(`No note ${noteId} yet`);
    return item;
  });

const lines = (region: HTMLElement) => within(region).getAllByRole('listitem').map((item) => item.querySelector('p')?.textContent?.replace(/\s+/g, ' ').trim());

const plain = (element: Element) => element.textContent?.replace(/\s+/g, ' ').trim();

/** The header band, once the page has loaded. */
const header = async () => (await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' })).closest<HTMLElement>('[data-slot="header-band"]')!;

/** The first line of the header: the name and the one status badge beside it. */
const firstLine = async () => (await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' })).parentElement!;

/** The titles of the panels on the page, in document order. */
const panelNames = () => [...document.querySelectorAll('[data-slot="panel"] > header h2')].map((heading) => heading.textContent?.trim());

type Level = 'NoConcerns' | 'ReviewSuggested' | 'VerificationRecommended';

/** The verification answer for an authenticity level, dismissed by a reviewer or not. */
function verificationAt(level: Level, dismissed = false): ApplicationVerification {
  const bot = botVerification();
  const dismissal = { actionId: 'd1', reason: 'FalsePositive' as const, note: null, actorUserId: user.id, actorName: 'Avery Quinn', dismissedAt: '2026-10-20T11:00:00Z' };
  return { ...bot, applicationId, authenticity: { ...bot.authenticity!, level, dismissal: dismissed ? dismissal : null } };
}

const stubVerification = (verification: ApplicationVerification) =>
  stubApi({ [`GET /v1/applications/${applicationId}/verification`]: () => Response.json(verification) });

/** The verification chip beside the name, once it shows `kind`: clear, review, verify, pending or dismissed. */
const chip = (band: HTMLElement, kind: string) =>
  waitFor(() => {
    const found = band.querySelector<HTMLElement>(`[data-verification-chip][data-level="${kind}"]`);
    if (!found) throw new Error(`No ${kind} chip yet`);
    return found;
  });

// An interview the candidate was offered, for the Overview and the Schedule interview tests.
const interviewId = '01a157d4-0000-7000-8000-000000000400';
const startsAt = '2026-11-05T15:00:00Z';
const interview = (overrides: Partial<ApplicationInterview> = {}): ApplicationInterview => ({
  id: interviewId,
  status: 'Open',
  startsAt: null,
  endsAt: null,
  windowStart: '2026-11-03T05:00:00Z',
  windowEnd: '2026-11-17T05:00:00Z',
  timeZone: 'America/New_York',
  durationMinutes: 30,
  dayStart: '09:00',
  dayEnd: '17:00',
  meetingUrl: null,
  interviewers: [{ userId: user.id, displayName: 'Avery Quinn', isOrganizer: true, syncStatus: 'Synced', syncProblem: null }],
  createdAt: '2026-10-20T12:00:00Z',
  createdByUserId: user.id,
  cancelledByCandidate: false,
  newTimesOfferedAt: null,
  ...overrides,
});

describe('application page', () => {
  it('has the seven tabs in the same order, with counts, and the signals on Verification when the level needs a look', async () => {
    stubApi({ [`GET /v1/applications/${applicationId}/verification`]: () => Response.json({ ...botVerification(), applicationId }) });
    renderPage();

    const tabs = await screen.findByRole('tablist', { name: 'Candidate sections' });
    await waitFor(() => expect(within(tabs).getByRole('tab', { name: /^Verification \d+$/ })).toBeInTheDocument());
    await waitFor(() =>
      expect(within(tabs).getAllByRole('tab').map((tab) => tab.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        'Overview',
        'Application',
        'Files 2',
        'Messages 0',
        'Scorecards 0',
        expect.stringMatching(/^Verification \d+$/),
        'Activity 6',
      ]),
    );
    expect(within(tabs).getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
  });

  it('opens the Files tab from ?tab=files', async () => {
    renderPage('files');
    const tabs = await screen.findByRole('tablist', { name: 'Candidate sections' });

    expect(within(tabs).getByRole('tab', { name: /^Files/ })).toHaveAttribute('aria-selected', 'true');
    expect(await screen.findByRole('region', { name: 'Files' })).toBeInTheDocument();
  });

  describe('the header', () => {
    const phone = '+44 20 7946 0000';
    const withPhone = { ...application, candidate: { ...application.candidate, phone } };
    const rated = { ...application, rating: { state: 'Rated', value: 4.6, scorecards: 1 } };
    const needsALook = [
      ['ReviewSuggested', 'review', 'Review', '2', 'Review suggested'],
      ['VerificationRecommended', 'verify', 'Verify', '3', 'Verification suggested'],
    ] as const;

    it.each(needsALook)('shows %s as a filled badge with its one word and the level mark, and the long name for screen readers only', async (level, kind, word, steps, longName) => {
      stubVerification(verificationAt(level));
      renderPage();
      const shown = await chip(await header(), kind);

      expect(shown.querySelector('span[aria-hidden="true"]:not([data-level-mark])')).toHaveTextContent(new RegExp(`^${word}$`));
      // Three squares, filled to the level: two for Review, three for Verify.
      expect(shown.querySelector('[data-level-mark]')).toHaveAttribute('data-level-mark', steps);
      expect(shown.querySelectorAll('[data-level-mark] > span')).toHaveLength(3);
      expect(shown.querySelectorAll('[data-level-mark] > span.bg-current')).toHaveLength(Number(steps));
      expect(shown.querySelector('.sr-only')).toHaveTextContent(`Verification: ${longName}`);
      // On the first line, beside the name.
      expect(await firstLine()).toContainElement(shown);
    });

    it.each(needsALook)('says exactly the level name in the tooltip for %s', async (level, kind, _word, _steps, longName) => {
      stubVerification(verificationAt(level));
      renderPage();

      await fireEvent.click(await chip(await header(), kind));

      await waitFor(() => expect(document.querySelector('[data-slot="tooltip-content"]')).toHaveTextContent(new RegExp(`^${longName}$`)));
    });

    it('says who set a dismissed result aside, when and why, in its tooltip', async () => {
      stubVerification(verificationAt('VerificationRecommended', true));
      renderPage();

      await fireEvent.click(await chip(await header(), 'dismissed'));

      await waitFor(() =>
        expect(document.querySelector('[data-slot="tooltip-content"]')).toHaveTextContent(
          /^Verification suggested, dismissed by you on Oct 20(, 2026)?: The observations do not apply\.$/,
        ),
      );
    });

    const quiet: [string, ApplicationVerification, string, string][] = [
      ['clear', verificationAt('NoConcerns'), 'Clear', 'Verification: No concerns'],
      ['pending', { ...pendingVerification(), applicationId }, 'Pending', 'Verification: Checks pending'],
      ['dismissed', verificationAt('VerificationRecommended', true), 'Dismissed', 'Verification: Verification suggested, dismissed after review'],
    ];

    it.each(quiet)('keeps %s quiet: a muted word and no level mark', async (kind, verification, word, spoken) => {
      stubVerification(verification);
      renderPage();
      const shown = await chip(await header(), kind);

      expect(shown).toHaveAttribute('data-quiet');
      expect(shown.querySelector('[data-level-mark]')).toBeNull();
      expect(shown.querySelector('[data-slot="badge"]')).toHaveClass('text-muted-foreground');
      expect(shown.querySelector('span[aria-hidden="true"]:not([data-level-mark])')).toHaveTextContent(new RegExp(`^${word}$`));
      expect(shown.querySelector('.sr-only')).toHaveTextContent(spoken);
    });

    it('shows how they left in place of the verification state, even when verification is suggested', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, stage: { id: 'rejected', name: 'Rejected', kind: 'Rejected' } }),
        [`GET /v1/applications/${applicationId}/verification`]: () => Response.json(verificationAt('VerificationRecommended')),
      });
      renderPage();
      const band = await header();

      // The tab counts the signals behind the level, so its number says the verification answer is in.
      await within(screen.getByRole('tablist', { name: 'Candidate sections' })).findByRole('tab', { name: /^Verification \d+$/ });
      expect(await firstLine()).toHaveTextContent('Rejected');
      expect(band.querySelector('[data-verification-chip]')).toBeNull();
    });

    it('puts the email, phone, job and when they applied on one meta line, with no copy buttons', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json(withPhone) });
      renderPage();
      const band = await header();

      expect(await within(band).findByRole('link', { name: 'casey.morgan@example.com' })).toHaveAttribute('href', 'mailto:casey.morgan@example.com');
      expect(within(band).getByRole('link', { name: phone })).toHaveAttribute('href', `tel:${phone}`);
      expect(within(band).getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/jobs/${jobId}`);
      expect(band.querySelectorAll('dl')).toHaveLength(1);
      expect([...band.querySelectorAll('dt')].map((label) => label.textContent)).toEqual(['Email', 'Phone', 'Job', 'Applied']);
      expect(band.querySelector('dl')).toHaveTextContent(/Applied .+ via the careers page$/);
      expect(within(band).queryByRole('button', { name: /copy/i })).toBeNull();
    });

    it('shows the rating as a link to the Scorecards tab, not a popover', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json(rated) });
      renderPage();
      const band = await header();

      const rating = await within(band).findByRole('link', { name: 'Rated 4.6 out of 5 from 1 scorecard' });
      expect(rating).toHaveAttribute('href', '?tab=scorecards');
      expect(rating).toHaveAttribute('data-rating-link');
      expect(within(band).queryByRole('button', { name: /^Rated/ })).toBeNull();
    });

    it('has no rating link until a scorecard counts', async () => {
      renderPage();
      const band = await header();
      await within(band).findByRole('list', { name: 'Pipeline' });
      await settled();

      expect(band.querySelector('[data-rating-link]')).toBeNull();
    });

    describe('tags', () => {
      const tagged = { ...application, tags: ['Referral'] };

      it('opens a field in place from Add tag, then adds the tag and closes the field', async () => {
        const fetch = stubApi({ [`POST /v1/applications/${applicationId}/tags`]: () => Response.json(tagged) });
        renderPage();
        const band = await header();
        expect(within(band).queryByLabelText('New tag')).toBeNull();

        await fireEvent.click(await within(band).findByRole('button', { name: 'Add tag' }));
        const field = within(band).getByLabelText('New tag');
        expect(field).toHaveFocus();
        await fireEvent.input(field, { target: { value: 'Referral' } });
        await fireEvent.click(within(band).getByRole('button', { name: 'Add tag' }));

        await waitFor(() => expect(within(band).queryByLabelText('New tag')).toBeNull());
        const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/tags') && init?.method === 'POST');
        expect(JSON.parse(String(post![1]!.body))).toEqual({ tag: 'Referral' });
        expect(within(band).getByRole('button', { name: 'Add tag' })).toBeInTheDocument();
      });

      it('closes the field on Escape and brings Add tag back, adding nothing', async () => {
        const fetch = stubApi();
        renderPage();
        const band = await header();

        await fireEvent.click(await within(band).findByRole('button', { name: 'Add tag' }));
        await fireEvent.keyDown(within(band).getByLabelText('New tag'), { key: 'Escape' });

        expect(within(band).queryByLabelText('New tag')).toBeNull();
        expect(within(band).getByRole('button', { name: 'Add tag' })).toBeInTheDocument();
        expect(fetch.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);
      });

      it('keeps Remove reachable by keyboard, and declares it shown on hover, focus and tap', async () => {
        stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json(tagged) });
        renderPage();
        const remove = await within(await header()).findByRole('button', { name: 'Remove tag Referral' });

        expect(remove).not.toHaveAttribute('tabindex', '-1');
        remove.focus();
        expect(remove).toHaveFocus();
        // jsdom has no hover and no touch media, so this only checks that the reveal is declared: hidden at rest.
        expect(remove).toHaveClass('opacity-0', 'group-hover/tag:opacity-100', 'group-focus-within/tag:opacity-100', 'focus-visible:opacity-100', 'pointer-coarse:opacity-100');
      });

      it('removes a tag with its own button beside the pill', async () => {
        const fetch = stubApi({
          [`GET /v1/applications/${applicationId}`]: () => Response.json(tagged),
          [`DELETE /v1/applications/${applicationId}/tags?tag=Referral`]: () => Response.json(application),
        });
        renderPage();

        await fireEvent.click(await within(await header()).findByRole('button', { name: 'Remove tag Referral' }));

        await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url).endsWith('/tags?tag=Referral') && init?.method === 'DELETE')).toBe(true));
      });
    });

    describe('stage rail', () => {
      // "Today" is fixed: the application entered Screening on Oct 20 at 10:00, so it has been there three days.
      beforeEach(() => {
        vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-10-23T12:00:00Z') });
      });
      afterEach(() => {
        vi.useRealTimers();
      });

      it("draws the job's stages in order, with how long they have been in the current one and no dates on the others", async () => {
        renderPage();
        const rail = await within(await header()).findByRole('list', { name: 'Pipeline' });

        expect(rail).toHaveAttribute('data-stage-rail');
        const steps = within(rail).getAllByRole('listitem');
        expect(steps.map((step) => step.getAttribute('data-state'))).toEqual(['passed', 'current', 'upcoming', 'upcoming']);
        expect(steps[1]).toHaveAttribute('aria-current', 'step');
        // A passed stage says nothing under its name: the meta line says when they applied.
        expect(steps[0]).toHaveTextContent(/^Applied, step 1 of 4$/);
        expect(steps[1]).toHaveTextContent(/^Screening, step 2 of 4.*Here 3 days$/);
        expect(steps[2]).toHaveTextContent(/^Interview, step 3 of 4$/);
        expect(steps[3]).toHaveTextContent(/^Offer, step 4 of 4$/);
      });
    });

    it('reads top to bottom: the name, the meta line, the rating and tags, then the stage rail', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...rated, tags: ['Referral'] }) });
      renderPage();
      const band = await header();
      await within(band).findByRole('list', { name: 'Pipeline' });

      const parts = [
        within(band).getByRole('heading', { level: 1 }),
        band.querySelector('dl')!,
        band.querySelector('[data-rating-link]')!,
        within(band).getByRole('list', { name: 'Tags' }),
        band.querySelector('[data-stage-rail]')!,
      ];
      parts.slice(1).forEach((part, index) => expect(parts[index]!.compareDocumentPosition(part) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy());
    });

    it('has no mini cards, no link to the candidate profile and no Details rail', async () => {
      renderPage();
      const band = await header();
      await within(band).findByRole('list', { name: 'Pipeline' });
      await settled();

      expect(band.querySelector('[data-header-cards]')).toBeNull();
      for (const name of ['Contact', 'Application', 'Also applied to', 'Rating', 'Interview']) expect(within(band).queryByRole('group', { name })).toBeNull();
      expect(within(band).queryByRole('link', { name: 'Candidate profile' })).toBeNull();
      expect(screen.queryByRole('complementary', { name: 'Details' })).toBeNull();
    });
  });

  describe('Files tab', () => {
    it('lists each file the candidate gave, and withholds one the scan flagged', async () => {
      renderPage('files');
      const files = await screen.findByRole('region', { name: 'Files' });

      expect(within(files).getByRole('button', { name: 'View Casey Morgan CV.pdf' })).toBeInTheDocument();
      // The scan withheld the portfolio, so its row says why and offers neither View nor Download.
      expect(files).toHaveTextContent('Unsafe file');
      expect(within(files).queryByRole('button', { name: 'View portfolio.pdf' })).toBeNull();
      expect(within(files).queryByRole('link', { name: 'Download portfolio.pdf' })).toBeNull();
    });

    it("links the resume through the API's download route, as a full navigation", async () => {
      renderPage('files');

      const link = await screen.findByRole('link', { name: 'Download Casey Morgan CV.pdf' });
      expect(link).toHaveAttribute('href', `/v1/applications/${applicationId}/files/${resumeId}`);
      expect(link).toHaveAttribute('data-sveltekit-reload');
    });
  });

  describe('Overview', () => {
    const needsYou = () => screen.findByRole('region', { name: 'Needs you' });
    const ratedBy = (scorecards: number) => ({ ...application, rating: { state: 'Rated', value: 4.6, scorecards } });
    const scorecard = (reviewerUserId: string, overrides: Partial<ScorecardEntry> = {}): ScorecardEntry => ({
      id: crypto.randomUUID(),
      stageId: 'interview',
      reviewerUserId,
      answers: [],
      rating: 4.6,
      ratings: [],
      recommendation: 'StrongYes',
      note: null,
      workflowVersion: 1,
      timelineEventId: crypto.randomUUID(),
      submittedAt: '2026-10-20T11:45:00Z',
      scorecardTemplateId: 'interview',
      scorecardName: 'Interview',
      withheld: false,
      ...overrides,
    });
    const sentId = '01a157d4-0000-7000-8000-0000000003b0';
    const replyId = '01a157d4-0000-7000-8000-0000000003b1';
    const otherApplicationId = '01a157d4-0000-7000-8000-0000000003b2';
    const emailEntry = (id: string, overrides: Partial<CandidateEmailEntry> = {}): CandidateEmailEntry => ({
      id,
      applicationId,
      jobTitle: 'Senior .NET Engineer',
      templateId: null,
      subject: 'Next steps',
      actorUserId: teammate,
      automationRunId: null,
      sentAt: '2026-10-27T12:00:00Z',
      kept: true,
      ...overrides,
    });
    const sentBody = {
      id: sentId,
      applicationId,
      templateId: null,
      to: 'casey.morgan@example.com',
      subject: 'Next steps',
      html: '<p>Hi Casey,</p>',
      text: '\n\nHi Casey,\nWe would like to talk on Thursday.',
      sentAt: '2026-10-27T12:00:00Z',
    };

    it('is four panels: Needs you and Scorecards, then Interview and Latest message', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json(ratedBy(1)),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([scorecard(teammate)]),
        [`GET /v1/applications/${applicationId}/interviews`]: () => Response.json([interview({ status: 'Booked', startsAt, endsAt: '2026-11-05T15:30:00Z' })]),
        [`GET /v1/candidates/${candidateId}/emails`]: () => Response.json([emailEntry(sentId)]),
        [`GET /v1/emails/${sentId}`]: () => Response.json(sentBody),
      });
      renderPage();

      // No Verification, Answers, Files, Tags, Activity, Automations or Other applications panel: each has a tab or the header.
      await waitFor(() => expect(panelNames()).toEqual(['Needs you', 'Scorecards', 'Interview', 'Latest message']));
    });

    it('draws only Needs you when there is no scorecard, interview or email to say anything about', async () => {
      renderPage();
      await within(await needsYou()).findByText('Call the references.');
      await settled();

      expect(panelNames()).toEqual(['Needs you']);
    });

    it('has no note box, no Run control and no link to the candidate profile: those are on other tabs', async () => {
      renderPage();
      await within(await needsYou()).findByText('Call the references.');
      await settled();

      expect(screen.queryByLabelText('Note for the hiring team')).toBeNull();
      expect(screen.queryByRole('button', { name: /^Run,/ })).toBeNull();
      expect(screen.queryByRole('link', { name: 'Candidate profile' })).toBeNull();
    });

    it('lists a task by its title only, with no kind or assignee pill', async () => {
      renderPage();
      const tasks = await within(await needsYou()).findByRole('list', { name: 'Tasks' });

      const rows = within(tasks).getAllByRole('listitem');
      expect(rows).toHaveLength(1);
      expect(rows[0]).toHaveTextContent(/^Call the references\.$/);
    });

    it.each([
      ['VerificationRecommended', 'Confirm who they are on a short video call before interviewing.', '3'],
      ['ReviewSuggested', 'Read the evidence before the first screen.', '2'],
    ] as const)('adds a step for %s with its level mark and a link to the reasons, and never speaks of rejecting', async (level, sentence, steps) => {
      stubVerification(verificationAt(level));
      renderPage();
      const needs = await needsYou();

      const row = await waitFor(() => {
        const found = needs.querySelector<HTMLElement>('[data-verification-step]');
        if (!found) throw new Error('No verification step yet');
        return found;
      });
      expect(within(row).getByText(sentence)).toBeInTheDocument();
      expect(row.querySelector('[data-level-mark]')).toHaveAttribute('data-level-mark', steps);
      expect(within(row).getByRole('link', { name: 'See the reasons' })).toHaveAttribute('href', '?tab=verification');
      expect(needs).not.toHaveTextContent(/reject/i);
      expect(within(await header()).queryByRole('button', { name: /reject/i })).toBeNull();
    });

    it.each([
      ['clear', verificationAt('NoConcerns')],
      ['pending', { ...pendingVerification(), applicationId }],
      ['dismissed', verificationAt('VerificationRecommended', true)],
    ] as [string, ApplicationVerification][])('adds no step while the state is %s', async (kind, verification) => {
      stubVerification(verification);
      renderPage();
      const needs = await needsYou();
      await chip(await header(), kind);
      await within(needs).findByText('Call the references.');

      expect(needs.querySelector('[data-verification-step]')).toBeNull();
    });

    it('says nothing needs you when there is no task and no step', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}/tasks`]: () => Response.json([]) });
      renderPage();
      const needs = await needsYou();
      await chip(await header(), 'pending');

      expect(await within(needs).findByText('Nothing needs you right now.')).toBeInTheDocument();
    });

    it('shows the verification step, not "Nothing needs you right now.", when only that is waiting', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}/tasks`]: () => Response.json([]),
        [`GET /v1/applications/${applicationId}/verification`]: () => Response.json(verificationAt('VerificationRecommended')),
      });
      renderPage();
      const needs = await needsYou();

      await within(needs).findByText('Confirm who they are on a short video call before interviewing.');
      expect(within(needs).queryByText('Nothing needs you right now.')).toBeNull();
    });

    it('says the verification state failed to load, not that nothing needs you, when its request fails', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}/tasks`]: () => Response.json([]),
        [`GET /v1/applications/${applicationId}/verification`]: () => new Response(null, { status: 500 }),
      });
      renderPage();
      const needs = await needsYou();

      expect(await within(needs).findByText(/Could not load the verification state/)).toBeInTheDocument();
      expect(within(needs).queryByText('Nothing needs you right now.')).toBeNull();
    });

    describe('Scorecards', () => {
      it('shows the rating, then one line per scorecard: who, which scorecard, the recommendation in words and the date', async () => {
        stubApi({
          [`GET /v1/applications/${applicationId}`]: () => Response.json(ratedBy(2)),
          [`GET /v1/applications/${applicationId}/scorecards`]: () =>
            Response.json([
              scorecard(teammate),
              scorecard(user.id, { scorecardName: 'Phone screen', recommendation: 'Yes', submittedAt: '2026-10-21T11:45:00Z' }),
            ]),
        });
        renderPage();
        const panel = await screen.findByRole('region', { name: 'Scorecards' });
        const list = await within(panel).findByRole('list', { name: 'Scorecards given' });

        // The number and, in muted text, how many scorecards it comes from; the full sentence is for screen readers.
        expect(panel).toHaveTextContent('4.6');
        expect(panel).toHaveTextContent('(2)');
        expect(within(panel).getByText('Rated').parentElement).toHaveTextContent(/Rated\s*4\.6\s*out of 5 from\s*\(2\)\s*2 scorecards/);
        await waitFor(() =>
          expect(within(list).getAllByRole('listitem').map(plain)).toEqual([
            expect.stringMatching(/^Robin Ashby Interview Strong yes Oct 20/),
            expect.stringMatching(/^You Phone screen Yes Oct 21/),
          ]),
        );
      });

      it('is left out until someone has given a scorecard', async () => {
        renderPage();
        await within(await needsYou()).findByText('Call the references.');
        await settled();

        expect(screen.queryByRole('region', { name: 'Scorecards' })).toBeNull();
      });

      it('says the rating comes after your scorecard while you have one to give, and offers Fill in your scorecard', async () => {
        stubApi({
          [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, rating: { state: 'AfterYourScorecard', scorecards: 0 } }),
          'GET /v1/scorecard-templates': () => Response.json([]),
        });
        renderPage();
        const panel = await screen.findByRole('region', { name: 'Scorecards' });

        expect(within(panel).getByText('Rating after your scorecard.')).toBeInTheDocument();
        expect(panel).not.toHaveTextContent('Rated');
        await fireEvent.click(within(panel).getByRole('button', { name: 'Fill in your scorecard' }));
        expect(await screen.findByRole('dialog', { name: 'Give your scorecard' })).toBeInTheDocument();
      });

      it('offers no Fill in your scorecard to a role that does not fill in scorecards', async () => {
        asRole('Viewer');
        stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, rating: { state: 'AfterYourScorecard', scorecards: 0 } }) });
        renderPage();
        const panel = await screen.findByRole('region', { name: 'Scorecards' });
        await settled();

        expect(within(panel).getByText('Rating after your scorecard.')).toBeInTheDocument();
        expect(within(panel).queryByRole('button', { name: 'Fill in your scorecard' })).toBeNull();
      });
    });

    describe('Latest message', () => {
      // "Today" is fixed so relative times read the same on every run.
      beforeEach(() => {
        vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-10-30T12:00:00Z') });
      });
      afterEach(() => {
        vi.useRealTimers();
      });

      it('says who emailed and how long ago, with the first line of the email, read by its own id', async () => {
        const fetch = stubApi({
          [`GET /v1/candidates/${candidateId}/emails`]: () => Response.json([emailEntry(sentId)]),
          [`GET /v1/emails/${sentId}`]: () => Response.json(sentBody),
        });
        renderPage();
        const panel = await screen.findByRole('region', { name: 'Latest message' });

        await within(panel).findByText('Hi Casey,');
        await waitFor(() => expect(panel).toHaveTextContent('Robin Ashby emailed 3 days ago'));
        expect(panel).not.toHaveTextContent('We would like to talk');
        expect(fetch.mock.calls.some(([url]) => String(url) === `/v1/emails/${sentId}`)).toBe(true);
      });

      it("says who replied, from the sender's name, with the first line of the reply, read by its own id", async () => {
        const fetch = stubApi({
          [`GET /v1/candidates/${candidateId}/emails`]: () =>
            Response.json([emailEntry(replyId, { direction: 'Received', fromAddress: 'casey.morgan@example.com', actorUserId: null, subject: 'Re: Next steps' })]),
          [`GET /v1/replies/${replyId}`]: () =>
            Response.json({
              id: replyId,
              applicationId,
              fromAddress: 'casey.morgan@example.com',
              fromName: 'Casey Morgan',
              subject: 'Re: Next steps',
              html: '',
              text: 'Thursday works for me.\nSee you then.',
              receivedAt: '2026-10-27T12:00:00Z',
              autoReply: false,
              attachments: [],
            }),
        });
        renderPage();
        const panel = await screen.findByRole('region', { name: 'Latest message' });

        await within(panel).findByText('Thursday works for me.');
        expect(panel).toHaveTextContent('Casey Morgan replied 3 days ago');
        expect(panel).not.toHaveTextContent('See you then.');
        expect(fetch.mock.calls.some(([url]) => String(url) === `/v1/replies/${replyId}`)).toBe(true);
      });

      it("is about this application: an email about the candidate's other application does not count", async () => {
        stubApi({
          [`GET /v1/candidates/${candidateId}/emails`]: () =>
            Response.json([emailEntry('01a157d4-0000-7000-8000-0000000003b4', { applicationId: otherApplicationId, sentAt: '2026-10-29T12:00:00Z' }), emailEntry(sentId)]),
          [`GET /v1/emails/${sentId}`]: () => Response.json(sentBody),
        });
        renderPage();
        const panel = await screen.findByRole('region', { name: 'Latest message' });

        await waitFor(() => expect(panel).toHaveTextContent('Robin Ashby emailed 3 days ago'));
      });

      it('is left out when no email is about this application', async () => {
        stubApi({
          [`GET /v1/candidates/${candidateId}/emails`]: () => Response.json([emailEntry('01a157d4-0000-7000-8000-0000000003b4', { applicationId: otherApplicationId })]),
        });
        renderPage();
        await within(await screen.findByRole('tablist', { name: 'Candidate sections' })).findByRole('tab', { name: 'Messages 1' });
        await settled();

        expect(screen.queryByRole('region', { name: 'Latest message' })).toBeNull();
      });
    });
  });

  it("labels each answer with the job's question and shows choices by their labels", async () => {
    renderPage('application');
    const answers = await screen.findByRole('region', { name: 'Answers' });

    await within(answers).findByText('Do you have the right to work in the UK?');
    const rows = within(answers)
      .getAllByRole('term')
      .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.trim()]);
    // The job's questions, then the ones every application asks, for what the candidate declared.
    expect(rows.map(([label]) => label)).toEqual([
      'Do you have the right to work in the UK?',
      'Years of experience',
      'Portfolio',
      'Why this role?',
      'Did you use AI tools for this application?',
      'Adjustments to the process',
    ]);
    expect(rows[0]![1]).toBe('Yes');
    expect(rows[1]![1]).toBe('3 to 5 years');
    expect(rows[3]![1]).toBe('Small teams.\nReal problems.');
    // A file answer names the file; its row, with View and Download, is on the Files tab.
    expect(rows[2]![1]).toBe('portfolio.pdf');
    expect(within(answers).queryAllByRole('link')).toHaveLength(0);
  });

  it('tells the activity newest first, saying who made each change', async () => {
    renderPage('activity');
    const region = await screen.findByRole('region', { name: 'Activity' });

    await within(region).findByText(/Moved from Screening to Interview/);
    await within(region).findByText(/by Robin Ashby/);
    expect(lines(region)).toEqual([
      'Note by a former teammate',
      'Moved from Screening to Interview by you',
      'Tagged "fast-track" by an automation',
      'Moved from Applied to Screening by Robin Ashby',
      'Verification checks ran',
      'Applied on the careers page, into Applied',
    ]);
    expect(within(region).getByText('"Strong portfolio."')).toBeInTheDocument();
    expect(within(region).getByText('Ask about the notice period.')).toBeInTheDocument();
  });

  it("lists what people and API keys did to the candidate's verification in Activity, under the Verification kind", async () => {
    const logged = (overrides: Partial<VerificationReviewEntry>): VerificationReviewEntry => ({
      id: 'log-1',
      type: 'ReasonExplained',
      assessmentKind: 'Authenticity',
      assessmentId: null,
      dismissReason: null,
      note: null,
      actorUserId: user.id,
      actorName: 'Avery Quinn',
      occurredAt: '2026-10-20T12:00:00Z',
      signalId: null,
      signalTitle: "Sent from a hosting provider's network",
      explainReason: 'ExpectedForRole',
      apiKeyId: null,
      apiKeyName: null,
      keyCreatedByName: null,
      ...overrides,
    });
    const read = (id: string, occurredAt: string) =>
      logged({ id, type: 'ReadByKey', actorUserId: null, actorName: null, signalTitle: null, explainReason: null, apiKeyId: 'key-1', apiKeyName: 'Auditor', keyCreatedByName: 'Avery Quinn', occurredAt });
    stubApi({
      [`GET /v1/applications/${applicationId}/verification`]: () =>
        Response.json({ ...botVerification(), applicationId, reviewLog: [logged({}), read('r1', '2026-10-20T13:00:00Z'), read('r2', '2026-10-20T13:05:00Z')] }),
    });
    renderPage('activity');
    const region = await screen.findByRole('region', { name: 'Activity' });

    await within(region).findByText('Verification read through the API key Auditor, 2 times');
    // Newest first among the timeline's own lines, a run of reads by one key as one line, who made the key after it.
    expect(lines(region)[0]).toBe('Verification read through the API key Auditor, 2 times key made by Avery Quinn');
    expect(lines(region)).toContain(`Explained "Sent from a hosting provider's network": Expected for this role by you`);
    const filter = within(region).getByRole('group', { name: 'Show' });
    await waitFor(() => expect(within(filter).getByRole('button', { name: /^Verification/ })).toHaveTextContent('Verification 3'));
    await fireEvent.click(within(filter).getByRole('button', { name: /^Verification/ }));
    await waitFor(() => expect(within(region).getAllByRole('listitem')).toHaveLength(3));
    // The tab's own count is every line.
    expect(screen.getByRole('tab', { name: /^Activity/ })).toHaveTextContent('Activity 8');
  });

  it('filters Activity by kind, with a count on each kind and none on All, which the tab shows', async () => {
    renderPage('activity');
    const region = await screen.findByRole('region', { name: 'Activity' });
    const filter = within(region).getByRole('group', { name: 'Show' });

    await waitFor(() =>
      expect(within(filter).getAllByRole('button').map((button) => button.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        'All',
        'Notes 1',
        'Emails 0',
        'Scorecards 0',
        'Interviews 0',
        'Stage and tags 4',
        'Verification 1',
      ]),
    );
    await fireEvent.click(within(filter).getByRole('button', { name: /^Notes/ }));
    await waitFor(() => expect(within(region).getAllByRole('listitem')).toHaveLength(1));
    expect(within(filter).getByRole('button', { name: /^Notes/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it("shows a note's mentions by their current names, says Edited, and keeps one line for a deleted note", async () => {
    stubApi({ [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, ...notes]) });
    renderPage('activity');
    const region = await screen.findByRole('region', { name: 'Activity' });

    const own = await noteItem(ownNote);
    expect(own).toHaveTextContent('Ask @Robin Ashby about the call. Edited');
    expect(region).not.toHaveTextContent('Edited a note');
    expect(document.getElementById(`note-${deletedNote}`)).not.toBeInTheDocument();
    expect(lines(region)[0]).toBe('Deleted a note by Robin Ashby');
  });

  it("offers Edit and Delete on your own note, Delete only to an owner or admin on another's, and no menu to a recruiter", async () => {
    stubApi({ [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, ...notes]) });
    renderPage('activity');
    await screen.findByRole('region', { name: 'Activity' });
    const own = await noteItem(ownNote);
    let menu = await openMenu(own);
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Edit', 'Delete']);
    await fireEvent.keyDown(menu, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('menu')).not.toBeInTheDocument());
    menu = await openMenu(document.getElementById(`note-${teammatesNote}`)!);
    expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Delete']);
    cleanup();

    asRole('Recruiter');
    stubApi({ [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, ...notes]) });
    renderPage('activity');
    const theirs = await noteItem(teammatesNote);
    expect(within(theirs).queryByRole('button', { name: 'More actions for this note' })).not.toBeInTheDocument();
    expect(within(document.getElementById(`note-${ownNote}`)!).getByRole('button', { name: 'More actions for this note' })).toBeInTheDocument();
  });

  it('edits your own note in place, keeping its mentions, and deletes one after a confirm', async () => {
    const fetch = stubApi({
      [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, ...notes]),
      [`PUT /v1/applications/${applicationId}/notes/${ownNote}`]: () => Response.json({ id: ownNote }),
      [`DELETE /v1/applications/${applicationId}/notes/${teammatesNote}`]: () => new Response(null, { status: 204 }),
    });
    renderPage('activity');
    await screen.findByRole('region', { name: 'Activity' });
    const own = await noteItem(ownNote);

    await fireEvent.click(within(await openMenu(own)).getByRole('menuitem', { name: 'Edit' }));
    const field = await within(own).findByLabelText('Edit your note');
    expect(field).toHaveValue('Ask @Robin Ashby about the call.');
    await fireEvent.input(field, { target: { value: 'Ask @Robin Ashby about the call today.' } });
    await fireEvent.click(within(own).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url).endsWith(`/notes/${ownNote}`) && init?.method === 'PUT')).toBe(true));
    const put = fetch.mock.calls.find(([url, init]) => String(url).endsWith(`/notes/${ownNote}`) && init?.method === 'PUT');
    expect(JSON.parse(String(put![1]!.body))).toEqual({ note: `Ask @[${teammate}] about the call today.` });

    await fireEvent.click(within(await openMenu(document.getElementById(`note-${teammatesNote}`)!)).getByRole('menuitem', { name: 'Delete' }));
    const dialog = await screen.findByRole('dialog', { name: 'Delete this note?' });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Delete note' }));
    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url).endsWith(`/notes/${teammatesNote}`) && init?.method === 'DELETE')).toBe(true));
  });

  it('shows each follow-up questionnaire with its answers on Application, and its sends and answers in Activity', async () => {
    const withFollowUps = [
      ...timeline,
      entry({ type: 'QuestionnaireSent', occurredAt: '2026-10-20T11:40:00Z', automationRunId: '01a157d4-0000-7000-8000-000000000312', questionnaireId: 'availability', questionnaireRequestId: '01a157d4-0000-7000-8000-000000000310' }),
      entry({ type: 'QuestionnaireCompleted', occurredAt: '2026-10-20T12:00:00Z', questionnaireId: 'availability', questionnaireRequestId: '01a157d4-0000-7000-8000-000000000310' }),
    ];
    stubApi({ [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json(withFollowUps) });
    renderPage('application');

    const answered = await screen.findByRole('region', { name: 'Follow-up: Availability' });
    const rows = within(answered)
      .getAllByRole('term')
      .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.trim()]);
    expect(rows).toEqual([
      ['When could you start?', 'Next month'],
      ['Do you hold a forklift licence?', 'No'],
    ]);
    expect(answered).toHaveTextContent(/Answered/);
    expect(await screen.findByRole('region', { name: 'Follow-up: References' })).toHaveTextContent(/Sent .* \(2 times\), not answered yet\./);
    cleanup();

    stubApi({ [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json(withFollowUps) });
    renderPage('activity');
    const activity = await screen.findByRole('region', { name: 'Activity' });
    await within(activity).findByText(/Answered the "Availability" questionnaire/);
    expect(within(activity).getByText(/Sent the "Availability" questionnaire/)).toBeInTheDocument();
  });

  it('lists every email to the candidate on Messages, newest first, each opening to the email as it went', async () => {
    const kept = '01a157d4-0000-7000-8000-000000000390';
    const old = '01a157d4-0000-7000-8000-000000000391';
    const emails: CandidateEmailEntry[] = [
      {
        id: kept,
        applicationId,
        jobTitle: 'Senior .NET Engineer',
        templateId: null,
        subject: 'Next steps for Senior .NET Engineer',
        actorUserId: teammate,
        automationRunId: null,
        sentAt: '2026-10-20T10:01:00Z',
        kept: true,
      },
      {
        id: old,
        applicationId,
        jobTitle: 'Senior .NET Engineer',
        templateId: 'application-received',
        subject: 'Application received',
        actorUserId: null,
        automationRunId: null,
        sentAt: '2026-10-20T09:02:00Z',
        kept: false,
      },
    ];
    stubApi({
      [`GET /v1/candidates/${candidateId}/emails`]: () => Response.json(emails),
      [`GET /v1/emails/${kept}`]: () =>
        Response.json({
          id: kept,
          applicationId,
          templateId: null,
          to: 'casey.morgan@example.com',
          subject: 'Next steps for Senior .NET Engineer',
          html: '<p>Hi Casey,</p>',
          text: 'Hi Casey,',
          sentAt: '2026-10-20T10:01:00Z',
        }),
    });
    renderPage('messages');
    const messages = await screen.findByRole('region', { name: 'Messages' });
    const list = await within(messages).findByRole('list', { name: 'Emails' });

    const rows = within(list).getAllByRole('button');
    expect(rows.map((row) => row.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      expect.stringContaining('Next steps for Senior .NET Engineer by Robin Ashby'),
      expect.stringContaining('Application received'),
    ]);
    expect(within(screen.getByRole('tablist', { name: 'Candidate sections' })).getByRole('tab', { name: /Messages/ })).toHaveTextContent('2');

    await fireEvent.click(rows[0]!);
    expect(rows[0]).toHaveAttribute('aria-expanded', 'true');
    // The opened email shows its parts; the row above it already names the subject, so it is not repeated (W4).
    expect(await within(messages).findByRole('group', { name: 'Part' })).toBeInTheDocument();
    expect(within(messages).getAllByText(/Next steps for Senior \.NET Engineer/)).toHaveLength(1);

    await fireEvent.click(rows[1]!);
    expect(within(messages).getByText(/Only the subject was kept/)).toBeInTheDocument();
    // No reply is listed here, so the tab says nothing about replies; a viewer gets no email box.
    expect(messages).not.toHaveTextContent(/repl/i);
    expect(within(messages).queryByRole('form')).not.toBeInTheDocument();
  });

  it('lists a reply on Messages beside the emails sent, opening to who it is from and its files, offering only the files the scan allows', async () => {
    const replyId = '01a157d4-0000-7000-8000-000000000392';
    const cleanFile = '01a157d4-0000-7000-8000-000000000393';
    const unsafeFile = '01a157d4-0000-7000-8000-000000000394';
    const emails: CandidateEmailEntry[] = [
      {
        id: replyId,
        applicationId,
        jobTitle: 'Senior .NET Engineer',
        templateId: null,
        subject: 'Re: Next steps',
        actorUserId: null,
        automationRunId: null,
        sentAt: '2026-10-20T11:00:00Z',
        kept: true,
        direction: 'Received',
        fromAddress: 'casey.morgan@example.com',
        attachmentCount: 2,
      },
    ];
    stubApi({
      [`GET /v1/candidates/${candidateId}/emails`]: () => Response.json(emails),
      [`GET /v1/replies/${replyId}`]: () =>
        Response.json({
          id: replyId,
          applicationId,
          fromAddress: 'casey.morgan@example.com',
          fromName: 'Casey Morgan',
          subject: 'Re: Next steps',
          html: '',
          text: 'Thursday works for me.',
          receivedAt: '2026-10-20T11:00:00Z',
          autoReply: false,
          attachments: [
            { id: cleanFile, fileName: 'availability.pdf', contentType: 'application/pdf', sizeBytes: 2048, scanStatus: 'Clean' },
            { id: unsafeFile, fileName: 'macro.docx', contentType: 'application/msword', sizeBytes: 4096, scanStatus: 'Infected' },
          ],
        }),
    });
    renderPage('messages');
    const messages = await screen.findByRole('region', { name: 'Messages' });
    const row = within(await within(messages).findByRole('list', { name: 'Emails' })).getByRole('button');

    // Who it is from, not who sent it, and the files as a count.
    expect(row).toHaveTextContent('Re: Next steps');
    expect(row).toHaveTextContent('Reply from casey.morgan@example.com');
    expect(within(row).getByRole('img', { name: '2 files' })).toBeInTheDocument();

    await fireEvent.click(row);
    expect(await within(messages).findByText('Casey Morgan <casey.morgan@example.com>')).toBeInTheDocument();
    const attachments = await within(messages).findByRole('list', { name: 'Attachments' });
    expect(within(attachments).getByRole('link', { name: 'Download availability.pdf' })).toHaveAttribute(
      'href',
      `/v1/replies/${replyId}/attachments/${cleanFile}`,
    );
    // A file the scan flagged is named and withheld, with no download.
    expect(within(attachments).getByText('Unsafe file')).toBeInTheDocument();
    expect(within(attachments).queryByRole('link', { name: 'Download macro.docx' })).not.toBeInTheDocument();
  });

  describe('an address the email provider flagged', () => {
    const flagged = (issue: 'Bounced' | 'SpamComplaint') => ({
      ...application,
      candidate: { ...application.candidate, emailIssue: issue, emailIssueAt: '2026-10-21T08:00:00Z', emailIssueReason: '550 mailbox not found' },
    });

    it('says so beside the email on the meta line and in a notice that names the way back, and offers no email box', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json(flagged('Bounced')) });
      renderPage('messages');
      const band = await header();

      const email = await within(band).findByRole('link', { name: 'casey.morgan@example.com' });
      expect(within(email.closest<HTMLElement>('[data-slot="meta-item"]')!).getByText('Email bounced')).toBeInTheDocument();
      expect(screen.getByText(/ScreenHire does not email this address any more/)).toHaveTextContent('(550 mailbox not found)');
      expect(screen.getByRole('button', { name: 'Allow email again' })).toBeInTheDocument();
      expect(screen.queryByRole('form', { name: /Email Casey Morgan/ })).not.toBeInTheDocument();
    });

    it('words a spam complaint without any judgement of the candidate', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json(flagged('SpamComplaint')) });
      renderPage();

      expect(await screen.findByText('Marked as spam')).toBeInTheDocument();
      expect(screen.getByText(/says nothing else about the candidate/)).toBeInTheDocument();
    });

    it('clears the flag on Allow email again', async () => {
      let cleared = 0;
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json(flagged('Bounced')),
        [`POST /v1/candidates/${candidateId}/email-issue/clear`]: () => {
          cleared += 1;
          return Response.json(application.candidate);
        },
      });
      renderPage();

      await fireEvent.click(await screen.findByRole('button', { name: 'Allow email again' }));

      await waitFor(() => expect(cleared).toBe(1));
    });

    it('gives a viewer the notice but not the button', async () => {
      asRole('Viewer');
      stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json(flagged('Bounced')) });
      renderPage();

      expect(await screen.findByText(/ScreenHire does not email this address any more/)).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Allow email again' })).not.toBeInTheDocument();
    });
  });

  it('shows the Verification tab with its checks, also at its old address', async () => {
    // ?tab=integrity is the tab's old address: a link from before the rename still opens it.
    renderPage('integrity');

    await screen.findByText(/Checks are still running/);
    expect(screen.getByRole('tab', { name: /^Verification/ })).toHaveAttribute('aria-selected', 'true');
    expect(document.body).toHaveTextContent('No automated decision has been made. Every action is logged.');
    expect(screen.queryByRole('region', { name: 'Review log' })).not.toBeInTheDocument();
  });

  it('adds a note from the box on Activity, mentioning a teammate picked from the list by keyboard', async () => {
    const fetch = stubApi({
      [`POST /v1/applications/${applicationId}/notes`]: () => Response.json(entry({ type: 'NoteAdded', note: 'Great call.' }), { status: 201 }),
    });
    renderPage('activity');
    const activity = await screen.findByRole('region', { name: 'Activity' });
    const field = (await within(activity).findByLabelText('Note for the hiring team')) as HTMLTextAreaElement;

    field.value = 'Great call, @Ro';
    field.setSelectionRange(15, 15);
    await fireEvent.input(field);
    const list = await within(activity).findByRole('listbox', { name: 'Teammates' });
    expect(within(list).getAllByRole('option').map((option) => option.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['RA Robin Ashby Interviewer']);
    expect(field).toHaveAttribute('aria-activedescendant', within(list).getByRole('option').id);
    await fireEvent.keyDown(field, { key: 'Enter' });
    await waitFor(() => expect(field).toHaveValue('Great call, @Robin Ashby '));
    expect(within(activity).queryByRole('listbox')).not.toBeInTheDocument();

    await fireEvent.click(within(activity).getByRole('button', { name: 'Save note' }));
    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url).endsWith('/notes') && init?.method === 'POST')).toBe(true));
    const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/notes') && init?.method === 'POST');
    expect(JSON.parse(String(post![1]!.body))).toEqual({ note: `Great call, @[${teammate}]` });
  });

  it('completes a task from Needs you', async () => {
    const fetch = stubApi({
      [`POST /v1/tasks/${taskId}/complete`]: () => Response.json({ ...task, completedAt: '2026-10-20T12:00:00Z', completedByUserId: user.id }),
    });
    renderPage();
    const tasks = await screen.findByRole('region', { name: 'Needs you' });

    expect(await within(tasks).findByText('Call the references.')).toBeInTheDocument();
    await fireEvent.click(within(tasks).getByRole('checkbox', { name: 'Mark done for Call the references.' }));
    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url) === `/v1/tasks/${taskId}/complete` && init?.method === 'POST')).toBe(true));
    expect(await within(tasks).findByRole('button', { name: 'Undo, Call the references.' })).toBeInTheDocument();
    expect(within(tasks).getByRole('checkbox', { name: 'Mark done for Call the references.' })).toBeChecked();
  });

  it('gives a review and a requested scorecard no checkbox: their row links to the Scorecards tab, for the person they are for', async () => {
    stubApi({
      [`GET /v1/applications/${applicationId}/tasks`]: () =>
        Response.json([
          { ...task, id: '01a157d4-0000-7000-8000-000000000330', kind: 'Review', note: 'Give your scorecard.', assigneeUserId: user.id },
          {
            ...task,
            id: '01a157d4-0000-7000-8000-000000000331',
            kind: 'Scorecard',
            note: 'Fill in the Interview scorecard.',
            assigneeUserId: user.id,
            scorecardTemplateId: 'interview',
          },
          { ...task, id: '01a157d4-0000-7000-8000-000000000332', kind: 'Scorecard', note: 'Fill in the Screening scorecard.', assigneeUserId: teammate, scorecardTemplateId: 'screening' },
          task,
        ]),
    });
    renderPage();
    const tasks = await screen.findByRole('region', { name: 'Needs you' });

    await within(tasks).findByText('Fill in the Interview scorecard.');
    expect(within(tasks).getAllByRole('checkbox').map((box) => box.getAttribute('aria-label'))).toEqual(['Mark done for Call the references.']);
    expect(within(tasks).getByRole('link', { name: 'Fill in: Fill in the Interview scorecard.' })).toHaveAttribute('href', '?tab=scorecards&scorecard=interview');
    // A review is finished by any scorecard, so its Fill in opens the sheet on the current stage's.
    expect(within(tasks).getByRole('link', { name: 'Fill in: Give your scorecard.' })).toHaveAttribute('href', '?tab=scorecards&fill=stage');
    // Robin's request is Robin's to do.
    expect(within(tasks).queryByRole('link', { name: 'Fill in: Fill in the Screening scorecard.' })).not.toBeInTheDocument();
  });

  describe('ticking a task on Needs you', () => {
    const tick = 'Mark done for Call the references.';
    const undo = 'Undo, Call the references.';
    const posted = (fetch: ReturnType<typeof stubApi>, url: string) =>
      fetch.mock.calls.some(([called, init]) => String(called) === url && init?.method === 'POST');
    const doneTask = { ...task, completedAt: '2026-10-20T12:00:00Z', completedByUserId: user.id };

    afterEach(() => vi.useRealTimers());

    it('keeps Undo for 10 seconds, then shows the task done with who did it', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      let listed = [task];
      stubApi({
        [`GET /v1/applications/${applicationId}/tasks`]: () => Response.json(listed),
        [`POST /v1/tasks/${taskId}/complete`]: () => {
          listed = [doneTask];
          return Response.json(doneTask);
        },
      });
      renderPage();
      const steps = await screen.findByRole('region', { name: 'Needs you' });

      await fireEvent.click(await within(steps).findByRole('checkbox', { name: tick }));
      expect(await screen.findByText('Marked "Call the references." done.')).toBeInTheDocument();
      expect(within(steps).getByRole('button', { name: undo })).toBeInTheDocument();

      await vi.advanceTimersByTimeAsync(10_000);
      await waitFor(() => expect(within(steps).queryByRole('button', { name: undo })).not.toBeInTheDocument());
      expect(within(steps).queryByRole('checkbox', { name: tick })).not.toBeInTheDocument();
      expect(steps).toHaveTextContent(/Done .* by you/);
    });

    it('opens the task again on Undo, and on unticking it', async () => {
      const fetch = stubApi({
        [`POST /v1/tasks/${taskId}/complete`]: () => Response.json(doneTask),
        [`POST /v1/tasks/${taskId}/reopen`]: () => Response.json(task),
      });
      renderPage();
      const steps = await screen.findByRole('region', { name: 'Needs you' });

      await fireEvent.click(await within(steps).findByRole('checkbox', { name: tick }));
      await fireEvent.click(await within(steps).findByRole('button', { name: undo }));
      await waitFor(() => expect(posted(fetch, `/v1/tasks/${taskId}/reopen`)).toBe(true));
      expect(await screen.findByText('"Call the references." is open again.')).toBeInTheDocument();
      expect(within(steps).getByRole('checkbox', { name: tick })).not.toBeChecked();
      expect(within(steps).queryByRole('button', { name: /^Undo/ })).not.toBeInTheDocument();

      fetch.mockClear();
      await fireEvent.click(within(steps).getByRole('checkbox', { name: tick }));
      await waitFor(() => expect(within(steps).getByRole('checkbox', { name: tick })).toBeChecked());
      await fireEvent.click(within(steps).getByRole('checkbox', { name: tick }));
      await waitFor(() => expect(posted(fetch, `/v1/tasks/${taskId}/reopen`)).toBe(true));
    });

    it('keeps Undo for a task ticked again while its Undo is still saving, and completes it after the reopen', async () => {
      let finishReopen = () => {};
      const order: string[] = [];
      const fetch = stubApi({
        [`POST /v1/tasks/${taskId}/complete`]: () => {
          order.push('complete');
          return Response.json(doneTask);
        },
        [`POST /v1/tasks/${taskId}/reopen`]: () =>
          new Promise<Response>((resolve) => {
            finishReopen = () => {
              order.push('reopen');
              resolve(Response.json(task));
            };
          }),
      });
      renderPage();
      const steps = await screen.findByRole('region', { name: 'Needs you' });

      await fireEvent.click(await within(steps).findByRole('checkbox', { name: tick }));
      await fireEvent.click(await within(steps).findByRole('button', { name: undo }));
      await waitFor(() => expect(posted(fetch, `/v1/tasks/${taskId}/reopen`)).toBe(true));
      await waitFor(() => expect(within(steps).getByRole('checkbox', { name: tick })).not.toBeChecked());

      // Ticked again before the reopen has answered.
      await fireEvent.click(within(steps).getByRole('checkbox', { name: tick }));
      await waitFor(() => expect(within(steps).getByRole('checkbox', { name: tick })).toBeChecked());
      finishReopen();

      await waitFor(() => expect(order).toEqual(['complete', 'reopen', 'complete']));
      expect(within(steps).getByRole('checkbox', { name: tick })).toBeChecked();
      expect(within(steps).getByRole('button', { name: undo })).toBeInTheDocument();
    });

    it('unticks the task and says why when completing fails', async () => {
      stubApi({ [`POST /v1/tasks/${taskId}/complete`]: () => new Response(null, { status: 500 }) });
      renderPage();
      const steps = await screen.findByRole('region', { name: 'Needs you' });

      await fireEvent.click(await within(steps).findByRole('checkbox', { name: tick }));

      expect((await screen.findAllByText(/Could not complete the task/)).length).toBeGreaterThan(0);
      expect(within(steps).getByRole('checkbox', { name: tick })).not.toBeChecked();
    });
  });

  it('runs a manual automation from Activity with its own idempotency key, and offers only manual ones', async () => {
    const fetch = stubApi({
      [`POST /v1/applications/${applicationId}/automations/ask-references/run`]: () =>
        Response.json({ applicationId, automationId: 'ask-references', eventId: crypto.randomUUID() }, { status: 202 }),
    });
    renderPage('activity');
    const automations = await screen.findByRole('region', { name: 'Automations' });

    expect(within(automations).queryByText('On arrival')).not.toBeInTheDocument();
    await fireEvent.click(await within(automations).findByRole('button', { name: 'Run, Ask for references' }));
    await within(automations).findByText(/Asked to run "Ask for references"/);
    const run = fetch.mock.calls.find(([url]) => String(url).endsWith('/automations/ask-references/run'));
    expect(new Headers(run![1]!.headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('puts the manual automations and the note box above the timeline on Activity', async () => {
    renderPage('activity');
    const automations = await screen.findByRole('region', { name: 'Automations' });
    const activity = await screen.findByRole('region', { name: 'Activity' });
    const box = await within(activity).findByLabelText('Note for the hiring team');
    const firstEvent = (await within(activity).findAllByRole('listitem'))[0]!;

    const follows = (first: Element, second: Element) => Boolean(first.compareDocumentPosition(second) & Node.DOCUMENT_POSITION_FOLLOWING);
    expect(follows(automations, activity)).toBe(true);
    expect(follows(box, firstEvent)).toBe(true);
  });

  it('moves with a note from Move to stage, in a sheet', async () => {
    const fetch = stubApi({
      [`POST /v1/applications/${applicationId}/move`]: () => Response.json({ ...application, stage: { id: 'interview', name: 'Interview', kind: 'Interview' } }),
      [`GET /v1/jobs/${jobId}/applications`]: () => Response.json([]),
      'GET /v1/applications': () => Response.json([]),
    });
    renderPage();

    const trigger = await screen.findByRole('button', { name: 'Move to stage, Casey Morgan' });
    await fireEvent.pointerDown(trigger, { button: 0, pointerType: 'mouse' });
    await fireEvent.click(await screen.findByRole('menuitem', { name: 'With a note…' }));
    const sheet = await screen.findByRole('dialog', { name: 'Move Casey Morgan' });
    await fireEvent.change(within(sheet).getByLabelText('Stage'), { target: { value: 'interview' } });
    await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: ' Strong second round. ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Move' }));

    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url).endsWith('/move') && init?.method === 'POST')).toBe(true));
    const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/move') && init?.method === 'POST');
    expect(JSON.parse(String(post![1]!.body))).toEqual({ toStageId: 'interview', note: 'Strong second round.' });
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Move Casey Morgan' })).not.toBeInTheDocument());
  });

  it('leaves the controls that change hiring data off Overview and the header for a viewer', async () => {
    asRole('Viewer');
    stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, tags: ['Referral'] }) });
    renderPage();
    const band = await header();
    const tasks = await screen.findByRole('region', { name: 'Needs you' });
    await within(tasks).findByText('Call the references.');
    await within(band).findByRole('list', { name: 'Tags' });
    await settled();

    expect(within(tasks).queryByRole('checkbox', { name: /^Mark done/ })).not.toBeInTheDocument();
    expect(within(band).queryByRole('button', { name: 'Add tag' })).not.toBeInTheDocument();
    expect(within(band).queryByRole('button', { name: 'Remove tag Referral' })).not.toBeInTheDocument();
    expect(within(band).queryByRole('button', { name: 'Move to stage, Casey Morgan' })).not.toBeInTheDocument();
  });

  it('leaves the note box, the note menus and the automations off Activity for a viewer', async () => {
    asRole('Viewer');
    stubApi({ [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, ...notes]) });
    renderPage('activity');
    const activity = await screen.findByRole('region', { name: 'Activity' });
    await noteItem(ownNote);
    await settled();

    expect(within(activity).queryByLabelText('Note for the hiring team')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'More actions for this note' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Automations' })).not.toBeInTheDocument();
  });

  it('gives an Interviewer tags but no Move or task controls on Overview', async () => {
    page.data.user = interviewer;
    renderPage();
    const band = await header();
    const tasks = await screen.findByRole('region', { name: 'Needs you' });
    await within(tasks).findByText('Call the references.');
    await settled();

    expect(within(band).getByRole('button', { name: 'Add tag' })).toBeInTheDocument();
    expect(within(band).queryByRole('button', { name: 'Move to stage, Casey Morgan' })).not.toBeInTheDocument();
    expect(within(tasks).queryByRole('checkbox', { name: /^Mark done/ })).not.toBeInTheDocument();
  });

  it('gives an Interviewer the note box on Activity but no automations', async () => {
    page.data.user = interviewer;
    renderPage('activity');
    const activity = await screen.findByRole('region', { name: 'Activity' });

    expect(await within(activity).findByLabelText('Note for the hiring team')).toBeInTheDocument();
    await settled();
    expect(screen.queryByRole('region', { name: 'Automations' })).not.toBeInTheDocument();
  });

  it("lists the candidate's declarations with their answers on Application, each under the question as asked", async () => {
    renderPage('application');
    const answers = await screen.findByRole('region', { name: 'Answers' });
    const row = (question: string) => within(answers).getByText(question).closest('div')!;

    expect(screen.queryByRole('region', { name: 'Declarations' })).not.toBeInTheDocument();
    expect(row('Did you use AI tools for this application?')).toHaveTextContent('Yes: Grammar checks only.');
    expect(row('Adjustments to the process')).toHaveTextContent('Asked for');
    // The question is muted and the answer in medium weight, so the two never read as one.
    expect(within(row('Adjustments to the process')).getByText('Asked for')).toHaveClass('font-medium');
  });

  it('shows only what the candidate declared (W6)', async () => {
    stubApi({
      [`GET /v1/applications/${applicationId}`]: () =>
        Response.json({ ...application, aiUseDeclaration: 'NotProvided', aiUseDetails: null, accommodationRequested: false }),
    });
    renderPage('application');
    const answers = await screen.findByRole('region', { name: 'Answers' });

    expect(answers).not.toHaveTextContent('Did you use AI tools');
    expect(answers).not.toHaveTextContent('Adjustments');
    expect(answers).not.toHaveTextContent('None requested');
    expect(answers).not.toHaveTextContent('Not asked or not answered');
  });

  describe('saved candidates', () => {
    // "Today" is fixed so relative times read the same on every run.
    const today = new Date('2026-10-30T12:00:00Z');
    const keptAt = '2026-10-29T12:00:00Z';
    const supportJobId = '01a157d4-0000-7000-8000-0000000009aa';
    const otherApplicationId = '01a157d4-0000-7000-8000-000000000390';

    beforeEach(() => {
      vi.useFakeTimers({ toFake: ['Date'], now: today });
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    /** The application as the API sends it for the case under test. */
    const stubDetail = (patch: Partial<ApplicationDetail>, extra: Parameters<typeof apiStub>[0] = {}) =>
      stubApi({ [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, ...patch }), ...extra });

    const openMoveMenu = async () => {
      await fireEvent.pointerDown(await screen.findByRole('button', { name: 'Move to stage, Casey Morgan' }), { button: 0, pointerType: 'mouse' });
      return screen.findByRole('menu');
    };
    const sending = { 'GET /v1/email-templates': () => Response.json({ sendingEnabled: true, templates: [], variables: [] }) };

    it('offers Save candidate in Move to stage when the candidate allowed it', async () => {
      stubDetail({ allowsFutureRoles: true, futureRolesGivenAt: '2026-10-20T09:00:00Z' });
      renderPage();

      const menu = await openMoveMenu();

      expect(within(menu).getByRole('menuitem', { name: 'Save candidate…' })).toBeInTheDocument();
    });

    it.each([
      ['never gave permission', {}],
      ['gave it and took it back', { allowsFutureRoles: false, futureRolesGivenAt: '2026-10-20T09:00:00Z' }],
      ['were rejected', { allowsFutureRoles: true, stage: { id: 'rejected', name: 'Rejected', kind: 'Rejected' as const } }],
    ])('leaves Save candidate out of Move to stage for a candidate who %s', async (_case, patch) => {
      stubDetail(patch);
      renderPage();

      const menu = await openMoveMenu();

      expect(within(menu).getAllByRole('menuitem').length).toBeGreaterThan(0);
      expect(within(menu).queryByRole('menuitem', { name: /Save candidate/ })).toBeNull();
    });

    it('gives someone without Move candidates no Move to stage button, so no way to save them', async () => {
      asRole('Viewer');
      stubDetail({ allowsFutureRoles: true });
      renderPage();
      await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' });

      expect(screen.queryByRole('button', { name: 'Move to stage, Casey Morgan' })).toBeNull();
      expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('saves the candidate from the sheet with a note, and announces it', async () => {
      const post = vi.fn((init: RequestInit | undefined) => Response.json({ kept: 1, notAllowed: 0, alreadyEnded: 0 }));
      stubDetail({ allowsFutureRoles: true, futureRolesGivenAt: '2026-10-20T09:00:00Z' }, { 'POST /v1/talent-pool': post });
      renderPage();

      await fireEvent.click(within(await openMoveMenu()).getByRole('menuitem', { name: 'Save candidate…' }));
      const sheet = await screen.findByRole('dialog', { name: 'Save Casey Morgan' });
      expect(sheet).toHaveTextContent('They leave this job without a rejection and stay saved until you remove them or they ask to stop.');
      await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: 'Strong systems thinking.' } });
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Save candidate' }));

      await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
      expect(JSON.parse(String(post.mock.calls[0]![0]!.body))).toEqual({ applicationIds: [applicationId], note: 'Strong systems thinking.' });
      expect(await screen.findByText('Saved Casey Morgan.', { selector: '[role="status"]' })).toBeInTheDocument();
    });

    it('says Saved beside the name, with a dark dot on the stage they left from, and no Saved fact until they are in the list', async () => {
      stubDetail({ keptInMindAt: keptAt, allowsFutureRoles: true, inTalentPool: false });
      renderPage();

      const line = await firstLine();
      await waitFor(() => expect(line).toHaveTextContent('Saved'));
      expect(line.querySelector('[data-status-icon="bookmark"]')).not.toBeNull();
      const left = within(await header()).getAllByRole('listitem').find((step) => step.getAttribute('data-state') === 'left')!;
      expect(left).toHaveTextContent(/^Screening, step 2 of 4.*Saved Oct 29$/);
      expect(within(await header()).queryByRole('listitem', { current: 'step' })).toBeNull();
      expect(screen.queryByRole('link', { name: 'In Saved candidates' })).toBeNull();
    });

    it('links to Saved candidates from the header while they are on the list', async () => {
      stubDetail({ keptInMindAt: keptAt, allowsFutureRoles: true, inTalentPool: true });
      renderPage();

      expect(await firstLine()).toHaveTextContent('Saved');
      expect(await screen.findByRole('link', { name: 'In Saved candidates' })).toHaveAttribute('href', '/candidates?list=saved');
    });

    it('says when they were last contacted on the meta line, from the newest email either way, in place of the phone', async () => {
      const email = (id: string, sentAt: string): CandidateEmailEntry => ({ id, applicationId, jobTitle: 'Senior .NET Engineer', subject: 'Hello', sentAt, direction: 'Sent' }) as CandidateEmailEntry;
      stubDetail(
        { keptInMindAt: keptAt, allowsFutureRoles: true, candidate: { ...application.candidate, phone: '+44 20 7946 0000' } },
        { [`GET /v1/candidates/${candidateId}/emails`]: () => Response.json([email('a', '2026-10-27T09:00:00Z'), email('b', '2026-10-20T09:00:00Z')]) },
      );
      renderPage();
      const band = await header();

      // The meta item's label for screen readers says it too, so the visible phrase is the span.
      expect(await within(band).findByText(/Last contacted/, { selector: 'span' })).toHaveTextContent('Last contacted 3 days ago');
      expect(within(band).queryByRole('link', { name: '+44 20 7946 0000' })).toBeNull();
    });

    it('says they were not contacted yet when no email went either way', async () => {
      stubDetail({ keptInMindAt: keptAt, allowsFutureRoles: true });
      renderPage();

      expect(await within(await header()).findByText('Not contacted yet')).toBeInTheDocument();
    });

    it('shows the phone, not when they were last contacted, for an application still in the job', async () => {
      stubDetail({ allowsFutureRoles: true, candidate: { ...application.candidate, phone: '+44 20 7946 0000' } });
      renderPage();
      const band = await header();

      expect(await within(band).findByRole('link', { name: '+44 20 7946 0000' })).toHaveAttribute('href', 'tel:+44 20 7946 0000');
      // The emails are in once the Messages tab has its count.
      await within(screen.getByRole('tablist', { name: 'Candidate sections' })).findByRole('tab', { name: 'Messages 0' });
      expect(band).not.toHaveTextContent('Last contacted');
      expect(band).not.toHaveTextContent('Not contacted yet');
    });

    it('offers Message and Invite to another job in place of Email and Move to stage', async () => {
      stubDetail({ keptInMindAt: keptAt, allowsFutureRoles: true, inTalentPool: true }, sending);
      renderPage();

      const band = await header();
      expect(await within(band).findByRole('button', { name: 'Message' })).toBeInTheDocument();
      expect(within(band).getByRole('button', { name: 'Invite to another job' })).toBeInTheDocument();
      expect(within(band).queryByRole('button', { name: 'Email' })).toBeNull();
      expect(within(band).queryByRole('button', { name: 'Move to stage, Casey Morgan' })).toBeNull();
    });

    it('offers neither Message nor Invite to another job for a saved candidate whose address the provider flagged', async () => {
      const fetch = stubDetail({ keptInMindAt: keptAt, allowsFutureRoles: true, inTalentPool: true, candidate: { ...application.candidate, emailIssue: 'Bounced' } }, sending);
      renderPage();

      const band = await header();
      await within(band).findByText('Email bounced');
      // Sending is on once this answer is in, so only the flag can hide the two buttons.
      await waitFor(() => expect(fetch.mock.calls.some(([url]) => String(url).includes('/v1/email-templates'))).toBe(true));
      await new Promise((resolve) => setTimeout(resolve));
      expect(within(band).queryByRole('button', { name: 'Message' })).toBeNull();
      expect(within(band).queryByRole('button', { name: 'Invite to another job' })).toBeNull();
    });

    it('shows the stage name as current, and Email and Move to stage, for an application still in the job', async () => {
      stubDetail({ allowsFutureRoles: true }, sending);
      renderPage();

      const band = await header();
      expect(await within(band).findByRole('button', { name: 'Move to stage, Casey Morgan' })).toBeInTheDocument();
      expect(within(band).getByRole('button', { name: 'Email' })).toBeInTheDocument();
      expect(within(band).queryByRole('button', { name: 'Message' })).toBeNull();
      expect(within(band).queryByRole('button', { name: 'Invite to another job' })).toBeNull();
      expect(within(band).getByRole('listitem', { current: 'step' })).toHaveTextContent(/^Screening/);
    });

    it.each([
      ['Rejected', 'rejected'],
      ['Withdrawn', 'withdrawn'],
      ['Hired', 'hired'],
    ])('says %s beside the name and leaves the dark dot on the stage the last move left from', async (label, id) => {
      stubDetail(
        { stage: { id, name: label, kind: label as 'Rejected' | 'Withdrawn' | 'Hired' }, stageEnteredAt: '2026-10-28T09:00:00Z' },
        {
          [`GET /v1/applications/${applicationId}/timeline`]: () =>
            Response.json([...timeline, entry({ type: 'StageChanged', occurredAt: '2026-10-28T09:00:00Z', fromStageId: 'interview', toStageId: id })]),
        },
      );
      renderPage();

      expect(await firstLine()).toHaveTextContent(label);
      const left = within(await header()).getAllByRole('listitem').find((step) => step.getAttribute('data-state') === 'left')!;
      expect(left).toHaveTextContent(new RegExp(`^Interview, step 3 of 4.*${label} Oct 28$`));
    });

    describe('Future roles on the Application tab', () => {
      it('says the candidate allowed it, with no end date', async () => {
        stubDetail({ futureRolesGivenAt: '2026-10-20T09:00:00Z', allowsFutureRoles: true });
        renderPage('application');

        const answers = await screen.findByRole('region', { name: 'Answers' });

        expect(answers).toHaveTextContent('Keep my details for future roles');
        expect(answers).toHaveTextContent('Allowed');
      });

      it('says they allowed it and then stopped', async () => {
        stubDetail({ futureRolesGivenAt: '2026-10-20T09:00:00Z', allowsFutureRoles: false });
        renderPage('application');

        expect(await screen.findByRole('region', { name: 'Answers' })).toHaveTextContent('Allowed, then stopped');
      });

      it('shows no row at all when the candidate did not tick the box', async () => {
        stubDetail({});
        renderPage('application');

        const answers = await screen.findByRole('region', { name: 'Answers' });

        expect(answers).not.toHaveTextContent('future roles');
      });
    });

    describe('Activity', () => {
      it('words saving with its note, an invitation and leaving saved candidates for a job', async () => {
        stubApi({
          [`GET /v1/applications/${applicationId}/timeline`]: () =>
            Response.json([
              entry({ type: 'Submitted', toStageId: 'applied' }),
              entry({ type: 'KeptInMind', occurredAt: '2026-10-20T12:00:00Z', actorUserId: user.id, note: 'Strong, wrong timing.' }),
              entry({ type: 'InvitedToApply', occurredAt: '2026-10-21T12:00:00Z', actorUserId: user.id, otherJobId: supportJobId }),
              entry({ type: 'LeftTalentPool', occurredAt: '2026-10-22T12:00:00Z', poolExit: 'AppliedAgain', otherJobId: supportJobId }),
            ]),
          'GET /v1/jobs': () => Response.json([{ id: supportJobId, title: 'Support Engineer' }]),
        });
        renderPage('activity');
        const region = await screen.findByRole('region', { name: 'Activity' });

        await within(region).findByText(/Left saved candidates: applied for Support Engineer/);
        expect(lines(region)).toEqual([
          expect.stringContaining('Left saved candidates: applied for Support Engineer'),
          'Invited to apply for Support Engineer by you',
          'Saved by you',
          'Applied on the careers page, into Applied',
        ]);
        expect(within(region).getByText('"Strong, wrong timing."')).toBeInTheDocument();
      });
    });

    describe('Other applications on the Application tab', () => {
      const summaryOf = (id: string, jobId_: string, jobTitle: string, patch: Record<string, unknown> = {}) => ({
        id,
        jobId: jobId_,
        candidateId,
        candidateName: 'Casey Morgan',
        stage: { id: 'applied', name: 'Applied', kind: 'Applied' },
        submittedAt: '2026-10-28T09:00:00Z',
        stageEnteredAt: '2026-10-28T09:00:00Z',
        jobTitle,
        source: 'CareersSite',
        tags: [],
        ...patch,
      });
      const thisOne = summaryOf(applicationId, supportJobId, 'Support Engineer');
      const candidate = (applications: unknown[]) => ({ ...application.candidate, applications });

      it('links to the candidate\'s other applications, with the stage as it reads now', async () => {
        const other = summaryOf(otherApplicationId, jobId, 'Senior .NET Engineer', { keptInMindAt: keptAt, stage: { id: 'screening', name: 'Screening', kind: 'Screening' } });
        stubApi({ [`GET /v1/candidates/${candidateId}`]: () => Response.json(candidate([thisOne, other])) });
        renderPage('application');

        const panel = await screen.findByRole('region', { name: 'Other applications' });

        expect(await within(panel).findByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/applications/${otherApplicationId}`);
        // The saved application read Screening when it was saved; the panel says it has left the job.
        expect(panel).toHaveTextContent('Saved');
        expect(panel).not.toHaveTextContent('Screening');
        expect(within(panel).queryByRole('link', { name: 'Support Engineer' })).toBeNull();
        expect(within(panel).getByRole('link', { name: 'Candidate profile' })).toHaveAttribute('href', `/candidates/${candidateId}`);
      });

      it('comes after the answers and the follow-ups', async () => {
        const other = summaryOf(otherApplicationId, jobId, 'Senior .NET Engineer');
        stubApi({ [`GET /v1/candidates/${candidateId}`]: () => Response.json(candidate([thisOne, other])) });
        renderPage('application');

        await waitFor(() => expect(panelNames()).toEqual(['Answers', 'Follow-up: Availability', 'Follow-up: References', 'Other applications']));
      });

      it('has no panel, only the link to the candidate profile, when the candidate has only this application', async () => {
        stubApi({ [`GET /v1/candidates/${candidateId}`]: () => Response.json(candidate([thisOne])) });
        renderPage('application');
        const link = await screen.findByRole('link', { name: 'Candidate profile' });
        await settled();

        expect(link).toHaveAttribute('href', `/candidates/${candidateId}`);
        expect(screen.queryByRole('region', { name: 'Other applications' })).toBeNull();
      });
    });
  });

  describe('scorecards', () => {
    // The job's copy of an interview scorecard; the application reached Interview (the timeline) and is back in Screening.
    const scoredJob = {
      ...job,
      workflow: {
        ...job.workflow,
        stages: stages.map((stage) =>
          stage.id === 'interview'
            ? {
                ...stage,
                scorecardTemplateId: 'interview',
                scorecard: {
                  name: 'Interview',
                  questions: [
                    { id: 'role_skills', type: 'Rating5', label: 'Skills the role needs', required: false, guidance: 'The skills in the job description.' },
                    { id: 'problem_solving', type: 'Rating10', label: 'Problem solving', required: true },
                    { id: 'finished', type: 'RatingYesNo', label: 'Finished in the time', required: false },
                    {
                      id: 'next',
                      type: 'MultiChoice',
                      label: 'Next interview covers',
                      required: false,
                      options: [
                        { id: 'design', label: 'System design' },
                        { id: 'testing', label: 'Testing' },
                      ],
                    },
                    { id: 'notes', type: 'LongText', label: 'Notes on the exercise', required: false },
                  ],
                },
              }
            : stage,
        ),
      },
    } as JobDetail;
    const submitted: ScorecardEntry = {
      id: '01a157d4-0000-7000-8000-000000000310',
      stageId: 'interview',
      reviewerUserId: teammate,
      answers: [
        { questionId: 'role_skills', label: 'Skills the role needs', type: 'Rating5', rating: 5 },
        { questionId: 'problem_solving', label: 'Problem solving', type: 'Rating10', rating: 7 },
        { questionId: 'finished', label: 'Finished in the time', type: 'RatingYesNo', value: 'yes' },
        {
          questionId: 'next',
          label: 'Next interview covers',
          type: 'MultiChoice',
          values: ['design', 'testing'],
          options: [
            { id: 'design', label: 'System design' },
            { id: 'testing', label: 'Testing' },
          ],
        },
        { questionId: 'notes', label: 'Notes on the exercise', type: 'LongText' },
      ],
      rating: 4.6,
      ratings: [],
      recommendation: 'StrongYes',
      note: 'Clear walk-through of the design.',
      workflowVersion: 1,
      timelineEventId: '01a157d4-0000-7000-8000-000000000311',
      submittedAt: '2026-10-20T11:45:00Z',
      scorecardTemplateId: 'interview',
      scorecardName: 'Interview',
      withheld: false,
    };
    // The library: a built-in, the team's own, and an archived one the pickers leave out.
    const library = [
      { id: 'phone-screen', name: 'Phone screen', builtIn: true, questions: 2, ratingQuestions: 2, lastUpdated: { at: null, by: 'ScreenHire' }, archived: false },
      { id: '01a157d4-0000-7000-8000-000000000320', name: 'Culture add', builtIn: false, questions: 1, ratingQuestions: 1, lastUpdated: { at: null, by: 'ScreenHire' }, archived: false },
      { id: '01a157d4-0000-7000-8000-000000000321', name: 'Old screen', builtIn: false, questions: 1, ratingQuestions: 1, lastUpdated: { at: null, by: 'ScreenHire' }, archived: true },
    ] as unknown as ScorecardTemplateSummary[];
    // Robin's custom role fills in scorecards.
    const roles = {
      ...roleCatalogue,
      roles: roleCatalogue.roles.map((role) => (role.role === 'Custom' ? { ...role, customRoleId: members[1].customRoleId } : role)),
    };
    // A v0.2 scorecard reads as it was given: labelled ratings, which never count toward the rating.
    const older: ScorecardEntry = {
      ...submitted,
      id: '01a157d4-0000-7000-8000-000000000312',
      stageId: 'screening',
      answers: [],
      rating: null,
      ratings: [{ criterionId: 'role_requirements', label: "Meets the role's requirements", rating: 'Demonstrated' }],
      recommendation: 'Yes',
      note: null,
      submittedAt: '2026-10-19T11:45:00Z',
    };

    it('lists the scorecards as a roster, and opens each with its answers in a side sheet that steps with Previous and Next', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([older, submitted]),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await within(panel).findByText('Strong yes');
      // One row per reviewer: who, the stage, the recommendation in words, the rating as a number, and when.
      const table = within(panel).getByRole('table', { name: 'Scorecards' });
      const [newest, earlier] = within(table).getAllByRole('row').slice(1);
      expect(newest).toHaveTextContent('Robin Ashby');
      expect(newest).toHaveTextContent('Strong yes');
      expect(newest).toHaveTextContent('4.6');
      expect(earlier).toHaveTextContent('Yes');
      // Every header sorts (L2); the roster opens on the newest.
      expect(within(table).getByRole('columnheader', { name: /Submitted/ })).toHaveAttribute('aria-sort', 'descending');
      expect(within(table).getAllByRole('columnheader').every((header) => within(header).queryByRole('button'))).toBe(true);
      expect(within(panel).queryByText('by Robin Ashby')).not.toBeInTheDocument();

      await fireEvent.click(within(newest!).getByRole('button', { name: 'Robin Ashby' }));
      const sheet = await screen.findByRole('dialog', { name: 'Interview scorecard' });
      const answers = (root: HTMLElement) =>
        within(root)
          .getAllByRole('term')
          .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.trim()]);
      expect(answers(sheet)).toEqual([
        ['Skills the role needs', '5 of 5'],
        ['Problem solving', '7 of 10'],
        ['Finished in the time', 'Yes'],
        ['Next interview covers', 'System design, Testing'],
        ['Notes on the exercise', 'Not answered'],
      ]);
      expect(sheet).toHaveTextContent('"Clear walk-through of the design."');
      expect(sheet).toHaveTextContent('Robin Ashby');
      expect(within(sheet).getByRole('button', { name: /Previous/ })).toBeDisabled();

      // Next reads the next row in the table's order; a v0.2 scorecard reads as it was given.
      await fireEvent.click(within(sheet).getByRole('button', { name: /Next/ }));
      await waitFor(() => expect(answers(sheet)).toEqual([["Meets the role's requirements", 'Demonstrated']]));
      expect(within(sheet).getByRole('button', { name: /Next/ })).toBeDisabled();
      // Nothing to submit: the sheet has no filled button (A1).
      expect(within(sheet).queryByRole('button', { name: 'Submit scorecard' })).not.toBeInTheDocument();
    });

    it('says the rating comes after your scorecard while you have a review to give', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, rating: { state: 'AfterYourScorecard', scorecards: 0 } }),
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await within(panel).findByText('Submit yours to see the others.');
      // The line carries the one Fill in; the header does not offer it twice.
      expect(within(panel).getAllByRole('button', { name: /^Fill in/ })).toHaveLength(1);
      expect(screen.getByText('Rating after your scorecard')).toBeInTheDocument();
      expect(screen.queryByRole('img', { name: /of 5 stars/ })).not.toBeInTheDocument();
    });

    it('fills a scorecard in a sheet: every kind answers its own way, and required questions and the recommendation are required', async () => {
      const fetch = stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`POST /v1/applications/${applicationId}/scorecards`]: () => Response.json({ ...submitted, reviewerUserId: user.id }, { status: 201 }),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await fireEvent.click(await within(panel).findByRole('button', { name: 'Fill in' }));
      const sheet = await screen.findByRole('dialog', { name: 'Give your scorecard' });
      // Only the Interview scorecard fits, so it is the sheet's subtitle and there is no select to choose from (A3).
      expect(within(sheet).queryByRole('combobox', { name: 'Scorecard' })).not.toBeInTheDocument();
      expect(sheet).toHaveTextContent('Interview scorecard');
      // Low and High are said once for the two scales, under the first.
      expect(within(sheet).getAllByText('Low')).toHaveLength(1);
      expect(within(sheet).getAllByText('High')).toHaveLength(1);
      expect(within(sheet).queryByText(/verification/i)).not.toBeInTheDocument();
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Submit scorecard' }));
      await within(sheet).findByText('Choose a recommendation.');
      expect(within(sheet).getByText('Answer this question.')).toBeInTheDocument();
      expect(fetch.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);

      const group = (name: RegExp) => within(sheet).getByRole('group', { name });
      expect(within(group(/^Skills the role needs/)).getAllByRole('radio')).toHaveLength(5);
      expect(within(group(/^Problem solving/)).getAllByRole('radio')).toHaveLength(10);
      await fireEvent.click(within(group(/^Skills the role needs/)).getByRole('radio', { name: '4' }));
      await fireEvent.click(within(group(/^Problem solving/)).getByRole('radio', { name: '10, High' }));
      await fireEvent.click(within(group(/^Finished in the time/)).getByRole('radio', { name: 'No' }));
      await fireEvent.click(within(group(/^Next interview covers/)).getByRole('checkbox', { name: 'Testing' }));
      await fireEvent.input(within(sheet).getByLabelText('Notes on the exercise'), { target: { value: '  Tidy code.  ' } });
      await fireEvent.click(within(group(/^Overall recommendation/)).getByRole('radio', { name: 'Strong yes' }));
      await fireEvent.input(within(sheet).getByLabelText('Note for the team'), { target: { value: '  Good depth.  ' } });
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Submit scorecard' }));

      // The sheet stays open on one line, with Submit scorecard gone; the page's status line says what was saved.
      await within(sheet).findByText('Scorecard submitted.');
      expect(within(sheet).queryByRole('button', { name: 'Submit scorecard' })).not.toBeInTheDocument();
      await within(panel).findByText('Interview scorecard saved.');
      const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/scorecards') && init?.method === 'POST');
      expect(JSON.parse(String(post![1]!.body))).toEqual({
        stageId: 'interview',
        scorecardTemplateId: null,
        answers: [
          { questionId: 'role_skills', rating: 4 },
          { questionId: 'problem_solving', rating: 10 },
          { questionId: 'finished', value: 'no' },
          { questionId: 'next', values: ['testing'] },
          { questionId: 'notes', value: 'Tidy code.' },
        ],
        recommendation: 'StrongYes',
        note: 'Good depth.',
      });
    });

    it('fills in a library scorecard, given at the stage the candidate is in, with its questions read when it is chosen', async () => {
      const fetch = stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        'GET /v1/scorecard-templates': () => Response.json(library),
        'GET /v1/scorecard-templates/phone-screen': () =>
          Response.json({ ...library[0], questions: [{ id: 'role_requirements', type: 'Rating5', label: "Meets the role's requirements", required: true }] }),
        [`POST /v1/applications/${applicationId}/scorecards`]: () => Response.json({ ...submitted, reviewerUserId: user.id }, { status: 201 }),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await fireEvent.click(await within(panel).findByRole('button', { name: 'Fill in' }));
      const sheet = await screen.findByRole('dialog', { name: 'Give your scorecard' });
      const picker = within(sheet).getByRole('combobox', { name: 'Scorecard' });
      await waitFor(() => expect(within(picker).getByRole('option', { name: 'Phone screen' })).toBeInTheDocument());
      expect(within(picker).queryByRole('option', { name: 'Old screen' })).not.toBeInTheDocument();
      await fireEvent.change(picker, { target: { value: 'library:phone-screen' } });
      const group = await within(sheet).findByRole('group', { name: /^Meets the role's requirements/ });
      await fireEvent.click(within(group).getByRole('radio', { name: '4' }));
      await fireEvent.click(within(within(sheet).getByRole('group', { name: /^Overall recommendation/ })).getByRole('radio', { name: 'Yes' }));
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Submit scorecard' }));

      await within(panel).findByText('Phone screen scorecard saved.');
      const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/scorecards') && init?.method === 'POST');
      expect(JSON.parse(String(post![1]!.body))).toEqual({
        stageId: null,
        scorecardTemplateId: 'phone-screen',
        answers: [{ questionId: 'role_requirements', rating: 4 }],
        recommendation: 'Yes',
        note: null,
      });
    });

    it("starts on the stage's scorecard even when Fill in is pressed before the job has loaded", async () => {
      let releaseJob = () => {};
      const jobLoaded = new Promise<void>((resolve) => (releaseJob = resolve));
      // The candidate is in Interview, whose scorecard the sheet starts on once the job says what it is.
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () =>
          Response.json({ ...application, stage: { ...application.stage, id: 'interview', name: 'Interview', kind: 'Interview' } }),
        [`GET /v1/jobs/${jobId}`]: async () => {
          await jobLoaded;
          return Response.json(scoredJob);
        },
        'GET /v1/scorecard-templates': () => Response.json(library),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await fireEvent.click(await within(panel).findByRole('button', { name: 'Fill in' }));
      const sheet = await screen.findByRole('dialog', { name: 'Give your scorecard' });
      releaseJob();

      await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Scorecard' })).toHaveValue('stage:interview'));
    });

    it("opens the sheet on the scorecard a link names, and only that one: a teammate's request from My tasks", async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        'GET /v1/scorecard-templates': () => Response.json(library),
      });
      page.url = new URL(`http://dashboard.test/applications/${applicationId}?tab=scorecards&scorecard=interview`);
      render(WithQueryClient, { props: { component: Page, props: { params: { applicationId } } } });

      const sheet = await screen.findByRole('dialog', { name: 'Give your scorecard' });
      // The library offers more, but the task asks for this one: its name is the subtitle and there is no select (pick 6).
      expect(await within(sheet).findByRole('group', { name: /^Skills the role needs/ })).toBeInTheDocument();
      expect(within(sheet).queryByRole('combobox', { name: 'Scorecard' })).not.toBeInTheDocument();
      expect(within(sheet).getByText('Interview scorecard')).toBeInTheDocument();
      await expectNoAxeViolations(sheet);
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());

      // Fill in, from the panel, offers every scorecard the reader can give.
      await fireEvent.click(await screen.findByRole('button', { name: 'Fill in' }));
      const again = await screen.findByRole('dialog', { name: 'Give your scorecard' });
      expect(within(again).getByRole('combobox', { name: 'Scorecard' })).toBeInTheDocument();
    });

    it('shows who gave a scorecard and when while yours is still to give, with the rest blank and nothing to open', async () => {
      const hidden: ScorecardEntry = {
        ...submitted,
        withheld: true,
        recommendation: null,
        rating: null,
        answers: [],
        ratings: [],
        note: null,
      };
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, rating: { state: 'AfterYourScorecard', scorecards: 0 } }),
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([hidden]),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      const row = (await within(panel).findAllByRole('row'))[1]!;
      expect(row).toHaveTextContent('Robin Ashby');
      expect(row).toHaveTextContent('Interview');
      expect(row).not.toHaveTextContent('Strong yes');
      expect(row).not.toHaveTextContent('4.6');
      // The blank cells keep their words for screen readers, and the row is not a control.
      expect(within(row).getAllByText('Hidden until you submit yours', { selector: '.sr-only' })).toHaveLength(2);
      expect(within(row).queryByRole('button')).not.toBeInTheDocument();
      await within(panel).findByText('Submit yours to see the others.');
    });

    it('lists a scorecard asked for that is not in yet under its own heading, and opens nothing for it', async () => {
      const asked = { ...task, id: '01a157d4-0000-7000-8000-0000000003c1', kind: 'Scorecard' as const, note: 'Fill in the Interview scorecard.', assigneeUserId: teammate, scorecardTemplateId: 'interview' };
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/tasks`]: () => Response.json([asked]),
        [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, entry({ type: 'TaskCreated', occurredAt: asked.createdAt, actorUserId: user.id, taskId: asked.id })]),
        'GET /v1/scorecard-templates': () => Response.json([{ ...library[0], id: 'interview', name: 'Interview' }]),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await within(panel).findByText('Asked for, not in yet');
      const row = within(panel).getByText('Robin Ashby').closest('tr')!;
      expect(row).toHaveTextContent('Waiting');
      expect(row).toHaveTextContent('Asked');
      expect(row).toHaveTextContent('by you');
      expect(within(row).queryByRole('button')).not.toBeInTheDocument();
    });

    it('says who asked on a requested scorecard, and after Submit offers the next task', async () => {
      const mine = {
        ...task,
        id: '01a157d4-0000-7000-8000-0000000003c2',
        kind: 'Scorecard' as const,
        note: 'Fill in the Interview scorecard.',
        assigneeUserId: user.id,
        scorecardTemplateId: 'interview',
        createdAt: '2026-10-20T10:00:00Z',
      };
      const nextApplication = '01a157d4-0000-7000-8000-0000000003c3';
      const next = { ...task, id: '01a157d4-0000-7000-8000-0000000003c4', applicationId: nextApplication, jobId, candidateName: 'Priya Natarajan', jobTitle: 'Senior .NET Engineer', assigneeUserId: user.id, createdAt: '2026-10-21T10:00:00Z' };
      let submittedOnce = false;
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/tasks`]: () => Response.json([mine]),
        [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, entry({ type: 'TaskCreated', occurredAt: mine.createdAt, actorUserId: teammate, taskId: mine.id })]),
        'GET /v1/tasks?status=Open&scope=Mine': () => Response.json(submittedOnce ? [next] : [{ ...mine, jobId, candidateName: 'Casey Morgan', jobTitle: 'Senior .NET Engineer' }, next]),
        'GET /v1/scorecard-templates': () => Response.json([{ ...library[0], id: 'interview', name: 'Interview' }]),
        [`POST /v1/applications/${applicationId}/scorecards`]: () => {
          submittedOnce = true;
          return Response.json({ ...submitted, reviewerUserId: user.id }, { status: 201 });
        },
      });
      page.url = new URL(`http://dashboard.test/applications/${applicationId}?tab=scorecards&scorecard=interview`);
      render(WithQueryClient, { props: { component: Page, props: { params: { applicationId } } } });

      const sheet = await screen.findByRole('dialog', { name: 'Give your scorecard' });
      await within(sheet).findByText(/^Asked by Robin Ashby on /);
      const group = (name: RegExp) => within(sheet).getByRole('group', { name });
      await fireEvent.click(within(group(/^Skills the role needs/)).getByRole('radio', { name: '4' }));
      await fireEvent.click(within(group(/^Problem solving/)).getByRole('radio', { name: '7' }));
      await fireEvent.click(within(group(/^Finished in the time/)).getByRole('radio', { name: 'Yes' }));
      await fireEvent.click(within(group(/^Overall recommendation/)).getByRole('radio', { name: 'Yes' }));
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Submit scorecard' }));

      await within(sheet).findByText('Scorecard submitted.');
      const link = await within(sheet).findByRole('link', { name: 'Open next task' });
      expect(link).toHaveAttribute('href', `/applications/${nextApplication}`);
    });

    it('offers Fill in to a role that fills in scorecards and Ask for one to a role that creates tasks, and neither to a Viewer', async () => {
      stubApi({ [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob) });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });
      expect(await within(panel).findByRole('button', { name: 'Fill in' })).toBeInTheDocument();
      expect(within(panel).getByRole('button', { name: 'Ask for one' })).toBeInTheDocument();
      cleanup();

      page.data.user = interviewer;
      stubApi({ [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob) });
      renderPage('scorecards');
      const scored = await screen.findByRole('region', { name: 'Scorecards' });
      expect(await within(scored).findByRole('button', { name: 'Fill in' })).toBeInTheDocument();
      expect(within(scored).queryByRole('button', { name: 'Ask for one' })).not.toBeInTheDocument();
      cleanup();

      page.data.user = userAs('Viewer');
      stubApi({ [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob) });
      renderPage('scorecards');
      const viewed = await screen.findByRole('region', { name: 'Scorecards' });
      await within(viewed).findByText('No scorecards yet.');
      expect(within(viewed).queryByRole('button', { name: 'Fill in' })).not.toBeInTheDocument();
      expect(within(viewed).queryByRole('button', { name: 'Ask for one' })).not.toBeInTheDocument();
    });

    it('offers Fill in your scorecard to a reviewer still held back, even with a scorecard of theirs at another stage', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, rating: { state: 'AfterYourScorecard', scorecards: 0 } }),
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([{ ...older, reviewerUserId: user.id }]),
        'GET /v1/scorecard-templates': () => Response.json(library),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });
      await within(panel).findByText('Submit yours to see the others.');

      expect(await within(panel).findByRole('button', { name: 'Fill in your scorecard' })).toBeInTheDocument();
    });

    it('offers no Fill in to a reader who has given a scorecard already', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([{ ...submitted, reviewerUserId: user.id }]),
        'GET /v1/scorecard-templates': () => Response.json([]),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });
      await within(panel).findByRole('button', { name: 'You' });

      expect(within(panel).queryByRole('button', { name: /^Fill in/ })).not.toBeInTheDocument();
      expect(within(panel).getByRole('button', { name: 'Ask for one' })).toBeInTheDocument();
    });

    it("asks a teammate who sees the job and fills in scorecards, on the current stage's scorecard by default", async () => {
      const fetch = stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, stage: { id: 'interview', name: 'Interview', kind: 'Interview' } }),
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/jobs/${jobId}/hiring-team`]: () => Response.json([]),
        'GET /v1/scorecard-templates': () => Response.json(library),
        'GET /v1/organization/roles': () => Response.json(roles),
        [`POST /v1/applications/${applicationId}/scorecard-requests`]: () =>
          Response.json({ ...task, kind: 'Scorecard', assigneeUserId: teammate, scorecardTemplateId: 'interview' }, { status: 201 }),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await fireEvent.click(await within(panel).findByRole('button', { name: 'Ask for one' }));
      const sheet = await screen.findByRole('dialog', { name: 'Ask for a scorecard' });
      const who = await within(sheet).findByRole('combobox', { name: 'Teammate' });
      // Robin's role fills in scorecards and sees every job; the signed-in Owner is not offered to themselves.
      await waitFor(() => expect(who).toHaveValue(teammate));
      expect(within(who).queryByRole('option', { name: user.displayName })).not.toBeInTheDocument();
      expect(within(sheet).getByRole('combobox', { name: 'Scorecard' })).toHaveValue('interview');
      // The request is the message, so the sheet asks for nothing more.
      expect(within(sheet).queryByLabelText('Note')).not.toBeInTheDocument();
      await expectNoAxeViolations(sheet);
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Ask' }));

      await within(panel).findByText('Asked Robin Ashby for the Interview scorecard.');
      const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/scorecard-requests') && init?.method === 'POST');
      expect(JSON.parse(String(post![1]!.body))).toEqual({ assigneeUserId: teammate, scorecardTemplateId: 'interview' });
    });

    it('says why when nobody else who sees the job fills in scorecards', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/jobs/${jobId}/hiring-team`]: () => Response.json([]),
        'GET /v1/scorecard-templates': () => Response.json(library),
        'GET /v1/organization/roles': () => Response.json({ ...roles, roles: roles.roles.filter((role) => role.role !== 'Custom') }),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await fireEvent.click(await within(panel).findByRole('button', { name: 'Ask for one' }));
      const sheet = await screen.findByRole('dialog', { name: 'Ask for a scorecard' });
      await within(sheet).findByText(/Nobody else on this job fills in scorecards/);
      expect(within(sheet).queryByRole('button', { name: 'Ask' })).not.toBeInTheDocument();
    });

    it('records a scorecard in Activity by its stage, leaving the judgement to the Scorecards tab', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/timeline`]: () =>
          Response.json([
            ...timeline,
            entry({ type: 'ScorecardSubmitted', occurredAt: '2026-10-20T11:45:00Z', actorUserId: teammate, stageId: 'interview', recommendation: 'StrongYes' }),
          ]),
      });
      renderPage('activity');
      const activity = await screen.findByRole('region', { name: 'Activity' });

      await within(activity).findByText(/Scorecard for Interview/);
      expect(activity).not.toHaveTextContent('Strong yes');
    });
  });

  describe('interviews', () => {
    const sending = { 'GET /v1/email-templates': () => Response.json({ sendingEnabled: true, templates: [], variables: [] }) };
    // A query the page asked for has been answered, so a control that should have appeared by now is really absent.
    const answered = async (fetch: ReturnType<typeof stubApi>, path: string) => {
      await waitFor(() => expect(fetch.mock.calls.some(([url]) => String(url) === path)).toBe(true));
      await new Promise((resolve) => setTimeout(resolve, 50));
    };

    it('offers Schedule interview beside Email, to someone who may email candidates, where this deployment can send', async () => {
      stubApi(sending);
      renderPage();
      const band = await header();

      expect(await within(band).findByRole('button', { name: 'Email' })).toBeInTheDocument();
      expect(within(band).getByRole('button', { name: 'Schedule interview' })).toBeInTheDocument();
      await fireEvent.click(within(band).getByRole('button', { name: 'Schedule interview' }));
      expect(await screen.findByRole('dialog', { name: 'Schedule interview' })).toBeInTheDocument();
    });

    it('hides it without a way to send email', async () => {
      const fetch = stubApi({ 'GET /v1/email-templates': () => Response.json({ sendingEnabled: false, templates: [], variables: [] }) });
      renderPage();
      const band = await header();

      await within(band).findByRole('button', { name: 'Move to stage, Casey Morgan' });
      await answered(fetch, '/v1/email-templates');
      expect(within(band).queryByRole('button', { name: 'Schedule interview' })).toBeNull();
    });

    it('hides it from a role that may not email candidates', async () => {
      const fetch = stubApi(sending);
      asRole('Viewer');
      renderPage();
      const band = await header();

      await answered(fetch, '/v1/email-templates');
      expect(within(band).queryByRole('button', { name: 'Schedule interview' })).toBeNull();
      expect(within(band).queryByRole('button', { name: 'Email' })).toBeNull();
    });

    it('hides it once the application has ended', async () => {
      stubApi({ ...sending, [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, stage: { id: 'rejected', name: 'Rejected', kind: 'Rejected' } }) });
      renderPage();
      const band = await header();

      await within(band).findByRole('button', { name: 'Email' });
      expect(within(band).queryByRole('button', { name: 'Schedule interview' })).toBeNull();
    });

    it('hides it for a candidate kept in mind', async () => {
      stubApi({ ...sending, [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, keptInMindAt: '2026-10-21T09:00:00Z' }) });
      renderPage();
      const band = await header();

      await within(band).findByRole('button', { name: 'Message' });
      expect(within(band).queryByRole('button', { name: 'Schedule interview' })).toBeNull();
    });

    it('hides it while an interview is open or booked, and offers it again once that one is cancelled or has expired', async () => {
      for (const status of ['Open', 'Booked'] as const) {
        const fetch = stubApi({ ...sending, [`GET /v1/applications/${applicationId}/interviews`]: () => Response.json([interview({ status })]) });
        renderPage();
        const band = await header();
        await within(band).findByRole('button', { name: 'Email' });
        await answered(fetch, `/v1/applications/${applicationId}/interviews`);
        expect(within(band).queryByRole('button', { name: 'Schedule interview' })).toBeNull();
        cleanup();
      }

      stubApi({
        ...sending,
        [`GET /v1/applications/${applicationId}/interviews`]: () => Response.json([interview({ status: 'Cancelled' }), interview({ id: 'older', status: 'Expired' })]),
      });
      renderPage();
      expect(await within(await header()).findByRole('button', { name: 'Schedule interview' })).toBeInTheDocument();
    });

    it('folds it into the More menu on a phone, beside Email', async () => {
      stubViewport(390);
      stubApi(sending);
      renderPage();
      const band = await header();

      await fireEvent.pointerDown(await within(band).findByRole('button', { name: 'More actions' }), { button: 0, pointerType: 'mouse' });
      const menu = await screen.findByRole('menu');
      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Email', 'Schedule interview']);
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Schedule interview' }));
      expect(await screen.findByRole('dialog', { name: 'Schedule interview' })).toBeInTheDocument();
    });

    it('puts a booked time first in the Interview panel, once and in the reader zone, and not in the header', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}/interviews`]: () => Response.json([interview({ status: 'Booked', startsAt, endsAt: '2026-11-05T15:30:00Z' })]) });
      renderPage();
      const band = await header();

      const panel = await screen.findByRole('region', { name: 'Interview' });
      const time = within(panel).getByText(formatDateTime(startsAt));
      expect(time).toHaveAttribute('datetime', startsAt);
      expect(time.parentElement).toHaveTextContent('30 minutes');
      // The first fact, ahead of who interviews.
      expect(time.compareDocumentPosition(within(panel).getByRole('list', { name: 'Interviewers' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
      expect(band).not.toHaveTextContent(formatDateTime(startsAt));
      expect(within(band).queryByRole('group', { name: 'Interview' })).toBeNull();
      // Said once on the whole screen.
      expect(document.body.textContent?.split(formatDateTime(startsAt))).toHaveLength(2);
    });

    it('says in the Interview panel that it is waiting for the candidate to pick a time, and says nothing in the header', async () => {
      stubApi({ [`GET /v1/applications/${applicationId}/interviews`]: () => Response.json([interview()]) });
      renderPage();
      const band = await header();

      const region = await screen.findByRole('region', { name: 'Interview' });
      expect(region).toHaveTextContent('Waiting for the candidate');
      expect(within(band).queryByRole('group', { name: 'Interview' })).toBeNull();
      expect(band).not.toHaveTextContent('Waiting for the candidate');
    });

    it('lists an interview in Activity with its time and counts it under Interviews', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}/timeline`]: () =>
          Response.json([...timeline, entry({ type: 'InterviewBooked', occurredAt: '2026-10-22T09:00:00Z', startsAt, endsAt: '2026-11-05T15:30:00Z', interviewId })]),
      });
      renderPage('activity');
      const region = await screen.findByRole('region', { name: 'Activity' });

      await within(region).findByText(`Interview booked for ${formatDateTime(startsAt)}`);
      await waitFor(() => expect(within(within(region).getByRole('group', { name: 'Show' })).getByRole('button', { name: /^Interviews/ })).toHaveTextContent('Interviews 1'));
    });
  });
});
