<script lang="ts">
  import ArrowDownIcon from '@lucide/svelte/icons/arrow-down';
  import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
  import ArrowUpIcon from '@lucide/svelte/icons/arrow-up';
  import CircleXIcon from '@lucide/svelte/icons/circle-x';
  import EllipsisIcon from '@lucide/svelte/icons/ellipsis';
  import InboxIcon from '@lucide/svelte/icons/inbox';
  import KeyboardIcon from '@lucide/svelte/icons/keyboard';
  import MailIcon from '@lucide/svelte/icons/mail';
  import MessageSquareIcon from '@lucide/svelte/icons/message-square';
  import MoonIcon from '@lucide/svelte/icons/moon';
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import PhoneIcon from '@lucide/svelte/icons/phone';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import SunIcon from '@lucide/svelte/icons/sun';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import UserPlusIcon from '@lucide/svelte/icons/user-plus';
  import type { ApplicationFileSummary, ScorecardEntry, SignalEvidence as Evidence, WorkflowStage } from '@screenhire/api-client';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import BrandBar from '#lib/components/brand-bar.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import FileTile from '#lib/components/file-tile.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import LiveIndicator from '#lib/components/live-indicator.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import NotificationTray from '#lib/components/notification-tray.svelte';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import ResumePreview from '#lib/components/resume-preview.svelte';
  import ScorecardList from '#lib/components/scorecard-list.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SecretDialog from '#lib/components/secret-dialog.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import SignalEvidence from '#lib/components/signal-evidence.svelte';
  import StatusBadge, { type Status } from '#lib/components/status-badge.svelte';
  import StepLine from '#lib/components/step-line.svelte';
  import StepMarkers, { type Step } from '#lib/components/step-markers.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TagList from '#lib/components/tag-list.svelte';
  import TaskList, { type TaskRow } from '#lib/components/task-list.svelte';
  import TruncatedText from '#lib/components/truncated-text.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button, buttonVariants } from '#lib/components/ui/button/index.js';
  import * as Dialog from '#lib/components/ui/dialog/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import * as Empty from '#lib/components/ui/empty/index.js';
  import * as Field from '#lib/components/ui/field/index.js';
  import * as Sheet from '#lib/components/ui/sheet/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Kbd, KbdGroup } from '#lib/components/ui/kbd/index.js';
  import * as Sidebar from '#lib/components/ui/sidebar/index.js';
  import { Skeleton } from '#lib/components/ui/skeleton/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import * as Table from '#lib/components/ui/table/index.js';
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { hour, day, minute } from '#lib/dates.js';
  import { aiAssistanceLevels, authenticityLevels } from '#lib/integrity.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { sections } from '#lib/shell.js';
  import EmailSheetDemo, { demoEmail } from './email-sheet-demo.svelte';

  // Every component in design system section 11, in each state, for authors and reviewers to compare against. Pick a
  // width to see the section 8 sizes; the components respond to the frame, not the window. Development builds only.

  const widths = [
    { value: 0, label: 'Fit the window' },
    { value: 360, label: 'Phone, 360px' },
    { value: 768, label: 'Tablet, 768px' },
    { value: 1024, label: 'Laptop, 1024px' },
    { value: 1536, label: 'Desktop, 1536px' },
  ];
  const width = $derived(Number(page.url.searchParams.get('width')) || 0);

  function setWidth(value: number) {
    const url = new URL(page.url.href);
    if (value) url.searchParams.set('width', String(value));
    else url.searchParams.delete('width');
    void goto(url, { replace: true, reset: false });
  }

  let shortcutsOpen = $state(false);
  let confirmOpen = $state(false);
  let secretOpen = $state(false);
  const demoStages: WorkflowStage[] = [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screening', name: 'Screening', kind: 'Screening' },
    { id: 'interview', name: 'Interview', kind: 'Interview' },
  ];

  const toggleOptions = [
    { id: 'all', label: 'All', count: null },
    { id: 'applied', label: 'Applied', count: 12 },
    { id: 'screening', label: 'Screening', count: 5 },
    { id: 'interview', label: 'Interview', count: 2 },
  ];
  let toggleChoice = $state('all');

  const now = Date.now();
  const ago = (milliseconds: number) => new Date(now - milliseconds).toISOString();

  const statuses: { status: Status; label: string }[] = [
    { status: 'clear', label: 'No concerns' },
    { status: 'review', label: 'Review suggested' },
    { status: 'verify', label: 'Verification recommended' },
    { status: 'ai', label: 'Some indicators' },
    { status: 'neutral', label: 'Draft' },
  ];

  const steps: Step[] = [
    { label: 'Details', state: 'done' },
    { label: 'Questions', state: 'current' },
    { label: 'Workflow', state: 'upcoming' },
    { label: 'Publish', state: 'upcoming' },
  ];
  const stages = [
    { name: 'Applied', limit: '' },
    { name: 'Phone screen', limit: '3-day limit' },
    { name: 'Interview', limit: '5-day limit' },
  ];

  const people = [
    { id: '1', name: 'Jordan Ellis', status: 'review', label: 'Review suggested', stage: 'Screening', source: 'Careers page', at: ago(2 * hour) },
    { id: '2', name: 'Priya Natarajan', status: 'clear', label: 'No concerns', stage: 'Interview', source: 'Referral', at: ago(3 * day) },
    { id: '3', name: 'Samuel Okonkwo-Hartley', status: 'verify', label: 'Verification recommended', stage: 'Applied', source: 'Job board', at: ago(9 * day) },
  ] as const;

  const activity: Activity[] = [
    { id: 'a', icon: ArrowRightIcon, event: 'Moved to Screening', actor: 'Avery Quinn', at: ago(25 * minute) },
    { id: 'b', icon: MessageSquareIcon, event: 'Note added', actor: 'Avery Quinn', note: 'Strong portfolio. Ask about the agency years.', at: ago(5 * hour) },
    { id: 'c', icon: UserPlusIcon, event: 'Applied', actor: 'Careers page', at: ago(10 * day) },
  ];

  // Tags change in place here; nothing is saved.
  let sampleTags = $state(['Referral', 'Relocating to Leeds']);
  const sampleTeam = [
    { id: 'm1', userId: 'u1', email: 'avery@example.com', displayName: 'Avery Quinn', role: 'Owner' as const, joinedAt: ago(30 * day) },
    { id: 'm2', userId: 'u2', email: 'robin@example.com', displayName: 'Robin Ashby', role: 'Recruiter' as const, joinedAt: ago(20 * day) },
  ];
  const sampleTasks: TaskRow[] = [
    { id: 't1', applicationId: 'a1', kind: 'Review', note: 'Review this application.', assigneeUserId: 'u1', createdAt: ago(2 * hour), completedAt: null, completedByUserId: null },
    { id: 't2', applicationId: 'a1', kind: 'RejectionReview', note: 'Not authorised to work in the UK.', assigneeUserId: null, createdAt: ago(day), completedAt: null, completedByUserId: null },
    { id: 't3', applicationId: 'a1', kind: 'Task', note: 'Call the references.', assigneeUserId: null, createdAt: ago(3 * day), completedAt: ago(5 * hour), completedByUserId: 'u2' },
  ];
  const sampleScorecards: ScorecardEntry[] = [
    {
      id: 's1',
      stageId: 'interview',
      reviewerUserId: 'u2',
      ratings: [
        { criterionId: 'role_skills', label: 'Skills the role needs', rating: 'StronglyDemonstrated' },
        { criterionId: 'problem_solving', label: 'Problem solving', rating: 'Demonstrated' },
        { criterionId: 'work_knowledge', label: 'Knowledge of the work', rating: null },
      ],
      recommendation: 'StrongYes',
      note: 'Walked through the cache design and its trade-offs.',
      workflowVersion: 1,
      timelineEventId: 'e1',
      submittedAt: ago(3 * hour),
    },
  ];
  let notifications = $state([
    { id: 'n1', title: 'Priya Natarajan, Product Designer', detail: 'A referral arrived. Call within a day.', href: '/style-guide' },
  ]);
  let sheetOpen = $state(false);

  const quote: Evidence = {
    kind: 'Quote',
    label: 'Answer to "Why this role?"',
    text: 'I have led small design teams for six years and want to build tools that help them hire well.',
    source: 'Question 2',
  };

  // Files as the API describes them. None of these exists: a download from this page fails, which the preview shows.
  const applicationId = '00000000-0000-7000-8000-000000000000';
  const file = (n: number, fileName: string, contentType: string, scanStatus: ApplicationFileSummary['scanStatus'], sizeBytes: number): ApplicationFileSummary => ({
    id: `00000000-0000-7000-8000-00000000000${n}`,
    questionId: 'resume',
    uploadId: `00000000-0000-7000-8000-00000000001${n}`,
    fileName,
    contentType,
    sizeBytes,
    scanStatus,
    uploadedAt: new Date(now).toISOString(),
  });
  const word = 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
  const files = {
    clean: file(1, 'jordan-ellis-resume.pdf', 'application/pdf', 'Clean', 248_000),
    long: file(2, 'portfolio-case-studies-2019-to-2026-final-version.pdf', 'application/pdf', 'NotScanned', 4_200_000),
    image: file(3, 'headshot.png', 'image/png', 'Clean', 820_000),
    word: file(4, 'jordan-ellis-resume.docx', word, 'Clean', 52_000),
    pending: file(5, 'cover-letter.docx', word, 'Pending', 48_000),
    infected: file(6, 'references.zip', 'application/zip', 'Infected', 12_000),
  };
