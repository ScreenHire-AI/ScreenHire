<script lang="ts" module>
  import type { BrandingImage, HeadingFont } from '@screenhire/api-client';

  /** What the branding preview draws: the form as it is typed, with the text colours the contrast check chose. */
  export interface BrandingDraft {
    organizationName: string;
    /** The brand colour and the white or ink text on it; null for ScreenHire's. */
    brand: { color: string; text?: string } | null;
    /** The second colour and the white or ink text on it; null for no header band. */
    header: { color: string; text?: string } | null;
    headingFont: HeadingFont | null;
    logo: BrandingImage | null;
    favicon: BrandingImage | null;
    sharingImage: BrandingImage | null;
  }
</script>

<script lang="ts">
  import { headingFont } from '@screenhire/ui/heading-fonts';
  import PreviewFrame from '#lib/components/preview-frame.svelte';

  // The branding's preview (design system A8, section 11): the careers page's header and a job in the brand colours and
  // heading font, the shared link a social site shows, and the top of an email to a candidate, each at its real size.
  // The colours are the form's own as typed (runtime custom properties over the candidate tokens), so a failing colour
  // shows as it would look. Only servable images draw.
  // `headingLevel` is 3 under a Panel title, and 2 in the pop-out, where the page's h1 is the only heading above it.
  let { draft, headingLevel = 3 }: { draft: BrandingDraft; headingLevel?: 2 | 3 } = $props();

  const logo = $derived(draft.logo?.url ? draft.logo : null);
  const favicon = $derived(draft.favicon?.url ? draft.favicon : null);
  const sharing = $derived(draft.sharingImage?.url ? draft.sharingImage : null);
  const family = $derived(headingFont(draft.headingFont).family);
</script>

<div
  class="flex min-w-0 flex-col gap-5"
  style:--candidate-cta={draft.brand?.color}
  style:--candidate-cta-foreground={draft.brand?.text}
  style:--candidate-brand-text={draft.brand?.color}
  style:--candidate-header={draft.header?.color}
  style:--candidate-header-foreground={draft.header?.text}
  style:--sh-font-display={family}
>
  <section aria-label="Careers page" class="flex flex-col gap-2">
    <svelte:element this={`h${headingLevel}`} class="text-heading">Careers page</svelte:element>
    <PreviewFrame>
      <div class="bg-candidate-background text-candidate-body text-candidate-foreground">
        <div class="flex items-center gap-2 border-b border-candidate-border bg-candidate-surface px-3 py-2 text-caption text-candidate-muted-foreground">
          {#if favicon}<img src={favicon.url} alt="" width="16" height="16" class="size-4 shrink-0 rounded-xs" />{/if}
          <span class="min-w-0 truncate">Jobs at {draft.organizationName}</span>
        </div>
        <div class="flex h-15 items-center justify-between gap-4 border-b border-candidate-border bg-candidate-header px-5 text-candidate-header-foreground">
          <span class="flex min-w-0 items-center gap-2.5 font-bold">
            {#if logo}
              <img src={logo.url} alt={draft.organizationName} width={logo.width} height={logo.height} class="h-10 w-auto max-w-60 object-contain object-left" />
            {:else}
              <span aria-hidden="true" class="grid size-8 shrink-0 place-items-center rounded-full bg-candidate-cta font-display font-extrabold text-candidate-cta-foreground">
                {draft.organizationName.charAt(0)}
              </span>
              <span class="truncate">{draft.organizationName}</span>
            {/if}
          </span>
          <span class="shrink-0 font-semibold">All jobs</span>
        </div>
        <div class="flex flex-col gap-4 px-5 py-6">
          <p class="font-display text-page-title font-bold">Open roles at {draft.organizationName}</p>
          <div class="flex flex-col gap-3 rounded-2xl border border-candidate-border bg-candidate-surface p-5">
            <p class="font-display text-panel-title font-bold">Product designer</p>
            <p class="text-candidate-muted-foreground">Remote, full time</p>
            <span class="font-semibold text-candidate-brand-text underline underline-offset-2">Read about the team</span>
            <span class="inline-flex min-h-11 items-center justify-center self-start rounded-xl bg-candidate-cta px-6 font-bold text-candidate-cta-foreground">
              Apply for this role
            </span>
          </div>
        </div>
      </div>
    </PreviewFrame>
  </section>

  <section aria-label="Shared link" class="flex flex-col gap-2">
    <svelte:element this={`h${headingLevel}`} class="text-heading">Shared link</svelte:element>
    <div class="max-w-form overflow-hidden rounded-lg border border-border bg-card">
      {#if sharing}
        <img src={sharing.url} alt="" width={sharing.width} height={sharing.height} class="aspect-[1.91/1] w-full object-cover" />
      {:else}
        <p class="bg-secondary px-4 py-6 text-muted-foreground">No sharing image: the link shows the title and description only.</p>
      {/if}
      <div class="flex flex-col gap-0.5 px-4 py-3">
        <p class="font-semibold">Jobs at {draft.organizationName}</p>
        <p class="text-muted-foreground">Open roles at {draft.organizationName}.</p>
      </div>
    </div>
  </section>

  <section aria-label="Email to a candidate" class="flex flex-col gap-2">
    <svelte:element this={`h${headingLevel}`} class="text-heading">Email to a candidate</svelte:element>
    <div class="max-w-form overflow-hidden rounded-lg border border-border bg-card">
      <div class={['flex items-center px-5 py-4', draft.header ? 'bg-candidate-header text-candidate-header-foreground' : 'border-b border-border']}>
        {#if logo}
          <img src={logo.url} alt={draft.organizationName} width={logo.width} height={logo.height} class="h-10 w-auto max-w-60 object-contain object-left" />
        {:else}
          <span class="text-panel-title font-bold">{draft.organizationName}</span>
        {/if}
      </div>
      <div class="flex flex-col gap-2 px-5 py-4">
        <p>Hi Jordan,</p>
        <p>Thank you for applying for Product designer at {draft.organizationName}.</p>
      </div>
    </div>
  </section>
</div>
