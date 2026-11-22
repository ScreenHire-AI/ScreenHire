<script lang="ts">
  import TypeIcon from '~icons/lucide/type';
  import type { HeadingFontCatalogueResponse } from '@screenhire/api-client';
  import { getListHeadingFontsQueryKey } from '@screenhire/api-client/svelte-query';
  import { QueryClient, setQueryClientContext } from '@tanstack/svelte-query';
  import HeadingFontSheet from '#lib/components/heading-font-sheet.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The heading font picker with sample data (design system section 11): the style guide has no API, so this example
  // brings its own query client, filled in advance and never refetched. The catalogue is the bundled fonts, which the
  // dashboard ships, so every name draws in its own font without a preview from the server. Fraunces is in use.
  const client = new QueryClient({ defaultOptions: { queries: { staleTime: Infinity, retry: false } } });
  const catalogue: HeadingFontCatalogueResponse = {
    complete: true,
    fonts: [
      { id: 'fraunces', family: 'Fraunces', styles: ['Serif'], license: 'Ofl', bundled: true },
      { id: 'inter', family: 'Inter', styles: ['Sans'], license: 'Ofl', bundled: true },
      { id: 'montserrat', family: 'Montserrat', styles: ['Sans'], license: 'Ofl', bundled: true },
      { id: 'nunito', family: 'Nunito', styles: ['Sans', 'Rounded'], license: 'Ofl', bundled: true },
      { id: 'playfair-display', family: 'Playfair Display', styles: ['Serif', 'Display'], license: 'Ofl', bundled: true },
    ],
  };
  client.setQueryData(getListHeadingFontsQueryKey(), { status: 200, headers: new Headers(), data: catalogue });
  setQueryClientContext(client);

  let open = $state(false);
  let value = $state('fraunces');
</script>

<Button variant="outline" onclick={() => (open = true)}><TypeIcon aria-hidden="true" />More fonts</Button>
<HeadingFontSheet bind:open {value} saved="fraunces" onchoose={(font) => (value = font.id)} />
