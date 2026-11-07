<script lang="ts">
  import { page } from '$app/state';
  import Notice from '#lib/components/notice.svelte';
  import { can } from '#lib/permissions.js';
  import type { PlanProblem } from '#lib/query.js';

  // A publish the plan refused (HTTP 402, `planProblem` in #lib/query.ts), in place of the plain failure line (E2): the API's
  // sentence, which already tells everyone else to ask an owner, and for an owner the link to where the plan is changed.
  let { problem }: { problem: PlanProblem } = $props();
</script>

<Notice href={can(page.data.user, 'ManageBilling') ? '/settings/billing' : undefined} link="Upgrade plan">{problem.detail}</Notice>
