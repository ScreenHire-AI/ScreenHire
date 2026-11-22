<script lang="ts">
  import type { OpenTimes, OpenTimesRequest } from '@screenhire/api-client';
  import { createPreviewInterviewOpenTimes } from '@screenhire/api-client/svelte-query';
  import Notice from '#lib/components/notice.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // Whether pick from our times has anything to offer (docs/architecture/interview-scheduling.md, "Changing course"), checked
  // as the sheet is filled in, before anything reaches the candidate. When the next 7 days have no open time it says so and
  // offers the other two ways, or a one-off time. A wait between changes keeps it to one question while someone types.
  let {
    request,
    onswitch,
    onaddtime,
  }: {
    /** The interview as it stands, or undefined while it is not complete enough to ask about. */
    request: OpenTimesRequest | undefined;
    onswitch: (option: 'TheirTimes' | 'OurSuggestion') => void;
    onaddtime?: () => void;
  } = $props();

  const preview = createPreviewInterviewOpenTimes();
  let answer = $state<OpenTimes | undefined>();
  let failed = $state(false);
  let asked = 0;

  // Asked again only when the question changes, not each time the sheet around it does.
  const key = $derived(request ? JSON.stringify(request) : '');

  $effect(() => {
    const body = key ? (JSON.parse(key) as OpenTimesRequest) : undefined;
    answer = undefined;
    failed = false;
    if (!body) return;
    const ask = ++asked;
    const timer = setTimeout(async () => {
      try {
        const response = await preview.mutateAsync({ data: body });
        if (ask === asked) answer = response.data;
      } catch {
        if (ask === asked) failed = true;
      }
    }, 400);
    return () => clearTimeout(timer);
  });
</script>

<div aria-live="polite" class="min-w-0">
  {#if failed}
    <p class="text-muted-foreground">The open times could not be checked just now.</p>
  {:else if answer && answer.unavailableInterviewerUserIds.length === 0}
    {#if answer.nextSevenDays > 0}
      <p class="text-muted-foreground">{answer.nextSevenDays === 1 ? '1 open time' : `${answer.nextSevenDays} open times`} in the next 7 days.</p>
    {:else}
      <Notice>
        Nothing is open in the next 7 days{answer.slots.length > 0 ? ', though later days have times' : ''}. Let them tell you when they are free, suggest a time
        yourself, or add a one-off time.
        {#snippet action()}
          <div class="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onclick={() => onswitch('TheirTimes')}>Tell us when you're free</Button>
            <Button type="button" variant="outline" onclick={() => onswitch('OurSuggestion')}>Suggest a time</Button>
            {#if onaddtime}<Button type="button" variant="outline" onclick={onaddtime}>Add a one-off time</Button>{/if}
          </div>
        {/snippet}
      </Notice>
    {/if}
  {/if}
</div>
