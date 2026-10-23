<script lang="ts">
  import type { EmailPreview } from '@screenhire/api-client';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';

  // An email as the candidate receives it (design system section 11): the subject, then the HTML part or the plain-text
  // part. The HTML is the API's rendering, but it is still a template a tenant wrote, so it renders in a sandboxed frame
  // with no scripts, never in the page. While `preview` is undefined it shows a skeleton of the same shape (E1).
  let { preview, failure }: { preview: EmailPreview | undefined; failure?: string } = $props();

  let part = $state<'html' | 'text'>('html');
  const parts = [
    { id: 'html', label: 'Email' },
    { id: 'text', label: 'Plain text' },
  ] as const;
</script>

<div data-email-preview class="flex min-w-0 flex-col gap-3">
  {#if failure}
    <FailureText message={failure} />
  {:else if !preview}
    <div aria-hidden="true" class="flex flex-col gap-3">
      <Skeleton class="h-5 w-2/3" />
      <Skeleton class="h-64 w-full rounded-lg" />
    </div>
    <p class="sr-only" role="status">Loading the preview…</p>
  {:else}
    <div class="flex min-w-0 flex-wrap items-center justify-between gap-2">
      <p class="min-w-0 break-words"><span class="text-muted-foreground">Subject</span> <span data-email-subject class="font-semibold">{preview.subject}</span></p>
      <div role="group" aria-label="Part" class="flex shrink-0 gap-1">
        {#each parts as option (option.id)}
          <Toggle bind:pressed={() => part === option.id, () => (part = option.id)}>{option.label}</Toggle>
        {/each}
      </div>
    </div>
    <div class="rounded-lg bg-secondary p-3">
      {#if part === 'html'}
        <iframe
          title="Email preview"
          sandbox=""
          srcdoc={preview.html}
          class="block h-80 w-full rounded-lg border border-border-subtle bg-card"
        ></iframe>
      {:else}
        <pre data-email-text class="h-80 overflow-auto rounded-lg border border-border-subtle bg-card p-4 font-sans text-body whitespace-pre-wrap">{preview.text}</pre>
      {/if}
    </div>
  {/if}
</div>
