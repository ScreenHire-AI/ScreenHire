<script lang="ts">
  import { createGetSentEmail } from '@screenhire/api-client/svelte-query';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import { describeError } from '#lib/query.js';

  // One email as it went to the candidate, loaded when its row in Messages opens. A copy never changes, so it loads once.
  let { emailId }: { emailId: string } = $props();

  const email = createGetSentEmail(() => emailId, () => ({ query: { staleTime: Infinity } }));
</script>

<EmailPreview preview={email.data?.data} failure={email.isError ? describeError(email.error, 'Could not load the email') : undefined} />
