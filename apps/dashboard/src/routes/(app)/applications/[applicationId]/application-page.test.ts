import type { ApplicationDetail, ApplicationTaskEntry, ApplicationTimelineEntry, JobDetail, ScorecardEntry, TeamMember } from '@screenhire/api-client';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId, stages, user } from '../../../../test-support/fixtures.js';
import { pendingIntegrity } from '../../../../test-support/integrity-fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
// After the fixtures: the mock below reads `user`.
import { page } from '$app/state';
import Page from './+page.svelte';

// superforms (the integrity panel's dialogs) reads `navigating` too.
vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));

const applicationId = '01a157d4-0000-7000-8000-000000000300';
const resumeId = '01a157d4-0000-7000-8000-000000000301';
const resumeUpload = '01a157d4-0000-7000-8000-000000000302';
const portfolioUpload = '01a157d4-0000-7000-8000-000000000303';
const teammate = '01a157d4-0000-7000-8000-0000000000cc';
const taskId = '01a157d4-0000-7000-8000-000000000307';

const members: TeamMember[] = [
  { id: '01a157d4-0000-7000-8000-000000000308', userId: user.id, email: user.email, displayName: user.displayName, role: 'Owner', joinedAt: '2026-10-01T09:00:00Z' },
  { id: '01a157d4-0000-7000-8000-000000000309', userId: teammate, email: 'robin@brightwater.example.com', displayName: 'Robin Ashby', role: 'Recruiter', joinedAt: '2026-10-02T09:00:00Z' },
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
  candidate: { id: '01a157d4-0000-7000-8000-000000000304', fullName: 'Casey Morgan', email: 'casey.morgan@example.com', phone: null, createdAt: '2026-10-20T09:00:00Z' },
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
  ...overrides,
});

const timeline: ApplicationTimelineEntry[] = [
  entry({ type: 'Submitted', toStageId: 'applied' }),
  entry({ type: 'IntegrityAssessed', occurredAt: '2026-10-20T09:01:00Z' }),
  entry({ type: 'StageChanged', occurredAt: '2026-10-20T10:00:00Z', actorUserId: teammate, fromStageId: 'applied', toStageId: 'screening', note: 'Strong portfolio.' }),
  entry({ type: 'TagAdded', occurredAt: '2026-10-20T10:05:00Z', tag: 'fast-track', automationRunId: '01a157d4-0000-7000-8000-000000000306' }),
  entry({ type: 'StageChanged', occurredAt: '2026-10-20T11:00:00Z', actorUserId: user.id, fromStageId: 'screening', toStageId: 'interview' }),
  entry({ type: 'NoteAdded', occurredAt: '2026-10-20T11:30:00Z', actorUserId: '01a157d4-0000-7000-8000-0000000000dd', note: 'Ask about the notice period.' }),
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
});

function renderPage() {
  return render(WithQueryClient, { props: { component: Page, props: { params: { applicationId } } } });
}

