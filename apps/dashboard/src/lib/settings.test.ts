import type { CurrentUser, OrganizationRole, Permission } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { builtInPermissions, interviewer, interviewerRoleId, roleCatalogue, userAs } from '../test-support/fixtures.js';
import {
  assignableRoles,
  mayAssign,
  mayGiveJobAccess,
  roleFromKey,
  roleKey,
  integrationsHref,
  seesAllJobsWith,
  settingsAreaOf,
  settingsSectionOf,
  featureOn,
  featureWaits,
  slackFeature,
  visibleIntegrations,
  visibleSettingsAreas,
  visibleSettingsSections,
} from './settings.js';

// The expectations follow TeamPolicy.cs (src/ScreenHire.Domain/Organizations): someone with Manage the team gives a role
// only when it holds nothing their own role does not. For the built-in roles that is Owners managing everyone, Admins
// everyone but Owners, and Recruiters and Viewers nobody.

type BuiltIn = Exclude<OrganizationRole, 'Custom'>;
const builtIns: BuiltIn[] = ['Owner', 'Admin', 'Recruiter', 'Viewer'];

describe('mayAssign', () => {
  const table: Record<BuiltIn, Record<BuiltIn, boolean>> = {
    Owner: { Owner: true, Admin: true, Recruiter: true, Viewer: true },
    Admin: { Owner: false, Admin: true, Recruiter: true, Viewer: true },
    Recruiter: { Owner: false, Admin: false, Recruiter: false, Viewer: false },
    Viewer: { Owner: false, Admin: false, Recruiter: false, Viewer: false },
  };

  for (const actor of builtIns) {
    for (const role of builtIns) {
      it(`${table[actor][role] ? 'lets' : 'does not let'} ${actor === 'Admin' ? 'an' : 'a'} ${actor} give the ${role} role`, () => {
        expect(mayAssign(builtInPermissions[actor], builtInPermissions[role])).toBe(table[actor][role]);
      });
    }
  }

  it('lets a custom role that manages the team give only what it has', () => {
    const lead = ['ViewHiring', 'AddNotesAndTags', 'ManageTeam'] as const;
    expect(mayAssign(lead, ['ViewHiring', 'AddNotesAndTags'])).toBe(true);
    expect(mayAssign(lead, ['ViewHiring', 'FillInScorecards'])).toBe(false);
    expect(mayAssign(lead, builtInPermissions.Recruiter)).toBe(false);
  });
});

describe('assignableRoles', () => {
  const names = (actor: BuiltIn) => assignableRoles(builtInPermissions[actor], roleCatalogue.roles).map((role) => role.name);

  it('offers an Owner every role, built-in ones by access first', () => {
    expect(names('Owner')).toEqual(['Owner', 'Admin', 'Recruiter', 'Viewer', 'Interviewer']);
  });

  it('never offers an Admin the Owner role', () => {
    expect(names('Admin')).toEqual(['Admin', 'Recruiter', 'Viewer', 'Interviewer']);
  });

  it('offers a Recruiter or a Viewer nothing', () => {
    expect(names('Recruiter')).toEqual([]);
    expect(names('Viewer')).toEqual([]);
  });
});

describe('role keys', () => {
  it('names a built-in role in lower case and a custom role by its id', () => {
    expect(roleKey({ role: 'Viewer' })).toBe('viewer');
    expect(roleKey({ role: 'Custom', customRoleId: interviewerRoleId })).toBe(interviewerRoleId);
    expect(roleFromKey(roleCatalogue.roles, interviewerRoleId)).toEqual({ role: 'Custom', customRoleId: interviewerRoleId });
    expect(roleFromKey(roleCatalogue.roles, 'admin')).toEqual({ role: 'Admin', customRoleId: null });
    expect(roleFromKey(roleCatalogue.roles, 'nobody')).toBeUndefined();
  });
});

describe('slackFeature', () => {
  it('waits for the first answer', () => {
    expect(slackFeature(undefined, false)).toBeUndefined();
  });

  it('counts a status that was never read as no Slack', () => {
    expect(slackFeature(undefined, true)).toBe(false);
  });

  it('keeps the last answer through a failed refetch', () => {
    expect(slackFeature({ available: true }, true)).toBe(true);
    expect(slackFeature({ available: false }, true)).toBe(false);
  });
});

