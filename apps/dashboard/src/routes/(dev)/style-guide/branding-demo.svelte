<script lang="ts">
  import type { BrandingImage, ColorContrast } from '@screenhire/api-client';
  import BrandingPreview from '#lib/components/branding-preview.svelte';
  import ColorField from '#lib/components/color-field.svelte';
  import ImageField from '#lib/components/image-field.svelte';
  import Panel from '#lib/components/panel.svelte';

  // Settings, Branding's components (design system section 11). The colours are the candidate tokens read at run time,
  // so this page holds no colour of its own: a pale one (the chip fill) that fails the links pair, and the careers
  // pages' call-to-action colour, which passes, as its nearest shade.
  const styles = getComputedStyle(document.documentElement);
  const pale = styles.getPropertyValue('--candidate-chip').trim() || 'pale';
  const shade = styles.getPropertyValue('--candidate-cta').trim() || 'shade';
  const ink = styles.getPropertyValue('--candidate-foreground').trim() || 'ink';

  let failing = $state(pale);
  const check = $derived<ColorContrast | null>(
    failing === pale
      ? {
          color: pale,
          passes: false,
          textColor: ink,
          pairs: [],
          suggestion: shade,
          problems: [`Links in ${pale} are hard to read on the page: 1.1:1, and they need 4.5:1. ${shade} is the nearest shade that passes.`],
        }
      : null,
  );
  let passing = $state('');

  const scanning: BrandingImage = {
    id: '7c1d2e3f-4a5b-4c6d-8e7f-9a0b1c2d3e4f',
    kind: 'Logo',
    scanStatus: 'Pending',
    contentType: 'image/png',
    width: 400,
    height: 100,
    sizeBytes: 24_576,
    url: null,
  };
  const blocked: BrandingImage = { ...scanning, kind: 'Favicon', scanStatus: 'Infected' };
</script>

<div class="flex flex-wrap items-start gap-5">
  <Panel title="Colour field" class="flex-[1_1_420px]">
    <div class="flex flex-col gap-5">
      <ColorField id="demo-brand" label="Brand colour" description="Buttons and links on your careers pages." bind:value={failing} {check} fallback={shade} />
      <ColorField id="demo-second" label="Second colour" description="The top of your careers pages and emails." bind:value={passing} fallback={shade} />
    </div>
  </Panel>

  <Panel title="Image field" class="flex-[1_1_420px]">
    <div class="flex flex-col gap-6">
      <ImageField id="demo-logo" noun="logo" description="PNG or JPEG, up to 1 MB." image={scanning} onchoose={() => {}} onremove={() => {}} />
      <ImageField id="demo-favicon" noun="favicon" description="A square PNG or JPEG." image={blocked} onchoose={() => {}} onremove={() => {}} />
      <ImageField id="demo-sharing" noun="sharing image" description="PNG or JPEG, at least 600 by 315 pixels." image={null} onchoose={() => {}} onremove={() => {}} />
    </div>
  </Panel>
</div>

<Panel title="Branding preview">
  <BrandingPreview
    draft={{
      organizationName: 'Brightwater Studio',
      brand: { color: shade },
      header: null,
      headingFont: 'Fraunces',
      logo: null,
      favicon: null,
      sharingImage: null,
    }}
  />
</Panel>
