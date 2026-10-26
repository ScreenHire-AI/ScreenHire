<script lang="ts">
  import EmailComposer from '#lib/components/email-composer.svelte';
  import * as Sheet from '#lib/components/ui/sheet/index.js';

  // The email composer in a sheet (design system section 11): for a selection on a list, or one candidate from their
  // header. The record stays behind it (section 9). It starts afresh each time it opens.
  let {
    open = $bindable(false),
    recipients,
    onsent,
  }: {
    open?: boolean;
    /** The applications to email, in the order the page shows them. */
    recipients: readonly { id: string; candidateName: string }[];
    /** After the API queued the emails, with a line saying how many. */
    onsent?: (message: string) => void;
  } = $props();

  const title = $derived(recipients.length === 1 ? `Email ${recipients[0]!.candidateName}` : `Email ${recipients.length} candidates`);
</script>

<Sheet.Root bind:open>
  <Sheet.Content side="right" class="w-full overflow-y-auto data-[side=right]:sm:max-w-2xl">
    <Sheet.Header>
      <Sheet.Title>{title}</Sheet.Title>
      <Sheet.Description>
        {recipients.length === 1 ? 'It appears in their Activity and Messages.' : 'Each candidate gets their own copy.'}
      </Sheet.Description>
    </Sheet.Header>
    {#if open}
      <div class="px-4 pb-4">
        <EmailComposer
          id="email-sheet"
          label={title}
          {recipients}
          active={open}
          oncancel={() => (open = false)}
          onsent={(message) => {
            open = false;
            onsent?.(message);
          }}
        />
      </div>
    {/if}
  </Sheet.Content>
</Sheet.Root>
