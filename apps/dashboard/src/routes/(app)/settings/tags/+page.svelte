<script lang="ts">
  import CombineIcon from '~icons/lucide/combine';
  import PencilIcon from '~icons/lucide/pencil';
  import Trash2Icon from '~icons/lucide/trash-2';
  import type { TagChangeResult, TagSummary } from '@screenhire/api-client';
  import { createDeleteTag, createListTags, createMergeTag, createRenameTag } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { createSortedRowModel, createTable, rowSortingFeature, sortFn_alphanumeric, sortFn_basic, tableFeatures, type ColumnDef } from '@tanstack/svelte-table';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MergeTagDialog from '#lib/components/merge-tag-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Notice from '#lib/components/notice.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RenameTagDialog from '#lib/components/rename-tag-dialog.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import SortHeader from '#lib/components/sort-header.svelte';
  import SortMenu from '#lib/components/sort-menu.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, jobsPath, tagsPath, templatesPath, viewsPath } from '#lib/realtime.js';
  import { settingsCrumbs } from '#lib/settings.js';
  import { createTableSort, tableSortOptions } from '#lib/table-sort.svelte.js';
  import { rowLink } from '#lib/row-link.js';

  // The organization's tags (design system section 21, Settings, Tags) on the list template: each tag, how many
  // candidates have it and the hiring templates whose published version names it. A row's menu renames it, merges it
  // into another tag or deletes it. A rename or a merge also changes the automations of every open job and template
  // draft and the saved views that filter on it; a published version never changes, so a template that still names the
  // old tag stays on its row until a new version is published. The candidates change a moment later, in batches, and
  // ApplicationTagsChanged refreshes the counts.
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const catalogue = createListTags();
  const tags = $derived<TagSummary[]>(catalogue.data?.data.tags ?? []);
  // Past about ten, a list has a search (S9); it matches the tag's name, ignoring case.
  let search = $state('');
  const shown = $derived(search.trim() ? tags.filter((tag) => tag.name.toLowerCase().includes(search.trim().toLowerCase())) : tags);
  const refresh = () => Promise.all([tagsPath, jobsPath, applicationsPath, candidatesPath, templatesPath, viewsPath].map((path) => invalidatePath(queryClient, path)));
  const rename = createRenameTag(() => ({ mutation: { onSettled: refresh } }));
  const merge = createMergeTag(() => ({ mutation: { onSettled: refresh } }));
  const remove = createDeleteTag(() => ({ mutation: { onSettled: refresh } }));

  const templatesOf = (tag: TagSummary) => tag.templates.map((template) => `${template.name} (version ${template.version})`).join(', ');
  const candidates = (count: number) => (count === 1 ? '1 candidate' : `${count} candidates`);

  const features = tableFeatures({ rowSortingFeature, sortedRowModel: createSortedRowModel(), sortFns: { basic: sortFn_basic, alphanumeric: sortFn_alphanumeric } });
  // `any`: one list mixes columns of different value types.
  const columns: ColumnDef<typeof features, TagSummary, any>[] = [
    { id: 'tag', header: 'Tag', accessorFn: (tag) => tag.name, sortFn: 'alphanumeric' },
    { id: 'candidates', header: 'Candidates', accessorFn: (tag) => tag.applications, sortFn: 'basic', sortDescFirst: true },
    { id: 'templates', header: 'Published templates', accessorFn: (tag) => templatesOf(tag), sortFn: 'alphanumeric' },
  ];
  // The API's order, most used first, until someone sorts (L2).
  const sort = createTableSort('tags', undefined, ['tag', 'candidates', 'templates']);
  const table = createTable({
    ...tableSortOptions(() => sort),
    features,
    columns,
    get data() {
      return shown;
    },
    getRowId: (tag) => tag.name,
  });
  const sorted = $derived(table.getRowModel().rows.map((row) => row.original));

  // Rename, Merge and Delete open their dialog once the row's menu has closed, so focus can go back to the menu.
  let chosen = $state<{ tag: TagSummary; action: 'rename' | 'merge' | 'delete' } | null>(null);
  let renaming = $state<string | null>(null);
  let merging = $state<string | null>(null);
  let deleting = $state<TagSummary | null>(null);
  let deleteOpen = $state(false);
  function onMenuClosed() {
    if (chosen?.action === 'rename') renaming = chosen.tag.name;
    else if (chosen?.action === 'merge') merging = chosen.tag.name;
    else if (chosen?.action === 'delete') {
      deleting = chosen.tag;
      deleteOpen = true;
    }
    chosen = null;
  }
  const others = $derived(tags.filter((tag) => tag.name !== merging).map((tag) => tag.name));

  // What the last change did, for screen readers; a published template that still names the old tag is a notice, because
  // someone has to publish a new version of it, and it stays until the next change.
  let announcement = $state('');
  let stale = $state<{ tag: string; templates: TagChangeResult['stillNamedBy'] } | null>(null);
  function said(done: string, old: string, result: TagChangeResult) {
    announcement = done;
    stale = result.stillNamedBy.length > 0 ? { tag: old, templates: result.stillNamedBy } : null;
  }

  async function onrename(tag: string, name: string): Promise<string | null> {
    stale = null;
    try {
      said(`Renamed "${tag}" to "${name}".`, tag, (await rename.mutateAsync({ data: { tag, newName: name } })).data);
      return null;
    } catch (error) {
      return describeError(error, 'Could not rename the tag');
    }
  }

  async function onmerge(tag: string, into: string): Promise<string | null> {
    stale = null;
    try {
      said(`Merged "${tag}" into "${into}".`, tag, (await merge.mutateAsync({ data: { tag, into } })).data);
      return null;
    } catch (error) {
      return describeError(error, 'Could not merge the tags');
    }
  }

  async function deleteTag() {
    if (!deleting) return;
    const name = deleting.name;
    stale = null;
    await remove.mutateAsync({ params: { tag: name } });
    announcement = `Deleted the tag "${name}".`;
  }
