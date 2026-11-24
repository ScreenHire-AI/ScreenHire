<script lang="ts">
  import ZapIcon from '~icons/lucide/zap';
  import type { StageFact } from '#lib/stage-facts.js';

  // A stage's automations in its sheet (design system section 11, Stage sheet): one a line, each a link to it on the
  // Automations tab (A5), the one place automations are edited, and a name that wraps rather than cuts.
  let {
    facts,
    automationHref,
    onshow,
  }: {
    facts: readonly StageFact[];
    /** Where an automation's line goes; left out where the workflow only reads. */
    automationHref?: (automationId: string) => string;
    /** Opens an automation in the page instead of following its link. */
    onshow?: (automationId: string) => void;
  } = $props();

  // Links are underlined text (A5): a light rule at rest that darkens on hover, so the lines stay calm.
  const linkClass = 'underline decoration-border underline-offset-2 hover:decoration-current focus-visible:decoration-current pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center';
</script>

{#if facts.length > 0}
  <ul class="flex flex-col gap-1">
    {#each facts as fact (fact.automationId)}
      <li class="flex items-start gap-1.5 break-words" data-fact="automation">
        <ZapIcon aria-hidden="true" class="mt-0.5 shrink-0 text-primary pointer-coarse:mt-3.5" />
        {#if automationHref}
          <a
            href={automationHref(fact.automationId)}
            class={['min-w-0', linkClass]}
            onclick={(event) => {
              if (!onshow || event.metaKey || event.ctrlKey || event.shiftKey) return;
              event.preventDefault();
              onshow(fact.automationId);
            }}>{fact.text}</a
          >
        {:else}
          <span class="min-w-0">{fact.text}</span>
        {/if}
      </li>
    {/each}
  </ul>
{/if}