describe('featureOn', () => {
  it('shows the page until the shell hears that this installation cannot send from a domain, and waits for no answer', () => {
    expect(featureOn({}, 'sendingDomain')).toBe(true);
    expect(featureWaits({}, 'sendingDomain')).toBe(false);
    expect(featureOn({ sendingDomain: false }, 'sendingDomain')).toBe(false);
    expect(featureOn({ sendingDomain: true }, 'sendingDomain')).toBe(true);
    // A failed query is not an answer: the entry stays and the page reports the failure.
    expect(featureOn({ sendingDomain: undefined }, 'sendingDomain')).toBe(true);
    // Slack is the other way round: off until it is heard to be on.
    expect(featureOn({}, 'slack')).toBe(false);
    expect(featureWaits({}, 'slack')).toBe(true);
  });
});

describe('visibleSettingsSections', () => {
  const labels = (user: CurrentUser | null) => visibleSettingsSections(user).map((section) => section.label);

  it('shows Owners and Admins every page, and email templates on the Templates page instead', () => {
    const withSlack = (user: CurrentUser) => visibleSettingsSections(user, { slack: true });
    expect(withSlack(userAs('Owner'))).toHaveLength(12);
    expect(withSlack(userAs('Admin'))).toHaveLength(12);
    expect(labels(userAs('Owner'))).not.toContain('Email templates');
  });

  it('hides Slack, page and menu entry, until the installation has a Slack app (A3)', () => {
    const owner = userAs('Owner');

    expect(labels(owner)).not.toContain('Slack');
    expect(labels(owner)).toHaveLength(11);
    expect(visibleSettingsSections(owner, { slack: false }).map((section) => section.label)).not.toContain('Slack');
    expect(visibleSettingsSections(owner, { slack: true }).map((section) => section.label)).toContain('Slack');
  });

  it('hides Email domain, page and menu entry, where the installation has no email provider (A3)', () => {
    const owner = userAs('Owner');

    expect(labels(owner)).toContain('Email domain');
    expect(visibleSettingsSections(owner, { sendingDomain: false }).map((section) => section.label)).not.toContain('Email domain');
  });

  it('shows Recruiters, Viewers and an Interviewer none', () => {
    expect(labels(userAs('Recruiter'))).toEqual([]);
    expect(labels(userAs('Viewer'))).toEqual([]);
    expect(labels(interviewer)).toEqual([]);
    expect(labels(null)).toEqual([]);
  });

  it('shows a custom role the pages its permissions open', () => {
    const keeper: CurrentUser = { ...interviewer, organization: { ...interviewer.organization!, permissions: ['ViewHiring', 'ManageApiKeys'] } };
    expect(labels(keeper)).toEqual(['MCP server', 'API keys']);
  });

  it('finds the page a path belongs to', () => {
    expect(settingsSectionOf('/settings/roles/compare')?.label).toBe('Roles');
    expect(settingsSectionOf('/settings/team')?.label).toBe('Team');
    expect(settingsSectionOf('/settings')).toBeUndefined();
  });
});

