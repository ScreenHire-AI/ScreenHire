<script lang="ts">
  import FolderArchiveIcon from '@lucide/svelte/icons/folder-archive';
  import ArchiveRestoreIcon from '@lucide/svelte/icons/archive-restore';
  import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
  import CopyIcon from '@lucide/svelte/icons/copy';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import RotateCcwIcon from '@lucide/svelte/icons/rotate-ccw';
  import type { EmailTemplateEntry, HiringTemplateSummary, ScorecardTemplateSummary, TemplateUse } from '@screenhire/api-client';
  import {
    createArchiveEmailTemplate,
    createArchiveHiringTemplate,
    createArchiveScorecardTemplate,
    createCreateEmailTemplate,
    createCreateHiringTemplate,
    createCreateScorecardTemplate,
    createDeleteEmailTemplate,
    createGetTemplateUsage,
    createListEmailTemplates,
    createListHiringTemplates,
    createListScorecardTemplates,
    createListTeamMembers,
    createUnarchiveEmailTemplate,
    createUnarchiveHiringTemplate,
    createUnarchiveScorecardTemplate,
    getGetHiringTemplateQueryOptions,
    getGetScorecardTemplateQueryOptions,
  } from '@screenhire/api-client/svelte-query';
  import { useQueryClient } from '@tanstack/svelte-query';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import NewHiringTemplateDialog from '#lib/components/new-hiring-template-dialog.svelte';
  import NewScorecardDialog from '#lib/components/new-scorecard-dialog.svelte';
  import RecordTabs from '#lib/components/record-tabs.svelte';
  import TemplateList, { type TemplateRow } from '#lib/components/template-list.svelte';
  import TopBar from '#lib/components/top-bar.svelte';
  import { buttonVariants } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { emailTemplatesPath } from '#lib/email.js';
  import { can } from '#lib/permissions.js';
  import { describeError, invalidatePath } from '#lib/query.js';
  import { templatesPath } from '#lib/realtime.js';
  import {
    copyName,
    maxNameLength,
    scorecardTemplatesPath,
    subjectParts,
    templateHref,
    templateKinds,
    templateKindViews,
    templateUsagePath,
    type TemplateKind,
  } from '#lib/templates.js';
  import { cn } from '#lib/utils.js';

  // The Templates page (design system section 21): one tab per kind, in the address (?tab=), each the list of its
  // templates with where each is used. New template offers the kinds the person's role may make. The row menus hold
  // Duplicate, Archive or Restore, and Reset to original for a built-in email the team edited. Archive is undone by
  // Restore on the same screen, so it does not confirm (section 10); Reset discards the team's wording, so it does (A2).
  const queryClient = useQueryClient();
  const sidebar = Sidebar.useSidebar();
  const hiring = createListHiringTemplates();
  const scorecards = createListScorecardTemplates();
  const emails = createListEmailTemplates();
  const usage = createGetTemplateUsage();
  const team = createListTeamMembers();
  const members = $derived(team.data?.data);
  const currentUserId = $derived(page.data.user?.id);
  const editsHiring = $derived(can(page.data.user, 'EditHiringTemplates'));
  const editsEmails = $derived(can(page.data.user, 'EditEmailTemplates'));

  const tab = $derived<TemplateKind>(templateKinds.find((value) => value === page.url.searchParams.get('tab')) ?? 'hiring');
  function selectTab(value: string) {
    const url = new URL(page.url.href);
    if (value === 'hiring') url.searchParams.delete('tab');
    else url.searchParams.set('tab', value);
    void goto(url, { replace: true, reset: false });
  }

  const uses = $derived.by(() => {
    const data = usage.data?.data;
    const index = (list: readonly TemplateUse[] | undefined) => new Map((list ?? []).map((use) => [use.templateId, use]));
    return { hiring: index(data?.hiring), scorecards: index(data?.scorecards), emails: index(data?.emails) };
  });
  const variableLabels = $derived(new Map((emails.data?.data.variables ?? []).map((variable) => [variable.name, variable.label])));

  const hiringRows = $derived<TemplateRow[]>(
    (hiring.data?.data ?? []).map((template) => ({
      id: template.id,
      name: template.name,
      builtIn: template.isSystem,
      archived: template.archived,
      href: templateHref('hiring', template.id),
      usage: uses.hiring.get(template.id),
      lastUpdated: template.lastUpdated,
      description: template.description,
      published: template.latestPublishedVersion,
      draft: template.draftVersion,
    })),
  );
  const scorecardRows = $derived<TemplateRow[]>(
    (scorecards.data?.data ?? []).map((scorecard) => ({
      id: scorecard.id,
      name: scorecard.name,
      builtIn: scorecard.builtIn,
      archived: scorecard.archived,
      href: templateHref('scorecards', scorecard.id),
      usage: uses.scorecards.get(scorecard.id),
      lastUpdated: scorecard.lastUpdated,
      questions: scorecard.questions,
    })),
  );
  const emailRows = $derived<TemplateRow[]>(
    (emails.data?.data.templates ?? []).map((template) => ({
      id: template.id,
      name: template.name,
      builtIn: template.builtIn,
      archived: template.archived,
      href: templateHref('emails', template.id),
      usage: uses.emails.get(template.id),
      lastUpdated: template.lastUpdated,
      subject: template.subject,
      subjectParts: subjectParts(template.subject, variableLabels),
    })),
  );
  const shownCount = (rows: readonly TemplateRow[]) => rows.filter((row) => !row.archived).length;
  const tabsView = $derived(
    templateKinds.map((kind) => {
      const query = kind === 'hiring' ? hiring : kind === 'scorecards' ? scorecards : emails;
      const rows = kind === 'hiring' ? hiringRows : kind === 'scorecards' ? scorecardRows : emailRows;
      return { value: kind, label: templateKindViews[kind].label, count: query.isSuccess ? shownCount(rows) : undefined };
    }),
  );

  // Every change refetches the kind's list and the usage, which Duplicate and Archive both change.
  const refresh = (path: string) => Promise.all([invalidatePath(queryClient, path), invalidatePath(queryClient, templateUsagePath)]);
  const onSettled = (path: string) => () => ({ mutation: { onSettled: () => refresh(path) } });
  const createHiring = createCreateHiringTemplate(onSettled(templatesPath));
  const archiveHiring = createArchiveHiringTemplate(onSettled(templatesPath));
  const unarchiveHiring = createUnarchiveHiringTemplate(onSettled(templatesPath));
  const createScorecard = createCreateScorecardTemplate(onSettled(scorecardTemplatesPath));
  const archiveScorecard = createArchiveScorecardTemplate(onSettled(scorecardTemplatesPath));
  const unarchiveScorecard = createUnarchiveScorecardTemplate(onSettled(scorecardTemplatesPath));
  const createEmail = createCreateEmailTemplate(onSettled(emailTemplatesPath));
  const archiveEmail = createArchiveEmailTemplate(onSettled(emailTemplatesPath));
  const unarchiveEmail = createUnarchiveEmailTemplate(onSettled(emailTemplatesPath));
  const resetEmail = createDeleteEmailTemplate(onSettled(emailTemplatesPath));

  let messages = $state<Record<TemplateKind, string>>({ hiring: '', scorecards: '', emails: '' });
  let failures = $state<Record<TemplateKind, string>>({ hiring: '', scorecards: '', emails: '' });

  async function act(kind: TemplateKind, run: () => Promise<string | void>, failure: string) {
    messages[kind] = '';
    failures[kind] = '';
    try {
      messages[kind] = (await run()) ?? '';
    } catch (error) {
      failures[kind] = describeError(error, failure);
    }
  }

  // Duplicate makes "<name> (copy)" as the team's own, and opens it.
  function duplicateHiring(template: HiringTemplateSummary) {
    return act(
      'hiring',
      async () => {
        let versionId = template.latestPublishedVersionId ?? null;
        if (!versionId) {
          const detail = (await queryClient.fetchQuery(getGetHiringTemplateQueryOptions(template.id))).data;
          versionId = detail.versions.find((version) => version.status === 'Draft')?.id ?? detail.versions.at(-1)?.id ?? null;
        }
        const response = await createHiring.mutateAsync({
          data: { name: copyName(template.name, maxNameLength.hiring), description: template.description, copyFromVersionId: versionId },
        });
        await goto(templateHref('hiring', response.data.id));
      },
      `Could not duplicate ${template.name}`,
    );
  }

  function duplicateScorecard(scorecard: ScorecardTemplateSummary) {
    return act(
      'scorecards',
      async () => {
        const detail = (await queryClient.fetchQuery(getGetScorecardTemplateQueryOptions(scorecard.id))).data;
        const response = await createScorecard.mutateAsync({
          data: { name: copyName(scorecard.name, maxNameLength.scorecards), questions: detail.questions, copiedFrom: scorecard.id },
        });
        await goto(templateHref('scorecards', response.data.id));
      },
      `Could not duplicate ${scorecard.name}`,
    );
  }

  function duplicateEmail(template: EmailTemplateEntry) {
    return act(
      'emails',
      async () => {
        const response = await createEmail.mutateAsync({
          data: { name: copyName(template.name, maxNameLength.emails), subject: template.subject, body: template.body },
        });
        await goto(templateHref('emails', response.data.id));
      },
      `Could not duplicate ${template.name}`,
    );
  }

  function setArchived(kind: TemplateKind, row: TemplateRow, archived: boolean) {
    const variables = { templateId: row.id };
    const run = {
      hiring: () => (archived ? archiveHiring : unarchiveHiring).mutateAsync(variables),
      scorecards: () => (archived ? archiveScorecard : unarchiveScorecard).mutateAsync(variables),
      emails: () => (archived ? archiveEmail : unarchiveEmail).mutateAsync(variables),
    }[kind];
    return act(
      kind,
      async () => {
        await run();
        return archived ? `Archived ${row.name}. It is under Archived until you restore it.` : `Restored ${row.name}.`;
      },
      archived ? `Could not archive ${row.name}` : `Could not restore ${row.name}`,
    );
  }

  // Reset to original confirms in a dialog, which opens once the row's menu has closed so focus can return to it.
  let resetting = $state<EmailTemplateEntry | null>(null);
  let resetChosen = false;
  let confirmReset = $state(false);
  const onMenuClosed = () => {
    if (resetChosen) confirmReset = true;
    resetChosen = false;
  };

  async function reset() {
    if (!resetting) return;
    await resetEmail.mutateAsync({ templateId: resetting.id });
    messages.emails = `${resetting.name} is back to ScreenHire's wording.`;
  }

  const hiringOf = (row: TemplateRow) => hiring.data?.data.find((template) => template.id === row.id);
  const scorecardOf = (row: TemplateRow) => scorecards.data?.data.find((scorecard) => scorecard.id === row.id);
  const emailOf = (row: TemplateRow) => emails.data?.data.templates.find((template) => template.id === row.id);
  // The rejection and follow-up questions emails use variables only their own actions fill, so a copy could never send.
  const duplicable = (template: EmailTemplateEntry | undefined) => Boolean(template && !template.rejectOnly && !template.questionnaireOnly && !template.invitationOnly);

  let creatingHiring = $state(false);
  let creatingScorecard = $state(false);
  const offersNew = $derived(editsHiring || editsEmails);
