<script lang="ts">
  import ImageIcon from '~icons/lucide/image';
  import UploadIcon from '~icons/lucide/upload';
  import type { BrandingImage } from '@screenhire/api-client';
  import FailureText from '#lib/components/failure-text.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import { fileSize } from '#lib/format.js';
  import { cn } from '#lib/utils.js';

  // A brand image field (design system section 11): a tile that shows the image where it is used, at its real
  // proportions. A logo sits on a light checkerboard, as it does on the careers header and in an email, so a transparent
  // one shows; a favicon is 32px inside a small browser tab; a sharing image is the card a shared link shows. Set, the
  // tile also names the file and its size, with Replace and Remove. Empty, it is dashed, says "No {noun} yet", takes a
  // dropped file and has Upload (the button is the way for anyone who cannot drop). While the malware scan runs it says
  // Scanning, in grey, because nobody needs to act (T3); a file the scan blocked says so. The upload itself is the
  // caller's (`BrandImageSlot` in #lib/brand-images.svelte.ts), and removing changes nothing until the page saves.
  // `noun` names the image in labels ("logo"); `errors` are a refused save's or a failed upload's.
  let {
    id,
    kind,
    noun,
    description,
    organizationName,
    image,
    busy = false,
    errors,
    onchoose,
    onremove,
  }: {
    id: string;
    /** Which part of the careers pages the image is, and so how its tile draws it. */
    kind: 'logo' | 'favicon' | 'sharing';
    noun: string;
    description: string;
    /** What the favicon's tab and the shared link say, as they do for candidates. */
    organizationName: string;
    image: BrandingImage | null;
    busy?: boolean;
    errors?: string[];
    onchoose: (file: File) => void;
    onremove: () => void;
  } = $props();

  let input = $state<HTMLInputElement>();
  let over = $state(false);
  const scanning = $derived(image?.scanStatus === 'Pending');
  const blocked = $derived(image?.scanStatus === 'Infected');
  const problems = $derived((errors ?? []).filter(Boolean));
  const title = $derived(`Jobs at ${organizationName}`);
  const label = $derived(noun.charAt(0).toUpperCase() + noun.slice(1));

  function choose(file: File | undefined) {
    if (file) onchoose(file);
  }

  // Only an empty tile takes a drop: a set one is changed with Replace, so a file dropped by mistake cannot swap it.
  const takesDrop = $derived(!image && !busy);
  function ondrop(event: DragEvent) {
    event.preventDefault();
    over = false;
    if (takesDrop) choose(event.dataTransfer?.files[0]);
  }
</script>

<Field.Field data-invalid={problems.length > 0 || blocked ? true : undefined}>
  <Field.Label for={id}>{label}</Field.Label>
  <!-- svelte-ignore a11y_no_noninteractive_element_interactions: a drop has no key press; Upload below is the keyboard way to the same file -->
  <!-- The tile is an inset (design system section 6): the image's mock sits on it as a card, so no card holds a card (S4). -->
  <div
    role="group"
    aria-label={label}
    data-image-tile={kind}
    data-empty={image ? undefined : ''}
    class={cn(
      'flex min-w-0 flex-col gap-3 rounded-lg border bg-secondary p-3',
      image ? 'border-transparent' : 'border-dashed border-input',
      takesDrop && over && 'border-solid border-ring bg-accent',
    )}
    ondragover={(event) => {
      if (!takesDrop) return;
      event.preventDefault();
      over = true;
    }}
    ondragleave={() => (over = false)}
    {ondrop}
  >
    {#if image?.url}
      {#if kind === 'logo'}
        <!-- The checkerboard is the careers pages' own light surfaces, so it stays light in the dark theme too. -->
        <div
          class="grid min-h-20 place-items-center rounded-md border border-border-subtle bg-candidate-background bg-[conic-gradient(var(--color-candidate-border)_25%,var(--color-candidate-background)_0_50%,var(--color-candidate-border)_0_75%,var(--color-candidate-background)_0)] bg-size-[16px_16px] p-3"
        >
          <img src={image.url} alt={`The ${noun}`} width={image.width} height={image.height} class="h-10 w-auto max-w-60 object-contain" />
        </div>
      {:else if kind === 'favicon'}
        <div class="flex items-end border-b border-border-subtle px-3">
          <div class="-mb-px flex w-full max-w-60 min-w-0 items-center gap-2 rounded-t-lg border border-b-0 border-border-subtle bg-card px-3 py-2">
            <img src={image.url} alt={`The ${noun}`} width="32" height="32" class="size-8 shrink-0 rounded-xs" />
            <span class="min-w-0 truncate text-caption">{title}</span>
          </div>
        </div>
      {:else}
        <div class="max-w-96 overflow-hidden rounded-lg border border-border-subtle bg-card">
          <img src={image.url} alt={`The ${noun}`} width={image.width} height={image.height} class="aspect-[1.91/1] w-full object-cover" />
          <div class="flex flex-col gap-0.5 px-4 py-3">
            <p class="truncate font-semibold">{title}</p>
            <p class="truncate text-muted-foreground">Open roles at {organizationName}.</p>
          </div>
        </div>
      {/if}
    {:else if scanning}
      <StatusBadge status="working" label="Scanning" class="self-start" />
    {:else if !image}
      <div class="flex flex-col items-center gap-2 px-3 py-4 text-center">
        <ImageIcon aria-hidden="true" class="size-6 text-muted-foreground" />
        <p class="font-semibold">No {noun} yet</p>
        <p class="text-muted-foreground">Drop a file here.</p>
      </div>
    {/if}

    <div class="flex min-w-0 flex-wrap items-center gap-3">
      {#if image}
        <!-- The basis, not a fixed width: on a phone the buttons take the next line before the name is squeezed. -->
        <div class="flex min-w-0 grow basis-48 flex-col">
          <TruncatedText text={image.fileName} class="font-semibold" />
          <p class="text-muted-foreground">{image.width} by {image.height} pixels, {fileSize(image.sizeBytes)}</p>
        </div>
      {/if}
      <div class={cn('flex min-w-0 flex-wrap gap-2', image ? 'ml-auto' : 'mx-auto justify-center')}>
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
            choose(file);
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
  </div>
  <Field.Description id={`${id}-hint`}>{description}</Field.Description>
  {#if blocked}
    <FailureText message={`This file did not pass the malware scan. Upload another ${noun}.`} />
  {/if}
  {#if problems.length > 0}
    <Field.Error errors={problems.map((message) => ({ message }))} />
  {/if}
</Field.Field>