describe('application page', () => {
  it("links the resume through the API's download route, as a full navigation", async () => {
    renderPage();

    const link = await screen.findByRole('link', { name: 'Download Casey Morgan CV.pdf' });
    expect(link).toHaveAttribute('href', `/v1/applications/${applicationId}/files/${resumeId}`);
    expect(link).toHaveAttribute('data-sveltekit-reload');
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
    // A file answer is a file tile; one the scan withheld says why and offers no download.
    expect(rows[2]![1]).toMatch(/portfolio\.pdf.*Withheld: malware found/);
    expect(within(answers).queryByRole('link')).not.toBeInTheDocument();
  });

  it('tells the activity newest first, saying who made each change', async () => {
    renderPage();
    const region = await screen.findByRole('region', { name: 'Activity' });

    await within(region).findByText(/Moved from Screening to Interview/);
    await within(region).findByText(/by Robin Ashby/);
    expect(within(region).getAllByRole('listitem').map((item) => item.querySelector('p')?.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Note by a former teammate',
      'Moved from Screening to Interview by you',
      'Tagged "fast-track" by an automation',
      'Moved from Applied to Screening by Robin Ashby',
      'Integrity checks ran',
      'Applied on the careers page, into Applied',
    ]);
    expect(within(region).getByText('"Strong portfolio."')).toBeInTheDocument();
    expect(within(region).getByText('"Ask about the notice period."')).toBeInTheDocument();
  });

  it('shows the integrity panel first, waiting for the checks', async () => {
    renderPage();
    const panel = await screen.findByRole('region', { name: 'Integrity' });

    await within(panel).findByText(/Checks are still running/);
    expect(panel).toHaveTextContent('No automated decision has been made. Every action is logged.');
    // First in the page's order, so one column (tablet and phone) starts with it.
    await screen.findByRole('region', { name: 'Tasks' });
    await screen.findByRole('region', { name: 'Automations' });
    const panels = [...document.querySelectorAll('[data-slot="panel"] > header h2')].map((heading) => heading.textContent?.trim());
    expect(panels).toEqual(['Integrity', 'Resume', 'Answers', 'Declarations', 'Tasks', 'Tags', 'Automations', 'Review log', 'Activity']);
  });

  it('adds a note from Activity, in a sheet', async () => {
    const fetch = stubApi({
      [`POST /v1/applications/${applicationId}/notes`]: () => Response.json(entry({ type: 'NoteAdded', note: 'Great call.' }), { status: 201 }),
    });
    renderPage();
    const activity = await screen.findByRole('region', { name: 'Activity' });

    await fireEvent.click(await within(activity).findByRole('button', { name: 'Add note' }));
    const sheet = await screen.findByRole('dialog', { name: 'Add a note' });
    await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: '  Great call.  ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Add note' }));

    await within(activity).findByText('Note added.');
    const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/notes') && init?.method === 'POST');
    expect(JSON.parse(String(post![1]!.body))).toEqual({ note: 'Great call.' });
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

  it("completes a task an automation made, naming who it is for", async () => {
    const fetch = stubApi({
      [`POST /v1/tasks/${taskId}/complete`]: () => Response.json({ ...task, completedAt: '2026-10-20T12:00:00Z', completedByUserId: user.id }),
    });
    renderPage();
    const tasks = await screen.findByRole('region', { name: 'Tasks' });

    expect(await within(tasks).findByText('Call the references.')).toBeInTheDocument();
    expect(within(tasks).getByText('To-do')).toBeInTheDocument();
    expect(within(tasks).getByText('For the team')).toBeInTheDocument();
    await fireEvent.click(within(tasks).getByRole('button', { name: 'Complete, Call the references.' }));
    await waitFor(() => expect(fetch.mock.calls.some(([url, init]) => String(url) === `/v1/tasks/${taskId}/complete` && init?.method === 'POST')).toBe(true));
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
    page.data.user = { ...user, organization: { ...user.organization!, role: 'Viewer' } };
    renderPage();
    const tasks = await screen.findByRole('region', { name: 'Tasks' });
    await within(tasks).findByText('Call the references.');

    expect(within(tasks).queryByRole('button', { name: /^Complete/ })).not.toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Tags' })).queryByRole('button', { name: 'Add tag' })).not.toBeInTheDocument();
    expect(within(screen.getByRole('region', { name: 'Activity' })).queryByRole('button', { name: 'Add note' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Automations' })).not.toBeInTheDocument();
  });

  it('puts the contact details under the name and the facts in the header band', async () => {
    renderPage();
    const heading = await screen.findByRole('heading', { level: 1, name: 'Casey Morgan' });
    const band = heading.closest<HTMLElement>('[data-slot="header-band"]')!;

    expect(within(band).getByRole('link', { name: 'casey.morgan@example.com' })).toHaveAttribute('href', 'mailto:casey.morgan@example.com');
    const facts = within(band)
      .getAllByRole('term')
      .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.trim()]);
    expect(facts.map(([label]) => label)).toEqual(['Stage', 'Job', 'Applied', 'Source']);
    expect(facts[0]![1]).toBe('Screening');
    expect(facts[3]![1]).toBe('Careers page');
    expect(within(band).getByRole('link', { name: 'Senior .NET Engineer' })).toHaveAttribute('href', `/jobs/${jobId}`);
    expect(await within(band).findByRole('button', { name: 'Move to stage, Casey Morgan' })).toBeInTheDocument();
  });

  it("shows the candidate's declarations", async () => {
    renderPage();
    const declarations = await screen.findByRole('region', { name: 'Declarations' });

    expect(declarations).toHaveTextContent('Used AI: Grammar checks only.');
    expect(declarations).toHaveTextContent('Asked for adjustments to the process');
  });

  describe('scorecards', () => {
    // The job with an interview scorecard; the application reached Interview (the timeline) and is back in Screening.
    const scoredJob = {
      ...job,
      workflow: {
        ...job.workflow,
        stages: stages.map((stage) =>
          stage.id === 'interview'
            ? {
                ...stage,
                scorecard: {
                  criteria: [
                    { id: 'role_skills', label: 'Skills the role needs', guidance: 'The skills in the job description.' },
                    { id: 'problem_solving', label: 'Problem solving' },
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
      ratings: [
        { criterionId: 'role_skills', label: 'Skills the role needs', rating: 'StronglyDemonstrated' },
        { criterionId: 'problem_solving', label: 'Problem solving', rating: null },
      ],
      recommendation: 'StrongYes',
      note: 'Clear walk-through of the design.',
      workflowVersion: 1,
      timelineEventId: '01a157d4-0000-7000-8000-000000000311',
      submittedAt: '2026-10-20T11:45:00Z',
    };

    it('shows each scorecard with its reviewer and its ratings in words, never a number', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([submitted]),
      });
      renderPage();
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await within(panel).findByText('Strong yes');
      expect(panel).toHaveTextContent('by Robin Ashby');
      expect(panel).toHaveTextContent('Interview');
      const rows = within(panel)
        .getAllByRole('term')
        .map((term) => [term.textContent?.trim(), term.nextElementSibling?.textContent?.trim()]);
      expect(rows).toEqual([
        ['Skills the role needs', 'Strongly demonstrated'],
        ['Problem solving', 'Not rated'],
      ]);
      expect(panel).toHaveTextContent('"Clear walk-through of the design."');
      // A rating or a recommendation is never a figure or a percentage.
      expect(within(panel).getByRole('list', { name: 'Scorecards' }).querySelector('dl')?.textContent).not.toMatch(/\d|%/);
    });

    it('fills a scorecard in a sheet: ratings by criterion, and the recommendation is required', async () => {
      const fetch = stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`POST /v1/applications/${applicationId}/scorecards`]: () => Response.json({ ...submitted, reviewerUserId: user.id }, { status: 201 }),
      });
      renderPage();
      const panel = await screen.findByRole('region', { name: 'Scorecards' });

      await fireEvent.click(await within(panel).findByRole('button', { name: 'Fill scorecard' }));
      const sheet = await screen.findByRole('dialog', { name: 'Fill scorecard' });
      expect(sheet).toHaveTextContent('Stage: Interview');
      expect(within(sheet).queryByText(/integrity/i)).not.toBeInTheDocument();
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Submit scorecard' }));
      await within(sheet).findByText('Choose a recommendation.');
      expect(fetch.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false);

      await fireEvent.click(within(within(sheet).getByRole('group', { name: 'Skills the role needs' })).getByRole('radio', { name: 'Demonstrated' }));
      await fireEvent.click(within(within(sheet).getByRole('group', { name: 'Recommendation' })).getByRole('radio', { name: 'Strong yes' }));
      await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: '  Good depth.  ' } });
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Submit scorecard' }));

      await within(panel).findByText('Scorecard for Interview saved.');
      const post = fetch.mock.calls.find(([url, init]) => String(url).endsWith('/scorecards') && init?.method === 'POST');
      expect(JSON.parse(String(post![1]!.body))).toEqual({
        stageId: 'interview',
        ratings: [{ criterionId: 'role_skills', rating: 'Demonstrated' }],
        recommendation: 'StrongYes',
        note: 'Good depth.',
      });
    });

    it('offers no Fill scorecard once you scored every reached stage, nor to a Viewer', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/scorecards`]: () => Response.json([{ ...submitted, reviewerUserId: user.id }]),
      });
      renderPage();
      const panel = await screen.findByRole('region', { name: 'Scorecards' });
      await within(panel).findByText('by you');
      expect(within(panel).queryByRole('button', { name: 'Fill scorecard' })).not.toBeInTheDocument();
      cleanup();

      page.data.user = { ...user, organization: { ...user.organization!, role: 'Viewer' } };
      stubApi({ [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob) });
      renderPage();
      const viewed = await screen.findByRole('region', { name: 'Scorecards' });
      await within(viewed).findByText('No scorecards yet.');
      expect(within(viewed).queryByRole('button', { name: 'Fill scorecard' })).not.toBeInTheDocument();
    });

    it('records a scorecard in Activity by its stage, leaving the judgement to the panel', async () => {
      stubApi({
        [`GET /v1/jobs/${jobId}`]: () => Response.json(scoredJob),
        [`GET /v1/applications/${applicationId}/timeline`]: () =>
          Response.json([
            ...timeline,
            entry({ type: 'ScorecardSubmitted', occurredAt: '2026-10-20T11:45:00Z', actorUserId: teammate, stageId: 'interview', recommendation: 'StrongYes' }),
          ]),
      });
      renderPage();
      const activity = await screen.findByRole('region', { name: 'Activity' });

      await within(activity).findByText(/Scorecard for Interview/);
      expect(activity).not.toHaveTextContent('Strong yes');
    });
  });
});
