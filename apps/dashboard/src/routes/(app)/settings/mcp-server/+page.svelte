<script lang="ts">
  import KeyRoundIcon from '@lucide/svelte/icons/key-round';
  import { createGetMeta } from '@screenhire/api-client/svelte-query';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { describeError } from '#lib/query.js';

  // The MCP server (docs/architecture/mcp-server.md), on the settings template: the values any MCP client needs, with no
  // client's own setup steps, and a link to API keys for the key it signs in with. The address comes from the API
  // (ScreenHire:Domains:ApiHost through /v1/meta), never from a host written here.
  const meta = createGetMeta();
  const apiOrigin = $derived(meta.data?.data.apiOrigin);
  const serverUrl = $derived(apiOrigin ? `${apiOrigin}/v1/mcp` : undefined);

  // The server's tools (apps/api/Mcp/HiringTools.cs), in words.
  const tools = ['Find jobs', 'Find candidates', 'Read an application', 'Add notes', 'Add tags', 'Remove tags', 'Create tasks', 'Move to a stage'];

  // The shape most clients' config files share; each client documents where its file lives.
  const config = (url: string) => `{
  "mcpServers": {
    "screenhire": {
      "type": "http",
      "url": "${url}",
      "headers": { "Authorization": "Bearer <your API key>" }
    }
  }
}`;
</script>

<svelte:head>
  <title>MCP server · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'MCP server' }]} />

<main>
  <HeaderBand title="MCP server" />

  <SettingsMenu>
    <Panel title="Connect a client">
      <div class="flex flex-col gap-4">
        <p class="max-w-measure">Any MCP client can work in ScreenHire with an API key. It never sees verification results.</p>
        <SummaryList>
          <SummaryItem label="Server URL">
            {#if serverUrl}
              <span class="font-mono text-code break-all">{serverUrl}</span>
            {:else if meta.isError}
              <FailureText message={describeError(meta.error, "Could not find the server's address")} />
            {:else}<Skeleton class="h-4 w-56" />{/if}
          </SummaryItem>
          <SummaryItem label="Transport">Streamable HTTP</SummaryItem>
          <SummaryItem label="Header"><span class="font-mono text-code break-all">Authorization: Bearer &lt;API key&gt;</span></SummaryItem>
          <SummaryItem label="Access">Read only, or read and write, as the key allows</SummaryItem>
          <SummaryItem label="Tools">
            <ul aria-label="Tools" class="flex flex-wrap gap-1.5">
              {#each tools as tool (tool)}<li><Badge variant="muted">{tool}</Badge></li>{/each}
            </ul>
          </SummaryItem>
        </SummaryList>
        {#if serverUrl}
          <pre aria-label="Example client configuration" class="rounded-lg bg-secondary px-3.5 py-3 font-mono text-code break-all whitespace-pre-wrap">{config(serverUrl)}</pre>
        {/if}
        <div>
          <a href="/settings/api-keys" class={buttonVariants({ variant: 'outline', size: 'lg' })}><KeyRoundIcon aria-hidden="true" />Create a key</a>
        </div>
      </div>
    </Panel>
  </SettingsMenu>
</main>
