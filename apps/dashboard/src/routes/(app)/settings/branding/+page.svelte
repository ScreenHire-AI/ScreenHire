<script lang="ts">
  import ExternalLinkIcon from '~icons/lucide/external-link';
  import SaveIcon from '~icons/lucide/save';
  import type { HeadingFont, HttpValidationProblemDetails } from '@screenhire/api-client';
  import {
    ApiError,
    createCheckBrandingContrast,
    createGetOrganization,
    createGetOrganizationBranding,
    getGetOrganizationBrandingHistoryQueryOptions,
    createUpdateOrganizationBranding,
    getGetOrganizationBrandingQueryKey,
  } from '@screenhire/api-client/svelte-query';
  import { defaultHeadingFont, headingFonts } from '@screenhire/ui/heading-fonts';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { defaults, superForm } from 'sveltekit-superforms';
  import { zod4 } from 'sveltekit-superforms/adapters';
  import { z } from 'zod';
  import BrandingPreview, { type BrandingDraft } from '#lib/components/branding-preview.svelte';
  import ColorField from '#lib/components/color-field.svelte';
  import EditPreviewLayout from '#lib/components/edit-preview-layout.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistoryFact from '#lib/components/history-fact.svelte';
  import ImageField from '#lib/components/image-field.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import { settingsCrumbs } from '#lib/settings.js';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { BrandImageSlot } from '#lib/brand-images.svelte.js';
  import { brandColorPattern, normalizeBrandColor } from '#lib/branding.js';
  import { applyValidationProblem } from '#lib/forms.js';
  import { createPreviewSource } from '#lib/preview-window.svelte.js';
  import { describeError } from '#lib/query.js';

  // Settings, Branding (docs/architecture/branding.md, design system section 18): the brand colour and the second
  // colour, each checked against WCAG 2.2 AA as it is typed and again on Save, the heading font, and the logo, favicon
  // and sharing image. The careers pages, the apply flow and every email to a candidate change only on Save, which
  // History records. Candidates see the result, so the Edit / Preview switch shows it (A8). The editor is wide: Colours and
  // Heading font side by side and the three images in a row, so at 1440 by 900 it fits without scrolling, even with all three
  // images set, and each stacks where its row would be cramped. Save sits in the switch's row, which stays in view while the page scrolls.
  const queryClient = useQueryClient();
  const branding = createGetOrganizationBranding();
  const organization = createGetOrganization();
  const detail = $derived(branding.data?.data);
  const update = createUpdateOrganizationBranding(() => ({
    mutation: { onSuccess: () => queryClient.invalidateQueries({ queryKey: getGetOrganizationBrandingQueryKey(), exact: true }) },
  }));

  const colour = (what: string) =>
    z.string().trim().refine((value) => value === '' || brandColorPattern.test(value), `Enter the ${what} as six hex digits, such as 1f4e5a.`);
  const schema = z.object({
    brandColor: colour('brand colour'),
    secondColor: colour('second colour'),
    headingFont: z.string(),
    logoId: z.string(),
    faviconId: z.string(),
    sharingImageId: z.string(),
  });

  const blank = { brandColor: '', secondColor: '', headingFont: '', logoId: '', faviconId: '', sharingImageId: '' };

  const logo = new BrandImageSlot('Logo', 'logo');
  const favicon = new BrandImageSlot('Favicon', 'favicon');
  const sharingImage = new BrandImageSlot('SharingImage', 'sharing image');
  let saved = $state('');
  let mode = $state<'edit' | 'preview'>('edit');

  const colourOrNull = (text: string) => (brandColorPattern.test(text.trim()) ? normalizeBrandColor(text) : null);

  const { form, errors, enhance, submitting, reset } = superForm(defaults(blank, zod4(schema)), {
    SPA: true,
    validators: zod4(schema),
    resetForm: false,
    applyAction: false,
    invalidateAll: false,
    async onUpdate({ form: submitted }) {
      saved = '';
      if (!submitted.valid) return;
      try {
        await update.mutateAsync({
          data: {
            brandColor: colourOrNull(submitted.data.brandColor),
            secondColor: colourOrNull(submitted.data.secondColor),
            headingFont: (submitted.data.headingFont || null) as HeadingFont | null,
            logoId: logo.image?.id ?? null,
            faviconId: favicon.image?.id ?? null,
            sharingImageId: sharingImage.image?.id ?? null,
          },
        });
        saved = 'Saved.';
      } catch (error) {
        if (error instanceof ApiError && error.status === 400) {
          applyValidationProblem(submitted, error.body as HttpValidationProblemDetails);
        } else {
          submitted.valid = false;
          submitted.errors._errors = [describeError(error, 'Could not save the branding')];
        }
      }
    },
    // Save can be pressed in Preview, where a field's message is hidden, so a refused field brings the editor back. A
    // failure that belongs to no field shows in the switch's row and stays where it is.
    onUpdated({ form: submitted }) {
      if (Object.entries(submitted.errors).some(([name, messages]) => name !== '_errors' && messages)) mode = 'edit';
    },
  });

  // The form starts from the saved branding once it loads; a refetch after that keeps what was typed.
  let started = false;
  $effect(() => {
    if (detail && !started) {
      started = true;
      reset({
        data: { ...blank, brandColor: detail.brandColor ?? '', secondColor: detail.secondColor ?? '', headingFont: detail.headingFont ?? '' },
      });
      logo.image = detail.logo;
      favicon.image = detail.favicon;
      sharingImage.image = detail.sharingImage;
    }
  });

  // The contrast check follows the colours a moment after typing stops, for whole colours only.
  let asked = $state<{ brandColor?: string; secondColor?: string }>({});
  $effect(() => {
    const next = { brandColor: colourOrNull($form.brandColor) ?? undefined, secondColor: colourOrNull($form.secondColor) ?? undefined };
    const timer = setTimeout(() => (asked = next), 250);
    return () => clearTimeout(timer);
  });
  const contrast = createCheckBrandingContrast(
    () => asked,
    () => ({ query: { enabled: !!(asked.brandColor || asked.secondColor) } }),
  );
  const checks = $derived(contrast.data?.data);

  // What the pickers show while a field is empty: the careers pages' own defaults (the candidate tokens).
  const styles = getComputedStyle(document.documentElement);
  const ctaDefault = styles.getPropertyValue('--candidate-cta').trim();
  const headerDefault = styles.getPropertyValue('--candidate-header').trim();

  const scanning = $derived(logo.scanning || favicon.scanning || sharingImage.scanning);

  const draft = $derived.by((): BrandingDraft => {
    const brand = colourOrNull($form.brandColor);
    const second = colourOrNull($form.secondColor);
    return {
      organizationName: organization.data?.data.name ?? '',
      brand: brand ? { color: brand, text: checks?.brandColor?.color === brand ? checks.brandColor.textColor : undefined } : null,
      header: second ? { color: second, text: checks?.secondColor?.color === second ? checks.secondColor.textColor : undefined } : null,
      headingFont: ($form.headingFont || null) as HeadingFont | null,
      logo: logo.image,
      favicon: favicon.image,
      sharingImage: sharingImage.image,
    };
  });

  const source = createPreviewSource(() => draft);

  const images = $derived([
    { id: 'logo', kind: 'logo' as const, noun: 'logo', maxBytes: 1024 * 1024, slot: logo, errors: $errors.logoId, description: 'PNG or JPEG, up to 1 MB. At the top of your careers pages and emails.' },
    {
      id: 'favicon',
      kind: 'favicon' as const,
      noun: 'favicon',
      maxBytes: 256 * 1024,
      slot: favicon,
      errors: $errors.faviconId,
      description: 'A square PNG or JPEG, at least 32 pixels, up to 256 KB. In the browser tab.',
    },
    {
      id: 'sharing-image',
      kind: 'sharing' as const,
      noun: 'sharing image',
      maxBytes: 5 * 1024 * 1024,
      slot: sharingImage,
      errors: $errors.sharingImageId,
      description: 'PNG or JPEG, at least 600 by 315 pixels, up to 5 MB. Shown on shared links.',
    },
  ]);

  // The editor's layout, shared with the loading skeleton so the page does not jump when the branding arrives. Colours and
  // Heading font share a row from 52rem of editor width, and the three images are one row there, two from 34rem and one below.
  const topRow = 'grid min-w-0 gap-5 @[52rem]/editor:grid-cols-[minmax(0,4fr)_minmax(0,5fr)]';
  const imageRow = 'grid gap-6 @[34rem]/editor:grid-cols-2 @[52rem]/editor:grid-cols-3';

  const fonts = [
    { value: '', title: defaultHeadingFont.label, description: "ScreenHire's", family: defaultHeadingFont.family },
    ...Object.entries(headingFonts).map(([value, font]) => ({ value, title: font.label, description: undefined, family: font.family })),
  ];
