<script lang="ts">
  import MailIcon from '@lucide/svelte/icons/mail';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import type { EmailTemplateEntry } from '@screenhire/api-client';
  import { createListEmailTemplates } from '@screenhire/api-client/svelte-query';
  import ActionBar from '#lib/components/action-bar.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { describeError } from '#lib/query.js';

  // The organization's email templates, on the list template: the built-in ones, whose wording it may change, then its
  // own. Each row opens the template's editor.
  const sidebar = Sidebar.useSidebar();
  const templates = createListEmailTemplates();
  const list = $derived(templates.data?.data.templates ?? []);
  const href = (id: string) => `/settings/email-templates/${encodeURIComponent(id)}`;
  const kind = (template: EmailTemplateEntry) => (template.builtIn ? (template.edited ? 'Built-in, edited' : 'Built-in') : 'Your own');
</script>

<svelte:head>
  <title>Email templates · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Settings' }, { label: 'Email templates' }]} />

<main class={[sidebar.isMobile && 'pb-28']}>
  <HeaderBand
    title="Email templates"
    count={templates.isSuccess ? list.length : undefined}
    countLabel={list.length === 1 ? 'template' : 'templates'}
    actions={sidebar.isMobile ? undefined : addButton}
  />

  <div class="flex w-full flex-col gap-5 px-4 py-6 md:px-6">
    {#if templates.isError}
      <Panel title="Templates">
        <QueryError message={describeError(templates.error, 'Could not load the templates')} onretry={() => templates.refetch()} inset />
      </Panel>
    {:else}
      <Panel title="Templates" flush>
        {#if templates.isPending}
          <div aria-hidden="true" class="flex flex-col">
            {#each [0, 1, 2] as row (row)}
              <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                <Skeleton class="h-4 w-40" />
                <Skeleton class="h-4 min-w-0 flex-1" />
                <Skeleton class="h-4 w-24" />
              </div>
            {/each}
          </div>
          <p class="sr-only" role="status">Loading the templates…</p>
        {:else if list.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><MailIcon /></Empty.Media>
              <Empty.Title>No templates yet</Empty.Title>
            </Empty.Header>
            <Empty.Content><Button href="/settings/email-templates/new" variant="outline" size="lg"><PlusIcon aria-hidden="true" />Add template</Button></Empty.Content>
          </Empty.Root>
        {:else}
          <ul class="md:hidden">
            {#each list as template (template.id)}
              <ListItem href={href(template.id)} title={template.name}>
                {#snippet facts()}
                  <Badge variant="muted">{kind(template)}</Badge>
                  <Badge variant="muted" class="max-w-full"><span class="truncate">{template.subject}</span></Badge>
                {/snippet}
              </ListItem>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                <Table.Row class="bg-muted hover:bg-muted">
                  <Table.Head class="px-4.5">Template</Table.Head>
                  <Table.Head>Subject</Table.Head>
                  <Table.Head>Type</Table.Head>
                  <Table.Head class="pr-4.5 text-right">Changed</Table.Head>
                </Table.Row>
              </Table.Header>
              <Table.Body>
                {#each list as template (template.id)}
                  <!-- The whole row is the link (design system section 9): the template's link covers it. -->
                  <Table.Row class="relative h-12">
                    <Table.Cell class="max-w-0 px-4.5 md:w-1/3">
                      <span class="flex min-w-0">
                        <TruncatedText
                          text={template.name}
                          href={href(template.id)}
                          class="font-semibold text-foreground no-underline after:absolute after:inset-0 hover:underline"
                        />
                      </span>
                    </Table.Cell>
                    <Table.Cell class="w-full max-w-0 text-muted-foreground"><span class="flex min-w-0"><TruncatedText text={template.subject} /></span></Table.Cell>
                    <Table.Cell class="whitespace-nowrap">{kind(template)}</Table.Cell>
                    <Table.Cell class="pr-4.5 text-right text-muted-foreground">
                      {#if template.updatedAt}<span class="relative"><DateText at={template.updatedAt} /></span>{/if}
                    </Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}
  </div>

  {#if sidebar.isMobile}
    <ActionBar>{@render addButton()}</ActionBar>
  {/if}
</main>

{#snippet addButton()}
  <Button href="/settings/email-templates/new" size="lg"><PlusIcon aria-hidden="true" />Add template</Button>
{/snippet}
