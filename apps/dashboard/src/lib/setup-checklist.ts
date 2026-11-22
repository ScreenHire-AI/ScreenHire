import type {
  JobSummary,
  OrganizationBranding,
  OrganizationSendingDomain,
  OrganizationSetup,
  PendingInvitation,
  SetupStepId,
  TeamMember,
} from '@screenhire/api-client';

/** One step of the Set up your organization panel. */
export interface SetupStep {
  /** The server's name for the step, which it keeps when the organization skips it. */
  id: SetupStepId;
  label: string;
  /** Where the step is done: the page the label links to. */
  href: string;
  /** Whether the organization's own data says it is done. */
  done: boolean;
  /**
   * A step a team may rightly never do (sending from its own domain needs a domain and DNS access). It says Optional and
   * never counts toward the bell or "steps left", so a team without a domain is not left with a badge it cannot clear
   * (the owner's walk-through, 2026-11-21).
   */
  optional?: boolean;
  /**
   * The organization said it does not need the step ('Not needed', saved on the server for every Owner and Admin). It
   * leaves the list and never counts, but keeps its place, so the steps after it keep their numbers. Its settings page
   * still does it.
   */
  skipped: boolean;
}

/** Each step's words, in the bell and on Settings, General, which names the steps the organization skipped. */
export const setupStepLabels: Record<SetupStepId, string> = {
  Branding: 'Set your branding',
  Job: 'Publish your first job',
  Team: 'Invite your team',
  Email: 'Send email from your own domain',
  Interviewing: 'Connect your calendar or set your hours',
};

/** The answers the steps read. A missing one (still loading) is `undefined`, and the panel waits for all of them. */
export interface SetupInputs {
  members: readonly TeamMember[] | undefined;
  invitations: readonly PendingInvitation[] | undefined;
  branding: OrganizationBranding | undefined;
  sendingDomain: OrganizationSendingDomain | undefined;
  jobs: readonly JobSummary[] | undefined;
  /** The organization's own choices: the steps it skipped and whether it hid the checklist. */
  setup: OrganizationSetup | undefined;
  /** Whether the person has a calendar connected or their own interview hours set: either lets times be offered from them. */
  interviewing: { calendarConnected: boolean; hasHours: boolean } | undefined;
}

/**
 * The checklist for a new organization, in the order a team does it: what candidates see first, the first job, then the
 * team, then the email domain, which can wait. Each step ticks from the organization's own data, never from a click: any
 * branding set, a published job, a teammate or an invitation, a verified email domain. There is no step for the careers
 * page: its settings have defaults that a team may rightly keep, so it could never tick. The email domain step is left out
 * where this installation cannot send from a company's own domain (design system A3). `undefined` while any answer is
 * still loading, so the panel never flashes steps that are already done.
 */
export function setupSteps(inputs: SetupInputs): SetupStep[] | undefined {
  const { members, invitations, branding, sendingDomain, jobs, setup, interviewing } = inputs;
  if (!members || !invitations || !branding || !sendingDomain || !jobs || !setup) return undefined;
  if (setup.hasInterviewStage && !interviewing) return undefined;

  const steps: Omit<SetupStep, 'skipped'>[] = [
    {
      id: 'Branding',
      label: setupStepLabels.Branding,
      href: '/settings/branding',
      done: [branding.logo, branding.favicon, branding.sharingImage, branding.brandColor, branding.secondColor, branding.headingFont].some(
        (value) => value !== null && value !== undefined,
      ),
    },
    { id: 'Job', label: setupStepLabels.Job, href: '/jobs/new', done: jobs.some((job) => job.publishedAt !== null) },
    { id: 'Team', label: setupStepLabels.Team, href: '/settings/team', done: members.length > 1 || invitations.length > 0 },
  ];
  // Only for an organization that interviews: a job with a stage that holds interviews.
  if (setup.hasInterviewStage && interviewing) {
    steps.push({
      id: 'Interviewing',
      label: setupStepLabels.Interviewing,
      href: '/account/connected',
      done: interviewing.calendarConnected || interviewing.hasHours,
    });
  }
  // Last, because it is DNS work outside ScreenHire and the team can start without it.
  if (sendingDomain.available) {
    steps.push({ id: 'Email', label: setupStepLabels.Email, href: '/settings/sending-domain', done: sendingDomain.domain?.status === 'Verified', optional: true });
  }
  return steps.map((step) => ({ ...step, skipped: !step.done && setup.skippedSteps.includes(step.id) }));
}

/** A step the list offers: not done and not skipped. */
export const isOpen = (step: SetupStep) => !step.done && !step.skipped;

/**
 * Whether the panel shows: every answer is in, something is left to do, and nobody hid the checklist for the organization.
 * Once every step left is done or skipped it goes by itself.
 */
export const showSetup = (steps: readonly SetupStep[] | undefined, hidden: boolean) => !hidden && steps !== undefined && steps.some(isOpen);
