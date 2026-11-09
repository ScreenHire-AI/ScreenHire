import type { CurrentUser } from '@screenhire/api-client';
import {
  createGetOrganization,
  createGetOrganizationBranding,
  createGetOrganizationSendingDomain,
  createListInvitations,
  createListJobs,
  createListTeamMembers,
} from '@screenhire/api-client/svelte-query';
import { can, seesAllJobs } from '#lib/permissions.js';
import { preferences } from '#lib/preferences.svelte.js';
import { setupSteps, showSetup } from '#lib/setup-checklist.js';

/**
 * Where a new organization is in setting up, for the notification bell (design system section 11, Setup checklist): the
 * steps, each ticking itself from the organization's own data. Call it while a component initializes, because it creates
 * queries. It is for whoever can do the steps and see every job (Manage the team, Edit settings and all jobs: Owners and
 * Admins), because a narrower job list could leave the last step open after a job is published. `steps` is undefined until
 * every answer is in, so a finished step never flashes open. Dismiss hides the setup in this browser for the organization,
 * and it goes by itself once the last step is done.
 */
export function createSetupProgress(user: () => CurrentUser | null | undefined) {
  const organizationId = $derived(user()?.organization?.id ?? '');
  const mayUse = $derived(can(user(), 'ManageTeam') && can(user(), 'EditSettings') && seesAllJobs(user()));
  const dismissed = $derived(organizationId !== '' && preferences.isSetupDismissed(organizationId));
  // Nobody who dismissed it, or cannot do the steps, pays for the questions.
  const enabled = $derived(mayUse && !dismissed);
  const options = () => ({ query: { enabled, staleTime: 60_000 } });

  const members = createListTeamMembers(options);
  const invitations = createListInvitations(options);
  const branding = createGetOrganizationBranding(options);
  const organization = createGetOrganization(options);
  const sendingDomain = createGetOrganizationSendingDomain(options);
  const jobs = createListJobs(options);

  const steps = $derived(
    enabled
      ? setupSteps({
          members: members.data?.data,
          invitations: invitations.data?.data,
          branding: branding.data?.data,
          organization: organization.data?.data,
          sendingDomain: sendingDomain.data?.data,
          jobs: jobs.data?.data,
        })
      : undefined,
  );
  const visible = $derived(steps !== undefined && showSetup(steps, dismissed));

  return {
    /** Every step, done or not, once all the answers are in; undefined while any is loading, or when the person has no setup to see. */
    get steps() {
      return visible ? steps : undefined;
    },
    /** The steps still to do. */
    get remaining() {
      return visible && steps ? steps.filter((step) => !step.done) : [];
    },
    /** How many steps are done. */
    get doneCount() {
      return visible && steps ? steps.filter((step) => step.done).length : 0;
    },
    dismiss() {
      if (organizationId !== '') preferences.dismissSetup(organizationId);
    },
  };
}
