<script lang="ts">
  import type { WorkflowStage } from '@screenhire/api-client';
  import { mergeProps } from 'bits-ui';
  import ClipboardListIcon from '~icons/lucide/clipboard-list';
  import ClockIcon from '~icons/lucide/clock';
  import ListIcon from '~icons/lucide/list';
  import MailIcon from '~icons/lucide/mail';
  import ZapIcon from '~icons/lucide/zap';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Popover from '#lib/components/ui/popover/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { plural } from '#lib/forms-timeline.js';
  import { chipsEmpty, respondWithin, type StageChipCounts } from '#lib/stage-facts.js';
  import ScorecardName from './scorecard-name.svelte';

  // A stage read at a glance (design system section 11, Stage chips): one row of small outline buttons, each an icon and a
  // count, in one order: the time limit ("Respond within 3 days"), automations, emails, questions, the scorecard. A count
  // opens a short list of what it counts, by name, each item a link to its place (the automation on the Automations tab,
  // the form on Forms); the time limit opens the stage's sheet and the scorecard its scorecard sheet. They are buttons with
  // a border, never badges (principle 4, A6), and their words are in the tooltip and the accessible name. Where the
  // pipeline only reads, the lists are text and the time limit and scorecard are plain chips. A stage with nothing on it
  // says "Nothing happens automatically", muted, in one line.
  let {
    chips,
    stageName,
    stage,
    automationHref,
    formHref,
    onshow,
    onshowform,
    onlimit,
    onscorecard,
    empty = 'Nothing happens automatically',
    class: className,
  }: {
    chips: StageChipCounts;
    /** The stage's name, for the lists' titles and the chips' names: "2 automations at Screening". */
    stageName: string;
    /** The stage, so a chip that does not open anything can name its scorecard. */
    stage?: WorkflowStage;
    /** Where an automation or email item goes; left out where the pipeline only reads. */
    automationHref?: (automationId: string) => string;
    /** Where a form item goes: the Forms tab with that form chosen. */
    formHref?: (formId: string) => string;
    /** Opens an automation in the page instead of following its link. */
    onshow?: (automationId: string) => void;
    /** Opens a form on the Forms tab instead of following its link. */
    onshowform?: (formId: string) => void;
    /** Opens the stage's sheet from the time limit chip. */
    onlimit?: () => void;
    /** Opens the scorecard sheet from the scorecard chip. */
    onscorecard?: () => void;
    /** What a stage with nothing on it says; empty for nothing at all. */
    empty?: string;
    class?: string;
  } = $props();

  // Links are underlined text (A5): a light rule at rest that darkens on hover.
  const linkClass =
    'underline decoration-border underline-offset-2 hover:decoration-current focus-visible:decoration-current pointer-coarse:inline-flex pointer-coarse:min-h-11 pointer-coarse:items-center';
  // A chip that does not answer a click: its icon and muted words with no border or fill, so it never passes for a button (A6).
  const plainChip = 'inline-flex h-7 items-center gap-1 text-caption text-muted-foreground [&_svg]:size-3.5';

  // A plain click opens the item in the page; a modified click follows the link, to a new tab or window.
  function show(event: MouseEvent, id: string, open: ((id: string) => void) | undefined) {
    if (!open || event.metaKey || event.ctrlKey || event.shiftKey) return;
    event.preventDefault();
    open(id);
  }
</script>

