<script lang="ts">
  import type { EmailPreview } from '@screenhire/api-client';
  import FailureText from '#lib/components/failure-text.svelte';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';

  // An email as the candidate receives it (design system section 11): the subject, then the HTML part or the plain-text
  // part. The HTML is the API's rendering, but it is still a template a tenant wrote, so it renders in a sandboxed frame
  // with no scripts, never in the page. While `preview` is undefined it shows a skeleton of the same shape (E1).
  // The frame takes the email's own height, so a short email leaves no empty space under it. Reading that height needs
  // `allow-same-origin`; scripts, forms, popups and navigation stay off. Nothing in the frame can run or fetch anything
  // but the organization's logo: the API builds the layout, and cleans the template's or reply's text to text elements
  // and links (RichText.cs).
  let {
    preview,
    failure,
    subject = true,
  }: {
    preview: EmailPreview | undefined;
    failure?: string;
    /** The subject line; false where the row that opened the email already shows it (W4). */
    subject?: boolean;
  } = $props();

  let part = $state<'html' | 'text'>('html');
  let frame = $state<HTMLIFrameElement>();
  let height = $state<number>();

  // The root element's box, not `scrollHeight`, which never reports less than the frame's own height.
  function fit() {
    const root = frame?.contentDocument?.documentElement;
    if (root) height = Math.ceil(root.getBoundingClientRect().height);
  }

  // A narrower frame wraps the email onto more lines, so its height follows the frame's width.
  $effect(() => {
    if (!frame) return;
    const observer = new ResizeObserver(fit);
    observer.observe(frame);
    return () => observer.disconnect();
  });
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
      {#if subject}
        <p class="min-w-0 break-words"><span class="text-muted-foreground">Subject</span> <span data-email-subject class="font-semibold">{preview.subject}</span></p>
      {/if}
      <div role="group" aria-label="Part" class="ml-auto flex shrink-0 gap-1">
        {#each parts as option (option.id)}
          <!-- Only a press chooses a part: the other toggle writing back "not pressed" must not switch it again. -->
          <Toggle bind:pressed={() => part === option.id, (pressed) => pressed && (part = option.id)}>{option.label}</Toggle>
        {/each}
      </div>
    </div>
    <div class="rounded-lg bg-secondary p-3">
      {#if part === 'html'}
        <iframe
          bind:this={frame}
          title="Email preview"
          sandbox="allow-same-origin"
          srcdoc={preview.html}
          onload={fit}
          style:height={height ? `${height + 2}px` : undefined}
          class="block h-80 w-full rounded-lg border border-border-subtle bg-card"
        ></iframe>
      {:else}
        <pre data-email-text class="h-80 max-w-measure overflow-auto rounded-lg border border-border-subtle bg-card p-4 font-sans text-body whitespace-pre-wrap">{preview.text}</pre>
      {/if}
    </div>
  {/if}
</div>
