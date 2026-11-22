<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import CopyIcon from '~icons/lucide/copy';
  import type { JobDetail, PostedField } from '@screenhire/api-client';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import { CopyFeedback } from '#lib/copy-feedback.svelte.js';
  import { copyFields } from '#lib/manual-postings.js';

  // The job's text, ready to copy to a board by hand (Post it yourself, design system section 11): one row per field with
  // its value and Copy. The description copies two ways: formatted, which keeps its headings and bullets where the board's
  // editor takes pasted formatting, or as plain text with "- " bullets for one that does not. A field the job changed since
  // the listing was posted carries a Changed badge, so the person knows what to paste again.
  let {
    job,
    changed = [],
  }: {
    job: Pick<JobDetail, 'title' | 'location' | 'employmentType' | 'salary' | 'description'>;
    /** The fields that changed since the listing was posted. */
    changed?: readonly PostedField[];
  } = $props();

  const fields = $derived(copyFields(job));

  // What was copied last, for two seconds, so each button can say it worked. Without a clipboard the button says so.
  const feedback = new CopyFeedback();
  const said = (key: string, label: string) => feedback.said(key, label);

  // Formatted: the HTML for editors that take it, with the plain text beside it for those that do not, and the plain text
  // alone where the browser has no rich clipboard.
  function copyFormatted(text: string) {
    return feedback.copy('formatted', async () => {
      try {
        if (typeof ClipboardItem === 'undefined') throw new Error('No rich clipboard');
        await navigator.clipboard.write([
          new ClipboardItem({
            'text/html': new Blob([job.description], { type: 'text/html' }),
            'text/plain': new Blob([text], { type: 'text/plain' }),
          }),
        ]);
      } catch {
        await navigator.clipboard.writeText(text);
      }
    });
  }

  // The button's name starts with what it shows and then says which field (WCAG 2.5.3): "Copy formatted description".
  const named = (key: string, label: string, field: string) => `${said(key, label)} ${field}`;
</script>

<ul class="flex min-w-0 flex-col divide-y divide-border-subtle rounded-lg border border-border-subtle" data-posting-text>
  {#each fields as field (field.field)}
    <li class="flex min-w-0 flex-col gap-2 px-3 py-2.5" data-copy-field={field.field}>
      <!-- Wraps: on a phone the description's two buttons go under its name rather than past the sheet's edge (F1). -->
      <div class="flex min-w-0 flex-wrap items-center gap-2">
        <span class="font-semibold">{field.label}</span>
        {#if changed.includes(field.field)}<Badge variant="muted" class="shrink-0">Changed</Badge>{/if}
        <div class="ml-auto flex min-w-0 flex-wrap justify-end gap-2">
          {#if field.field === 'Description'}
            <Button variant="outline" size="sm" onclick={() => copyFormatted(field.text)} aria-label={named('formatted', 'Copy formatted', 'description')}>
              {#if feedback.copied('formatted')}<CheckIcon aria-hidden="true" />{:else}<CopyIcon aria-hidden="true" />{/if}{said('formatted', 'Copy formatted')}
            </Button>
            <Button variant="outline" size="sm" onclick={() => feedback.copyText('plain', field.text)} aria-label={named('plain', 'Copy as plain text', 'description')}>
              {#if feedback.copied('plain')}<CheckIcon aria-hidden="true" />{:else}<CopyIcon aria-hidden="true" />{/if}{said('plain', 'Copy as plain text')}
            </Button>
          {:else}
            <!-- The visible word starts the name, which says which field (WCAG 2.5.3). -->
            <Button variant="outline" size="sm" onclick={() => feedback.copyText(field.field, field.text)} aria-label={named(field.field, 'Copy', field.label.toLowerCase())}>
              {#if feedback.copied(field.field)}<CheckIcon aria-hidden="true" />{:else}<CopyIcon aria-hidden="true" />{/if}{said(field.field, 'Copy')}
            </Button>
          {/if}
        </div>
      </div>
      <!-- The value as it will paste: the description's first lines, the rest one Copy away. -->
      <p class={['min-w-0 break-words whitespace-pre-line text-muted-foreground', field.field === 'Description' && 'line-clamp-4']}>{field.text}</p>
    </li>
  {/each}
</ul>
<p role="status" class="sr-only">{feedback.last ? (feedback.last.ok ? 'Copied' : 'Could not copy') : ''}</p>