{#snippet item(text: string, href: string | undefined, id?: string, open?: (id: string) => void)}
  {#if href}
    <a {href} class={['min-w-0 break-words', linkClass]} onclick={(event) => id && show(event, id, open)}>{text}</a>
  {:else}
    <span class="min-w-0 break-words">{text}</span>
  {/if}
{/snippet}

{#snippet counted(kind: string, Icon: typeof ZapIcon, count: number, label: string, list: import('svelte').Snippet)}
  <li>
    <Popover.Root>
      <Tooltip.Root>
        <Tooltip.Trigger>
          {#snippet child({ props: tooltipProps })}
            <Popover.Trigger>
              {#snippet child({ props })}
                <Button {...mergeProps(tooltipProps, props)} variant="outline" size="sm" aria-label={`${label} at ${stageName}`} data-chip={kind} class="px-2 tabular-nums">
                  <Icon aria-hidden="true" class={kind === 'automations' ? 'text-primary' : 'text-muted-foreground'} />{count}
                </Button>
              {/snippet}
            </Popover.Trigger>
          {/snippet}
        </Tooltip.Trigger>
        <Tooltip.Content>{label}</Tooltip.Content>
      </Tooltip.Root>
      <Popover.Content align="start" role="dialog" class="w-auto max-w-80 min-w-56 gap-2" aria-label={`${label} at ${stageName}`} data-chip-list={kind}>
        <p class="font-semibold">{label}</p>
        {@render list()}
      </Popover.Content>
    </Popover.Root>
  </li>
{/snippet}

{#snippet automationList()}
  <ul class="flex flex-col gap-1.5">
    {#each chips.automations as automation (automation.id)}
      <li class="flex items-start gap-1.5"><ZapIcon aria-hidden="true" class="mt-0.5 shrink-0 text-primary" />{@render item(automation.name, automationHref?.(automation.id), automation.id, onshow)}</li>
    {/each}
  </ul>
{/snippet}

{#snippet emailList()}
  <ul class="flex flex-col gap-1.5">
    {#each chips.emails as email, index (index)}
      <li class="flex items-start gap-1.5"><MailIcon aria-hidden="true" class="mt-0.5 shrink-0 text-muted-foreground" />{@render item(email.name, automationHref?.(email.automationId), email.automationId, onshow)}</li>
    {/each}
  </ul>
{/snippet}

{#snippet formList()}
  <ul class="flex flex-col gap-1.5">
    {#each chips.forms as form (form.id)}
      <li class="flex items-start gap-1.5">
        {@render item(form.name, formHref?.(form.id), form.id, onshowform)}
        <Badge variant="count" class="ml-auto shrink-0"><span aria-hidden="true">{form.questions}</span><span class="sr-only">{plural(form.questions, 'question')}</span></Badge>
      </li>
    {/each}
  </ul>
{/snippet}

{#if chipsEmpty(chips)}
  {#if empty}<p class={['text-caption text-muted-foreground', className]} data-chips-empty>{empty}</p>{/if}
{:else}
  <ul class={['flex flex-wrap items-center gap-1.5', className]} aria-label={`What happens at ${stageName}`}>
    {#if chips.limitDays}
      {@const limit = respondWithin(chips.limitDays)}
      <!-- The time limit is the widest chip. In a narrow card it takes its own line and, past the card's width, cuts with
           its words in the tooltip (F5), so it never runs out of the card. The read-only flow's columns are wide enough. -->
      <li class="max-w-full min-w-0">
        {#if onlimit}
          <Tooltip.Root>
            <Tooltip.Trigger>
              {#snippet child({ props })}
                <Button {...props} variant="outline" size="sm" aria-haspopup="dialog" data-chip="limit" class="max-w-full gap-1 px-2 tabular-nums" onclick={onlimit}>
                  <ClockIcon aria-hidden="true" class="text-muted-foreground" /><span class="truncate">{limit}</span>
                </Button>
              {/snippet}
            </Tooltip.Trigger>
            <Tooltip.Content>{limit}</Tooltip.Content>
          </Tooltip.Root>
        {:else}
          <span class={plainChip} data-chip="limit"><ClockIcon aria-hidden="true" />{limit}</span>
        {/if}
      </li>
    {/if}
    {#if chips.automations.length > 0}
      {@render counted('automations', ZapIcon, chips.automations.length, plural(chips.automations.length, 'automation'), automationList)}
    {/if}
    {#if chips.emails.length > 0}
      {@render counted('emails', MailIcon, chips.emails.length, plural(chips.emails.length, 'email'), emailList)}
    {/if}
    {#if chips.forms.length > 0}
      {@render counted('questions', ListIcon, chips.questions, plural(chips.questions, 'question'), formList)}
    {/if}
    {#if chips.scorecard}
      <li>
        {#if onscorecard}
          <!-- An icon like the counts beside it, with its word in the tooltip and the accessible name, so the counts keep one row. -->
          <Tooltip.Root>
            <Tooltip.Trigger>
              {#snippet child({ props })}
                <Button {...props} variant="outline" size="sm" aria-haspopup="dialog" aria-label="Scorecard" data-chip="scorecard" class="px-2" onclick={onscorecard}>
                  <!-- Touch has no hover to show the tooltip before the sheet opens, so there the word shows too. -->
                  <ClipboardListIcon aria-hidden="true" class="text-muted-foreground" /><span aria-hidden="true" class="hidden pointer-coarse:inline">Scorecard</span>
                </Button>
              {/snippet}
            </Tooltip.Trigger>
            <Tooltip.Content>Scorecard</Tooltip.Content>
          </Tooltip.Root>
        {:else}
          <!-- Where nothing opens, the chip names the scorecard, with the word Scorecard for screen readers only (W9). -->
          <span class={plainChip} data-chip="scorecard"
            ><ClipboardListIcon aria-hidden="true" />{#if stage}<span><span class="sr-only">Scorecard:</span>{' '}<ScorecardName {stage} /></span>{:else}Scorecard{/if}</span
          >
        {/if}
      </li>
    {/if}
  </ul>
{/if}