</script>

<svelte:head>
  <title>Branding · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs('/settings/branding')} />

<main class="flex flex-col">
  <HeaderBand title="Branding" oneRow>
    {#snippet actions()}
      {#if detail}<HistoryFact value={detail.lastUpdated} name="Branding" page={(before) => getGetOrganizationBrandingHistoryQueryOptions({ before })} />{/if}
    {/snippet}
  </HeaderBand>

  <SettingsMenu>
    {#if branding.isError && !detail}
      <div class="flex w-full max-w-form flex-col gap-5">
        <Panel title="Colours">
          <QueryError message={describeError(branding.error, 'Could not load the branding')} onretry={() => branding.refetch()} inset />
        </Panel>
      </div>
    {:else if !detail}
      <div class="@container/editor flex w-full min-w-0 flex-col gap-4">
        <div aria-hidden="true" class="flex gap-2"><Skeleton class="h-9 w-20" /><Skeleton class="h-9 w-24" /></div>
        <div class="flex min-w-0 flex-col gap-5">
          <div class={topRow}>
            <Panel title="Colours">
              <div aria-hidden="true" class="grid gap-5 @sm:grid-cols-2"><Skeleton class="h-24 w-full" /><Skeleton class="h-24 w-full" /></div>
              <p class="sr-only" role="status">Loading the branding…</p>
            </Panel>
            <Panel title="Heading font">
              <div aria-hidden="true" class="grid gap-2 @md:grid-cols-2">
                {#each Array.from({ length: 6 }, (_, index) => index) as index (index)}<Skeleton class="h-14 w-full" />{/each}
              </div>
            </Panel>
          </div>
          <Panel title="Images">
            <div aria-hidden="true" class={imageRow}>
              {#each Array.from({ length: 3 }, (_, index) => index) as index (index)}<Skeleton class="h-52 w-full" />{/each}
            </div>
          </Panel>
        </div>
      </div>
    {:else}
      <EditPreviewLayout wide bind:mode onpopout={() => source.open('/preview/branding')}>
        {#snippet actions()}
          <FailureText message={$errors._errors?.[0]} />
          <p role="status" class="text-muted-foreground empty:hidden">
            {$errors._errors ? '' : scanning ? 'Save waits for the malware scan.' : saved}
          </p>
          <Button type="submit" form="branding-form" size="lg" aria-label="Save branding" disabled={$submitting || scanning}><SaveIcon aria-hidden="true" />Save</Button>
        {/snippet}

        {#snippet editor()}
          <form id="branding-form" method="POST" use:enhance novalidate aria-label="Branding" class="flex min-w-0 flex-col gap-5">
            <div class={topRow}>
              <Panel title="Colours">
                <div class="@container">
                  <div class="grid gap-5 @[22rem]:grid-cols-2">
                    <ColorField
                      id="brand-color"
                      label="Brand colour"
                      description="Buttons and links. Empty is ScreenHire's."
                      bind:value={$form.brandColor}
                      check={checks?.brandColor}
                      errors={$errors.brandColor}
                      fallback={ctaDefault}
                    />
                    <ColorField
                      id="second-color"
                      label="Second colour"
                      description="The top of your pages and emails. Empty is none."
                      bind:value={$form.secondColor}
                      check={checks?.secondColor}
                      errors={$errors.secondColor}
                      fallback={headerDefault}
                      placeholder="None"
                    />
                  </div>
                </div>
              </Panel>

              <Panel title="Heading font">
                <div class="@container">
                  <div role="radiogroup" aria-label="Heading font" class="grid gap-2 @md:grid-cols-2 @[40rem]:grid-cols-3">
                    {#each fonts as font (font.value)}
                      <label
                        class="flex items-start gap-2 rounded-lg border border-input p-2.5 has-checked:border-primary has-checked:bg-accent has-focus-visible:ring-2 has-focus-visible:ring-ring"
                      >
                        <input type="radio" name="headingFont" value={font.value} bind:group={$form.headingFont} class="mt-1 size-4 shrink-0 accent-primary" />
                        <span class="flex min-w-0 flex-col gap-0.5">
                          <span class="text-heading font-bold break-words" style:font-family={font.family}>{font.title}</span>
                          {#if font.description}<span class="text-muted-foreground">{font.description}</span>{/if}
                        </span>
                      </label>
                    {/each}
                  </div>
                </div>
              </Panel>
            </div>

            <Panel title="Images">
              <div class={imageRow}>
                {#each images as { id, kind, noun, maxBytes, description, slot, errors } (id)}
                  <ImageField
                    {id}
                    {kind}
                    organizationName={draft.organizationName}
                    {noun}
                    {maxBytes}
                    {description}
                    image={slot.image}
                    busy={slot.uploading}
                    errors={[...(errors ?? []), slot.failure]}
                    onchoose={(file) => slot.choose(file)}
                    onremove={() => slot.remove()}
                  />
                {/each}
              </div>
            </Panel>
          </form>
        {/snippet}

        {#snippet preview()}
          <Panel title="Preview">
            {#snippet actions()}
              {#if organization.data}
                <Button href={organization.data.data.careersUrl} target="_blank" rel="noopener" variant="outline">
                  <ExternalLinkIcon aria-hidden="true" />View<span class="sr-only"> careers page (opens in a new tab)</span>
                </Button>
              {/if}
            {/snippet}
            <div class="flex flex-col gap-4">
              <p class="text-muted-foreground">This follows your unsaved changes. Candidates see none of it until you save.</p>
              <BrandingPreview {draft} />
            </div>
          </Panel>
        {/snippet}
      </EditPreviewLayout>
    {/if}
  </SettingsMenu>
</main>
