import type { CurrentUser, OrganizationRole, Permission } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { builtInPermissions, interviewer, interviewerRoleId, roleCatalogue, userAs } from '../test-support/fixtures.js';
import {
  assignableRoles,
  billingFeature,
  mayAssign,
  mayGiveJobAccess,
  roleFromKey,
  roleKey,
  matchesWords,
  searchSettings,
  seesAllJobsWith,
  settingsCrumbs,
  settingsGroupOf,
  settingsGroups,
  settingsHome,
  settingsSearchText,
  settingsSectionOf,
  featureOn,
  featureWaits,
  slackFeature,
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

  it('shows Owners and Admins every page, with email templates on the Templates page and Verification in the sidebar instead', () => {
    const withSlack = (user: CurrentUser) => visibleSettingsSections(user, { slack: true });
    expect(withSlack(userAs('Owner'))).toHaveLength(11);
    expect(withSlack(userAs('Admin'))).toHaveLength(11);
    expect(labels(userAs('Owner'))).not.toContain('Email templates');
    expect(labels(userAs('Owner'))).not.toContain('Verification');
  });

  it('hides Slack, page and menu entry, until the installation has a Slack app (A3)', () => {
    const owner = userAs('Owner');

    expect(labels(owner)).not.toContain('Slack');
    expect(labels(owner)).toHaveLength(10);
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

describe('settings groups', () => {
  const groups = (user: CurrentUser | null, features = { slack: true }) =>
    settingsGroups(visibleSettingsSections(user, features)).map(({ group, sections }) => [group, sections.map((section) => section.label)]);
  const holding = (...permissions: Permission[]): CurrentUser => ({
    ...interviewer,
    organization: { ...interviewer.organization!, permissions: ['ViewHiring', ...permissions], seesAllJobs: true },
  });

  it('gives an Owner three groups, with Email domain under Organization and Branding under Careers site', () => {
    expect(groups(userAs('Owner'))).toEqual([
      ['Organization', ['General', 'Team', 'Roles', 'Email domain', 'Data requests']],
      ['Careers site', ['Careers page', 'Branding']],
      ['Integrations', ['Slack', 'MCP server', 'Webhooks', 'API keys']],
    ]);
  });

  it('leaves out a setting the role cannot use, and a group with none left (A3)', () => {
    expect(groups(holding('ManageWebhooks'))).toEqual([['Integrations', ['Webhooks']]]);
    expect(groups(holding('ManageTeam'))).toEqual([['Organization', ['Team', 'Roles']]]);
    expect(groups(holding('EditSettings'))).toEqual([
      ['Organization', ['General', 'Email domain']],
      ['Careers site', ['Careers page', 'Branding']],
    ]);
    expect(groups(userAs('Recruiter'))).toEqual([]);
    expect(groups(null)).toEqual([]);
  });

  it('leaves Integrations out of a role that sees only its own jobs, whose integrations all reach every job', () => {
    const own: CurrentUser = { ...userAs('Owner'), organization: { ...userAs('Owner').organization!, seesAllJobs: false } };

    expect(groups(own).map(([group]) => group)).not.toContain('Integrations');
  });

  it('puts a path in its group, and the Settings home in none', () => {
    expect(settingsGroupOf('/settings/roles/compare')).toBe('Organization');
    expect(settingsGroupOf('/settings/sending-domain')).toBe('Organization');
    expect(settingsGroupOf('/settings/branding')).toBe('Careers site');
    expect(settingsGroupOf('/settings/webhooks/new')).toBe('Integrations');
    expect(settingsGroupOf(settingsHome)).toBeUndefined();
    expect(settingsGroupOf('/settings/integrations')).toBeUndefined();
  });

  it('counts Slack only once it is on, so a Slack-only role has no home until then', () => {
    const slackOnly: CurrentUser = { ...interviewer, organization: { ...interviewer.organization!, permissions: ['ViewHiring', 'ManageIntegrations'] } };

    expect(visibleSettingsSections(slackOnly)).toEqual([]);
    expect(visibleSettingsSections(slackOnly, { slack: true }).map((section) => section.label)).toEqual(['Slack']);
  });
});

describe('Settings search', () => {
  const owner = visibleSettingsSections(userAs('Owner'), { slack: true, billing: true });
  const find = (query: string) => searchSettings(owner, query).map((section) => section.label);

  it('finds a setting by its name, case aside', () => {
    expect(find('team')).toContain('Team');
    expect(find('BRANDING')).toEqual(['Branding']);
  });

  it('finds a setting by the words kept beside it, so "logo" finds Branding and "invoice" finds Billing', () => {
    expect(find('logo')).toEqual(['Branding']);
    expect(find('invoice')).toEqual(['Billing']);
    expect(find('invite')).toEqual(['Team', 'Roles']);
  });

  it('finds a setting by its one line and by its group', () => {
    expect(find('erase')).toContain('Data requests');
    expect(find('integrations')).toEqual(['Slack', 'MCP server', 'Webhooks', 'API keys']);
    expect(find('careers site')).toContain('Careers page');
  });

  it('wants every word typed, and puts a name match before a match in the description', () => {
    expect(find('send email')).toEqual(['Email domain']);
    expect(find('team')[0]).toBe('Team');
  });

  it('is a contains match, never fuzzy, and an empty search finds everything', () => {
    expect(find('tm')).toEqual([]);
    expect(find('zzz')).toEqual([]);
    expect(find('  ')).toHaveLength(owner.length);
  });

  it('only searches what the person may use', () => {
    const recruiter = visibleSettingsSections(userAs('Recruiter'));
    expect(searchSettings(recruiter, 'logo')).toEqual([]);
  });

  it('reads name, description, keywords and group, lower case, for the command palette too', () => {
    const text = settingsSearchText(settingsSectionOf('/settings/branding')!);
    expect(text).toContain('branding');
    expect(text).toContain('logo');
    expect(text).toContain('careers site');
    expect(matchesWords(text, 'Logo careers')).toBe(true);
    expect(matchesWords(text, 'logo invoice')).toBe(false);
  });
});

describe('Settings breadcrumbs', () => {
  it('reads Settings, the group, then the page, which does not link to itself', () => {
    expect(settingsCrumbs('/settings/team')).toEqual([{ label: 'Settings', href: '/settings' }, { label: 'Organization' }, { label: 'Team' }]);
    expect(settingsCrumbs('/settings/sending-domain')).toEqual([{ label: 'Settings', href: '/settings' }, { label: 'Organization' }, { label: 'Email domain' }]);
  });

  it('links the page and adds the record under it', () => {
    expect(settingsCrumbs('/settings/webhooks', { label: 'Add endpoint' })).toEqual([
      { label: 'Settings', href: '/settings' },
      { label: 'Integrations' },
      { label: 'Webhooks', href: '/settings/webhooks' },
      { label: 'Add endpoint' },
    ]);
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

    expect(organization(owner)).toEqual(['General', 'Team', 'Roles', 'Email domain', 'Data requests']);
    expect(organization(userAs('Admin'))).toContain('Data requests');
    expect(organization(scoped)).not.toContain('Data requests');
    expect(organization(userAs('Recruiter'))).toEqual([]);
    expect(organization(userAs('Viewer'))).toEqual([]);
    expect(settingsSectionOf('/settings/data-requests')).toMatchObject({ label: 'Data requests', permission: 'EraseCandidates', allJobs: true });
  });
});

// Billing is the Owner's (ManageBilling) and exists only where a payment provider is set up (A3).
describe('Billing', () => {
  const organization = (user: CurrentUser, features = {}) =>
    visibleSettingsSections(user, features).filter((section) => section.group === 'Organization').map((section) => section.label);

  it('ends the Organization pages for an Owner once the shell has heard that billing is set up, and for no one else', () => {
    expect(organization(userAs('Owner'), { billing: true })).toEqual(['General', 'Team', 'Roles', 'Email domain', 'Data requests', 'Billing']);
    expect(organization(userAs('Admin'), { billing: true })).not.toContain('Billing');
    expect(organization(userAs('Recruiter'), { billing: true })).toEqual([]);
    expect(settingsSectionOf('/settings/billing')).toMatchObject({ label: 'Billing', permission: 'ManageBilling', group: 'Organization', feature: 'billing' });
  });

  it('hides it until the shell has heard, and where there is no payment provider', () => {
    expect(organization(userAs('Owner'))).not.toContain('Billing');
    expect(organization(userAs('Owner'), { billing: false })).not.toContain('Billing');
    expect(featureWaits({}, 'billing')).toBe(true);
    expect(featureOn({}, 'billing')).toBe(false);
  });

  it('keeps the Organization group on the Settings home for an Owner whatever billing says', () => {
    expect(settingsGroups(visibleSettingsSections(userAs('Owner'), { billing: false })).map(({ group }) => group)).toContain('Organization');
  });

  it('reads the feature from the billing answer, and holds the last answer through a failed refetch', () => {
    expect(billingFeature({ available: true }, false)).toBe(true);
    expect(billingFeature({ available: false }, false)).toBe(false);
    expect(billingFeature(undefined, false)).toBeUndefined();
    expect(billingFeature(undefined, true)).toBe(false);
    expect(billingFeature({ available: true }, true)).toBe(true);
  });
});
