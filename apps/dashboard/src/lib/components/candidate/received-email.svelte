<script lang="ts">
  import { getDownloadReplyAttachmentUrl, type ReceivedAttachment } from '@screenhire/api-client';
  import { createGetReceivedEmail } from '@screenhire/api-client/svelte-query';
  import DownloadIcon from '~icons/lucide/download';
  import FileImageIcon from '~icons/lucide/file-image';
  import FileTextIcon from '~icons/lucide/file-text';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { canDownload, withheldReason } from '#lib/files.js';
  import { fileSize } from '#lib/format.js';
  import { describeError } from '#lib/query.js';
  import { needsAttention } from '#lib/status.js';
  import { replyHtml } from '#lib/received-email.js';

  // One reply as it arrived, loaded when its row in Messages opens: who it is from, the message, and the files that came with
  // it. The HTML is the sender's, cleaned by the API to the editor's elements, and still renders only in the preview's
  // sandboxed frame. A file is offered once its malware scan allows it, as an applicant's upload is (docs/architecture/email.md).
  let { replyId }: { replyId: string } = $props();

  const reply = createGetReceivedEmail(() => replyId, () => ({ query: { staleTime: Infinity } }));
  const received = $derived(reply.data?.data);

  const iconFor = (file: ReceivedAttachment) => (file.contentType.startsWith('image/') ? FileImageIcon : FileTextIcon);
</script>

<div class="flex min-w-0 flex-col gap-3">
  {#if received}
    <p class="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 break-words">
      <span class="text-muted-foreground">From</span>
      <span class="font-semibold">{received.fromName ? `${received.fromName} <${received.fromAddress}>` : received.fromAddress}</span>
      {#if received.autoReply}<Badge variant="muted">Automatic reply</Badge>{/if}
    </p>
  {/if}
  <!-- The row that opened it shows the subject, so the preview does not repeat it (W4). -->
  <EmailPreview
    subject={false}
    preview={received ? { subject: received.subject, html: replyHtml(received), text: received.text } : undefined}
    failure={reply.isError ? describeError(reply.error, 'Could not load the reply') : undefined}
  />
  {#if received && received.attachments.length > 0}
    <ul aria-label="Attachments" class="flex flex-col gap-2">
      {#each received.attachments as file (file.id)}
        {@const Icon = iconFor(file)}
        {@const attention = withheldReason(file)}
        <li class="flex min-w-0 items-center gap-3">
          <Icon aria-hidden="true" class="shrink-0 text-muted-foreground" />
          <div class="flex min-w-0 flex-1 flex-col">
            <TruncatedText text={file.fileName} class="font-semibold" />
            <span class="text-caption text-muted-foreground tabular-nums">{fileSize(file.sizeBytes)}</span>
          </div>
          {#if attention}
            <StatusBadge status={attention.status} label={attention.label} quiet={!needsAttention(attention.status)} />
          {:else if canDownload(file)}
            <IconButton label={`Download ${file.fileName}`} variant="outline" href={getDownloadReplyAttachmentUrl(received.id, file.id)} data-sveltekit-reload>
              <DownloadIcon aria-hidden="true" />
            </IconButton>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</div>