</script>

<svelte:head>
  <title>Tags · ScreenHire</title>
</svelte:head>

<TopBar crumbs={settingsCrumbs('/settings/tags')} />

<main class={[sidebar.isMobile && 'pb-28']}>
  <HeaderBand title="Tags" frame oneRow />

  <SettingsMenu>
    <p role="status" class="sr-only">{announcement}</p>
    {#if stale}
      {@const names = stale.templates.map((template) => template.name).join(', ')}
      {@const one = stale.templates.length === 1 ? stale.templates[0] : undefined}
      <Notice href={one ? `/templates/${one.templateId}` : undefined} link={one ? `Open ${one.name}` : undefined}>
        {names} still {one ? 'names' : 'name'} "{stale.tag}" in {one ? 'its' : 'their'} published version. Publish a new version to use the new name.
      </Notice>
    {/if}
    {#if catalogue.isError}
      <Panel title="Tags">
        <QueryError message={describeError(catalogue.error, 'Could not load the tags')} onretry={() => catalogue.refetch()} inset />
      </Panel>
    {:else}
      <Panel title="Tags" count={catalogue.isSuccess ? tags.length : undefined} flush>
        {#snippet tools()}
          {#if tags.length > 10}<SearchField bind:value={search} label="Search tags" placeholder="Search" class="ml-auto flex-[0_1_15rem] max-md:ml-0 max-md:flex-1" />{/if}
        {/snippet}
        {#if catalogue.isPending}
          <div aria-hidden="true" class="flex flex-col">
            {#each [0, 1, 2, 3] as row (row)}
              <div class="flex h-12 items-center gap-4 border-b border-border-subtle px-4.5 last:border-0 max-md:px-4">
                <Skeleton class="h-4 w-32" />
                <Skeleton class="h-4 w-16" />
                <Skeleton class="h-4 min-w-0 flex-1" />
              </div>
            {/each}
          </div>
          <p class="sr-only" role="status">Loading the tags…</p>
        {:else if tags.length === 0}
          <Empty.Root>
            <Empty.Header>
              <Empty.Title>No tags yet</Empty.Title>
              <Empty.Description>Tags you add to candidates show here.</Empty.Description>
            </Empty.Header>
          </Empty.Root>
        {:else}
          {#if shown.length === 0}
            <p class="border-b border-border-subtle px-4.5 py-3 text-muted-foreground max-md:px-4">No tag matches the search.</p>
          {/if}
          <div class="flex justify-end border-b border-border-subtle px-4 py-2 md:hidden"><SortMenu {table} /></div>
          <ul class="md:hidden">
            {#each sorted as tag (tag.name)}
              <li class="flex min-h-12 items-center gap-3 border-b border-border-subtle px-4 py-2 last:border-0">
                <span class="flex min-w-0 flex-1 flex-col gap-1">
                  <span class="min-w-0 font-semibold break-words">{tag.name}</span>
                  <span class="flex flex-wrap gap-1">
                    <Badge variant="muted">{candidates(tag.applications)}</Badge>
                    {#if tag.templates.length > 0}<span class="text-caption text-muted-foreground">In {templatesOf(tag)}</span>{/if}
                  </span>
                </span>
                {@render tagMenu(tag)}
              </li>
            {/each}
          </ul>
          <div class="overflow-x-auto max-md:hidden">
            <Table.Root>
              <Table.Header>
                {#each table.getHeaderGroups() as group (group.id)}
                  <Table.Row class="bg-muted hover:bg-muted">
                    {#each group.headers as header (header.id)}
                      <SortHeader {header} class={header.id === 'tag' ? 'px-4.5' : undefined} />
                    {/each}
                    <Table.Head class="w-12 pr-4.5"><span class="sr-only">Actions</span></Table.Head>
                  </Table.Row>
                {/each}
              </Table.Header>
              <Table.Body>
                {#each sorted as tag (tag.name)}
                  <!-- A click anywhere on the row opens the tag to rename (L5): its name is the button. -->
                  <Table.Row {@attach rowLink} class="h-12 hover:bg-muted">
                    <Table.Cell class="max-w-64 px-4.5">
                      <span class="flex min-w-0">
                        <TruncatedText
                          text={tag.name}
                          data-row-link
                          aria-label={`Rename ${tag.name}`}
                          class="font-semibold underline-offset-2 hover:underline pointer-coarse:min-h-11"
                          onclick={() => (renaming = tag.name)}
                        />
                      </span>
                    </Table.Cell>
                    <Table.Cell class="tabular-nums">{tag.applications}</Table.Cell>
                    <Table.Cell class="w-full max-w-0 min-w-48">
                      <span class="flex min-w-0"><TruncatedText text={templatesOf(tag)} /></span>
                    </Table.Cell>
                    <Table.Cell class="pr-4.5 text-right">{@render tagMenu(tag)}</Table.Cell>
                  </Table.Row>
                {/each}
              </Table.Body>
            </Table.Root>
          </div>
        {/if}
      </Panel>
    {/if}
  </SettingsMenu>
</main>

{#snippet tagMenu(tag: TagSummary)}
  <MoreMenu label={`Actions for ${tag.name}`} size="icon" quiet onclosed={onMenuClosed}>
    <DropdownMenu.Item onSelect={() => (chosen = { tag, action: 'rename' })}><PencilIcon aria-hidden="true" />Rename</DropdownMenu.Item>
    {#if tags.length > 1}
      <DropdownMenu.Item onSelect={() => (chosen = { tag, action: 'merge' })}><CombineIcon aria-hidden="true" />Merge into another tag</DropdownMenu.Item>
    {/if}
    {#if tag.applications > 0}
      <DropdownMenu.Item variant="destructive" onSelect={() => (chosen = { tag, action: 'delete' })}><Trash2Icon aria-hidden="true" />Delete tag</DropdownMenu.Item>
    {/if}
  </MoreMenu>
{/snippet}

<RenameTagDialog tag={renaming} {onrename} onclose={() => (renaming = null)} />
<MergeTagDialog tag={merging} {others} {onmerge} onclose={() => (merging = null)} />

<ConfirmDialog
  bind:open={deleteOpen}
  title={`Delete the tag "${deleting?.name ?? ''}"?`}
  description={`It comes off ${candidates(deleting?.applications ?? 0)}. Automations that add it keep adding it.`}
  cancelLabel="Keep it"
  confirmLabel="Delete tag"
  failure="Could not delete the tag"
  onconfirm={deleteTag}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>
