<script lang="ts">
  import ImageIcon from '~icons/lucide/image';
  import type { BrandingImage } from '@screenhire/api-client';
  import { DropArea } from '@screenhire/ui/questions';
  import FailureText from '#lib/components/failure-text.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import * as Field from '#lib/components/ui/field/index.js';

  // A brand image field (design system section 11): a tile that shows the image where it is used, at its real
  // proportions. A logo sits on a light checkerboard, as it does on the careers header and in an email, so a transparent
  // one shows; a favicon is 32px inside a small browser tab; a sharing image is the card a shared link shows. The tile
  // is the drop area (Drop area, A12), so the picture is part of the target: empty, it says "No {noun} yet"; set, it names
  // the file and its size with Remove, and a click or a dropped file replaces the image. While the malware scan runs the
  // tile says Scanning, in grey, because nobody needs to act (T3); a file the scan blocked says so. A file of the wrong type or too
  // large is refused in the field's error before it is sent. A set tile is compact (about 200px) so the three of them sit in
  // one row of the wide Branding editor without the page scrolling: the sharing image's card is a thumbnail beside its
  // words, and Replace and Remove share a row. The upload itself is the caller's (`BrandImageSlot` in
  // #lib/brand-images.svelte.ts), and removing changes nothing until the page saves. `noun` names the image in labels
  // ("logo"); `errors` are a refused save's or a failed upload's.
  let {
    id,
    kind,
    noun,
    description,
    organizationName,
    image,
    busy = false,
    maxBytes,
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
    /** The largest file the kind takes, from the same rules the API checks (BrandImageRules). */
    maxBytes?: number;
    errors?: string[];
    onchoose: (file: File) => void;
    onremove: () => void;
  } = $props();

  // The picture types the API takes (it checks the bytes again); a wrong one is refused here with the reason.
  const accept = ['.png', '.jpg', '.jpeg'];
  let refused = $state('');
  const scanning = $derived(image?.scanStatus === 'Pending');
  const blocked = $derived(image?.scanStatus === 'Infected');
  const problems = $derived([...(errors ?? []), refused].filter(Boolean));
  const title = $derived(`Jobs at ${organizationName}`);
  const label = $derived(noun.charAt(0).toUpperCase() + noun.slice(1));
  // The buttons of a set tile share one row, so their nouns are short ("Replace image" for the sharing image, "Replace icon"
  // for the favicon); the tile's group still names the field.
  const short = $derived(kind === 'sharing' ? 'image' : kind === 'favicon' ? 'icon' : noun);
</script>

<Field.Field data-invalid={problems.length > 0 || blocked ? true : undefined}>
  <Field.Label for={id}>{label}</Field.Label>
  <!-- The tile is the drop area, an inset (design system section 6): the image's mock sits on it as a card, so no card holds a card (S4). -->
  <div role="group" aria-label={label} data-image-tile={kind} data-empty={image ? undefined : ''} class="min-w-0">
    <DropArea
      tone="app"
      keep={false}
      {id}
      {accept}
      {maxBytes}
      disabled={busy}
      invalid={problems.length > 0 || blocked}
      describedby={`${id}-hint`}
      file={image ? { name: image.fileName, size: image.sizeBytes } : null}
      status={busy ? 'Uploading...' : null}
      chooseLabel={`Upload ${noun}`}
      replaceLabel={`Replace ${short}`}
      removeLabel={`Remove ${short}`}
      onfile={(file) => {
        refused = '';
        onchoose(file);
      }}
      onproblem={(message) => (refused = message)}
      onremove={() => {
        refused = '';
        onremove();
      }}
    >
      {#snippet preview()}
        {#if image?.url}
          {#if kind === 'logo'}
            <!-- The checkerboard is the careers pages' own light surfaces, so it stays light in the dark theme too. -->
            <div
              class="grid place-items-center rounded-md border border-border-subtle bg-candidate-background bg-[conic-gradient(var(--color-candidate-border)_25%,var(--color-candidate-background)_0_50%,var(--color-candidate-border)_0_75%,var(--color-candidate-background)_0)] bg-size-[16px_16px] p-2"
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
            <div class="flex min-w-0 items-center gap-3 overflow-hidden rounded-lg border border-border-subtle bg-card pr-3">
              <img src={image.url} alt={`The ${noun}`} width={image.width} height={image.height} class="aspect-[1.91/1] w-24 shrink-0 object-cover" />
              <div class="flex min-w-0 flex-col">
                <p class="truncate font-semibold">{title}</p>
                <p class="truncate text-muted-foreground">Open roles at {organizationName}.</p>
              </div>
            </div>
          {/if}
        {:else if scanning}
          <StatusBadge status="working" label="Scanning" class="self-start" />
        {:else if !image}
          <div class="flex flex-col items-center gap-2 px-3 py-2 text-center">
            <ImageIcon aria-hidden="true" class="size-6 text-muted-foreground" />
            <p class="font-semibold">No {noun} yet</p>
          </div>
        {/if}
        {#if image}
          <p class="text-center text-muted-foreground">{image.width} by {image.height} pixels</p>
        {/if}
      {/snippet}
    </DropArea>
  </div>
  <Field.Description id={`${id}-hint`}>{description}</Field.Description>
  {#if blocked}
    <FailureText message={`This file did not pass the malware scan. Upload another ${noun}.`} />
  {/if}
  {#if problems.length > 0}
    <Field.Error errors={problems.map((message) => ({ message }))} />
  {/if}
</Field.Field>
