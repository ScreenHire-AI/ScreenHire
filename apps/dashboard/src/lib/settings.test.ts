import type { CurrentUser, OrganizationRole } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { builtInPermissions, interviewer, interviewerRoleId, roleCatalogue, userAs } from '../test-support/fixtures.js';
import { assignableRoles, mayAssign, mayGiveJobAccess, roleFromKey, roleKey, seesAllJobsWith, settingsSectionOf, visibleSettingsSections } from './settings.js';

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

describe('visibleSettingsSections', () => {
  const labels = (user: CurrentUser | null) => visibleSettingsSections(user).map((section) => section.label);

  it('shows Owners and Admins every page, and email templates on the Templates page instead', () => {
    expect(labels(userAs('Owner'))).toHaveLength(8);
    expect(labels(userAs('Admin'))).toHaveLength(8);
    expect(labels(userAs('Owner'))).not.toContain('Email templates');
  });

  it('shows Recruiters, Viewers and an Interviewer none', () => {
    expect(labels(userAs('Recruiter'))).toEqual([]);
    expect(labels(userAs('Viewer'))).toEqual([]);
    expect(labels(interviewer)).toEqual([]);
    expect(labels(null)).toEqual([]);
  });

  it('shows a custom role the pages its permissions open', () => {
    const keeper: CurrentUser = { ...interviewer, organization: { ...interviewer.organization!, permissions: ['ViewHiring', 'ManageApiKeys'] } };
    expect(labels(keeper)).toEqual(['API keys', 'MCP server']);
  });

  it('finds the page a path belongs to', () => {
    expect(settingsSectionOf('/settings/roles/compare')?.label).toBe('Roles');
    expect(settingsSectionOf('/settings/team')?.label).toBe('Team');
    expect(settingsSectionOf('/settings')).toBeUndefined();
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
});
