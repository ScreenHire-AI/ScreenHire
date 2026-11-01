<script lang="ts">
  import UploadIcon from '~icons/lucide/upload';
  import type { BrandingImage } from '@screenhire/api-client';
  import FailureText from '#lib/components/failure-text.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { fileSize } from '#lib/format.js';

  // A brand image field (design system section 11): the image in use with its pixel size and file size, Upload or
  // Replace, and Remove. While its malware scan runs it says Scanning, in grey, because nobody needs to act (T3); a file
  // the scan blocked says so. The upload itself is the caller's (`BrandImageSlot` in #lib/brand-images.svelte.ts), and
  // removing changes nothing until the page saves. `noun` names the image in labels ("logo"); `errors` are a refused
  // save's or a failed upload's.
  let {
    id,
    noun,
    description,
    image,
    busy = false,
    errors,
    onchoose,
    onremove,
  }: {
    id: string;
    noun: string;
    description: string;
    image: BrandingImage | null;
    busy?: boolean;
    errors?: string[];
    onchoose: (file: File) => void;
    onremove: () => void;
  } = $props();

  let input = $state<HTMLInputElement>();
  const scanning = $derived(image?.scanStatus === 'Pending');
  const blocked = $derived(image?.scanStatus === 'Infected');
  const problems = $derived((errors ?? []).filter(Boolean));
</script>

<Field.Field data-invalid={problems.length > 0 || blocked ? true : undefined}>
  <Field.Label for={id}>{noun.charAt(0).toUpperCase() + noun.slice(1)}</Field.Label>
  <div class="flex min-w-0 flex-wrap items-center gap-3">
    {#if image?.url}
      <img
        src={image.url}
        alt={`The ${noun}`}
        width={image.width}
        height={image.height}
        class="h-12 w-auto max-w-48 shrink-0 rounded-md border border-border bg-secondary object-contain p-1"
      />
      <p class="min-w-0 text-muted-foreground">{image.width} by {image.height} pixels, {fileSize(image.sizeBytes)}</p>
    {:else if scanning}
      <StatusBadge status="working" label="Scanning" />
    {:else if !blocked}
      <p class="text-muted-foreground">No {noun}</p>
    {/if}
    <div class="ml-auto flex shrink-0 gap-2">
      <!-- The native picker, behind the button that opens it; its label is the field's. -->
      <input
        bind:this={input}
        {id}
        type="file"
        accept="image/png,image/jpeg"
        class="sr-only"
        tabindex="-1"
        aria-describedby={`${id}-hint`}
        onchange={(event) => {
          const file = event.currentTarget.files?.[0];
          event.currentTarget.value = '';
          if (file) onchoose(file);
        }}
      />
      <Button variant="outline" disabled={busy} onclick={() => input?.click()}>
        <UploadIcon aria-hidden="true" />{image ? `Replace ${noun}` : `Upload ${noun}`}
      </Button>
      {#if image}
        <Button variant="outline" disabled={busy} onclick={onremove}>Remove {noun}</Button>
      {/if}
    </div>
  </div>
  <Field.Description id={`${id}-hint`}>{description}</Field.Description>
  {#if blocked}
    <FailureText message={`This file did not pass the malware scan. Upload another ${noun}.`} />
  {/if}
  {#if problems.length > 0}
    <Field.Error errors={problems.map((message) => ({ message }))} />
  {/if}
</Field.Field>
