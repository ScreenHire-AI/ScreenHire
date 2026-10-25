import type {
  ApplicationSummary,
  BoardSummary,
  CurrentUser,
  JobPostingStatus,
  JobSummary,
  OrganizationRole,
  Permission,
  RoleCatalogue,
  WorkflowStage,
} from '@screenhire/api-client';

/** Fictional data for component tests, shaped like the API's responses. */

/** Every permission an Owner has, in catalogue order, as `GET /v1/auth/me` lists them. */
const recruiterPermissions: Permission[] = [
  'ViewHiring', 'EditJobs', 'AddCandidates', 'MoveCandidates', 'AddNotesAndTags', 'CompleteTasks', 'EmailCandidates',
  'FillInScorecards', 'ReviewIntegrity', 'RunAutomations', 'EditHiringTemplates',
];

export const ownerPermissions: Permission[] = [
  'ViewHiring', 'EditJobs', 'AddCandidates', 'MoveCandidates', 'AddNotesAndTags', 'DeleteAnyNote', 'CompleteTasks',
  'EmailCandidates', 'FillInScorecards', 'ReviewIntegrity', 'RunAutomations', 'EditHiringTemplates', 'EditEmailTemplates',
  'EditSettings', 'ManageApiKeys', 'ManageWebhooks', 'ManageTeam', 'ManageOwners',
];

/** What each built-in role has, as the API's catalogue says (RolePermissions.cs). */
export const builtInPermissions: Record<Exclude<OrganizationRole, 'Custom'>, Permission[]> = {
  Owner: ownerPermissions,
  Admin: ownerPermissions.filter((permission) => permission !== 'ManageOwners'),
  Recruiter: recruiterPermissions,
  Viewer: ['ViewHiring'],
};

export const user: CurrentUser = {
  id: '01a157d4-0000-7000-8000-0000000000aa',
  email: 'avery@brightwater.example.com',
  displayName: 'Avery Quinn',
  organization: {
    id: '01a157d4-0000-7000-8000-0000000000bb',
    name: 'Brightwater Studio',
    slug: 'brightwater',
    role: 'Owner',
    customRoleId: null,
    roleName: 'Owner',
    permissions: ownerPermissions,
    seesAllJobs: true,
  },
};

/** `user` signed in with a built-in role instead, with that role's permissions. */
export const userAs = (role: Exclude<OrganizationRole, 'Custom'>): CurrentUser => ({
  ...user,
  organization: { ...user.organization!, role, roleName: role, customRoleId: null, permissions: builtInPermissions[role] },
});

/** The custom role the fixtures use: an Interviewer copied from Viewer, who notes, tags and fills in scorecards. */
export const interviewerRoleId = '01a157d4-0000-7000-8000-0000000000cc';

/** `user` signed in with the Interviewer role. */
export const interviewer: CurrentUser = {
  ...user,
  organization: {
    ...user.organization!,
    role: 'Custom',
    customRoleId: interviewerRoleId,
    roleName: 'Interviewer',
    permissions: ['ViewHiring', 'AddNotesAndTags', 'FillInScorecards'],
  },
};

const byScreenHire = { at: null, by: 'ScreenHire' } as const;

/** Part of the API's permission catalogue (GET /v1/organization/roles): every built-in role, the Interviewer, five permissions. */
export const roleCatalogue: RoleCatalogue = {
  roles: [
    { role: 'Owner', customRoleId: null, name: 'Owner', description: 'Everything, including making and removing other Owners.', permissions: ['ViewHiring', 'MoveCandidates', 'FillInScorecards', 'ManageTeam', 'ManageOwners'], people: 2, lastUpdated: byScreenHire },
    { role: 'Admin', customRoleId: null, name: 'Admin', description: 'Everything a Recruiter does, plus settings and the team.', permissions: ['ViewHiring', 'MoveCandidates', 'FillInScorecards', 'ManageTeam'], people: 1, lastUpdated: byScreenHire },
    { role: 'Recruiter', customRoleId: null, name: 'Recruiter', description: 'Works the jobs.', permissions: ['ViewHiring', 'MoveCandidates', 'FillInScorecards'], people: 1, lastUpdated: byScreenHire },
    { role: 'Viewer', customRoleId: null, name: 'Viewer', description: 'Sees jobs and candidates and changes nothing.', permissions: ['ViewHiring'], people: 0, lastUpdated: byScreenHire },
    {
      role: 'Custom',
      customRoleId: interviewerRoleId,
      name: 'Interviewer',
      description: 'See jobs and candidates and fill in scorecards.',
      permissions: ['ViewHiring', 'FillInScorecards'],
      people: 1,
      lastUpdated: { at: '2026-10-25T09:00:00Z', by: 'Member', userId: user.id, apiKeyId: null },
    },
  ],
  permissions: [
    { permission: 'ViewHiring', group: 'Hiring', name: 'See jobs and candidates', description: 'Jobs, candidates, their files, scorecards and integrity results.', ownerOnly: false, everyRole: true },
    { permission: 'MoveCandidates', group: 'Hiring', name: 'Move candidates', description: 'Move a candidate to another stage.', ownerOnly: false, everyRole: false },
    { permission: 'FillInScorecards', group: 'Hiring', name: 'Fill in scorecards', description: 'Score a candidate at a stage.', ownerOnly: false, everyRole: false },
    { permission: 'ManageTeam', group: 'Organization', name: 'Manage the team', description: 'Invite people, change roles and remove members, except Owners, and make roles.', ownerOnly: false, everyRole: false },
    { permission: 'ManageOwners', group: 'Organization', name: 'Manage Owners', description: 'Make someone an Owner, or change or remove an Owner.', ownerOnly: true, everyRole: false },
  ],
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
    lastUpdated: { at: '2026-10-19T09:00:00Z', by: 'Member', userId: user.id },
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
    lastUpdated: { at: '2026-10-18T12:00:00Z', by: 'ScreenHire' },
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
    tags: index === 1 ? ['Referral'] : [],
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
