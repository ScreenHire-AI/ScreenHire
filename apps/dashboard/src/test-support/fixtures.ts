import type { ApplicationSummary, BoardSummary, CurrentUser, JobPostingStatus, JobSummary, WorkflowStage } from '@screenhire/api-client';

/** Fictional data for component tests, shaped like the API's responses. */

export const user: CurrentUser = {
  id: '01a157d4-0000-7000-8000-0000000000aa',
  email: 'avery@brightwater.example.com',
  displayName: 'Avery Quinn',
  organization: { id: '01a157d4-0000-7000-8000-0000000000bb', name: 'Brightwater Studio', slug: 'brightwater', role: 'Owner' },
};

export const jobs: JobSummary[] = [
  {
    id: '01a157d4-0000-7000-8000-000000000001',
    title: 'Customer Success Lead',
    slug: 'customer-success-lead',
    location: 'Manchester, UK',
    workplaceType: 'Onsite',
    status: 'Draft',
    createdAt: '2026-10-19T09:00:00Z',
    publishedAt: null,
    employmentType: 'FullTime',
    department: null,
    templateUpdateAvailable: false,
  },
  {
    id: '01a157d4-0000-7000-8000-000000000002',
    title: 'Senior .NET Engineer',
    slug: 'senior-dotnet-engineer',
    location: 'Remote (GB, IE)',
    workplaceType: 'Remote',
    status: 'Open',
    createdAt: '2026-10-18T09:00:00Z',
    publishedAt: '2026-10-18T12:00:00Z',
    employmentType: 'FullTime',
    department: 'Engineering',
    templateUpdateAvailable: false,
  },
];

export const jobId = '01a157d4-0000-7000-8000-000000000002';

export const stages: WorkflowStage[] = [
  { id: 'applied', name: 'Applied', kind: 'Applied' },
  { id: 'screening', name: 'Screening', kind: 'Screening' },
  { id: 'interview', name: 'Interview', kind: 'Interview' },
  { id: 'offer', name: 'Offer', kind: 'Offer' },
];

/** "Now" for the fixtures: two hours after the newest application. */
export const now = Date.parse('2026-10-20T12:00:00Z');

function application(index: number, candidateName: string, stage: WorkflowStage, submittedAt: string): ApplicationSummary {
  return {
    id: `01a157d4-0000-7000-8000-0000000001${index.toString().padStart(2, '0')}`,
    jobId,
    candidateId: `01a157d4-0000-7000-8000-0000000002${index.toString().padStart(2, '0')}`,
    candidateName,
    stage: { id: stage.id, name: stage.name, kind: stage.kind },
    submittedAt,
    stageEnteredAt: submittedAt,
    jobTitle: 'Senior .NET Engineer',
    source: index % 2 === 0 ? 'CareersSite' : 'Recruiter',
  };
}

export const applications: ApplicationSummary[] = [
  application(0, 'Casey Morgan', stages[0]!, '2026-10-20T10:00:00Z'),
  application(1, 'Jordan Lee', stages[1]!, '2026-10-19T12:00:00Z'),
  application(2, 'Sam Taylor', stages[0]!, '2026-10-17T12:00:00Z'),
];

export function board(id: string, name: string, overrides: Partial<BoardSummary> = {}): BoardSummary {
  return {
    id,
    name,
    mode: 'Push',
    canClose: true,
    canUnpublishPerBoard: true,
    confirmsLive: true,
    confirmsRemoval: true,
    publishEtaSeconds: null,
    removalEtaSeconds: null,
    dailyQuota: null,
    ...overrides,
  };
}

export function posting(boardId: string, boardName: string, overrides: Partial<JobPostingStatus> = {}): JobPostingStatus {
  return {
    id: `posting-${boardId}`,
    boardId,
    boardName,
    mode: 'Push',
    state: 'Live',
    jobVersion: 1,
    confirmsLive: true,
    confirmsRemoval: true,
    canUnpublish: true,
    expectedBy: null,
    issues: [],
    lastError: null,
    updatedAt: '2026-10-20T11:00:00Z',
    submittedAt: null,
    liveAt: null,
    closingAt: null,
    closedAt: null,
    ...overrides,
  };
}

/** A fetch stub that answers each request by method and path, and records every call. */
export function apiStub(routes: Record<string, (init: RequestInit | undefined) => Response | Promise<Response>>) {
  return async (input: RequestInfo | URL, init?: RequestInit) => {
    const key = `${(init?.method ?? 'GET').toUpperCase()} ${String(input)}`;
    const route = routes[key];
    if (!route) throw new Error(`No stub for ${key}`);
    return route(init);
  };
}
