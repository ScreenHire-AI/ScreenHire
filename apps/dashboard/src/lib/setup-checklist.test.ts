import type { OrganizationBranding, OrganizationSendingDomain, OrganizationSetup } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { jobs } from '../test-support/fixtures.js';
import { isOpen, setupSteps, showSetup, type SetupInputs } from './setup-checklist.js';

const noBranding = {
  brandColor: null,
  secondColor: null,
  headingFont: null,
  logo: null,
  favicon: null,
  sharingImage: null,
  lastUpdated: { at: null, by: 'ScreenHire' },
} as unknown as OrganizationBranding;
const noDomain = { available: true, sandbox: false, domain: null } as OrganizationSendingDomain;
const withDomain = (status: 'Pending' | 'Verified') => ({ available: true, sandbox: false, domain: { status } }) as OrganizationSendingDomain;
const published = { ...jobs[0]!, publishedAt: '2026-11-03T09:00:00Z' };
const nothingSkipped: OrganizationSetup = { hidden: false, skippedSteps: [] };

const fresh: SetupInputs = {
  members: [{ id: 'a' }] as unknown as SetupInputs['members'],
  invitations: [],
  branding: noBranding,
  sendingDomain: noDomain,
  jobs: jobs.map((job) => ({ ...job, publishedAt: null })),
  setup: nothingSkipped,
  interviewing: { calendarConnected: false, hasHours: false },
};

const done = (inputs: SetupInputs) => Object.fromEntries((setupSteps(inputs) ?? []).map((step) => [step.id, step.done]));

describe('the setup checklist', () => {
  it('asks an organization that interviews to connect a calendar or set hours, done by either', () => {
    const interviewing = { ...fresh, setup: { ...nothingSkipped, hasInterviewStage: true } };

    expect(setupSteps(fresh)!.map((step) => step.id)).not.toContain('Interviewing');
    expect(setupSteps(interviewing)!.find((step) => step.id === 'Interviewing')).toMatchObject({
      label: 'Connect your calendar or set your hours',
      href: '/account/connected',
      done: false,
    });
    expect(done({ ...interviewing, interviewing: { calendarConnected: false, hasHours: true } }).Interviewing).toBe(true);
    expect(done({ ...interviewing, interviewing: { calendarConnected: true, hasHours: false } }).Interviewing).toBe(true);
    expect(setupSteps({ ...interviewing, interviewing: undefined })).toBeUndefined();
  });

  it('lists the four steps in the order a team does them, each linking to its page, all open for a new organization', () => {
    const steps = setupSteps(fresh)!;

    // What candidates see, the first job, the team, then the settings that can wait. The DNS step is last. No step is for
    // the careers page: a team that keeps its defaults could never finish the list.
    expect(steps.map((step) => [step.label, step.href, step.done])).toEqual([
      ['Set your branding', '/settings/branding', false],
      ['Publish your first job', '/jobs/new', false],
      ['Invite your team', '/settings/team', false],
      ['Send email from your own domain', '/settings/sending-domain', false],
    ]);
  });

  it('ticks the team step for a second member or a pending invitation', () => {
    expect(done({ ...fresh, members: [{ id: 'a' }, { id: 'b' }] as unknown as SetupInputs['members'] }).Team).toBe(true);
    expect(done({ ...fresh, invitations: [{ id: 'i' }] as unknown as SetupInputs['invitations'] }).Team).toBe(true);
  });

  it('ticks the branding step once any of the logo, the icon, the sharing image, the colours or the font is set', () => {
    for (const patch of [
      { brandColor: 'teal' },
      { secondColor: 'plum' },
      { headingFont: { id: 'fraunces', family: 'Fraunces' } },
      { logo: { id: 'l' } },
      { favicon: { id: 'f' } },
      { sharingImage: { id: 's' } },
    ]) {
      expect(done({ ...fresh, branding: { ...noBranding, ...patch } as unknown as OrganizationBranding }).Branding).toBe(true);
    }
  });

  it('ticks the email step only for a verified domain, and leaves the step out where this installation cannot send (A3)', () => {
    expect(done({ ...fresh, sendingDomain: withDomain('Pending') }).Email).toBe(false);
    expect(done({ ...fresh, sendingDomain: withDomain('Verified') }).Email).toBe(true);
    expect(setupSteps({ ...fresh, sendingDomain: { ...noDomain, available: false } })!.map((step) => step.id)).toEqual(['Branding', 'Job', 'Team']);
  });

  it('ticks the job step for a job that was published, not for a draft', () => {
    expect(done({ ...fresh, jobs: [published] }).Job).toBe(true);
    expect(done(fresh).Job).toBe(false);
  });

  it('waits for every answer, so a finished step never flashes open', () => {
    for (const missing of ['members', 'invitations', 'branding', 'sendingDomain', 'jobs', 'setup'] as const) {
      expect(setupSteps({ ...fresh, [missing]: undefined })).toBeUndefined();
    }
  });

  it("marks a step the organization said it does not need as skipped, in its place, and never a step that is done", () => {
    const steps = setupSteps({ ...fresh, members: [{ id: 'a' }, { id: 'b' }] as unknown as SetupInputs['members'], setup: { hidden: false, skippedSteps: ['Email', 'Team'] } })!;

    // The skipped step keeps its place, so the steps after it keep their numbers in the bell.
    expect(steps.map((step) => [step.id, step.skipped, isOpen(step)])).toEqual([
      ['Branding', false, true],
      ['Job', false, true],
      ['Team', false, false], // done before anyone skipped it: done wins, so it counts toward "steps done"
      ['Email', true, false],
    ]);
  });

  it('goes once every step left is done or skipped, and while someone hid it for the organization', () => {
    const allSkipped = setupSteps({ ...fresh, setup: { hidden: false, skippedSteps: ['Branding', 'Job', 'Team', 'Email'] } });
    const oneLeft = setupSteps({ ...fresh, setup: { hidden: false, skippedSteps: ['Branding', 'Job', 'Team'] } });

    expect(showSetup(allSkipped, false)).toBe(false);
    expect(showSetup(oneLeft, false)).toBe(true);
    expect(showSetup(oneLeft, true)).toBe(false);
  });

  it('shows while a step is open and nobody hid it, and goes once everything is done', () => {
    const open = setupSteps(fresh);
    const finished = setupSteps({
      members: [{ id: 'a' }, { id: 'b' }] as unknown as SetupInputs['members'],
      invitations: [],
      branding: { ...noBranding, brandColor: 'teal' } as unknown as OrganizationBranding,
      sendingDomain: withDomain('Verified'),
      jobs: [published],
      setup: nothingSkipped,
      interviewing: { calendarConnected: false, hasHours: false },
    });

    expect(showSetup(open, false)).toBe(true);
    expect(showSetup(open, true)).toBe(false);
    expect(showSetup(undefined, false)).toBe(false);
    expect(showSetup(finished, false)).toBe(false);
  });
});
