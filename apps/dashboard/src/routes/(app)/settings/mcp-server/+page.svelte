<script lang="ts">
  import KeyRoundIcon from '~icons/lucide/key-round';
  import PlugIcon from '~icons/lucide/plug';
  import UnplugIcon from '~icons/lucide/unplug';
  import type { ApiKeyScope, OAuthConnectionSummary } from '@screenhire/api-client';
  import { createGetMeta, createListOAuthConnections, createRevokeOAuthConnection } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import {
    createSortedRowModel,
    createTable,
    rowSortingFeature,
    sortFn_alphanumeric,
    sortFn_basic,
    tableFeatures,
    type ColumnDef,
  } from '@tanstack/svelte-table';
  import CodeBlock from '#lib/components/code-block.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';

  // The MCP server (docs/architecture/mcp-server.md), on the settings template: the values any MCP client needs, with no
  // client's own setup steps, and a link to API keys for the key it signs in with. The address comes from the API
  // (ScreenHire:Domains:ApiHost through /v1/meta), never from a host written here. Under it, the assistants people
  // connected by signing in (ADR-0028).
  const meta = createGetMeta();
  const apiOrigin = $derived(meta.data?.data.apiOrigin);
  const serverUrl = $derived(apiOrigin ? `${apiOrigin}/v1/mcp` : undefined);

  // The server's tools in words: HiringTools.cs, and VerificationTools.cs, which only a key with the scope sees (ADR-0021).
  const tools = [
    'Find jobs',
    'Find candidates',
    'Read an application',
    'Read the hiring report',
    'List saved candidates',
    'Add notes',
    'Add tags',
    'Remove tags',
    'Create tasks',
    'Move to a stage',
  ];
  const verificationTools = ["Read an application's verification", 'Find candidates by verification level'];

  // The connected assistants, on the API keys page's table pattern. Disconnecting ends the assistant's access at once and is
  // for good: it connects again only when someone approves it again.
  const connectionsPath = '/v1/organization/oauth-connections';
  const queryClient = useQueryClient();
  const connections = createListOAuthConnections();
  const revoke = createRevokeOAuthConnection(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, connectionsPath) } }));
  const list = $derived(connections.data?.data ?? []);

  const access = (scopes: readonly ApiKeyScope[]) => {
    const base = scopes.includes('Write') ? 'Read and write' : 'Read only';
    return scopes.includes('VerificationRead') ? `${base}, with Verification read` : base;
  };

  // The table sorts (L2), newest connection first.
  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, OAuthConnectionSummary, any>[] = [
    { id: 'status', header: 'Status', accessorFn: (connection) => (connection.revokedAt ? 'Revoked' : 'Active'), sortFn: 'alphanumeric' },
    { id: 'assistant', header: 'Assistant', accessorFn: (connection) => connection.clientName, sortFn: 'alphanumeric' },
    { id: 'access', header: 'Access', accessorFn: (connection) => access(connection.scopes), sortFn: 'alphanumeric' },
    { id: 'connectedBy', header: 'Connected by', accessorFn: (connection) => connection.connectedByName ?? '', sortFn: 'alphanumeric' },
    { id: 'lastUsed', header: 'Last used', accessorFn: (connection) => connection.lastUsedAt ?? '', sortFn: 'basic', sortDescFirst: true },
    { id: 'connected', header: 'Connected', accessorFn: (connection) => connection.createdAt, sortFn: 'basic', sortDescFirst: true },
  ];
  const sort = createTableSort('oauth-connections', { id: 'connected', desc: true }, ['status', 'assistant', 'access', 'connectedBy', 'lastUsed', 'connected']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return list;
    },
    getRowId: (connection) => connection.id,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  let announcement = $state('');
  let disconnecting = $state<OAuthConnectionSummary | null>(null);
  let disconnectOpen = $state(false);
  let disconnectChosen = false;
  const onMenuClosed = () => {
    if (disconnectChosen) disconnectOpen = true;
    disconnectChosen = false;
  };

  async function disconnect() {
    if (!disconnecting) return;
    await revoke.mutateAsync({ connectionId: disconnecting.id });
    announcement = `${disconnecting.clientName} was disconnected.`;
  }

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

<TopBar crumbs={[{ label: 'Integrations', href: '/settings/integrations' }, { label: 'MCP server' }]} back />

