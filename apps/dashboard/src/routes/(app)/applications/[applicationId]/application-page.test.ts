import type {
  ApplicationDetail,
  ApplicationTaskEntry,
  CandidateEmailEntry,
  ApplicationTimelineEntry,
  JobDetail,
  ScorecardEntry,
  ScorecardTemplateSummary,
  TeamMember,
} from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub, interviewer, jobId, roleCatalogue, stages, user, userAs } from '../../../../test-support/fixtures.js';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { botIntegrity, pendingIntegrity } from '../../../../test-support/integrity-fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
// After the fixtures: the mock below reads `user`.
import { page } from '$app/state';
import Page from './+page.svelte';

// superforms (the integrity panel's dialogs) reads `navigating` too.
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
  entry({ type: 'IntegrityAssessed', occurredAt: '2026-10-20T09:01:00Z' }),
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
  [`GET /v1/jobs/${jobId}`]: () => Response.json(job),
  [`GET /v1/applications/${applicationId}/integrity`]: () => Response.json({ ...pendingIntegrity(), applicationId }),
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

/** Renders the page on `tab`, as its address (?tab=) says; Playwright switches tabs by clicking. */
function renderPage(tab?: string) {
  page.url = new URL(`http://dashboard.test/applications/${applicationId}${tab ? `?tab=${tab}` : ''}`);
  return render(WithQueryClient, { props: { component: Page, props: { params: { applicationId } } } });
}

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

