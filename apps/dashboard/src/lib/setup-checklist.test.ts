import type { OrganizationBranding, OrganizationProfile, OrganizationSendingDomain } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { jobs } from '../test-support/fixtures.js';
import { setupSteps, showSetup, type SetupInputs } from './setup-checklist.js';

const noBranding = {
  brandColor: null,
  secondColor: null,
  headingFont: null,
  logo: null,
  favicon: null,
  sharingImage: null,
  lastUpdated: { at: null, by: 'ScreenHire' },
} as unknown as OrganizationBranding;
const newOrganization = {
  id: '01a157d4-0000-7000-8000-0000000000bb',
  name: 'Brightwater Studio',
  slug: 'brightwater',
  careersUrl: 'https://careers.example.com/brightwater',
  createdAt: '2026-11-01T09:00:00Z',
  lastUpdated: { at: '2026-11-01T09:00:00Z', by: null, created: true },
  questionsPerPage: 3,
  embedOrigins: [],
} as unknown as OrganizationProfile;
const noDomain = { available: true, sandbox: false, domain: null } as OrganizationSendingDomain;
const withDomain = (status: 'Pending' | 'Verified') => ({ available: true, sandbox: false, domain: { status } }) as OrganizationSendingDomain;
const published = { ...jobs[0]!, publishedAt: '2026-11-03T09:00:00Z' };

const fresh: SetupInputs = {
  members: [{ id: 'a' }] as unknown as SetupInputs['members'],
  invitations: [],
  branding: noBranding,
  organization: newOrganization,
  sendingDomain: noDomain,
  jobs: jobs.map((job) => ({ ...job, publishedAt: null })),
};

const done = (inputs: SetupInputs) => Object.fromEntries((setupSteps(inputs) ?? []).map((step) => [step.id, step.done]));

describe('the setup checklist', () => {
  it('lists the five steps in order, each linking to its page, all open for a new organization', () => {
    const steps = setupSteps(fresh)!;

    expect(steps.map((step) => [step.label, step.href, step.done])).toEqual([
      ['Invite your team', '/settings/team', false],
      ['Set your branding', '/settings/branding', false],
      ['Set up your careers site', '/settings/careers-site', false],
      ['Add your email domain', '/settings/sending-domain', false],
      ['Publish your first job', '/jobs/new', false],
    ]);
  });

  it('ticks the team step for a second member or a pending invitation', () => {
    expect(done({ ...fresh, members: [{ id: 'a' }, { id: 'b' }] as unknown as SetupInputs['members'] }).team).toBe(true);
    expect(done({ ...fresh, invitations: [{ id: 'i' }] as unknown as SetupInputs['invitations'] }).team).toBe(true);
  });

  it('ticks the branding step once any of the logo, the icon, the sharing image, the colours or the font is set', () => {
    for (const patch of [
      { brandColor: 'teal' },
      { secondColor: 'plum' },
      { headingFont: 'Fraunces' },
      { logo: { id: 'l' } },
      { favicon: { id: 'f' } },
      { sharingImage: { id: 's' } },
    ]) {
      expect(done({ ...fresh, branding: { ...noBranding, ...patch } as unknown as OrganizationBranding }).branding).toBe(true);
    }
  });

  it('ticks the careers step once the organization was edited or a site may embed it, not for its creation alone', () => {
    const edited = { ...newOrganization, lastUpdated: { at: '2026-11-02T09:00:00Z', by: 'User', created: false } } as unknown as OrganizationProfile;

    expect(done({ ...fresh, organization: edited }).careers).toBe(true);
    expect(done({ ...fresh, organization: { ...newOrganization, embedOrigins: ['https://www.example.com'] } }).careers).toBe(true);
    expect(done(fresh).careers).toBe(false);
  });

  it('ticks the email step only for a verified domain, and leaves the step out where this installation cannot send (A3)', () => {
    expect(done({ ...fresh, sendingDomain: withDomain('Pending') }).email).toBe(false);
    expect(done({ ...fresh, sendingDomain: withDomain('Verified') }).email).toBe(true);
    expect(setupSteps({ ...fresh, sendingDomain: { ...noDomain, available: false } })!.map((step) => step.id)).toEqual(['team', 'branding', 'careers', 'job']);
  });

  it('ticks the job step for a job that was published, not for a draft', () => {
    expect(done({ ...fresh, jobs: [published] }).job).toBe(true);
    expect(done(fresh).job).toBe(false);
  });

  it('waits for every answer, so a finished step never flashes open', () => {
    for (const missing of ['members', 'invitations', 'branding', 'organization', 'sendingDomain', 'jobs'] as const) {
      expect(setupSteps({ ...fresh, [missing]: undefined })).toBeUndefined();
    }
  });

  it('shows while a step is open and nobody dismissed it, and goes once everything is done', () => {
    const open = setupSteps(fresh);
    const finished = setupSteps({
      members: [{ id: 'a' }, { id: 'b' }] as unknown as SetupInputs['members'],
      invitations: [],
      branding: { ...noBranding, brandColor: 'teal' } as unknown as OrganizationBranding,
      organization: { ...newOrganization, embedOrigins: ['https://www.example.com'] },
      sendingDomain: withDomain('Verified'),
      jobs: [published],
    });

    expect(showSetup(open, false)).toBe(true);
    expect(showSetup(open, true)).toBe(false);
    expect(showSetup(undefined, false)).toBe(false);
    expect(showSetup(finished, false)).toBe(false);
  });
});
