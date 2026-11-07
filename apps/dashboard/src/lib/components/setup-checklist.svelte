<script lang="ts">
  import type { CurrentUser } from '@screenhire/api-client';
  import {
    createGetOrganization,
    createGetOrganizationBranding,
    createGetOrganizationSendingDomain,
    createListInvitations,
    createListJobs,
    createListTeamMembers,
  } from '@screenhire/api-client/svelte-query';
  import SetupSteps from '#lib/components/setup-steps.svelte';
  import { can, seesAllJobs } from '#lib/permissions.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { setupSteps, showSetup } from '#lib/setup-checklist.js';

  // The Set up your organization panel on Jobs (design system section 11): the steps a new team takes, each linking to
  // the page where it is done and ticking itself from the organization's own data. For whoever can do the steps and see
  // every job (Manage the team, Edit settings and all jobs: Owners and Admins), because a narrower job list could leave
  // the last step open after a job is published. It shows nothing until every answer is in, so a finished step never
  // flashes open; Dismiss hides it in this browser, and it goes by itself once the last step is done.
  let { user }: { user: CurrentUser | null | undefined } = $props();

  const organizationId = $derived(user?.organization?.id ?? '');
  const mayUse = $derived(can(user, 'ManageTeam') && can(user, 'EditSettings') && seesAllJobs(user));
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
    setupSteps({
      members: members.data?.data,
      invitations: invitations.data?.data,
      branding: branding.data?.data,
      organization: organization.data?.data,
      sendingDomain: sendingDomain.data?.data,
      jobs: jobs.data?.data,
    }),
  );
</script>

{#if enabled && steps && showSetup(steps, dismissed)}
  <SetupSteps {steps} ondismiss={() => preferences.dismissSetup(organizationId)} />
{/if}
