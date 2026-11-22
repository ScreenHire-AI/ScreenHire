<script lang="ts">
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import SaveIcon from '~icons/lucide/save';
  import type { EmailChoice, NoticeKind, NoticeScope, ToastKind } from '@screenhire/api-client';
  import { createGetAccountNotifications, createUpdateAccountNotifications } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { accountCrumbs, emailChoices, noticeRows, scopeChoices } from '#lib/account.js';
  import AccountMenu from '#lib/components/account-menu.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import * as ToggleGroup from '#lib/components/ui/toggle-group/index.js';
  import { selectClass } from '#lib/components/workflow/controls.js';
  import { draftFrom, inAppOn, requestFrom, setInApp, setToast, teammateToastLabels, type NoticeDraft } from '#lib/notice-grid.js';
  import { toastLifetime } from '#lib/notifications.svelte.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { cn } from '#lib/utils.js';

  // Your account > Notifications (docs/architecture/notifications.md, "Each person's choices"; design system section 21):
  // a row per kind of thing, and a column per way to hear of it. In the app switches the toast or card in the tray (the bell
  // keeps what is addressed to the person either way); Email is now, in the daily digest or never; Slack shows once the
  // person's Slack account is linked, on the rows ScreenHire sends a Slack message for. A cell with no such channel is a
  // dash, never a disabled control (A3). Verification and AI use start from Settings > Notification defaults. One Save
  // sends the whole grid; the draft fills once, so a refetch while someone is choosing leaves their choices alone.
  const path = '/v1/account/notifications';
  const queryClient = useQueryClient();
  const notifications = createGetAccountNotifications();
  const data = $derived(notifications.data?.data);
  const update = createUpdateAccountNotifications(() => ({ mutation: { onSettled: () => invalidatePath(queryClient, path) } }));

  let draft = $state<NoticeDraft | null>(null);
  let failure = $state('');
  let saved = $state('');
  let expanded = $state(false);

  $effect(() => {
    if (data && !draft) draft = draftFrom(data);
  });

  const showSlack = $derived(Boolean(data?.slackLinked));

  function setEmail(kind: NoticeKind, email: EmailChoice) {
    if (!draft) return;
    draft.rows = draft.rows.map((row) => (row.kind === kind ? { ...row, email } : row));
  }

  function setSlack(kind: NoticeKind, slack: boolean) {
    if (!draft) return;
    draft.rows = draft.rows.map((row) => (row.kind === kind ? { ...row, slack } : row));
  }

  async function save(event: SubmitEvent) {
    event.preventDefault();
    if (!draft) return;
    failure = '';
    saved = '';
    try {
      await update.mutateAsync({ data: requestFrom(draft) });
      saved = 'Saved.';
    } catch (error) {
      failure = describeError(error, 'Could not save your notification choices');
    }
  }
</script>

<svelte:head>
  <title>Notifications · ScreenHire</title>
</svelte:head>

<TopBar crumbs={accountCrumbs('/account/notifications')} />

