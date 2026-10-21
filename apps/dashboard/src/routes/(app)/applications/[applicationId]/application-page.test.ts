import type { ApplicationDetail, ApplicationTimelineEntry, JobDetail } from '@screenhire/api-client';
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId, stages, user } from '../../../../test-support/fixtures.js';
import { pendingIntegrity } from '../../../../test-support/integrity-fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// superforms (the integrity panel's dialogs) reads `navigating` too.
vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));

const applicationId = '01a157d4-0000-7000-8000-000000000300';
const resumeId = '01a157d4-0000-7000-8000-000000000301';
const resumeUpload = '01a157d4-0000-7000-8000-000000000302';
const portfolioUpload = '01a157d4-0000-7000-8000-000000000303';
const teammate = '01a157d4-0000-7000-8000-0000000000cc';

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
  ...overrides,
});

const timeline: ApplicationTimelineEntry[] = [
  entry({ type: 'Submitted', toStageId: 'applied' }),
  entry({ type: 'IntegrityAssessed', occurredAt: '2026-10-20T09:01:00Z' }),
  entry({ type: 'StageChanged', occurredAt: '2026-10-20T10:00:00Z', actorUserId: teammate, fromStageId: 'applied', toStageId: 'screening', note: 'Strong portfolio.' }),
  entry({ type: 'TagAdded', occurredAt: '2026-10-20T10:05:00Z', tag: 'fast-track', automationRunId: '01a157d4-0000-7000-8000-000000000306' }),
  entry({ type: 'StageChanged', occurredAt: '2026-10-20T11:00:00Z', actorUserId: user.id, fromStageId: 'screening', toStageId: 'interview' }),
];

beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        [`GET /v1/applications/${applicationId}`]: () => Response.json(application),
        [`GET /v1/applications/${applicationId}/timeline`]: () => Response.json(timeline),
        [`GET /v1/jobs/${jobId}`]: () => Response.json(job),
        [`GET /v1/applications/${applicationId}/integrity`]: () => Response.json({ ...pendingIntegrity(), applicationId }),
      }),
    ),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
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
    expect(within(region).getAllByRole('listitem').map((item) => item.querySelector('p')?.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Moved from Screening to Interview by you',
      'Tagged "fast-track" by an automation',
      'Moved from Applied to Screening by a teammate',
      'Integrity checks ran',
      'Applied on the careers page, into Applied',
    ]);
    expect(within(region).getByText('"Strong portfolio."')).toBeInTheDocument();
  });

  it('shows the integrity panel first, waiting for the checks', async () => {
    renderPage();
    const panel = await screen.findByRole('region', { name: 'Integrity' });

    await within(panel).findByText(/Checks are still running/);
    expect(panel).toHaveTextContent('No automated decision has been made. Every action is logged.');
    // First in the page's order, so one column (tablet and phone) starts with it.
    const panels = [...document.querySelectorAll('[data-slot="panel"] > header h2')].map((heading) => heading.textContent?.trim());
    expect(panels).toEqual(['Integrity', 'Resume', 'Answers', 'Declarations', 'Review log', 'Activity']);
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
});
