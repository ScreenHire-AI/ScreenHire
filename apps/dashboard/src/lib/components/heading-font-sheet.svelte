<script lang="ts">
  import CheckIcon from '~icons/lucide/check';
  import type { HeadingFontOption, HeadingFontStyle } from '@screenhire/api-client';
  import { createListHeadingFonts } from '@screenhire/api-client/svelte-query';
  import { untrack } from 'svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { loadHeadingFontFace, previewFamily } from '#lib/heading-font-faces.svelte.js';
  import { describeError } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // More fonts, from Branding's Heading font panel (docs/architecture/branding.md section 3, design system section 18): the
  // Google Fonts catalogue in a sheet (section 9, in place), with a search and the style filters, each name drawn in its
  // own font. A name's file loads from the API only when its row scrolls into view. Use font fills the panel's choice;
  // nothing reaches candidates until the page's Save, which hosts the family.
  let {
    open = $bindable(false),
    value,
    saved,
    onchoose,
  }: {
    open?: boolean;
    /** The font the form holds now, '' for ScreenHire's. */
    value: string;
    /** The font candidates see now, the last save's, '' for ScreenHire's: the one marked "In use". */
    saved: string;
    onchoose: (font: HeadingFontOption) => void;
  } = $props();

  const pageSize = 40;
  const styleFilters: readonly { value: HeadingFontStyle | 'All'; label: string }[] = [
    { value: 'All', label: 'All' },
    { value: 'Sans', label: 'Sans' },
    { value: 'Serif', label: 'Serif' },
    { value: 'Display', label: 'Display' },
    { value: 'Rounded', label: 'Rounded' },
    { value: 'Slab', label: 'Slab' },
  ];

  const catalogue = createListHeadingFonts(() => ({ query: { enabled: open, staleTime: 60 * 60 * 1000 } }));
  const fonts = $derived(catalogue.data?.data.fonts ?? []);
  let query = $state('');
  let style = $state<HeadingFontStyle | 'All'>('All');
  let selected = $state('');
  let shown = $state(pageSize);

  // Starts from the form's font each time it opens.
  $effect(() => {
    if (!open) return;
    untrack(() => {
      selected = value;
      query = '';
      style = 'All';
      shown = pageSize;
    });
  });

  const matches = $derived.by(() => {
    const words = query.trim().toLowerCase();
    return fonts.filter((font) => (style === 'All' || font.styles.includes(style)) && (!words || font.family.toLowerCase().includes(words)));
  });
  const visible = $derived(matches.slice(0, shown));
  const chosen = $derived(fonts.find((font) => font.id === selected));
  const count = $derived(new Intl.NumberFormat().format(matches.length));

  // A row's name loads its face once the row is in view (or about to be).
  function face(id: string) {
    return (row: HTMLElement) => {
      if (typeof IntersectionObserver === 'undefined') {
        loadHeadingFontFace(id);
        return;
      }
      const observer = new IntersectionObserver(
        (entries) => {
          if (entries.some((entry) => entry.isIntersecting)) {
            loadHeadingFontFace(id);
            observer.disconnect();
          }
        },
        { rootMargin: '200px' },
      );
      observer.observe(row);
      return () => observer.disconnect();
    };
  }

  function use() {
    if (!chosen) return;
    onchoose(chosen);
    open = false;
  }
</script>

<Sheet.Root bind:open>
  <!-- Full width on a phone, so the six style filters fit on one row (design system F5). -->
  <Sheet.Content side="right" class="w-full gap-0 data-[side=right]:w-full data-[side=right]:sm:max-w-md">
    <Sheet.Header class="border-b border-border-subtle">
      <Sheet.Title>Heading fonts</Sheet.Title>
      <Sheet.Description>Open-licence Google Fonts, hosted by ScreenHire for your careers site.</Sheet.Description>
    </Sheet.Header>
    <div class="flex flex-col gap-3 border-b border-border-subtle p-4">
      <SearchField bind:value={() => query, (next) => ((query = next), (shown = pageSize))} label="Search fonts" />
      <ToggleGroup.Root
        type="single"
        variant="outline"
        spacing={0}
        aria-label="Style"
        class="max-w-full overflow-x-auto"
        bind:value={() => style, (next) => ((style = (next || 'All') as HeadingFontStyle | 'All'), (shown = pageSize))}
      >
        {#each styleFilters as filter (filter.value)}
          <ToggleGroup.Item value={filter.value} class="text-foreground data-[state=on]:inset-ring-2 data-[state=on]:inset-ring-accent-foreground">
            {filter.label}
          </ToggleGroup.Item>
        {/each}
      </ToggleGroup.Root>
    </div>

    <div class="min-h-0 flex-1 overflow-y-auto">
      {#if catalogue.isError && !catalogue.data}
        <div class="p-4"><QueryError message={describeError(catalogue.error, 'Could not load the fonts')} onretry={() => catalogue.refetch()} inset /></div>
      {:else if !catalogue.data}
        <div aria-hidden="true" class="flex flex-col gap-2 p-4">
          {#each Array.from({ length: 8 }, (_, index) => index) as index (index)}<Skeleton class="h-11 w-full" />{/each}
        </div>
        <p class="sr-only" role="status">Loading the fonts…</p>
      {:else}
        {#if !catalogue.data.data.complete}
          <p class="border-b border-border-subtle p-4 text-muted-foreground">
            The full catalogue cannot be reached right now, so only ScreenHire's fonts and the ones already in use are listed. Try again later.
          </p>
        {/if}
        <p role="status" class="px-4 pt-3 text-caption text-muted-foreground">{matches.length === 1 ? '1 font' : `${count} fonts`}</p>
        {#if matches.length === 0}
          <p class="p-4 text-muted-foreground">No font matches. Try another name or style.</p>
        {:else}
          <div role="radiogroup" aria-label="Heading fonts" class="flex flex-col py-1">
            {#each visible as font (font.id)}
              <label
                {@attach face(font.id)}
                class="flex min-h-11 items-center gap-3 border-b border-border-subtle px-4 py-2 last:border-b-0 has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring has-focus-visible:ring-inset"
              >
                <input type="radio" name="heading-font-choice" value={font.id} bind:group={selected} class="size-4 shrink-0 accent-primary" />
                <span class="min-w-0 flex-1 truncate text-heading font-bold" style:font-family={previewFamily(font.id, font.styles.includes('Serif'))}>
                  {font.family}
                </span>
                {#if font.id === saved}<span class="shrink-0 text-caption text-muted-foreground">In use</span>{/if}
              </label>
            {/each}
          </div>
          {#if matches.length > shown}
            <div class="p-4">
              <Button variant="outline" class="w-full" onclick={() => (shown += pageSize)}>Show {Math.min(pageSize, matches.length - shown)} more</Button>
            </div>
          {/if}
        {/if}
      {/if}
    </div>

    <Sheet.Footer class="flex-row justify-end gap-2 border-t border-border-subtle">
      <Sheet.Close type="button" class={cn(buttonVariants({ variant: 'outline', size: 'lg' }))}>Cancel</Sheet.Close>
      <Button size="lg" disabled={!chosen} onclick={use}><CheckIcon aria-hidden="true" />Use {chosen?.family ?? 'font'}</Button>
    </Sheet.Footer>
  </Sheet.Content>
</Sheet.Root>
