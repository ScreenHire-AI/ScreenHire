<script lang="ts">
  import { createAddApplicationTag, createRemoveApplicationTag } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import Panel from '#lib/components/panel.svelte';
  import TagList from '#lib/components/tag-list.svelte';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, jobsPath } from '#lib/realtime.js';

  // An application's Tags panel: the tag list, adding and removing through the API. Every list shows tags and filters
  // by them, so a change refetches them all; other dashboards get it through ApplicationTagsChanged. Automations read
  // tags as `application.tags`.
  let { applicationId, tags, editable }: { applicationId: string; tags: readonly string[]; editable: boolean } = $props();

  const queryClient = useQueryClient();
  const refresh = () => Promise.all([jobsPath, applicationsPath, candidatesPath].map((path) => invalidatePath(queryClient, path)));
  const add = createAddApplicationTag(() => ({ mutation: { onSettled: refresh } }));
  const remove = createRemoveApplicationTag(() => ({ mutation: { onSettled: refresh } }));

  let failure = $state('');
  let announcement = $state('');

  async function onadd(tag: string): Promise<boolean> {
    failure = '';
    try {
      await add.mutateAsync({ applicationId, data: { tag } });
      announcement = `Tagged "${tag}".`;
      return true;
    } catch (error) {
      failure = describeError(error, 'Could not add the tag');
      return false;
    }
  }

  async function onremove(tag: string) {
    failure = '';
    try {
      await remove.mutateAsync({ applicationId, params: { tag } });
      announcement = `Removed the tag "${tag}".`;
    } catch (error) {
      failure = describeError(error, 'Could not remove the tag');
    }
  }
</script>

<Panel title="Tags" count={tags.length}>
  <TagList
    {tags}
    onadd={editable ? onadd : undefined}
    onremove={editable ? onremove : undefined}
    busy={add.isPending || remove.isPending}
    {failure}
  />
  <p role="status" class="sr-only">{announcement}</p>
</Panel>
