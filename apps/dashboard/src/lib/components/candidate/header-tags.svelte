<script lang="ts">
  import { createAddApplicationTag, createListTags, createRemoveApplicationTag } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import TagList from '#lib/components/tag-list.svelte';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { applicationsPath, candidatesPath, jobsPath, tagsPath } from '#lib/realtime.js';

  // An application's tags in the candidate's header (design system section 21): the compact tag list, adding and removing through the API. Every list shows tags and filters
  // by them, so a change refetches them all; other dashboards get it through ApplicationTagsChanged. Automations read
  // tags as `application.tags`. The field suggests the organization's tags (`GET /v1/tags`), read once someone can add.
  let { applicationId, tags, editable }: { applicationId: string; tags: readonly string[]; editable: boolean } = $props();

  const queryClient = useQueryClient();
  const refresh = () => Promise.all([jobsPath, applicationsPath, candidatesPath, tagsPath].map((path) => invalidatePath(queryClient, path)));
  const known = createListTags(() => ({ query: { enabled: editable } }));
  const suggestions = $derived(known.data?.data.tags ?? []);
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

<TagList
  compact
  {tags}
  {suggestions}
  onadd={editable ? onadd : undefined}
  onremove={editable ? onremove : undefined}
  busy={add.isPending || remove.isPending}
  {failure}
/>
<p role="status" class="sr-only">{announcement}</p>
