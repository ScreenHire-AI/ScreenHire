<script lang="ts">
  import { createGetSentEmail } from '@screenhire/api-client/svelte-query';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import { describeError } from '#lib/query.js';

  // One email as it went to the candidate, loaded when its row in Messages opens. A copy never changes, so it loads once.
  let { emailId }: { emailId: string } = $props();

  const email = createGetSentEmail(() => emailId, () => ({ query: { staleTime: Infinity } }));
</script>

<!-- The row that opened it shows the subject, so the preview does not repeat it (W4). Who it went to shares a line with
     the Email and Plain text toggle. -->
<EmailPreview subject={false} preview={email.data?.data} failure={email.isError ? describeError(email.error, 'Could not load the email') : undefined}>
  {#snippet header()}
    {#if email.data}
      <p class="min-w-0 [overflow-wrap:anywhere]"><span class="text-muted-foreground">To</span> <span class="font-semibold">{email.data.data.to}</span></p>
    {/if}
  {/snippet}
</EmailPreview>