describe('application page', () => {
  it('has the same six tabs in the same order, with counts, and the signals on Verification when the level needs a look', async () => {
    stubApi({ [`GET /v1/applications/${applicationId}/integrity`]: () => Response.json({ ...botIntegrity(), applicationId }) });
    renderPage();

    const tabs = await screen.findByRole('tablist', { name: 'Candidate sections' });
    await waitFor(() => expect(within(tabs).getByRole('tab', { name: /^Verification \d+$/ })).toBeInTheDocument());
    await waitFor(() =>
      expect(within(tabs).getAllByRole('tab').map((tab) => tab.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        'Overview',
        'Application',
        'Messages 0',
        'Scorecards 0',
        expect.stringMatching(/^Verification \d+$/),
        'Activity 6',
      ]),
    );
    expect(within(tabs).getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
  });

  it('puts who they are and where they stand in the header, and the files and tags on Overview', async () => {
    renderPage();
    const heading = await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' });
    const band = heading.closest<HTMLElement>('[data-slot="header-band"]')!;

    // One meta line above the tabs, where the eye starts on every tab; there is no rail at the window's far edge. Each
    // value reads without its label, which only screen readers hear.
    const facts = within(band)
      .getAllByRole('term')
      .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.replace(/\s+/g, ' ').trim()]);
    expect(facts.map(([label]) => label)).toEqual(['Stage', 'Job', 'Applied', 'Rating']);
    for (const term of within(band).getAllByRole('term')) expect(term).toHaveClass('sr-only');
    expect(facts[0]![1]).toBe('Screening');
    expect(facts[2]![1]).toMatch(/^Applied .+ via the careers page$/);
    expect(within(band).getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/jobs/${jobId}`);
    expect(within(band).getByRole('link', { name: 'casey.morgan@example.com' })).toHaveAttribute('href', 'mailto:casey.morgan@example.com');
    expect(screen.queryByRole('complementary', { name: 'Details' })).toBeNull();
    const files = await screen.findByRole('region', { name: 'Files' });
    expect(within(files).getByRole('button', { name: 'View Casey Morgan CV.pdf' })).toBeInTheDocument();
    // The scan withheld the portfolio, so its row says why and offers neither View nor Download.
    expect(files).toHaveTextContent('Unsafe file');
    expect(screen.getByRole('region', { name: 'Tags' })).toBeInTheDocument();
    expect(await within(band).findByRole('button', { name: 'Move to stage, Casey Morgan' })).toBeInTheDocument();
  });

  it("links the resume through the API's download route, as a full navigation", async () => {
    renderPage();

    const link = await screen.findByRole('link', { name: 'Download Casey Morgan CV.pdf' });
    expect(link).toHaveAttribute('href', `/v1/applications/${applicationId}/files/${resumeId}`);
    expect(link).toHaveAttribute('data-sveltekit-reload');
  });

  it('puts what needs you now, the last five things anyone did and the answers on Overview', async () => {
    renderPage();

    const steps = await screen.findByRole('region', { name: 'Next steps' });
    await within(steps).findByText('Call the references.');
    const activity = screen.getByRole('region', { name: 'Activity' });
    await waitFor(() => expect(within(activity).getAllByRole('listitem')).toHaveLength(5));
    expect(within(activity).getByRole('link', { name: 'All 6 in Activity' })).toHaveAttribute('href', '?tab=activity');
    await screen.findByRole('region', { name: 'Automations' });
    const panels = [...document.querySelectorAll('[data-slot="panel"] > header h2')].map((heading) => heading.textContent?.trim());
    expect(panels).toEqual(['Next steps', 'Automations', 'Activity', 'Files', 'Tags', 'Answers']);
  });

  it('shows the integrity next step on Overview when the level needs a look, never as a reason to reject', async () => {
    stubApi({ [`GET /v1/applications/${applicationId}/integrity`]: () => Response.json({ ...botIntegrity(), applicationId }) });
    renderPage();
    const steps = await screen.findByRole('region', { name: 'Next steps' });

    await within(steps).findByText(/Confirm the candidate is who they say they are/);
    // The caveat shows exactly once: this step's own words carry it, so the row adds none of its own (ADR-0004).
    expect(steps.textContent?.match(/not a reason to reject/gi)).toHaveLength(1);
    expect(steps).toHaveTextContent('Verification suggested by the checks.');
    expect(within(steps).getByRole('link', { name: 'See the reasons' })).toHaveAttribute('href', '?tab=verification');
    // The level is also the candidate's status, beside the name on every tab (S2, T2).
    const band = screen.getByRole('heading', { level: 1 }).closest<HTMLElement>('[data-slot="header-band"]')!;
    expect(within(band).getByText('Verification suggested')).toBeInTheDocument();
  });

  it("labels each answer with the job's question and shows choices by their labels", async () => {
    renderPage();
    const answers = await screen.findByRole('region', { name: 'Answers' });

    await within(answers).findByText('Do you have the right to work in the UK?');
    const rows = within(answers)
      .getAllByRole('term')
      .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.trim()]);
    expect(rows.map(([label]) => label)).toEqual(['Do you have the right to work in the UK?', 'Years of experience', 'Portfolio', 'Why this role?']);
    expect(rows[0]![1]).toBe('Yes');
    expect(rows[1]![1]).toBe('3 to 5 years');
    expect(rows[3]![1]).toBe('Small teams.\nReal problems.');
    // A file answer names the file; its row is in the details rail.
    expect(rows[2]![1]).toBe('portfolio.pdf');
    expect(within(answers).getAllByRole('link').map((link) => link.getAttribute('href'))).toEqual(['?tab=application']);
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

  it('filters Activity by kind, with a count on each', async () => {
    renderPage('activity');
    const region = await screen.findByRole('region', { name: 'Activity' });
    const filter = within(region).getByRole('group', { name: 'Show' });

    await waitFor(() =>
      expect(within(filter).getAllByRole('button').map((button) => button.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        'All 6',
        'Notes 1',
        'Emails 0',
        'Scorecards 0',
        'Stage and tags 4',
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
    expect(dialog).toHaveTextContent('It leaves Overview and Activity for the whole team.');
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
    // Candidates cannot reply into ScreenHire yet, and the tab does not say they can; a viewer gets no email box.
    expect(messages).not.toHaveTextContent(/repl/i);
    expect(within(messages).queryByRole('form')).not.toBeInTheDocument();
  });

  it('shows the Verification tab with its checks and the review log, also at its old address', async () => {
    // ?tab=integrity is the tab's old address: a link from before the rename still opens it.
    renderPage('integrity');

    await screen.findByText(/Checks are still running/);
    expect(screen.getByRole('tab', { name: /^Verification/ })).toHaveAttribute('aria-selected', 'true');
    expect(document.body).toHaveTextContent('No automated decision has been made. Every action is logged.');
    expect(await screen.findByRole('region', { name: 'Review log' })).toBeInTheDocument();
  });

  it('adds a note from the box, mentioning a teammate picked from the list by keyboard', async () => {
    const fetch = stubApi({
      [`POST /v1/applications/${applicationId}/notes`]: () => Response.json(entry({ type: 'NoteAdded', note: 'Great call.' }), { status: 201 }),
    });
    renderPage();
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

  it('adds a tag and removes one, each with its own button beside the pill', async () => {
    const fetch = stubApi({
      [`POST /v1/applications/${applicationId}/tags`]: () => Response.json({ ...application, tags: ['Referral'] }),
      [`DELETE /v1/applications/${applicationId}/tags?tag=Referral`]: () => Response.json(application),
    });
    renderPage();
    const tags = await screen.findByRole('region', { name: 'Tags' });
    expect(within(tags).getByText('No tags yet.')).toBeInTheDocument();

    await fireEvent.input(within(tags).getByLabelText('New tag'), { target: { value: 'Referral' } });
    await fireEvent.click(within(tags).getByRole('button', { name: 'Add tag' }));
    await within(tags).findByText('Tagged "Referral".');
    const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/tags') && init?.method === 'POST');
    expect(JSON.parse(String(post![1]!.body))).toEqual({ tag: 'Referral' });

    application.tags = ['Referral'];
    try {
      cleanup();
      renderPage();
      const tagged = await screen.findByRole('region', { name: 'Tags' });
      await fireEvent.click(await within(tagged).findByRole('button', { name: 'Remove tag Referral' }));
      await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url).endsWith('/tags?tag=Referral') && init?.method === 'DELETE')).toBe(true));
    } finally {
      application.tags = [];
    }
  });

  it('completes a task an automation made, naming who it is for', async () => {
    const fetch = stubApi({
      [`POST /v1/tasks/${taskId}/complete`]: () => Response.json({ ...task, completedAt: '2026-10-20T12:00:00Z', completedByUserId: user.id }),
    });
    renderPage();
    const tasks = await screen.findByRole('region', { name: 'Next steps' });

    expect(await within(tasks).findByText('Call the references.')).toBeInTheDocument();
    expect(within(tasks).getByText('To-do')).toBeInTheDocument();
    expect(within(tasks).getByText('For the team')).toBeInTheDocument();
    await fireEvent.click(within(tasks).getByRole('checkbox', { name: 'Mark done for Call the references.' }));
    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url) === `/v1/tasks/${taskId}/complete` && init?.method === 'POST')).toBe(true));
    expect(await within(tasks).findByRole('button', { name: 'Undo, Call the references.' })).toBeInTheDocument();
    expect(within(tasks).getByRole('checkbox', { name: 'Mark done for Call the references.' })).toBeChecked();
  });

  it('gives a review and a requested scorecard no checkbox: their row links to the Scorecards tab, for the person they are for', async () => {
    stubApi({
      [`GET /v1/applications/${applicationId}/tasks`]: () =>
        Response.json([
          { ...task, id: '01a157d4-0000-7000-8000-000000000330', kind: 'Review', note: 'Review this application.', assigneeUserId: user.id },
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
    const tasks = await screen.findByRole('region', { name: 'Next steps' });

    await within(tasks).findByText('Fill in the Interview scorecard.');
    expect(within(tasks).getAllByRole('checkbox').map((box) => box.getAttribute('aria-label'))).toEqual(['Mark done for Call the references.']);
    expect(within(tasks).getByRole('link', { name: 'Fill in: Fill in the Interview scorecard.' })).toHaveAttribute('href', '?tab=scorecards&scorecard=interview');
    expect(within(tasks).getByRole('link', { name: 'Fill in: Review this application.' })).toHaveAttribute('href', '?tab=scorecards');
    // Robin's request is Robin's to do.
    expect(within(tasks).queryByRole('link', { name: 'Fill in: Fill in the Screening scorecard.' })).not.toBeInTheDocument();
  });

  describe('ticking a task on Next steps', () => {
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
      const steps = await screen.findByRole('region', { name: 'Next steps' });

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
      const steps = await screen.findByRole('region', { name: 'Next steps' });

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
      const steps = await screen.findByRole('region', { name: 'Next steps' });

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
      const steps = await screen.findByRole('region', { name: 'Next steps' });

      await fireEvent.click(await within(steps).findByRole('checkbox', { name: tick }));

      expect((await screen.findAllByText(/Could not complete the task/)).length).toBeGreaterThan(0);
      expect(within(steps).getByRole('checkbox', { name: tick })).not.toBeChecked();
    });
  });

  it('runs a manual automation with its own idempotency key, and offers only manual ones', async () => {
    const fetch = stubApi({
      [`POST /v1/applications/${applicationId}/automations/ask-references/run`]: () =>
        Response.json({ applicationId, automationId: 'ask-references', eventId: crypto.randomUUID() }, { status: 202 }),
    });
    renderPage();
    const automations = await screen.findByRole('region', { name: 'Automations' });

    expect(within(automations).queryByText('On arrival')).not.toBeInTheDocument();
    await fireEvent.click(await within(automations).findByRole('button', { name: 'Run, Ask for references' }));
    await within(automations).findByText(/Asked to run "Ask for references"/);
    const run = fetch.mock.calls.find(([url]) => String(url).endsWith('/automations/ask-references/run'));
    expect(new Headers(run![1]!.headers).get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/);
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

  it('leaves out the controls that change hiring data for a viewer', async () => {
    asRole('Viewer');
    stubApi({ [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json([...timeline, ...notes]) });
    renderPage();
    const tasks = await screen.findByRole('region', { name: 'Next steps' });
    await within(tasks).findByText('Call the references.');

    expect(within(tasks).queryByRole('checkbox', { name: /^Mark done/ })).not.toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Tags' })).queryByRole('button', { name: 'Add tag' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Activity' })).queryByLabelText('Note for the hiring team')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'More actions for this note' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Automations' })).not.toBeInTheDocument();
  });

  it('gives an Interviewer notes and tags but no Move, task or automation controls', async () => {
    page.data.user = interviewer;
    renderPage();
    const tasks = await screen.findByRole('region', { name: 'Next steps' });
    await within(tasks).findByText('Call the references.');

    expect(within(screen.getByRole('region', { name: 'Activity' })).getByLabelText('Note for the hiring team')).toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Tags' })).getByRole('button', { name: 'Add tag' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Move to stage, Casey Morgan' })).not.toBeInTheDocument();
    expect(within(tasks).queryByRole('checkbox', { name: /^Mark done/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Automations' })).not.toBeInTheDocument();
  });

  it("shows the candidate's declarations on Application", async () => {
    renderPage('application');
    const declarations = await screen.findByRole('region', { name: 'Declarations' });

    expect(declarations).toHaveTextContent('Used AI: Grammar checks only.');
    expect(declarations).toHaveTextContent('Asked for adjustments to the process');
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
    };

    it('shows each scorecard with its reviewer and each answer, and a v0.2 scorecard as it was', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([older, submitted]),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await within(panel).findByText('Strong yes');
      expect(panel).toHaveTextContent('by Robin Ashby');
      expect(panel).toHaveTextContent('Interview');
      const rows = within(panel)
        .getAllByRole('term')
        .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.trim()]);
      expect(rows).toEqual([
        ["Meets the role's requirements", 'Demonstrated'],
        ['Skills the role needs', '5 of 5'],
        ['Problem solving', '7 of 10'],
        ['Finished in the time', 'Yes'],
        ['Next interview covers', 'System design, Testing'],
        ['Notes on the exercise', 'Not answered'],
      ]);
      expect(panel).toHaveTextContent('"Clear walk-through of the design."');
    });

    it("shows the candidate's rating beside the title and opens it to each scorecard", async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, rating: { state: 'Rated', value: 4.6, scorecards: 1 } }),
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([older, submitted]),
      });
      renderPage('overview');

      const rating = await screen.findByRole('button', { name: 'Rated 4.6 out of 5 from 1 scorecard' });
      await fireEvent.click(rating);
      const list = (await screen.findByText('From 1 scorecard')).closest<HTMLElement>('[data-slot="popover-content"]')!;
      expect(list).toHaveTextContent('Robin Ashby');
      expect(list).toHaveTextContent('4.6');
      // Only scorecards that count: the v0.2 one has no number.
      expect(within(list).getAllByRole('listitem')).toHaveLength(1);
    });

    it('says the rating comes after your scorecard while you have a review to give', async () => {
      stubApi({
        [`GET /v1/applications/${applicationId}`]: () => Response.json({ ...application, rating: { state: 'AfterYourScorecard', scorecards: 0 } }),
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await within(panel).findByText('Other scorecards show after you submit yours.');
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
      const sheet = await screen.findByRole('dialog', { name: 'Fill in a scorecard' });
      // The candidate is in Screening, which has no scorecard, so nothing is chosen until the reviewer picks one.
      const picker = within(sheet).getByRole('combobox', { name: 'Scorecard' });
      expect(picker).toHaveValue('');
      await fireEvent.change(picker, { target: { value: 'stage:interview' } });
      expect(within(sheet).queryByText(/integrity/i)).not.toBeInTheDocument();
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
      const sheet = await screen.findByRole('dialog', { name: 'Fill in a scorecard' });
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

    it("opens the sheet on the scorecard a link names: a teammate's request from My tasks", async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        'GET /v1/scorecard-templates': () => Response.json(library),
      });
      page.url = new URL(`http://dashboard.test/applications/${applicationId}?tab=scorecards&scorecard=interview`);
      render(WithQueryClient, { props: { component: Page, props: { params: { applicationId } } } });

      const sheet = await screen.findByRole('dialog', { name: 'Fill in a scorecard' });
      expect(within(sheet).getByRole('combobox', { name: 'Scorecard' })).toHaveValue('stage:interview');
      expect(within(sheet).getByRole('group', { name: /^Skills the role needs/ })).toBeInTheDocument();
      await expectNoAxeViolations(sheet);
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

    it('says so when the reader has given every scorecard there is, with nothing to submit', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([{ ...submitted, reviewerUserId: user.id }]),
        'GET /v1/scorecard-templates': () => Response.json([]),
      });
      renderPage('scorecards');
      const panel = await screen.findByRole('region', { name: 'Scorecards' });
      await within(panel).findByText('by you');

      await fireEvent.click(within(panel).getByRole('button', { name: 'Fill in' }));
      const sheet = await screen.findByRole('dialog', { name: 'Fill in a scorecard' });
      await within(sheet).findByText('You have given every scorecard you can for this candidate at this stage.');
      expect(within(sheet).queryByRole('button', { name: 'Submit scorecard' })).not.toBeInTheDocument();
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
      await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: '  Focus on the pairing exercise.  ' } });
      await expectNoAxeViolations(sheet);
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Ask' }));

      await within(panel).findByText('Asked Robin Ashby for the Interview scorecard.');
      const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/scorecard-requests') && init?.method === 'POST');
      expect(JSON.parse(String(post![1]!.body))).toEqual({ assigneeUserId: teammate, scorecardTemplateId: 'interview', note: 'Focus on the pairing exercise.' });
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
      await within(sheet).findByText(/Nobody else who sees this job fills in scorecards/);
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
});
