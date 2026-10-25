<script lang="ts">
  import { getDownloadApplicationFileUrl, type ApplicationFileSummary } from '@screenhire/api-client';
  import DownloadIcon from '@lucide/svelte/icons/download';
  import XIcon from '@lucide/svelte/icons/x';
  import FileFrame from '#lib/components/file-frame.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';

  // The file viewer (design system section 9, section 11): a file opens over the page in a full-window dialog with Close
  // and Download, never in a new browser tab, and a page never embeds a preview. The file is drawn by `FileFrame`, in
  // isolation. Escape closes it too. `FileRow` opens it; it shows only files `canView` (#lib/files.ts) allows.
  let {
    open = $bindable(false),
    applicationId,
    file,
    description,
  }: {
    open?: boolean;
    applicationId: string;
    file: ApplicationFileSummary;
    /** One line under the name, such as the candidate and the job. */
    description?: string;
  } = $props();

  let pageCount = $state<number>();
</script>

<Dialog.Root bind:open>
  <Dialog.Content
    showCloseButton={false}
    class="inset-0 flex h-dvh w-full max-w-none translate-x-0 translate-y-0 flex-col gap-0 rounded-none bg-background p-0 ring-0 sm:max-w-none data-closed:zoom-out-100 data-open:zoom-in-100"
  >
    <header class="flex min-h-16 shrink-0 items-center gap-3 border-b border-border bg-card px-4 py-3 md:gap-4 md:px-6">
      <Dialog.Close>
        {#snippet child({ props })}
          <Button {...props} variant="outline" size="lg"><XIcon aria-hidden="true" />Close</Button>
        {/snippet}
      </Dialog.Close>
      <div class="flex min-w-0 flex-1 flex-col">
        <Dialog.Title class="truncate text-body font-semibold">{file.fileName}</Dialog.Title>
        {#if description}<Dialog.Description class="truncate text-caption text-muted-foreground">{description}</Dialog.Description>{/if}
      </div>
      {#if pageCount}
        <span class="shrink-0 text-muted-foreground tabular-nums max-sm:hidden">{pageCount === 1 ? '1 page' : `${pageCount} pages`}</span>
      {/if}
      <!-- A full navigation, not the client router: the API redirects to a short-lived link that downloads the file. -->
      <Button variant="outline" size="lg" href={getDownloadApplicationFileUrl(applicationId, file.id)} data-sveltekit-reload class="shrink-0">
        <DownloadIcon aria-hidden="true" />Download
      </Button>
    </header>
    <div class="min-h-0 flex-1 overflow-auto bg-secondary px-4 py-6 md:px-6">
      <!-- A page's width, with its loading and error states in the same column. -->
      <div class="mx-auto w-full max-w-204"><FileFrame {applicationId} {file} bind:pageCount /></div>
    </div>
  </Dialog.Content>
</Dialog.Root>