describe('settings areas', () => {
  const areas = (user: CurrentUser | null, features = { slack: true }) => visibleSettingsAreas(user, features).map((area) => [area.label, area.href]);
  const holding = (...permissions: Permission[]): CurrentUser => ({
    ...interviewer,
    organization: { ...interviewer.organization!, permissions: ['ViewHiring', ...permissions], seesAllJobs: true },
  });

  it('gives an Owner three items, each opening the first page of its group, and Integrations its directory', () => {
    expect(areas(userAs('Owner'))).toEqual([
      ['Organization', '/settings/organization'],
      ['Hiring setup', '/settings/careers-site'],
      ['Integrations', integrationsHref],
    ]);
  });

  it('shows an item only for a role with a permission for at least one page in it', () => {
    expect(areas(holding('ManageWebhooks'))).toEqual([['Integrations', integrationsHref]]);
    expect(areas(holding('ManageTeam'))).toEqual([['Organization', '/settings/team']]);
    expect(areas(holding('EditSettings'))).toEqual([
      ['Organization', '/settings/organization'],
      ['Hiring setup', '/settings/careers-site'],
    ]);
    expect(areas(userAs('Recruiter'))).toEqual([]);
    expect(areas(null)).toEqual([]);
  });

  it('leaves Integrations out of a role that sees only its own jobs, whose integrations all reach every job', () => {
    const own: CurrentUser = { ...userAs('Owner'), organization: { ...userAs('Owner').organization!, seesAllJobs: false } };

    expect(areas(own).map(([label]) => label)).not.toContain('Integrations');
  });

  it('puts a path in its area, and the directory in Integrations', () => {
    expect(settingsAreaOf('/settings/roles/compare')).toBe('Organization');
    expect(settingsAreaOf('/settings/branding')).toBe('Hiring setup');
    expect(settingsAreaOf('/settings/webhooks/new')).toBe('Integrations');
    expect(settingsAreaOf(integrationsHref)).toBe('Integrations');
    expect(settingsAreaOf('/settings')).toBeUndefined();
  });

  it('lists the integrations a person may open, without Slack where there is no Slack app', () => {
    const labels = (features = {}) => visibleIntegrations(userAs('Owner'), features).map((integration) => integration.label);

    expect(labels({ slack: true })).toEqual(['Slack', 'MCP server', 'Webhooks', 'API keys']);
    expect(labels({ slack: false })).toEqual(['MCP server', 'Webhooks', 'API keys']);
    expect(visibleIntegrations(userAs('Recruiter'))).toEqual([]);
  });
});

// TeamPolicy.MayGiveJobAccess and JobAccessRules.SeesAllJobs (ADR-0015).
describe('job access', () => {
  it('has Owners and Admins see every job whatever they were set to', () => {
    expect(seesAllJobsWith('Owner', 'HiringTeamOnly')).toBe(true);
    expect(seesAllJobsWith('Admin', 'HiringTeamOnly')).toBe(true);
    expect(seesAllJobsWith('Recruiter', 'HiringTeamOnly')).toBe(false);
    expect(seesAllJobsWith('Custom', 'All')).toBe(true);
  });

  it('lets someone on their own jobs give only that, never All jobs or a role that sees them all', () => {
    expect(mayGiveJobAccess(false, 'Viewer', 'HiringTeamOnly')).toBe(true);
    expect(mayGiveJobAccess(false, 'Viewer', 'All')).toBe(false);
    expect(mayGiveJobAccess(false, 'Admin', 'HiringTeamOnly')).toBe(false);
    expect(mayGiveJobAccess(true, 'Admin', 'All')).toBe(true);
  });

  it('leaves API keys and webhooks out of Settings for someone on their own jobs', () => {
    const owner = userAs('Owner');
    const scoped: CurrentUser = { ...owner, organization: { ...owner.organization!, seesAllJobs: false } };

    expect(visibleSettingsSections(owner).map((section) => section.label)).toContain('API keys');
    expect(visibleSettingsSections(scoped).map((section) => section.label)).not.toContain('API keys');
    expect(visibleSettingsSections(scoped).map((section) => section.label)).not.toContain('Webhooks');
  });

  it('puts Data requests after Roles for anyone who may erase candidates and sees every job, and for no one else', () => {
    const owner = userAs('Owner');
    const scoped: CurrentUser = { ...owner, organization: { ...owner.organization!, seesAllJobs: false } };
    const organization = (user: CurrentUser) => visibleSettingsSections(user).filter((section) => section.group === 'Organization').map((section) => section.label);

    expect(organization(owner)).toEqual(['General', 'Team', 'Roles', 'Data requests']);
    expect(organization(userAs('Admin'))).toContain('Data requests');
    expect(organization(scoped)).not.toContain('Data requests');
    expect(organization(userAs('Recruiter'))).toEqual([]);
    expect(organization(userAs('Viewer'))).toEqual([]);
    expect(settingsSectionOf('/settings/data-requests')).toMatchObject({ label: 'Data requests', permission: 'EraseCandidates', allJobs: true });
  });
});
