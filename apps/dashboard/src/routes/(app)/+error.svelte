<script lang="ts">
  import ArrowLeftIcon from '~icons/lucide/arrow-left';
  import { page } from '$app/state';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';

  // The error page under the signed-in shell (design system section 7): an unknown address, or a page that failed to
  // load, keeps the sidebar and top bar, so the way back is the sidebar or the one button. The root error page is for
  // everything outside the shell (sign-in, the session check) and brings its own brand bar.
  const notFound = $derived(page.status === 404);
  const title = $derived(notFound ? 'Page not found' : 'Something went wrong');
  // SvelteKit's own message for an unknown address is "Not Found", which the title already says.
  const message = $derived(notFound ? 'Check the address, or go back to your jobs.' : page.error?.message || 'Try again in a minute.');
</script>

<svelte:head>
  <title>{title} · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: title }]} />

<main class="flex min-w-0 flex-col">
  <HeaderBand {title} />

  <div class="w-full px-4 py-5 md:px-6">
    <div class="max-w-form">
      <Panel title={`Error ${page.status}`} footer={back}>
        <p>{message}</p>
      </Panel>
    </div>
  </div>
</main>

{#snippet back()}
  <div class="flex flex-wrap items-center justify-end gap-2">
    <Button href="/" size="lg"><ArrowLeftIcon aria-hidden="true" />Back to jobs</Button>
  </div>
{/snippet}