</script>

<svelte:head>
  <title>Style guide · ScreenHire</title>
</svelte:head>

<ShortcutsDialog bind:open={shortcutsOpen} />
<SecretDialog bind:open={secretOpen} title="Your new API key" label="API key" value="shk_example_7Qm2vR8kLx4pN9wZ3cT6yB1dF5gH0jS" />

<ConfirmDialog
  bind:open={confirmOpen}
  title="Close this job and remove it everywhere?"
  description="The careers page stops taking applications and every board is asked to remove the job, for good."
  cancelLabel="Keep it open"
  confirmLabel="Close & remove everywhere"
  failure="Could not close the job"
  onconfirm={async () => {}}
>
  {#snippet icon()}<CircleXIcon aria-hidden="true" />{/snippet}
</ConfirmDialog>

<main class="flex min-h-screen flex-col">
  <HeaderBand title="Style guide" wide>
    {#snippet actions()}
      <label class="flex items-center gap-2">
        <span class="text-muted-foreground">Width</span>
        <select
          class="h-9 rounded-lg border border-input bg-card px-2.5 pointer-coarse:h-11 pointer-coarse:text-reading"
          value={String(width)}
          onchange={(event) => setWidth(Number(event.currentTarget.value))}
        >
          {#each widths as option (option.value)}<option value={String(option.value)}>{option.label}</option>{/each}
        </select>
      </label>
      <Button variant="outline" size="lg" onclick={() => preferences.toggleTheme()}>
        {#if preferences.theme === 'dark'}<SunIcon aria-hidden="true" />Light theme{:else}<MoonIcon aria-hidden="true" />Dark theme{/if}
      </Button>
    {/snippet}
    {#snippet facts()}
      <Fact label="Source"><code class="block min-w-0"><TruncatedText text="docs/design/design-system.md" class="font-mono text-code" /></code></Fact>
      <Fact label="Catalogue">Section 11</Fact>
    {/snippet}
  </HeaderBand>

  <div class="overflow-x-auto">
    <div class={['mx-auto flex flex-col gap-5 px-4 py-6 md:px-6', !width && 'w-full max-w-page 2xl:max-w-page-wide']} style:width={width ? `${width}px` : undefined}>
      <Panel title="Header band">
        <div class="flex flex-col gap-4">
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">Detail: status, details, actions and facts</figcaption>
            <div class="overflow-hidden rounded-lg bg-secondary">
              <HeaderBand level={3} title="Samuel Okonkwo-Hartley">
                {#snippet status()}<StatusBadge status="review" label="Review suggested" />{/snippet}
                {#snippet details()}
                  <TruncatedText text="samuel.okonkwo@example.com" href="mailto:samuel.okonkwo@example.com" icon={MailIcon} underlined class="text-muted-foreground" />
                  <TruncatedText text="+1 555 0142" href="tel:+15550142" icon={PhoneIcon} underlined class="text-muted-foreground" />
                {/snippet}
                {#snippet actions()}
                  <Button variant="outline" size="lg"><ArrowRightIcon aria-hidden="true" />Move to stage</Button>
                {/snippet}
                {#snippet facts()}
                  <Fact label="Job">Senior product designer</Fact>
                  <Fact label="Stage">Screening</Fact>
                  <Fact label="Applied"><RelativeTime at={ago(2 * hour)} /></Fact>
                  <Fact label="Source">Careers page</Fact>
                {/snippet}
              </HeaderBand>
            </div>
          </figure>
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">With tabs</figcaption>
            <div class="overflow-hidden rounded-lg bg-secondary">
              <HeaderBand level={3} title="Senior product designer">
                {#snippet status()}<StatusBadge status="clear" label="Live" />{/snippet}
                {#snippet actions()}<Button variant="outline" size="lg"><PencilIcon aria-hidden="true" />Edit</Button>{/snippet}
                {#snippet tabs()}
                  <Tabs.Root value="pipeline">
                    <Tabs.List variant="line">
                      <Tabs.Trigger value="pipeline">Pipeline</Tabs.Trigger>
                      <Tabs.Trigger value="details">Details</Tabs.Trigger>
                      <Tabs.Trigger value="workflow">Workflow</Tabs.Trigger>
                    </Tabs.List>
                  </Tabs.Root>
                {/snippet}
              </HeaderBand>
            </div>
          </figure>
        </div>
      </Panel>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Panel" count={3} class="flex-[999_1_520px]">
          {#snippet actions()}
            <Tooltip.Root>
              <Tooltip.Trigger>
                {#snippet child({ props })}
                  <Button {...props} variant="ghost" size="icon" aria-label="More actions"><EllipsisIcon /></Button>
                {/snippet}
              </Tooltip.Trigger>
              <Tooltip.Content>More actions</Tooltip.Content>
            </Tooltip.Root>
            <Button variant="outline"><PlusIcon aria-hidden="true" />Add</Button>
          {/snippet}
          <p>The body. The title sits inside a fixed-height header, with an optional count and actions on the right.</p>
          {#snippet footer()}<p class="text-caption text-muted-foreground">An optional footer inset</p>{/snippet}
        </Panel>
        <div class="flex flex-[1_1_320px] flex-col gap-5">
          <Panel title="Loading">
            <div class="flex flex-col gap-2" aria-hidden="true">
              <Skeleton class="h-4 w-3/4" />
              <Skeleton class="h-4 w-1/2" />
            </div>
          </Panel>
          <Panel title="Could not load">
            <QueryError message="Could not load the timeline. Check your connection." onretry={() => {}} inset />
          </Panel>
          <Panel title="Form field">
            <Field.Field data-invalid>
              <Field.Label for="style-guide-email">Email</Field.Label>
              <Input id="style-guide-email" type="email" value="samuel.okonkwo" aria-invalid="true" aria-describedby="style-guide-email-error" />
              <Field.Error id="style-guide-email-error" errors={[{ message: 'Enter an email address, such as name@example.com.' }]} />
            </Field.Field>
          </Panel>
          <Panel title="Failure text">
            <FailureText message="Could not publish Senior product designer. Try again in a minute." />
          </Panel>
        </div>
      </div>

      <Panel title="Status badge">
        <div class="flex flex-col gap-4">
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">Needs attention, or beside a title</figcaption>
            <div class="flex flex-wrap gap-2">
              {#each statuses as item (item.status)}<StatusBadge status={item.status} label={item.label} />{/each}
            </div>
          </figure>
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">Calm, in a list or table</figcaption>
            <div class="flex flex-wrap gap-4">
              <StatusBadge status="clear" label="No concerns" quiet />
              <StatusBadge status="clear" label="Live" quiet />
              <StatusBadge status="neutral" label="Closed" quiet />
            </div>
          </figure>
        </div>
      </Panel>

      <Panel title="Overdue badge">
        <!-- Past the stage's time limit by two days and by five hours; within its limit, nothing shows. -->
        <div class="flex flex-wrap items-center gap-2">
          <OverdueBadge dueAt={new Date(Date.now() - 2 * 86_400_000).toISOString()} />
          <OverdueBadge dueAt={new Date(Date.now() - 5 * 3_600_000).toISOString()} />
          <Badge variant="muted">Screening</Badge>
        </div>
      </Panel>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Fact pill" class="flex-[1_1_320px]">
          <div class="flex flex-wrap gap-1.5">
            <Badge variant="muted">Full time</Badge>
            <Badge variant="muted">Remote</Badge>
            <Badge variant="muted">Screening</Badge>
          </div>
        </Panel>
        <Panel title="Count" class="flex-[1_1_320px]">
          <div class="flex flex-wrap gap-4">
            <span class="flex items-center gap-1.5">Candidates <Badge variant="count">24</Badge></span>
            <span class="flex items-center gap-1.5">Open jobs <Badge variant="count">3</Badge></span>
            <span class="flex items-center gap-1.5">New today <Badge variant="count">128</Badge></span>
          </div>
        </Panel>
      </div>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Toggle" class="flex-[999_1_520px]">
          <div class="flex flex-col gap-4">
            <div role="group" aria-label="Stage" class="flex gap-1 overflow-x-auto">
              {#each toggleOptions as option (option.id)}
                <Toggle bind:pressed={() => toggleChoice === option.id, () => (toggleChoice = option.id)}>
                  {option.label}
                  {#if option.count !== null}<Badge variant="count">{option.count}</Badge>{/if}
                </Toggle>
              {/each}
            </div>
            <div role="group" aria-label="View" class="flex gap-1">
              <Toggle pressed>Table</Toggle>
              <Toggle>Board</Toggle>
            </div>
          </div>
        </Panel>
        <Panel title="More menu" class="flex-[1_1_320px]">
          <div class="flex items-center gap-2">
            <Button variant="outline" size="lg"><PencilIcon aria-hidden="true" />Edit details</Button>
            <MoreMenu label="More actions">
              <DropdownMenu.Item>Duplicate</DropdownMenu.Item>
              <DropdownMenu.Separator />
              <DropdownMenu.Item variant="destructive">Close &amp; remove everywhere</DropdownMenu.Item>
            </MoreMenu>
          </div>
        </Panel>
      </div>

      <Panel title="Step marker">
        <div class="flex flex-wrap items-start gap-x-8 gap-y-5">
          <figure class="flex min-w-0 flex-[999_1_320px] flex-col gap-4">
            <figcaption class="text-caption text-muted-foreground">Across, for progress</figcaption>
            <StepMarkers label="New job" {steps} />
            <StepMarkers label="New job, finished" steps={steps.map((step) => ({ ...step, state: 'done' as const }))} />
          </figure>
          <figure class="flex min-w-0 flex-[1_1_240px] flex-col gap-4">
            <figcaption class="text-caption text-muted-foreground">Down, for a workflow's stages</figcaption>
            <StepLine items={stages} label="Stages in order" key={(stage) => stage.name}>
              {#snippet children(stage)}
                <span class="font-semibold">{stage.name}</span>
                {#if stage.limit}<span class="flex flex-wrap gap-1.5"><Badge variant="muted">{stage.limit}</Badge></span>{/if}
              {/snippet}
            </StepLine>
          </figure>
        </div>
      </Panel>

      <Panel title="Icon button">
        <div class="flex flex-wrap items-center gap-1">
          <IconButton label="Move up"><ArrowUpIcon /></IconButton>
          <IconButton label="Move down"><ArrowDownIcon /></IconButton>
          <IconButton label="Remove"><Trash2Icon /></IconButton>
        </div>
      </Panel>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Summary list" class="flex-[999_1_520px]">
          {#snippet actions()}<Button variant="outline"><PencilIcon aria-hidden="true" />Edit</Button>{/snippet}
          <SummaryList>
            <SummaryItem label="Title">Senior product designer</SummaryItem>
            <SummaryItem label="Location">Remote, United States</SummaryItem>
            <SummaryItem label="Employment">Full time</SummaryItem>
            <SummaryItem label="Salary">$120,000 to $145,000 a year</SummaryItem>
          </SummaryList>
        </Panel>
        <Panel title="File tile" class="flex-[1_1_320px]">
          <div class="flex flex-col gap-3">
            <FileTile {applicationId} file={files.clean}>
              {#snippet facts()}<Badge variant="muted" class="tabular-nums">2 pages</Badge>{/snippet}
            </FileTile>
            <FileTile {applicationId} file={files.long} />
            <FileTile {applicationId} file={files.image} />
            <FileTile {applicationId} file={files.pending} />
            <FileTile {applicationId} file={files.infected} />
          </div>
        </Panel>
      </div>

      <Panel title="Resume preview">
        <div class="flex flex-wrap items-start gap-x-6 gap-y-5">
          <figure class="flex min-w-0 flex-[1_1_320px] flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">A PDF that does not load. A loaded one draws its pages in a sandboxed frame</figcaption>
            <ResumePreview {applicationId} file={files.clean} />
          </figure>
          <figure class="flex min-w-0 flex-[1_1_320px] flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">A type with no preview</figcaption>
            <ResumePreview {applicationId} file={files.word} />
          </figure>
          <figure class="flex min-w-0 flex-[1_1_320px] flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">Withheld until the malware scan clears it</figcaption>
            <ResumePreview {applicationId} file={files.pending} />
            <ResumePreview {applicationId} file={files.infected} />
          </figure>
        </div>
      </Panel>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Key cap and tooltip" class="flex-[1_1_320px]">
          <div class="flex flex-wrap items-center gap-4">
            <KbdGroup><Kbd>Ctrl</Kbd><Kbd>K</Kbd></KbdGroup>
            <span class="flex items-center gap-1 text-caption text-muted-foreground"><Kbd>G</Kbd>then<Kbd>J</Kbd></span>
            <Kbd>?</Kbd>
            <Tooltip.Root>
              <Tooltip.Trigger>
                {#snippet child({ props })}
                  <Button {...props} variant="outline" size="icon" aria-label="Keyboard shortcuts" onclick={() => (shortcutsOpen = true)}>
                    <KeyboardIcon />
                  </Button>
                {/snippet}
              </Tooltip.Trigger>
              <Tooltip.Content>Keyboard shortcuts <Kbd>?</Kbd></Tooltip.Content>
            </Tooltip.Root>
          </div>
        </Panel>
        <Panel title="Shortcuts dialog" class="flex-[1_1_320px]">
          <Button variant="outline" onclick={() => (shortcutsOpen = true)}><KeyboardIcon aria-hidden="true" />Keyboard shortcuts</Button>
        </Panel>
      </div>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Move to stage" class="flex-[1_1_320px]">
          <MoveToStage candidateName="Samuel Okonkwo-Hartley" currentStageId="screening" stages={demoStages} variant="outline" size="lg" onmove={() => {}} />
        </Panel>
        <Panel title="Confirm dialog" class="flex-[1_1_320px]">
          <Button variant="outline" onclick={() => (confirmOpen = true)}>Close the job</Button>
        </Panel>
        <Panel title="Secret dialog" class="flex-[1_1_320px]">
          <Button variant="outline" onclick={() => (secretOpen = true)}>Show a new key</Button>
        </Panel>
        <Panel title="Email sheet" class="flex-[1_1_320px]">
          <EmailSheetDemo />
        </Panel>
        <Panel title="Live indicator" class="flex-[1_1_320px]">
          <div class="flex flex-col gap-1">
            {#each ['connected', 'reconnecting', 'disconnected'] as const as status (status)}<LiveIndicator {status} />{/each}
          </div>
        </Panel>
      </div>

      <Panel title="Email preview">
        <EmailPreview preview={demoEmail} />
      </Panel>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Empty state" class="flex-[999_1_520px]">
          <Empty.Root>
            <Empty.Header>
              <Empty.Media variant="icon"><InboxIcon /></Empty.Media>
              <Empty.Title>No candidates yet</Empty.Title>
              <Empty.Description>Applications appear here once the job is live.</Empty.Description>
            </Empty.Header>
            <Empty.Content><Button variant="outline">Publish the job</Button></Empty.Content>
          </Empty.Root>
        </Panel>
        <Panel title="Time and long text" class="flex-[1_1_320px]">
          <SummaryList>
            <SummaryItem label="Time ago"><RelativeTime at={ago(3 * hour)} /></SummaryItem>
            <SummaryItem label="Date in a table"><DateText at={ago(9 * day)} /></SummaryItem>
            <SummaryItem label="Truncated"><TruncatedText text="Senior product designer, design systems and accessibility, North America" /></SummaryItem>
            <SummaryItem label="Truncated link">
              <TruncatedText text="samuel.okonkwo-hartley@careers.example.com" href="mailto:samuel.okonkwo-hartley@careers.example.com" icon={MailIcon} underlined />
            </SummaryItem>
          </SummaryList>
        </Panel>
      </div>

      <Panel title="Sidebar">
        <div class="flex flex-wrap gap-4">
          {#each [{ label: 'Expanded', open: true }, { label: 'Icon rail', open: false }] as demo (demo.label)}
            <figure class="flex min-w-0 flex-col gap-2">
              <figcaption class="text-caption text-muted-foreground">{demo.label}; a sheet behind the menu button on phones</figcaption>
              <!-- An inset frame, not a surface (S4). transform-gpu holds the sidebar, which is fixed to the window, inside this frame. -->
              <div class="h-72 w-104 max-w-full transform-gpu overflow-hidden rounded-lg bg-secondary">
                <Sidebar.Provider open={demo.open} class="h-full min-h-0">
                  <Sidebar.Root collapsible="icon" class="h-full">
                    <Sidebar.Content>
                      <Sidebar.Group>
                        <Sidebar.Menu>
                          {#each sections as section, index (section.href)}
                            {@const Icon = section.icon}
                            <Sidebar.MenuItem>
                              <Sidebar.MenuButton isActive={index === 0} tooltipContent={section.label}>
                                {#snippet child({ props })}
                                  <a href="#sidebar" {...props}><Icon aria-hidden="true" /><span>{section.label}</span></a>
                                {/snippet}
                              </Sidebar.MenuButton>
                            </Sidebar.MenuItem>
                          {/each}
                        </Sidebar.Menu>
                      </Sidebar.Group>
                    </Sidebar.Content>
                  </Sidebar.Root>
                  <Sidebar.Inset class="bg-background p-3">
                    <Sidebar.Trigger />
                  </Sidebar.Inset>
                </Sidebar.Provider>
              </div>
            </figure>
          {/each}
        </div>
      </Panel>

      <Panel title="Brand bar">
        <figure class="flex flex-col gap-2">
          <figcaption class="text-caption text-muted-foreground">Sign-in, onboarding and error pages, outside the app shell</figcaption>
          <div class="overflow-hidden rounded-lg bg-secondary"><BrandBar /></div>
        </figure>
      </Panel>

      <Panel title="Table" count={people.length} flush>
        <div class="overflow-x-auto">
          <Table.Root>
            <Table.Header>
              <Table.Row class="bg-muted hover:bg-muted">
                <Table.Head class="px-4">Candidate</Table.Head>
                <Table.Head>Authenticity</Table.Head>
                <Table.Head>Stage</Table.Head>
                <Table.Head>Applied</Table.Head>
              </Table.Row>
            </Table.Header>
            <Table.Body>
              {#each people as person (person.id)}
                <Table.Row class="h-12">
                  <Table.Cell class="px-4"><a href="#table" class="font-semibold text-foreground no-underline hover:underline">{person.name}</a></Table.Cell>
                  <Table.Cell><StatusBadge status={person.status} label={person.label} quiet={person.status === 'clear'} /></Table.Cell>
                  <Table.Cell>{person.stage}</Table.Cell>
                  <Table.Cell><DateText at={person.at} /></Table.Cell>
                </Table.Row>
              {/each}
            </Table.Body>
          </Table.Root>
        </div>
      </Panel>

      <Panel title="Search field" flush>
        {#snippet tools()}
          <SearchField label="Search candidates" class="ml-auto flex-[0_1_15rem]" />
          <Badge variant="muted">2 selected</Badge>
        {/snippet}
        <p class="px-4.5 py-4 text-muted-foreground max-md:px-4">A list's table panel holds its title, then its search and filters, in the header.</p>
      </Panel>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="List item, the table on a phone" flush class="flex-[1_1_320px]">
          <ul>
            {#each people as person (person.id)}
              <ListItem href="#list-item" title={person.name}>
                {#snippet status()}<StatusBadge status={person.status} label={person.label} quiet={person.status === 'clear'} />{/snippet}
                {#snippet meta()}<DateText at={person.at} />{/snippet}
                {#snippet facts()}
                  <Badge variant="muted">{person.stage}</Badge>
                  <Badge variant="muted">{person.source}</Badge>
                {/snippet}
              </ListItem>
            {/each}
          </ul>
        </Panel>
        <Panel title="Activity" class="flex-[1_1_320px]">
          <ActivityList label="Activity" items={activity} />
        </Panel>
      </div>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Tag list" class="flex-[1_1_320px]">
          <TagList
            tags={sampleTags}
            onadd={async (tag) => ((sampleTags = [...sampleTags, tag]), true)}
            onremove={(tag) => (sampleTags = sampleTags.filter((other) => other !== tag))}
          />
        </Panel>
        <Panel title="Task list" class="flex-[1_1_320px]">
          <TaskList label="Tasks" tasks={sampleTasks} members={sampleTeam} currentUserId="u1" oncomplete={() => {}} />
        </Panel>
        <Panel title="Scorecard" class="flex-[1_1_320px]">
          <ScorecardList scorecards={sampleScorecards} stageName={() => 'Interview'} members={sampleTeam} currentUserId="u1" />
        </Panel>
      </div>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Notification tray" class="flex-[1_1_320px]">
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">Fixed to the bottom corner of the app; shown in place here</figcaption>
            <NotificationTray class="static w-full" items={notifications} ondismiss={(id) => (notifications = notifications.filter((item) => item.id !== id))} />
          </figure>
        </Panel>
        <Panel title="Sheet" class="flex-[1_1_320px]">
          <Button variant="outline" onclick={() => (sheetOpen = true)}>Add note</Button>
          <Sheet.Root bind:open={sheetOpen}>
            <Sheet.Content side="right" class="w-full overflow-y-auto">
              <Sheet.Header>
                <Sheet.Title>Add a note</Sheet.Title>
                <Sheet.Description>Your team sees it in Activity, with your name and the time.</Sheet.Description>
              </Sheet.Header>
              <form class="flex flex-col gap-4 px-4 pb-4" onsubmit={(event) => (event.preventDefault(), (sheetOpen = false))}>
                <Field.Field>
                  <Field.Label for="style-guide-note">Note</Field.Label>
                  <Textarea id="style-guide-note" rows={4} />
                </Field.Field>
                <div class="flex justify-end gap-2">
                  <Sheet.Close type="button" class={buttonVariants({ variant: 'outline', size: 'lg' })}>Cancel</Sheet.Close>
                  <Button type="submit" size="lg">Add note</Button>
                </div>
              </form>
            </Sheet.Content>
          </Sheet.Root>
        </Panel>
      </div>

      <Panel title="Action bar">
        <figure class="flex flex-col gap-2">
          <figcaption class="text-caption text-muted-foreground">A phone's primary action, fixed to the bottom of the screen; shown in place here</figcaption>
          <div class="max-w-95 overflow-hidden rounded-lg bg-secondary pt-6">
            <ActionBar class="static z-auto"><Button size="lg"><ArrowRightIcon aria-hidden="true" />Move to stage</Button></ActionBar>
          </div>
        </figure>
      </Panel>

      <Panel title="Integrity chip and evidence">
        <div class="flex flex-col gap-4">
          <div class="flex flex-wrap gap-2">
            {#each Object.values(authenticityLevels) as level (level.label)}<IntegrityChip kind="authenticity" {level} />{/each}
            <IntegrityChip kind="authenticity" level={null} compact />
          </div>
          <div class="flex flex-wrap gap-2">
            {#each Object.values(aiAssistanceLevels) as level (level.label)}<IntegrityChip kind="aiAssistance" {level} />{/each}
          </div>
          <SignalEvidence evidence={quote} />
        </div>
      </Panel>
    </div>
  </div>
</main>
