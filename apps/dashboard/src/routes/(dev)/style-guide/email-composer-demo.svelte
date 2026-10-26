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
  import { getListEmailTemplatesQueryKey } from '@screenhire/api-client/svelte-query';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import EmailComposerSheet from '#lib/components/email-composer-sheet.svelte';
  import EmailSelectionButton from '#lib/components/email-selection-button.svelte';

  // The email composer with sample data, in its sheet: the style guide has no API, so this example brings its own query
  // client, filled in advance and never refetched. Preview and Send have nowhere to go here, so they show their failure
  // lines.
  const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false }, mutations: { retry: false } } });
  const recipients = [
    { id: 'demo-application-1', candidateName: 'Samuel Okonkwo-Hartley' },
    { id: 'demo-application-2', candidateName: 'Priya Raman' },
    { id: 'demo-application-3', candidateName: 'Lena Fischer' },
  ];
  const lastUpdated = { by: 'ScreenHire', at: null };
  client.setQueryData(getListEmailTemplatesQueryKey(), {
    status: 200,
    headers: new Headers(),
    data: {
      sendingEnabled: true,
      variables: [
        { name: 'candidateName', label: 'Full name', rejectOnly: false },
        { name: 'firstName', label: 'First name', rejectOnly: false },
        { name: 'lastName', label: 'Last name', rejectOnly: false },
        { name: 'jobTitle', label: 'Job title', rejectOnly: false },
      ],
      templates: [
        {
          id: 'application-received',
          name: 'Application received',
          builtIn: true,
          edited: false,
          rejectOnly: false,
          subject: 'We received your application for {{ jobTitle }}',
          body: 'Hi {{ firstName }},\n\nThank you for applying for {{ jobTitle }}.',
          updatedAt: null,
          lastUpdated,
        },
      ],
    },
  });
  setQueryClientContext(client);

  let open = $state(false);
</script>

<EmailSelectionButton count={3} onclick={() => (open = true)} />
<EmailComposerSheet bind:open {recipients} />
