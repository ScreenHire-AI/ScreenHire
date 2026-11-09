import type { JobSummary, OrganizationBranding, OrganizationProfile, OrganizationSendingDomain, PendingInvitation, TeamMember } from '@screenhire/api-client';
import { DEFAULT_QUESTIONS_PER_PAGE } from '@screenhire/ui/questions/model';

/** One step of the Set up your organization panel. */
export interface SetupStep {
  id: 'team' | 'branding' | 'careers' | 'email' | 'job';
  label: string;
  /** Where the step is done: the page the label links to. */
  href: string;
  /** Whether the organization's own data says it is done. */
  done: boolean;
}

/** The answers the steps read. A missing one (still loading) is `undefined`, and the panel waits for all of them. */
export interface SetupInputs {
  members: readonly TeamMember[] | undefined;
  invitations: readonly PendingInvitation[] | undefined;
  branding: OrganizationBranding | undefined;
  organization: OrganizationProfile | undefined;
  sendingDomain: OrganizationSendingDomain | undefined;
  jobs: readonly JobSummary[] | undefined;
}

/**
 * The checklist for a new organization, in the order a team does it: what candidates see first, the first job, then the
 * team, then the settings that can wait. Each step ticks from the organization's own data, never from a click: any
 * branding set, a published job, a teammate or an invitation, a careers page changed from its defaults, a verified email
 * domain. The email domain step is left out where this installation cannot send from a company's own
 * domain (design system A3). `undefined` while any answer is still loading, so the panel never flashes steps that are
 * already done.
 */
export function setupSteps(inputs: SetupInputs): SetupStep[] | undefined {
  const { members, invitations, branding, organization, sendingDomain, jobs } = inputs;
  if (!members || !invitations || !branding || !organization || !sendingDomain || !jobs) return undefined;

  const steps: SetupStep[] = [
    {
      id: 'branding',
      label: 'Set your branding',
      href: '/settings/branding',
      done: [branding.logo, branding.favicon, branding.sharingImage, branding.brandColor, branding.secondColor, branding.headingFont].some(
        (value) => value !== null && value !== undefined,
      ),
    },
    { id: 'job', label: 'Publish your first job', href: '/jobs/new', done: jobs.some((job) => job.publishedAt !== null) },
    { id: 'team', label: 'Invite your team', href: '/settings/team', done: members.length > 1 || invitations.length > 0 },
    {
      id: 'careers',
      label: 'Set up your careers page',
      href: '/settings/careers-site',
      // The careers page's own settings, away from ScreenHire's defaults. Not `lastUpdated`: renaming the organization
      // moves it too. Someone who keeps the defaults has no data to tick, and dismisses the panel instead.
      done: organization.embedOrigins.length > 0 || organization.questionsPerPage !== DEFAULT_QUESTIONS_PER_PAGE,
    },
  ];
  // Last, because it is DNS work outside ScreenHire and the team can start without it.
  if (sendingDomain.available) {
    steps.push({ id: 'email', label: 'Send email from your own domain', href: '/settings/sending-domain', done: sendingDomain.domain?.status === 'Verified' });
  }
  return steps;
}

/** Whether the panel shows: every answer is in, something is left to do, and nobody dismissed it. */
export const showSetup = (steps: readonly SetupStep[] | undefined, dismissed: boolean) =>
  !dismissed && steps !== undefined && steps.some((step) => !step.done);
