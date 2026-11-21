import type { CurrentUser, OrganizationSetup, SetupStepId } from '@screenhire/api-client';
import {
  createGetOrganizationBranding,
  createGetOrganizationSendingDomain,
  createGetOrganizationSetup,
  createHideSetup,
  createListInvitations,
  createListJobs,
  createListTeamMembers,
  createShowSetupAgain,
  createSkipSetupStep,
  getGetOrganizationSetupQueryKey,
} from '@screenhire/api-client/svelte-query';
import { useQueryClient, type QueryClient } from '@tanstack/svelte-query';
import { can, seesAllJobs } from '#lib/permissions.js';
import { invalidatePath } from '#lib/query.js';
import { isOpen, setupSteps, showSetup } from '#lib/setup-checklist.js';

/** The organization's choices about the checklist (`GET /v1/organization/setup`). */
export const setupPath = '/v1/organization/setup';

/**
 * Whether the person has a setup checklist: whoever can do the steps and see every job (Manage the team, Edit settings and
 * all jobs: Owners and Admins), because a narrower job list could leave the last step open after a job is published.
 */
export const mayUseSetup = (user: CurrentUser | null | undefined) => can(user, 'ManageTeam') && can(user, 'EditSettings') && seesAllJobs(user);

/**
 * Puts a change's answer in the cache at once, so the step leaves the list on the click, then reads it again, because
 * another Owner may have changed it meanwhile.
 */
export function setupChanged(queryClient: QueryClient, setup: OrganizationSetup) {
  queryClient.setQueryData(getGetOrganizationSetupQueryKey(), (old: { data: OrganizationSetup } | undefined) => (old ? { ...old, data: setup } : old));
  return invalidatePath(queryClient, setupPath);
}

/**
 * Where a new organization is in setting up, for the notification bell (design system section 11, Setup checklist): the
 * steps, each ticking itself from the organization's own data. Call it while a component initializes, because it creates
 * queries. It is for whoever `mayUseSetup`. `steps` is undefined until every answer is in, so a finished step never flashes
 * open. Skipping a step and hiding the checklist are saved on the server for the organization, so they hold for every
 * Owner and Admin on every device; the checklist goes by itself once every step left is done or skipped.
 */
export function createSetupProgress(user: () => CurrentUser | null | undefined) {
  const queryClient = useQueryClient();
  const mayUse = $derived(mayUseSetup(user()));
  const setup = createGetOrganizationSetup(() => ({ query: { enabled: mayUse, staleTime: 60_000 } }));
  const choices = $derived(setup.data?.data);
  // An organization that hid the checklist pays for none of the questions, and none are asked before that is known.
  const enabled = $derived(mayUse && choices !== undefined && !choices.hidden);
  const options = () => ({ query: { enabled, staleTime: 60_000 } });

  const members = createListTeamMembers(options);
  const invitations = createListInvitations(options);
  const branding = createGetOrganizationBranding(options);
  const sendingDomain = createGetOrganizationSendingDomain(options);
  const jobs = createListJobs(options);

  const changed = { mutation: { onSuccess: (response: { data: unknown }) => setupChanged(queryClient, response.data as OrganizationSetup) } };
  const skip = createSkipSetupStep(() => changed);
  const hide = createHideSetup(() => changed);

  const steps = $derived(
    enabled
      ? setupSteps({
          members: members.data?.data,
          invitations: invitations.data?.data,
          branding: branding.data?.data,
          sendingDomain: sendingDomain.data?.data,
          jobs: jobs.data?.data,
          setup: choices,
        })
      : undefined,
  );
  const visible = $derived(steps !== undefined && showSetup(steps, choices?.hidden ?? true));

  return {
    /** Every step, done, skipped or open, once all the answers are in; undefined while any is loading, or when there is no setup to see. */
    get steps() {
      return visible ? steps : undefined;
    },
    /** The steps still to do that count: an optional or skipped step never adds to the bell or "steps left". */
    get remaining() {
      return visible && steps ? steps.filter((step) => isOpen(step) && !step.optional) : [];
    },
    /** How many steps are done. A skipped step is not done. */
    get doneCount() {
      return visible && steps ? steps.filter((step) => step.done).length : 0;
    },
    /** Whether a skip or a hide is on its way to the server. */
    get saving() {
      return skip.isPending || hide.isPending;
    },
    /** The last skip or hide failed. */
    get failed() {
      return skip.isError || hide.isError;
    },
    /** Takes a step off the checklist for the organization ('Not needed'). */
    skip(step: SetupStepId) {
      hide.reset();
      return skip.mutateAsync({ data: { step } });
    },
    /** Hides the whole checklist for the organization; Settings, General shows it again. */
    hide() {
      skip.reset();
      return hide.mutateAsync();
    },
  };
}

/** Settings, General's "Show setup again": the checklist comes back for the organization with every skipped step. */
export function createShowSetupAgainAction() {
  const queryClient = useQueryClient();
  return createShowSetupAgain(() => ({ mutation: { onSuccess: (response) => setupChanged(queryClient, response.data as OrganizationSetup) } }));
}
