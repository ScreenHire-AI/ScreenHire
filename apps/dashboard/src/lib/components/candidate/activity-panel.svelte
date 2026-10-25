<script lang="ts">
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import { deleteApplicationNote, type ApplicationTimelineEntry, type Permission, type TeamMember } from '@screenhire/api-client';
  import { ApiError } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import NoteForm from '#lib/components/note-form.svelte';
  import NoteText from '#lib/components/note-text.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import { activityKind, activityKinds, actorText, describeEvent, eventIcons, kindCounts, visibleEntries, type ActivityKind, type ActivityNames } from '#lib/activity.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath } from '#lib/realtime.js';

  // An application's Activity (design system section 22): a box for a note, then what anyone did, newest first, saying
  // who and how long ago. On Overview it shows the latest five and links to the Activity tab; there, every entry, with a
  // filter by kind and its counts. A note's More menu holds Edit for its author, and Delete for the author, an Owner or
  // an Admin, confirmed in a dialog; anyone else sees no menu. The API checks both.
  let {
    applicationId,
    entries,
    error,
    onretry,
    members,
    currentUserId,
    permissions,
    names,
    limit,
    allHref,
  }: {
    applicationId: string;
    /** The timeline, oldest first as the API sends it; undefined while it loads. */
    entries: readonly ApplicationTimelineEntry[] | undefined;
    error?: unknown;
    onretry: () => void;
    members: readonly TeamMember[] | undefined;
    currentUserId: string | undefined;
    /** What the person's role allows: Add notes and tags writes, edits and deletes their own notes, Delete anyone's notes any note. */
    permissions: readonly Permission[];
    names: ActivityNames;
    /** Overview's latest few, with the link to every entry; leave out for the Activity tab and its filter. */
    limit?: number;
    allHref?: string;
  } = $props();

  const queryClient = useQueryClient();
  let filter = $state<ActivityKind | 'all'>('all');
  let editing = $state<string | null>(null);
  let deleting = $state<string | null>(null);
  let confirmOpen = $state(false);
  // Delete was chosen in a menu: its dialog opens once the menu has closed and focus is back on the menu's button.
  let deleteChosen = false;
  let announcement = $state('');

  const visible = $derived(entries ? visibleEntries(entries) : undefined);
  const counts = $derived(kindCounts(visible ?? []));
  const filtered = $derived(visible?.filter((entry) => filter === 'all' || activityKind(entry) === filter));
  const shown = $derived(limit === undefined ? filtered : filtered?.slice(0, limit));
  const byId = $derived(new Map((entries ?? []).map((entry) => [entry.id, entry])));

  const items = $derived<Activity[] | undefined>(
    shown?.map((entry) => ({
      id: entry.id,
      icon: eventIcons[entry.type],
      event: describeEvent(entry, names),
      actor: actorText(entry, members, currentUserId),
      note: entry.type === 'NoteAdded' ? undefined : (entry.note ?? undefined),
      noteId: entry.type === 'NoteAdded' ? (entry.noteId ?? undefined) : undefined,
      at: entry.occurredAt,
    })),
  );

  const isAuthor = (entry: ApplicationTimelineEntry) => Boolean(entry.actorUserId) && entry.actorUserId === currentUserId;
  const editable = $derived(permissions.includes('AddNotesAndTags'));
  const mayEdit = (entry: ApplicationTimelineEntry) => editable && isAuthor(entry);
  const mayDelete = (entry: ApplicationTimelineEntry) => editable && (isAuthor(entry) || permissions.includes('DeleteAnyNote'));

  async function refresh(message: string) {
    announcement = message;
    editing = null;
    await invalidatePath(queryClient, `${applicationsPath}/${applicationId}/timeline`);
  }

  async function confirmDelete() {
    if (!deleting) return;
    const response = await deleteApplicationNote(applicationId, deleting);
    if (response.status !== 204 && response.status !== 404) throw new ApiError(response.status, response.data);
    deleting = null;
    await refresh('Note deleted.');
  }
</script>

{#snippet noteBody(item: Activity)}
  {@const entry = byId.get(item.id)}
  {#if entry && item.noteId && editing === item.noteId}
    <NoteForm
      {applicationId}
      note={{ id: item.noteId, text: entry.note ?? '' }}
      members={members ?? []}
      {currentUserId}
      ondone={refresh}
      oncancel={() => (editing = null)}
    />
  {:else if entry}
    <NoteText text={entry.note ?? ''} {members} editedAt={entry.noteEditedAt} />
  {/if}
{/snippet}

{#snippet noteActions(item: Activity)}
  {@const entry = byId.get(item.id)}
  {#if entry && item.noteId && editing !== item.noteId && (mayEdit(entry) || mayDelete(entry))}
    {@const noteId = item.noteId}
    <MoreMenu label="More actions for this note" quiet size="icon" onclosed={() => deleteChosen && ((deleteChosen = false), (confirmOpen = true))}>
      {#if mayEdit(entry)}
        <DropdownMenu.Item onSelect={() => (editing = noteId)}><PencilIcon aria-hidden="true" />Edit</DropdownMenu.Item>
      {/if}
      {#if mayDelete(entry)}
        <DropdownMenu.Item variant="destructive" onSelect={() => ((deleting = noteId), (deleteChosen = true))}><Trash2Icon aria-hidden="true" />Delete</DropdownMenu.Item>
      {/if}
    </MoreMenu>
  {/if}
{/snippet}

{#snippet allLink()}
  <a href={allHref} class="font-semibold text-primary underline underline-offset-2">All {visible?.length} in Activity</a>
{/snippet}

<Panel
  title="Activity"
  footer={limit !== undefined && allHref && visible && visible.length > limit ? allLink : undefined}
>
  <div class="flex flex-col gap-4">
    {#if limit === undefined}
      <!-- One line that scrolls inside the panel rather than wrapping (F5). -->
      <div class="-m-1 overflow-x-auto p-1">
        <div role="group" aria-label="Show" class="flex w-max gap-1">
          <Toggle size="lg" bind:pressed={() => filter === 'all', () => (filter = 'all')}>All <Badge variant="count">{counts.all}</Badge></Toggle>
          {#each activityKinds as kind (kind.value)}
            <Toggle size="lg" bind:pressed={() => filter === kind.value, () => (filter = filter === kind.value ? 'all' : kind.value)}>
              {kind.label} <Badge variant="count">{counts[kind.value]}</Badge>
            </Toggle>
          {/each}
        </div>
      </div>
    {/if}
    {#if editable}
      <NoteForm {applicationId} members={members ?? []} {currentUserId} ondone={refresh} />
    {/if}
    {#if error}
      <QueryError message={describeError(error, 'Could not load the activity')} {onretry} inset />
    {:else if items && items.length === 0}
      <p class="text-muted-foreground">{filter === 'all' ? 'Nothing yet.' : 'Nothing of this kind yet.'}</p>
    {:else}
      <ActivityList label="Events" {items} body={noteBody} actions={noteActions} />
    {/if}
    <p role="status" class="sr-only">{announcement}</p>
  </div>
</Panel>

<ConfirmDialog
  bind:open={confirmOpen}
  title="Delete this note?"
  description="It leaves Overview and Activity for the whole team. You can't undo this."
  confirmLabel="Delete note"
  failure="Could not delete the note"
  onconfirm={confirmDelete}
>
  {#snippet icon()}<Trash2Icon aria-hidden="true" />{/snippet}
</ConfirmDialog>
