<script lang="ts" module>
  import type { EmailPreview } from '@screenhire/api-client';

  /** The sample email the style guide shows, as the API renders it. */
  export const demoEmail: EmailPreview = {
    subject: 'We received your application for Product designer',
    html: '<!doctype html><html lang="en"><body style="font-family: sans-serif; line-height: 1.5;"><p>Hi Samuel Okonkwo-Hartley,</p><p>Thank you for applying for Product designer at Brightwater Studio. Your application has arrived, and the hiring team will review it.</p><p>Brightwater Studio</p></body></html>',
    text: 'Hi Samuel Okonkwo-Hartley,\n\nThank you for applying for Product designer at Brightwater Studio. Your application has arrived, and the hiring team will review it.\n\nBrightwater Studio\n',
  };
</script>

<script lang="ts">
  import { getListEmailTemplatesQueryKey, getPreviewApplicationEmailQueryKey } from '@screenhire/api-client/svelte-query';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import EmailSelectionButton from '#lib/components/email-selection-button.svelte';
  import EmailSheet from '#lib/components/email-sheet.svelte';

  // The Email sheet with sample data: the style guide has no API, so this example brings its own query client, filled in
  // advance and never refetched. Send has nowhere to go here, so it shows the failure line.
  const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false }, mutations: { retry: false } } });
  const recipients = [
    { id: 'demo-application-1', candidateName: 'Samuel Okonkwo-Hartley' },
    { id: 'demo-application-2', candidateName: 'Priya Raman' },
    { id: 'demo-application-3', candidateName: 'Lena Fischer' },
  ];
  client.setQueryData(getListEmailTemplatesQueryKey(), {
    status: 200,
    headers: new Headers(),
    data: {
      sendingEnabled: true,
      variables: [],
      templates: [
        { id: 'application-received', name: 'Application received', builtIn: true, edited: false, rejectOnly: false, subject: '', body: '', updatedAt: null },
        { id: 'stage-change', name: 'Moved to the next step', builtIn: true, edited: false, rejectOnly: false, subject: '', body: '', updatedAt: null },
      ],
    },
  });
  client.setQueryData(getPreviewApplicationEmailQueryKey({ applicationId: recipients[0]!.id, templateId: 'application-received' }), {
    status: 200,
    headers: new Headers(),
    data: demoEmail,
  });
  setQueryClientContext(client);

  let open = $state(false);
</script>

<EmailSelectionButton count={3} onclick={() => (open = true)} />
<EmailSheet bind:open {recipients} />
