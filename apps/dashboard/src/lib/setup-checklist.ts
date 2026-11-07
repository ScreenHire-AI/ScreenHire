import type { JobSummary, OrganizationBranding, OrganizationProfile, OrganizationSendingDomain, PendingInvitation, TeamMember } from '@screenhire/api-client';

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
 * The checklist for a new organization, in the order a team does it. Each step ticks from the organization's own data,
 * never from a click: a teammate or an invitation, any branding set, an edit to the careers site, a verified email
 * domain, a published job. The email domain step is left out where this installation cannot send from a company's own
 * domain (design system A3). `undefined` while any answer is still loading, so the panel never flashes steps that are
 * already done.
 */
export function setupSteps(inputs: SetupInputs): SetupStep[] | undefined {
  const { members, invitations, branding, organization, sendingDomain, jobs } = inputs;
  if (!members || !invitations || !branding || !organization || !sendingDomain || !jobs) return undefined;

  const steps: SetupStep[] = [
    { id: 'team', label: 'Invite your team', href: '/settings/team', done: members.length > 1 || invitations.length > 0 },
    {
      id: 'branding',
      label: 'Set your branding',
      href: '/settings/branding',
      done: [branding.logo, branding.favicon, branding.sharingImage, branding.brandColor, branding.secondColor, branding.headingFont].some(
        (value) => value !== null && value !== undefined,
      ),
    },
    {
      id: 'careers',
      label: 'Set up your careers site',
      href: '/settings/careers-site',
      // The careers site starts from ScreenHire's defaults. The organization's change history records a renaming, the
      // questions per page and the sites that may embed it (SettingsChanges.Name), so any recorded change means someone
      // has been through the careers settings; the history of a new organization holds only its creation.
      done: organization.embedOrigins.length > 0 || organization.lastUpdated.created === false,
    },
  ];
  if (sendingDomain.available) {
    steps.push({ id: 'email', label: 'Add your email domain', href: '/settings/sending-domain', done: sendingDomain.domain?.status === 'Verified' });
  }
  steps.push({ id: 'job', label: 'Publish your first job', href: '/jobs/new', done: jobs.some((job) => job.publishedAt !== null) });
  return steps;
}

/** Whether the panel shows: every answer is in, something is left to do, and nobody dismissed it. */
export const showSetup = (steps: readonly SetupStep[] | undefined, dismissed: boolean) =>
  !dismissed && steps !== undefined && steps.some((step) => !step.done);