<main class="flex flex-col">
  <HeaderBand title="Notifications" frame oneRow />

  <AccountMenu>
    <!-- The switches keep the form column; the grid is a table, which never stops short (S6). -->
    <div class="flex w-full flex-col gap-5">
      {#if notifications.isError && !draft}
        <Panel title="What you hear about">
          <QueryError message={describeError(notifications.error, 'Could not load your notification choices')} onretry={() => notifications.refetch()} inset />
        </Panel>
      {:else if !draft || !data}
        <Panel title="What you hear about">
          <div aria-hidden="true" class="flex flex-col gap-3">
            {#each [0, 1, 2, 3] as row (row)}<Skeleton class="h-10 w-full" />{/each}
          </div>
          <p class="sr-only" role="status">Loading your notification choices…</p>
        </Panel>
      {:else}
        {@const current = draft}
        <form onsubmit={save} novalidate aria-label="Your notification choices" class="flex flex-col gap-5">
          <Panel title="Toasts and jobs">
            <div class="flex flex-col gap-4">
              <div class="flex flex-col gap-2">
                <span id="notice-scope" class="font-semibold">Jobs</span>
                <!-- One line that scrolls inside its panel on a narrow screen, never wraps (F5). -->
                <div class="-m-1 max-w-[calc(100%+--spacing(2))] overflow-x-auto p-1 [scrollbar-width:thin]">
                  <ToggleGroup.Root
                    type="single"
                    variant="outline"
                    size="lg"
                    spacing={0}
                    aria-labelledby="notice-scope"
                    aria-describedby="notice-scope-hint"
                    bind:value={() => current.scope, (next) => next && (current.scope = next as NoticeScope)}
                  >
                    {#each scopeChoices as choice (choice.value)}
                      <ToggleGroup.Item value={choice.value} class="text-foreground data-[state=on]:inset-ring-2 data-[state=on]:inset-ring-accent-foreground"
                        >{choice.label}</ToggleGroup.Item
                      >
                    {/each}
                  </ToggleGroup.Root>
                </div>
                <span id="notice-scope-hint" class="text-caption text-muted-foreground">New applications and teammates' changes on your hiring teams' jobs, or on every job you can see.</span>
              </div>
              <div class="flex items-center justify-between gap-4">
                <Label for="notice-pause">Pause toasts</Label>
                <Switch id="notice-pause" bind:checked={current.pauseToasts} />
              </div>
              <div class="flex items-start justify-between gap-4">
                <div class="flex flex-col gap-0.5">
                  <Label for="notice-close">Close toasts automatically</Label>
                  <span id="notice-close-hint" class="text-caption text-muted-foreground">After {toastLifetime / 1000} seconds</span>
                </div>
                <Switch id="notice-close" bind:checked={current.closeToasts} aria-describedby="notice-close-hint" />
              </div>
            </div>
          </Panel>

          <Panel title="What you hear about" flush>
            {#if data.slackAvailable && !data.slackLinked}
              <p class="border-b border-border-subtle px-4.5 py-3 text-muted-foreground max-md:px-4">
                Link your Slack account in <a href="/account/connected">Calendar and accounts</a> to get Slack messages too.
              </p>
            {/if}
            <!-- A table once the panel is wide enough for every column: a row per kind, a column per way to hear of it. Narrower,
                 beside the account menu on a laptop or on a phone, each kind is a card (F1, F2, F5). -->
            <div class="@container/grid">
              <div class="overflow-x-auto @max-2xl/grid:hidden">
                <Table.Root>
                  <Table.Header>
                    <Table.Row class="bg-muted hover:bg-muted">
                      <Table.Head class="px-4.5">Kind</Table.Head>
                      <Table.Head>In the app</Table.Head>
                      <Table.Head>Email</Table.Head>
                      {#if showSlack}<Table.Head class="pr-4.5">Slack</Table.Head>{/if}
                    </Table.Row>
                  </Table.Header>
                  <Table.Body>
                    {#each current.rows as row (row.kind)}
                      {@const words = noticeRows[row.kind]}
                      <Table.Row class="h-14">
                        <Table.Cell class="px-4.5 whitespace-normal">
                          <span class="flex flex-col">
                            <span class="font-semibold">{words.label}</span>
                            <span class="text-caption text-muted-foreground">{words.hint}</span>
                          </span>
                        </Table.Cell>
                        <Table.Cell>{@render inApp(row.kind, row.toasts, 'table')}</Table.Cell>
                        <Table.Cell>{@render email(row.kind, row.email, 'table')}</Table.Cell>
                        {#if showSlack}<Table.Cell class="pr-4.5">{@render slack(row.kind, row.slack, 'table')}</Table.Cell>{/if}
                      </Table.Row>
                      {#if row.kind === 'TeammateChanges' && expanded}
                        {#each row.toasts as kind (kind)}
                          <Table.Row class="h-11">
                            <Table.Cell class="px-4.5 pl-9 text-muted-foreground"><span id={`notice-toast-${kind}`}>{teammateToastLabels[kind]}</span></Table.Cell>
                            <Table.Cell>
                              <Switch
                                aria-labelledby={`notice-toast-${kind}`}
                                checked={!current.toastsOff.includes(kind)}
                                onCheckedChange={(on) => (current.toastsOff = setToast(current, kind, on))}
                              />
                            </Table.Cell>
                            <Table.Cell></Table.Cell>
                            {#if showSlack}<Table.Cell></Table.Cell>{/if}
                          </Table.Row>
                        {/each}
                      {/if}
                    {/each}
                  </Table.Body>
                </Table.Root>
              </div>
              <ul class="@2xl/grid:hidden">
                {#each current.rows as row (row.kind)}
                  {@const words = noticeRows[row.kind]}
                  <li class="flex flex-col gap-3 border-b border-border-subtle px-4 py-3 last:border-0">
                    <span class="flex flex-col">
                      <span class="font-semibold">{words.label}</span>
                      <span class="text-caption text-muted-foreground">{words.hint}</span>
                    </span>
                    <div class="flex items-center justify-between gap-4"><span>In the app</span>{@render inApp(row.kind, row.toasts, 'phone')}</div>
                    <div class="flex items-center justify-between gap-4"><span>Email</span>{@render email(row.kind, row.email, 'phone')}</div>
                    {#if showSlack}<div class="flex items-center justify-between gap-4"><span>Slack</span>{@render slack(row.kind, row.slack, 'phone')}</div>{/if}
                    {#if row.kind === 'TeammateChanges' && expanded}
                      <ul class="flex flex-col gap-3 border-l border-border-subtle pl-4" aria-label="Kinds of teammates' changes">
                        {#each row.toasts as kind (kind)}
                          <li class="flex items-center justify-between gap-4">
                            <span id={`notice-phone-toast-${kind}`} class="text-muted-foreground">{teammateToastLabels[kind]}</span>
                            <Switch
                              aria-labelledby={`notice-phone-toast-${kind}`}
                              checked={!current.toastsOff.includes(kind)}
                              onCheckedChange={(on) => (current.toastsOff = setToast(current, kind, on))}
                            />
                          </li>
                        {/each}
                      </ul>
                    {/if}
                  </li>
                {/each}
              </ul>
            </div>
            {#snippet footer()}
              <div class="flex flex-wrap items-center justify-end gap-2">
                <FailureText message={failure} class="mr-auto" />
                <p role="status" class="mr-auto text-muted-foreground empty:hidden">{failure ? '' : saved}</p>
                <Button type="submit" size="lg" disabled={update.isPending}><SaveIcon aria-hidden="true" />Save</Button>
              </div>
            {/snippet}
          </Panel>
        </form>
      {/if}
    </div>
  </AccountMenu>
</main>

{#snippet dash(label: string)}
  <span class="text-muted-foreground"><span aria-hidden="true">-</span><span class="sr-only">{label}</span></span>
{/snippet}

{#snippet inApp(kind: NoticeKind, toasts: readonly ToastKind[], where: 'table' | 'phone')}
  {#if !draft || toasts.length === 0}
    {@render dash('No toast for this')}
  {:else}
    {@const current = draft}
    <span class="flex items-center gap-2">
      <Switch
        aria-label={`${noticeRows[kind].label}: in the app`}
        checked={inAppOn(current, toasts)}
        onCheckedChange={(on) => (current.toastsOff = setInApp(current, toasts, on))}
      />
      {#if kind === 'TeammateChanges'}
        <Button variant="outline" size="sm" aria-expanded={expanded} data-where={where} onclick={() => (expanded = !expanded)}>
          Choose kinds<ChevronDownIcon aria-hidden="true" class={['transition-transform', expanded && 'rotate-180']} />
        </Button>
      {/if}
    </span>
  {/if}
{/snippet}

{#snippet email(kind: NoticeKind, value: EmailChoice, where: 'table' | 'phone')}
  <select
    class={cn(selectClass, 'w-auto min-w-36')}
    aria-label={`${noticeRows[kind].label}: email`}
    {value}
    data-where={where}
    onchange={(event) => setEmail(kind, event.currentTarget.value as EmailChoice)}
  >
    {#each emailChoices as choice (choice.value)}<option value={choice.value}>{choice.label}</option>{/each}
  </select>
{/snippet}

{#snippet slack(kind: NoticeKind, value: boolean | null, where: 'table' | 'phone')}
  {#if value === null}
    {@render dash('No Slack message for this')}
  {:else}
    <Switch aria-label={`${noticeRows[kind].label}: Slack`} checked={value} onCheckedChange={(on) => setSlack(kind, on)} data-where={where} />
  {/if}
{/snippet}