<main>
  <HeaderBand title="MCP server" />

  <SettingsMenu>
    <p role="status" class="sr-only">{announcement}</p>

    <Panel title="Connect a client">
      <!-- The key it signs in with is made on API keys, so that page's own action is the one in this header (A5). -->
      {#snippet actions()}
        <a href="/settings/api-keys" class={buttonVariants({ variant: 'outline' })}><KeyRoundIcon aria-hidden="true" />Create a key</a>
      {/snippet}
      <div class="flex flex-col gap-4">
        <SummaryList>
          <SummaryItem label="Server URL">
            {#if serverUrl}
              <span class="font-mono text-code break-all">{serverUrl}</span>
            {:else if meta.isError}
              <FailureText message={describeError(meta.error, "Could not find the server's address")} />
            {:else}<Skeleton class="h-4 w-56" />{/if}
          </SummaryItem>
          <SummaryItem label="Transport">Streamable HTTP</SummaryItem>
          <SummaryItem label="OAuth sign-in">
            A claude.ai custom connector needs only the server URL. You sign in and choose what it may do.
          </SummaryItem>
          <SummaryItem label="API key header"><span class="font-mono text-code break-all">Authorization: Bearer &lt;API key&gt;</span></SummaryItem>
          <SummaryItem label="API key access">Read only, or read and write, as the key allows</SummaryItem>
          <SummaryItem label="Tools" count={tools.length}>
            <ul aria-label="Tools" class="flex flex-wrap gap-1.5">
              {#each tools as tool (tool)}<li><Badge variant="muted">{tool}</Badge></li>{/each}
            </ul>
          </SummaryItem>
          <SummaryItem label="Also with Verification read" count={verificationTools.length}>
            <ul aria-label="Also with Verification read" class="flex flex-wrap gap-1.5">
              {#each verificationTools as tool (tool)}<li><Badge variant="muted">{tool}</Badge></li>{/each}
            </ul>
          </SummaryItem>
        </SummaryList>
        {#if serverUrl}
          <CodeBlock code={config(serverUrl)} label="Example client configuration" />
        {/if}
      </div>
    </Panel>

    <Panel title="Connected assistants" count={connections.isSuccess ? list.length : undefined} flush={!connections.isError}>
      {#if connections.isError}
        <QueryError message={describeError(connections.error, 'Could not load the connected assistants')} onretry={() => connections.refetch()} inset />
      {:else if connections.isPending}
        <div aria-hidden="true" class="flex flex-col">
          {#each [0, 1] as row (row)}
            <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
              <Skeleton class="h-4 w-20" />
              <Skeleton class="h-4 min-w-0 flex-1" />
              <Skeleton class="h-4 w-24" />
            </div>
          {/each}
        </div>
        <p class="sr-only" role="status">Loading the connected assistants…</p>
      {:else if list.length === 0}
        <Empty.Root>
          <Empty.Header>
            <Empty.Media variant="icon"><PlugIcon /></Empty.Media>
            <Empty.Title>No assistants connected</Empty.Title>
            <Empty.Description>Nothing has signed in yet.</Empty.Description>
          </Empty.Header>
        </Empty.Root>
      {:else}
        <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
        <ul class="md:hidden">
          {#each sorted as connection (connection.id)}
            <li class="flex min-w-0 items-start gap-2 border-b border-border-subtle px-4 py-3 last:border-0">
              <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                <span class="flex min-w-0 flex-wrap items-center gap-2">
                  {@render status(connection)}
                  <span class="min-w-0 font-semibold break-words">{connection.clientName}</span>
                </span>
                <span class="flex flex-wrap gap-1.5">
                  <Badge variant="muted">{access(connection.scopes)}</Badge>
                </span>
                <span class="text-caption text-muted-foreground">
                  {connection.connectedByName ?? 'Unknown'}, {#if connection.lastUsedAt}last used <DateText at={connection.lastUsedAt} />{:else}never used{/if}
                </span>
              </div>
              {#if !connection.revokedAt}{@render connectionMenu(connection)}{/if}
            </li>
          {/each}
        </ul>
        <div class="overflow-x-auto max-md:hidden">
          <Table.Root>
            <Table.Header>
              {#each table.getHeaderGroups() as group (group.id)}
                <Table.Row class="bg-muted hover:bg-muted">
                  {#each group.headers as header (header.id)}
                    <SortHeader {header} class={header.id === 'status' ? 'px-4.5' : undefined} />
                  {/each}
                  <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                </Table.Row>
              {/each}
            </Table.Header>
            <Table.Body>
              {#each sorted as connection (connection.id)}
                <Table.Row class="h-12">
                  <Table.Cell class="px-4.5">{@render status(connection)}</Table.Cell>
                  <Table.Cell class="w-full max-w-0"><TruncatedText text={connection.clientName} class="font-semibold" /></Table.Cell>
                  <Table.Cell><TruncatedText text={access(connection.scopes)} /></Table.Cell>
                  <Table.Cell class="text-muted-foreground"><TruncatedText text={connection.connectedByName ?? 'Unknown'} /></Table.Cell>
                  <Table.Cell class="text-muted-foreground">{#if connection.lastUsedAt}<DateText at={connection.lastUsedAt} />{:else}Never{/if}</Table.Cell>
                  <Table.Cell class="text-muted-foreground"><DateText at={connection.createdAt} /></Table.Cell>
                  <Table.Cell class="pr-4.5 text-right">{#if !connection.revokedAt}{@render connectionMenu(connection)}{/if}</Table.Cell>
                </Table.Row>
              {/each}
            </Table.Body>
          </Table.Root>
        </div>
      {/if}
    </Panel>
  </SettingsMenu>
</main>

{#snippet status(connection: OAuthConnectionSummary)}
  {#if connection.revokedAt}<StatusBadge status="notInEffect" label="Revoked" quiet />{:else}<StatusBadge status="done" label="Active" quiet />{/if}
{/snippet}

{#snippet connectionMenu(connection: OAuthConnectionSummary)}
  <MoreMenu label={`Actions for ${connection.clientName}`} size="icon" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Item
      variant="destructive"
      onSelect={() => {
        disconnecting = connection;
        disconnectChosen = true;
      }}><UnplugIcon aria-hidden="true" />Disconnect assistant</DropdownMenu.Item
    >
  </MoreMenu>
{/snippet}

<ConfirmDialog
  bind:open={disconnectOpen}
  title={`Disconnect ${disconnecting?.clientName ?? 'this assistant'}?`}
  description="It loses access at once. It can connect again only if someone approves it again."
  cancelLabel="Keep it"
  confirmLabel="Disconnect assistant"
  failure="Could not disconnect the assistant"
  onconfirm={disconnect}
>
  {#snippet icon()}<UnplugIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>