</script>

<svelte:head>
  <title>Templates · ScreenHire</title>
</svelte:head>

<TopBar crumbs={[{ label: 'Templates' }]} />

<main class={['flex flex-col', sidebar.isMobile && offersNew && 'pb-28']}>
  <Tabs.Root value={tab} onValueChange={selectTab}>
    <!-- The page's primary action; a phone has it in the action bar (section 8). -->
    <HeaderBand title="Templates" actions={sidebar.isMobile || !offersNew ? undefined : newTemplate}>
      {#snippet tabs()}<RecordTabs tabs={tabsView} label="Kinds of template" />{/snippet}
    </HeaderBand>

    <!-- The content container (S6): one table panel per tab. -->
    <Tabs.Content value="hiring" class="flex w-full flex-col px-4 py-6 md:px-6">
      <TemplateList
        kind="hiring"
        rows={hiringRows}
        pending={hiring.isPending}
        error={hiring.isError ? describeError(hiring.error, 'Could not load the hiring templates') : undefined}
        onretry={() => hiring.refetch()}
        usagePending={usage.isPending}
        {members}
        {currentUserId}
        menu={hiringMenu}
        hasMenu={() => editsHiring}
        oncreate={editsHiring ? () => (creatingHiring = true) : undefined}
        message={messages.hiring}
        failure={failures.hiring}
      />
    </Tabs.Content>
    <Tabs.Content value="scorecards" class="flex w-full flex-col px-4 py-6 md:px-6">
      <TemplateList
        kind="scorecards"
        rows={scorecardRows}
        pending={scorecards.isPending}
        error={scorecards.isError ? describeError(scorecards.error, 'Could not load the scorecards') : undefined}
        onretry={() => scorecards.refetch()}
        usagePending={usage.isPending}
        {members}
        {currentUserId}
        menu={scorecardMenu}
        hasMenu={() => editsHiring}
        oncreate={editsHiring ? () => (creatingScorecard = true) : undefined}
        message={messages.scorecards}
        failure={failures.scorecards}
      />
    </Tabs.Content>
    <Tabs.Content value="emails" class="flex w-full flex-col px-4 py-6 md:px-6">
      <TemplateList
        kind="emails"
        rows={emailRows}
        pending={emails.isPending}
        error={emails.isError ? describeError(emails.error, 'Could not load the email templates') : undefined}
        onretry={() => emails.refetch()}
        usagePending={usage.isPending}
        {members}
        {currentUserId}
        menu={emailMenu}
        hasMenu={(row) => editsEmails && (duplicable(emailOf(row)) || !row.builtIn || Boolean(emailOf(row)?.edited))}
        message={messages.emails}
        failure={failures.emails}
      />
    </Tabs.Content>
  </Tabs.Root>

  {#if sidebar.isMobile && offersNew}
    <ActionBar>{@render newTemplate()}</ActionBar>
  {/if}
</main>

{#snippet newTemplate()}
  <DropdownMenu.Root>
    <DropdownMenu.Trigger class={cn(buttonVariants({ size: 'lg' }))}><PlusIcon aria-hidden="true" />New template<ChevronDownIcon aria-hidden="true" /></DropdownMenu.Trigger>
    <DropdownMenu.Content align="end" class="w-56">
      {#if editsHiring}
        <DropdownMenu.Item onSelect={() => (creatingHiring = true)}>Hiring template</DropdownMenu.Item>
        <DropdownMenu.Item onSelect={() => (creatingScorecard = true)}>Scorecard</DropdownMenu.Item>
      {/if}
      {#if editsEmails}
        <DropdownMenu.Item>{#snippet child({ props })}<a href="/templates/emails/new" {...props}>Email template</a>{/snippet}</DropdownMenu.Item>
      {/if}
    </DropdownMenu.Content>
  </DropdownMenu.Root>
{/snippet}

{#snippet archiveItems(kind: TemplateKind, row: TemplateRow)}
  {#if !row.builtIn}
    {#if row.archived}
      <DropdownMenu.Item onSelect={() => setArchived(kind, row, false)}><ArchiveRestoreIcon aria-hidden="true" />Restore</DropdownMenu.Item>
    {:else}
      <DropdownMenu.Item onSelect={() => setArchived(kind, row, true)}><FolderArchiveIcon aria-hidden="true" />Archive</DropdownMenu.Item>
    {/if}
  {/if}
{/snippet}

{#snippet hiringMenu(row: TemplateRow)}
  <MoreMenu label={`Actions for ${row.name}`} size="icon" quiet>
    <DropdownMenu.Item onSelect={() => hiringOf(row) && duplicateHiring(hiringOf(row)!)}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>
    {@render archiveItems('hiring', row)}
  </MoreMenu>
{/snippet}

{#snippet scorecardMenu(row: TemplateRow)}
  <MoreMenu label={`Actions for ${row.name}`} size="icon" quiet>
    <DropdownMenu.Item onSelect={() => scorecardOf(row) && duplicateScorecard(scorecardOf(row)!)}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>
    {@render archiveItems('scorecards', row)}
  </MoreMenu>
{/snippet}

{#snippet emailMenu(row: TemplateRow)}
  {@const template = emailOf(row)}
  <MoreMenu label={`Actions for ${row.name}`} size="icon" quiet onclosed={onMenuClosed}>
    {#if duplicable(template)}
      <DropdownMenu.Item onSelect={() => template && duplicateEmail(template)}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>
    {/if}
    {@render archiveItems('emails', row)}
    {#if template?.builtIn && template.edited}
      <DropdownMenu.Item
        variant="destructive"
        onSelect={() => {
          resetting = template;
          resetChosen = true;
        }}><RotateCcwIcon aria-hidden="true" />Reset to original</DropdownMenu.Item
      >
    {/if}
  </MoreMenu>
{/snippet}

<NewHiringTemplateDialog bind:open={creatingHiring} />
<NewScorecardDialog bind:open={creatingScorecard} />

<ConfirmDialog
  bind:open={confirmReset}
  title={`Reset ${resetting?.name ?? 'this email'} to the original?`}
  description="Your team's wording is discarded, and the email goes out in ScreenHire's current wording from now on."
  cancelLabel="Keep ours"
  confirmLabel="Reset to original"
  failure="Could not reset the email"
  onconfirm={reset}
>
  {#snippet icon()}<RotateCcwIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>
