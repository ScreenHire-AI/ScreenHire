<script lang="ts">
  import ArrowRightIcon from '@lucide/svelte/icons/arrow-right';
  import BotIcon from '@lucide/svelte/icons/bot';
  import ChevronDownIcon from '@lucide/svelte/icons/chevron-down';
  import CircleXIcon from '@lucide/svelte/icons/circle-x';
  import EllipsisIcon from '@lucide/svelte/icons/ellipsis';
  import FileTextIcon from '@lucide/svelte/icons/file-text';
  import GlobeIcon from '@lucide/svelte/icons/globe';
  import HourglassIcon from '@lucide/svelte/icons/hourglass';
  import InboxIcon from '@lucide/svelte/icons/inbox';
  import KeyboardIcon from '@lucide/svelte/icons/keyboard';
  import MailIcon from '@lucide/svelte/icons/mail';
  import MessageSquareIcon from '@lucide/svelte/icons/message-square';
  import MoonIcon from '@lucide/svelte/icons/moon';
  import PencilIcon from '@lucide/svelte/icons/pencil';
  import PhoneIcon from '@lucide/svelte/icons/phone';
  import PlusIcon from '@lucide/svelte/icons/plus';
  import SendIcon from '@lucide/svelte/icons/send';
  import StarIcon from '@lucide/svelte/icons/star';
  import SunIcon from '@lucide/svelte/icons/sun';
  import TimerIcon from '@lucide/svelte/icons/timer';
  import Trash2Icon from '@lucide/svelte/icons/trash-2';
  import UserPlusIcon from '@lucide/svelte/icons/user-plus';
  import UserRoundIcon from '@lucide/svelte/icons/user-round';
  import UsersIcon from '@lucide/svelte/icons/users';
  import type { ApplicationFileSummary, HistoryEntry, ScorecardEntry, SignalEvidence as Evidence, WorkflowStage } from '@screenhire/api-client';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import ActionBar from '#lib/components/action-bar.svelte';
  import ActivityList, { type Activity } from '#lib/components/activity-list.svelte';
  import AnswerMenu from '#lib/components/answer-menu.svelte';
  import BrandBar from '#lib/components/brand-bar.svelte';
  import BuiltInBadge from '#lib/components/built-in-badge.svelte';
  import ChoiceCard from '#lib/components/choice-card.svelte';
  import ConfirmDialog from '#lib/components/confirm-dialog.svelte';
  import CountButton, { type CountItem } from '#lib/components/count-button.svelte';
  import Picker from '#lib/components/picker.svelte';
  import DateText from '#lib/components/date-text.svelte';
  import EmailPreview from '#lib/components/email-preview.svelte';
  import Fact from '#lib/components/fact.svelte';
  import FilterMenu from '#lib/components/filter-menu.svelte';
  import FailureText from '#lib/components/failure-text.svelte';
  import FileRow from '#lib/components/file-row.svelte';
  import HeaderBand from '#lib/components/header-band.svelte';
  import HistorySheet from '#lib/components/history-sheet.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import IntegrityChip from '#lib/components/integrity-chip.svelte';
  import LastUpdated from '#lib/components/last-updated.svelte';
  import ListItem from '#lib/components/list-item.svelte';
  import LiveIndicator from '#lib/components/live-indicator.svelte';
  import MetricCard, { type MetricPoint } from '#lib/components/metric-card.svelte';
  import MultiFilterMenu from '#lib/components/multi-filter-menu.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import MoveToStage from '#lib/components/move-to-stage.svelte';
  import ExpandableRow from '#lib/components/expandable-row.svelte';
  import Notice from '#lib/components/notice.svelte';
  import NotificationTray from '#lib/components/notification-tray.svelte';
  import OverdueBadge from '#lib/components/overdue-badge.svelte';
  import Panel from '#lib/components/panel.svelte';
  import QueryError from '#lib/components/query-error.svelte';
  import RecordTabs, { type RecordTab } from '#lib/components/record-tabs.svelte';
  import RelativeTime from '#lib/components/relative-time.svelte';
  import LevelScale from '#lib/components/level-scale.svelte';
  import MentionField from '#lib/components/mention-field.svelte';
  import RichTextEditor from '#lib/components/rich-text-editor.svelte';
  import NoteText from '#lib/components/note-text.svelte';
  import Rating from '#lib/components/rating.svelte';
  import RatingAnswer from '#lib/components/rating-answer.svelte';
  import ScorecardList from '#lib/components/scorecard-list.svelte';
  import SearchField from '#lib/components/search-field.svelte';
  import SecretDialog from '#lib/components/secret-dialog.svelte';
  import SettingsMenu from '#lib/components/settings-menu.svelte';
  import ShortcutsDialog from '#lib/components/shortcuts-dialog.svelte';
  import SignalEvidence from '#lib/components/signal-evidence.svelte';
  import StatusBadge from '#lib/components/status-badge.svelte';
  import StepLine from '#lib/components/step-line.svelte';
  import StepMarkers, { type Step } from '#lib/components/step-markers.svelte';
  import SummaryBand from '#lib/components/summary-band.svelte';
  import SummaryCard from '#lib/components/summary-card.svelte';
  import SummaryItem from '#lib/components/summary-item.svelte';
  import SummaryList from '#lib/components/summary-list.svelte';
  import TagList from '#lib/components/tag-list.svelte';
  import type { TaskRow } from '#lib/components/task-list.svelte';
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
  import * as Tabs from '#lib/components/ui/tabs/index.js';
  import { Toggle } from '#lib/components/ui/toggle/index.js';
  import * as Tooltip from '#lib/components/ui/tooltip/index.js';
  import { formatDate, hour, day, minute } from '#lib/dates.js';
  import { applyFormKinds, scorecardKinds } from '#lib/form-builder.js';
  import { aiAssistanceLevels, aiAssistanceOrder, authenticityLevels, authenticityOrder } from '#lib/integrity.js';
  import { mentionToken, type PickedMention } from '#lib/mentions.js';
  import { preferences } from '#lib/preferences.svelte.js';
  import { sections } from '#lib/shell.js';
  import type { StatusName } from '#lib/status.js';
  import EditPreviewDemo from './edit-preview-demo.svelte';
  import AddCandidateDemo from './add-candidate-demo.svelte';
  import BrandingDemo from './branding-demo.svelte';
  import EmailComposerDemo, { demoEmail } from './email-composer-demo.svelte';
  import FormBuilderDemo from './form-builder-demo.svelte';
  import ReorderDemo from './reorder-demo.svelte';
  import SortTableDemo from './sort-table-demo.svelte';
  import TaskCheckboxDemo from './task-checkbox-demo.svelte';

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
  let emailOpen = $state(true);
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
  let ratingFive = $state('4');
  let ratingTen = $state('');
  let ratingYesNo = $state('yes');

  const now = Date.now();
  const ago = (milliseconds: number) => new Date(now - milliseconds).toISOString();

  // A record's tabs (section 11): counts in the count badge, and a state in the count where one fits (7/8).
  const candidateTabs: RecordTab[] = [
    { value: 'overview', label: 'Overview' },
    { value: 'application', label: 'Application' },
    { value: 'messages', label: 'Messages', count: 3 },
    { value: 'scorecards', label: 'Scorecards', count: 0 },
    { value: 'verification', label: 'Verification', count: 5 },
    { value: 'activity', label: 'Activity', count: 10 },
  ];
  const jobTabs: RecordTab[] = [
    { value: 'candidates', label: 'Candidates', count: 24 },
    { value: 'distribution', label: 'Distribution', count: 7, of: 8, countLabel: 'posted on 7 of 8 boards' },
    { value: 'details', label: 'Details' },
  ];
  let candidateTab = $state('overview');
  let jobTab = $state('distribution');

  // Twelve weeks of a metric, oldest first, for the metric cards.
  const weeks = (values: number[]): MetricPoint[] =>
    values.map((value, index) => ({ label: `Week of ${formatDate(ago((values.length - 1 - index) * 7 * day))}`, value }));

  // Behind a count (T4): two items list as they are; past 10 the sheet offers a search.
  const guideStages = [
    { value: 'applied', label: 'Applied', count: 4 },
    { value: 'screening', label: 'Screening', count: 2 },
    { value: 'interview', label: 'Interview', count: 1 },
  ];
  const guideJobs = [
    { value: 'dotnet', label: 'Senior .NET Engineer', count: 7 },
    { value: 'support', label: 'Support Engineer', count: 3 },
  ];
  const usedBy: CountItem[] = [
    { id: 'j1', label: 'Senior product designer', href: '#count-button' },
    { id: 'j2', label: 'Support engineer', href: '#count-button' },
  ];
  const manyJobs: CountItem[] = [
    'Account executive', 'Backend engineer', 'Customer success lead', 'Data analyst', 'Designer, brand', 'Frontend engineer',
    'Marketing manager', 'Office coordinator', 'Product manager', 'QA engineer', 'Recruiter', 'Site reliability engineer',
    'Support engineer', 'Technical writer',
  ].map((label, index) => ({ id: `m${index}`, label, href: '#count-button' }));

  // Every status in design system section 3.2, by its row: the icon alone tells them apart.
  const statuses: { status: StatusName; label: string }[] = [
    { status: 'done', label: 'No concerns' },
    { status: 'open', label: 'Open' },
    { status: 'review', label: 'Review suggested' },
    { status: 'overdue', label: 'Overdue by 2 days' },
    { status: 'verification', label: 'Verification suggested' },
    { status: 'needsFixes', label: 'Needs fixes' },
    { status: 'unsafeFile', label: 'Unsafe file' },
    { status: 'ai', label: 'Some signs of AI help' },
    { status: 'waiting', label: 'Sent' },
    { status: 'working', label: 'Scanning' },
    { status: 'closed', label: 'Closed' },
    { status: 'draft', label: 'Draft' },
    { status: 'notInEffect', label: 'Not posted' },
  ];
  const calmStatuses: { status: StatusName; label: string }[] = [
    { status: 'done', label: 'No concerns' },
    { status: 'open', label: 'Live' },
    { status: 'ai', label: 'No signs' },
    { status: 'waiting', label: 'Queued' },
    { status: 'closed', label: 'Closed' },
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
    { id: '2', name: 'Priya Natarajan', status: 'done', label: 'No concerns', stage: 'Interview', source: 'Referral', at: ago(3 * day) },
    { id: '3', name: 'Samuel Okonkwo-Hartley', status: 'verification', label: 'Verification suggested', stage: 'Applied', source: 'Job board', at: ago(9 * day) },
  ] as const;

  const activity: Activity[] = [
    { id: 'a', icon: ArrowRightIcon, event: 'Moved to Screening', actor: 'Avery Quinn', at: ago(25 * minute) },
    { id: 'b', icon: MessageSquareIcon, event: 'Note added', actor: 'Avery Quinn', note: 'Strong portfolio. Ask about the agency years.', at: ago(5 * hour) },
    { id: 'c', icon: UserPlusIcon, event: 'Applied', actor: 'Careers page', at: ago(10 * day) },
  ];

  // Tags change in place here; nothing is saved.
  let sampleTags = $state(['Referral', 'Relocating to Leeds']);
  let jobsChoice = $state<'All' | 'HiringTeamOnly'>('All');
  const sampleTeam = [
    { id: 'm1', userId: 'u1', email: 'avery@example.com', displayName: 'Avery Quinn', role: 'Owner' as const, customRoleId: null, roleName: 'Owner', joinedAt: ago(30 * day), jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
    { id: 'm2', userId: 'u2', email: 'robin@example.com', displayName: 'Robin Ashby', role: 'Recruiter' as const, customRoleId: null, roleName: 'Recruiter', joinedAt: ago(20 * day), jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
  ];
  // The mention example: a type-ahead of teammates; @ opens it. People's ids are user ids, as the API stores them.
  const mentionTeam = [
    { id: 'm1', userId: '0199e8a0-0000-7000-8000-0000000000a1', email: 'avery@example.com', displayName: 'Avery Quinn', role: 'Owner' as const, customRoleId: null, roleName: 'Owner', joinedAt: ago(30 * day), jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
    { id: 'm2', userId: '0199e8a0-0000-7000-8000-0000000000a2', email: 'robin@example.com', displayName: 'Robin Hale', role: 'Recruiter' as const, customRoleId: null, roleName: 'Recruiter', joinedAt: ago(20 * day), jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
    { id: 'm3', userId: '0199e8a0-0000-7000-8000-0000000000a3', email: 'rowan@example.com', displayName: 'Rowan Ellis', role: 'Viewer' as const, customRoleId: null, roleName: 'Viewer', joinedAt: ago(10 * day), jobAccess: 'All' as const, seesAllJobs: true, jobCount: 0 },
  ];
  let mentionText = $state('Can you book the call once they confirm, @Ro');
  let mentionPicked = $state<PickedMention[]>([]);
  let richTextEditor = $state<ReturnType<typeof RichTextEditor> | null>(null);
  let richText = $state('<h2>Next steps</h2><p>Hi {{ firstName }}, thank you for applying for <strong>{{ jobTitle }}</strong>.</p><ul><li>Book a time</li><li>Bring your portfolio</li></ul>');
  const sampleTasks: TaskRow[] = [
    { id: 't1', applicationId: 'a1', kind: 'Review', note: 'Review this application.', assigneeUserId: 'u1', createdAt: ago(2 * hour), completedAt: null, completedByUserId: null },
    { id: 't2', applicationId: 'a1', kind: 'RejectionReview', note: 'Not authorised to work in the UK.', assigneeUserId: null, createdAt: ago(day), completedAt: null, completedByUserId: null },
    { id: 't3', applicationId: 'a1', kind: 'Task', note: 'Call the references.', assigneeUserId: null, createdAt: ago(3 * day), completedAt: ago(5 * hour), completedByUserId: 'u2' },
    {
      id: 't4',
      applicationId: 'a1',
      kind: 'Scorecard',
      note: 'Fill in the Interview scorecard. Focus on the pairing exercise.',
      assigneeUserId: 'u2',
      scorecardTemplateId: 'interview',
      createdAt: ago(4 * hour),
      completedAt: null,
      completedByUserId: null,
    },
  ];
  const sampleScorecards: ScorecardEntry[] = [
    {
      id: 's1',
      stageId: 'interview',
      reviewerUserId: 'u2',
      answers: [
        { questionId: 'problem_solving', label: 'Problem solving', type: 'Rating5', rating: 5 },
        { questionId: 'dotnet', label: '.NET and C#', type: 'Rating10', rating: 8 },
        { questionId: 'finished', label: 'Did they finish the exercise in the time?', type: 'RatingYesNo', value: 'yes' },
        { questionId: 'notes', label: 'Notes on the exercise', type: 'LongText' },
      ],
      rating: 4.6,
      ratings: [],
      recommendation: 'StrongYes',
      note: 'Walked through the cache design and its trade-offs.',
      workflowVersion: 1,
      timelineEventId: 'e1',
      submittedAt: ago(3 * hour),
      scorecardTemplateId: 'interview',
      scorecardName: 'Interview',
    },
  ];
  let notifications = $state([
    { id: 'n1', title: 'Priya Natarajan, Product Designer', detail: 'A referral arrived. Call within a day.', href: '/style-guide' },
  ]);
  let sheetOpen = $state(false);
  let historyOpen = $state(false);
  const part = (text: string, strong = false) => ({ text, strong });
  const sampleHistory: HistoryEntry[] = [
    {
      id: 'h1',
      at: ago(20 * minute),
      by: 'Member',
      userId: 'u2',
      apiKeyId: null,
      lines: [
        {
          text: 'changed Years of experience from Short text to Number',
          parts: [part('changed '), part('Years of experience', true), part(' from Short text to Number')],
          details: [
            { label: 'Answer', before: 'Short text', after: 'Number', kind: 'Text' },
            { label: 'Required', before: 'on', after: 'on', kind: 'Text' },
          ],
        },
        {
          text: 'added Did they finish the exercise in the time?',
          parts: [part('added '), part('Did they finish the exercise in the time?', true)],
          details: [{ label: 'Answer', before: null, after: 'Yes or no', kind: 'Text' }],
        },
      ],
    },
    {
      id: 'h2',
      at: ago(3 * hour),
      by: 'Member',
      userId: 'u1',
      apiKeyId: null,
      lines: [{ text: 'made Notes on the exercise optional', parts: [part('made '), part('Notes on the exercise', true), part(' optional')], details: [] }],
    },
    {
      id: 'h3',
      at: ago(3 * day),
      by: 'Member',
      userId: 'u1',
      apiKeyId: null,
      lines: [
        {
          text: 'renamed the template from Tech interview to Technical interview',
          parts: [part('renamed the template from Tech interview to '), part('Technical interview', true)],
          details: [],
        },
      ],
    },
    {
      id: 'h4',
      at: ago(3 * day + hour),
      by: 'ScreenHire',
      userId: null,
      apiKeyId: null,
      lines: [{ text: 'created the template from Software engineer', parts: [part('created the template from '), part('Software engineer', true)], details: [] }],
    },
  ];

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
  <HeaderBand title="Style guide">
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
    <div class={['flex flex-col gap-5 px-4 py-6 md:px-6', !width && 'w-full']} style:width={width ? `${width}px` : undefined}>
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
                {#snippet status()}<StatusBadge status="open" label="Open" />{/snippet}
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
        <!-- Light and dark side by side: a `.dark` box takes the dark tokens whatever the page's theme (section 3.3). -->
        <div class="grid gap-4 @container lg:grid-cols-2">
          {#each ['light', 'dark'] as theme (theme)}
            <section aria-label={theme === 'dark' ? 'Dark' : 'Light'} class={['flex flex-col gap-4 rounded-lg bg-background p-4 text-foreground', theme]}>
              <figure class="flex flex-col gap-2">
                <figcaption class="text-caption text-muted-foreground">Needs attention, or beside a title</figcaption>
                <div class="flex flex-wrap gap-2">
                  {#each statuses as item (item.status)}<StatusBadge status={item.status} label={item.label} />{/each}
                </div>
              </figure>
              <figure class="flex flex-col gap-2">
                <figcaption class="text-caption text-muted-foreground">Calm, in a list or table</figcaption>
                <div class="flex flex-wrap gap-4">
                  {#each calmStatuses as item (item.status)}<StatusBadge status={item.status} label={item.label} quiet />{/each}
                </div>
              </figure>
              <figure class="flex flex-col gap-2">
                <figcaption class="text-caption text-muted-foreground">A panel on the page, with the soft surface shadow</figcaption>
                <div class="rounded-panel border border-border bg-card p-4 shadow-surface">
                  <p class="flex flex-wrap items-center gap-2">Count on a pressed toggle <Toggle pressed size="lg">Screening <Badge variant="count">5</Badge></Toggle></p>
                </div>
              </figure>
            </section>
          {/each}
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
        <Panel title="Picker" class="flex-[1_1_320px]">
          <Picker
            label="Add a person"
            icon={UserPlusIcon}
            options={[
              { id: 'm2', label: 'Robin Hale', hint: "Interviewer, only jobs they're on" },
              { id: 'm3', label: 'Rowan Ellis', hint: 'Viewer, all jobs' },
            ]}
            searchLabel="Search people"
            empty="Nobody else to add."
            onpick={() => {}}
          />
        </Panel>
        <Panel title="Choice card" class="flex-[1_1_320px]">
          <div role="radiogroup" aria-label="Jobs" class="flex flex-col gap-2">
            <ChoiceCard name="style-guide-jobs" value="All" bind:group={jobsChoice} title="All jobs" description="Including jobs added later." />
            <ChoiceCard name="style-guide-jobs" value="HiringTeamOnly" bind:group={jobsChoice} title="Only jobs they're on" description="Each job's hiring team decides." />
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
          <IconButton label="More actions"><EllipsisIcon /></IconButton>
          <IconButton label="Edit"><PencilIcon /></IconButton>
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
      </div>

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
        <Panel title="Email composer" class="flex-[1_1_320px]">
          <EmailComposerDemo />
        </Panel>
        <Panel title="Add candidate sheet" class="flex-[1_1_320px]">
          <AddCandidateDemo />
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

      <BrandingDemo />

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
            <SummaryItem label="Truncated, two lines">
              <TruncatedText
                lines={2}
                text="Send the follow-up questionnaire about availability, notice period, preferred start date and the right to work"
              />
            </SummaryItem>
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

      <Panel title="Table" count={4} flush>
        <SortTableDemo />
      </Panel>

      <Panel title="Search field" flush>
        {#snippet tools()}
          <SearchField label="Search candidates" class="ml-auto flex-[0_1_15rem]" />
          <span class="flex shrink-0 items-center gap-1.5 text-muted-foreground">Selected <Badge variant="count">2</Badge></span>
        {/snippet}
        <p class="px-4.5 py-4 text-muted-foreground max-md:px-4">A list's table panel holds its title, then its search and filters, in the header.</p>
      </Panel>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="List item, the table on a phone" flush class="flex-[1_1_320px]">
          <ul>
            {#each people as person (person.id)}
              <ListItem href="#list-item" title={person.name}>
                {#snippet status()}<StatusBadge status={person.status} label={person.label} quiet={person.status === 'done'} />{/snippet}
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
        <Panel title="Mention" class="flex-[1_1_320px]">
          <div class="flex flex-col gap-4">
            <div class="flex flex-col gap-2">
              <label for="style-guide-mention" class="text-caption font-semibold text-muted-foreground">Note for the hiring team</label>
              <MentionField id="style-guide-mention" bind:value={mentionText} bind:picked={mentionPicked} members={mentionTeam} currentUserId={mentionTeam[0].userId} />
            </div>
            <NoteText text={`Good detail on the payments service. ${mentionToken(mentionTeam[1].userId)}, ask how they measured it.`} members={mentionTeam} editedAt={ago(2 * minute)} />
          </div>
        </Panel>
        <Panel title="Rich text editor" class="flex-[1_1_320px]">
          <div class="flex flex-col gap-2">
            <Field.Label id="style-guide-rich-text-label" for="style-guide-rich-text" class="text-caption font-semibold text-muted-foreground" onclick={() => richTextEditor?.focus()}>Message</Field.Label>
            <RichTextEditor
              bind:this={richTextEditor}
              bind:value={richText}
              id="style-guide-rich-text"
              labelledBy="style-guide-rich-text-label"
              variables={[
                { name: 'firstName', label: 'First name' },
                { name: 'jobTitle', label: 'Job title' },
              ]}
            />
          </div>
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
          <TaskCheckboxDemo tasks={sampleTasks} members={sampleTeam} />
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
        <Panel title="History sheet" class="flex-[1_1_320px]">
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">The last fact of a header (T6), with History opening the sheet</figcaption>
            <dl class="flex flex-wrap gap-x-8 gap-y-3">
              <Fact label="Last updated">
                <LastUpdated value={{ at: ago(20 * minute), by: 'Member', userId: 'u2' }} members={sampleTeam} currentUserId="u1" onhistory={() => (historyOpen = true)} />
              </Fact>
              <Fact label="Last updated">
                <LastUpdated value={{ at: null, by: 'ScreenHire' }} members={sampleTeam} currentUserId="u1" />
              </Fact>
              <Fact label="Created">
                <LastUpdated value={{ at: ago(2 * 60 * minute), by: 'Member', userId: 'u2', created: true }} members={sampleTeam} currentUserId="u1" />
              </Fact>
            </dl>
          </figure>
          <figure class="mt-4 flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">On a settings page's title row, where no label shows, it starts with its verb</figcaption>
            <p class="text-muted-foreground">
              <LastUpdated value={{ at: ago(2 * 60 * minute), by: 'Member', userId: 'u2', created: true }} lead members={sampleTeam} currentUserId="u1" />
            </p>
          </figure>
          <HistorySheet
            bind:open={historyOpen}
            name="Technical interview"
            entries={sampleHistory}
            members={sampleTeam}
            currentUserId="u1"
            note="Jobs keep the copy they started with; a change here reaches jobs created after it."
          />
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

      <Panel title="Tabs">
        <div class="flex flex-col gap-4">
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">A candidate: a count on each tab where one fits</figcaption>
            <div class="overflow-hidden rounded-lg bg-secondary">
              <Tabs.Root bind:value={candidateTab}>
                <div class="border-b border-border bg-card px-4 pt-2 md:px-6"><RecordTabs label="Candidate sections" tabs={candidateTabs} /></div>
              </Tabs.Root>
            </div>
          </figure>
          <figure class="flex flex-col gap-2">
            <figcaption class="text-caption text-muted-foreground">A job: the state in the count, posted on 7 of 8 boards</figcaption>
            <div class="overflow-hidden rounded-lg bg-secondary">
              <Tabs.Root bind:value={jobTab}>
                <div class="border-b border-border bg-card px-4 pt-2 md:px-6"><RecordTabs label="Job sections" tabs={jobTabs} /></div>
              </Tabs.Root>
            </div>
          </figure>
        </div>
      </Panel>

      <Panel title="Expandable row">
        <ol aria-label="Emails" class="flex flex-col divide-y divide-border-subtle">
          <li class="flex flex-col gap-3 py-2 first:pt-0 last:pb-0">
            <ExpandableRow id="style-guide-email-1" bind:expanded={emailOpen}>
              {#snippet icon()}<SendIcon />{/snippet}
              {#snippet summary()}
                <span class="font-semibold break-words">Your interview with Brightwater Studio</span>
                <span class="text-caption text-muted-foreground">Robin Ashby, 2 hours ago</span>
              {/snippet}
              <p>Hi Priya, thank you for your time on Thursday. We would like to invite you to a second interview.</p>
            </ExpandableRow>
          </li>
          <li class="flex flex-col gap-3 py-2 first:pt-0 last:pb-0">
            <ExpandableRow id="style-guide-email-2">
              {#snippet icon()}<SendIcon />{/snippet}
              {#snippet summary()}
                <span class="font-semibold break-words">We received your application for Senior Product Designer</span>
                <span class="text-caption text-muted-foreground">Sent, yesterday</span>
              {/snippet}
              <p>Hi Priya, thank you for applying. The hiring team will review it.</p>
            </ExpandableRow>
          </li>
        </ol>
      </Panel>

      <Panel title="Notice">
        <div class="flex flex-col gap-3">
          <Notice href="#notice" link="Fix on Distribution"><span class="font-semibold">Adzuna refused this job's posting.</span> The job stays off that board until it is fixed.</Notice>
          <Notice href="#notice" link="Fix in Branding">The careers page could not be published because the logo is missing.</Notice>
        </div>
      </Panel>

      <Panel title="Summary card" flush>
        <SummaryBand>
          <SummaryCard icon={GlobeIcon} title="Network" count="2 signals" description="A hosting provider's network, in a different country from theirs." />
          <SummaryCard icon={UserRoundIcon} title="Identity" count="1 signal" description="Shares an email, phone, device and resume with 2 other applications." />
          <SummaryCard icon={BotIcon} title="Automation" count="No signals" description="Nothing automated was seen while they applied." />
          <SummaryCard icon={FileTextIcon} title="Content" count="1 signal" description="Resume and answers give different years of experience." href="#summary-card" />
        </SummaryBand>
      </Panel>

      <Panel title="Metric card" flush>
        <SummaryBand>
          <MetricCard icon={UsersIcon} title="Applicants this week" value="16" change={7} period="last week" better="up" points={weeks([4, 7, 6, 9, 8, 10, 7, 11, 12, 10, 9, 16])} href="#metric-card" />
          <MetricCard icon={TimerIcon} title="Time to hire, days" value="21" change={-3} period="last month" better="down" points={weeks([30, 28, 29, 27, 26, 27, 25, 24, 24, 23, 24, 21])} />
          <MetricCard icon={HourglassIcon} title="Overdue now" value="3" change={2} period="last week" better="down" points={weeks([0, 2, 0, 2, 4, 2, 4, 2, 4, 4, 1, 3])} />
        </SummaryBand>
      </Panel>


      <div class="flex flex-wrap items-start gap-5">
        <Panel title="File row and file viewer" class="flex-[999_1_520px]">
          <div class="flex flex-col gap-4">
            <FileRow {applicationId} file={files.clean} description="Jordan Ellis, Senior product designer" />
            <FileRow {applicationId} file={files.long} />
            <FileRow {applicationId} file={files.image} />
            <FileRow {applicationId} file={files.word} />
            <FileRow {applicationId} file={files.pending} />
            <FileRow {applicationId} file={files.infected} />
          </div>
        </Panel>
        <div class="flex flex-[1_1_320px] flex-col gap-5">
          <Panel title="Count button">
            <!-- As in a header band's facts row: the clip hides each line's first rule, its margin keeps focus rings in. -->
            <div class="-m-1 overflow-hidden p-1">
              <dl class="flex flex-wrap gap-x-8 gap-y-3">
                <Fact label="Used by"><CountButton items={usedBy} one="job" other="jobs" title="Used by" description="Technical interview" /></Fact>
                <Fact label="Open in"><CountButton items={manyJobs} one="job" other="jobs" title="Open in" description="Brightwater engineering" /></Fact>
              </dl>
            </div>
          </Panel>
          <Panel title="Filter menu">
            <div class="flex flex-wrap items-center gap-2">
              <FilterMenu label="Stage" options={guideStages} total={7} />
              <MultiFilterMenu label="Job" options={guideJobs} values={['support', 'dotnet']} />
            </div>
          </Panel>
          <Panel title="Built in badge">
            <p class="flex flex-wrap items-center gap-2"><span class="font-semibold">Software engineer</span><BuiltInBadge /></p>
          </Panel>
        </div>
      </div>

      <!-- The menu as a settings page holds it: on the page under the header band, never inside a panel (S4). The menu
           brings its own page gutter, which the negative margin cancels so it lines up with the panels around it. -->
      <section aria-label="Settings menu" class="-mx-4 -my-6 md:-mx-6">
        <SettingsMenu current="/settings/team">
          <Panel title="Members"><p>A settings page's panels sit here, beside the menu.</p></Panel>
        </SettingsMenu>
      </section>

      <div class="flex flex-wrap items-start gap-5">
        <Panel title="Edit / Preview switch" class="flex-[999_1_520px]">
          <EditPreviewDemo />
        </Panel>
        <Panel title="Reorder grip" flush class="flex-[1_1_320px]">
          <ReorderDemo />
        </Panel>
      </div>

      <div class="flex flex-wrap items-start gap-5">
        <section aria-label="Form builder" class="flex min-w-0 flex-[999_1_520px] flex-col">
          <FormBuilderDemo />
        </section>
        <Panel title="Rating answer" class="flex-[1_1_320px]">
          <div class="flex flex-col gap-4">
            <div class="flex flex-col gap-2">
              <span id="sg-rating-five" class="font-semibold">Problem solving</span>
              <RatingAnswer type="Rating5" labelledby="sg-rating-five" bind:value={ratingFive} />
            </div>
            <div class="flex flex-col gap-2">
              <span id="sg-rating-ten" class="font-semibold">.NET and C#</span>
              <RatingAnswer type="Rating10" labelledby="sg-rating-ten" bind:value={ratingTen} />
            </div>
            <div class="flex flex-col gap-2">
              <span id="sg-rating-yes" class="font-semibold">Did they finish the exercise in the time?</span>
              <RatingAnswer type="RatingYesNo" labelledby="sg-rating-yes" bind:value={ratingYesNo} />
            </div>
          </div>
        </Panel>
        <Panel title="Rating" class="flex-[1_1_320px]">
          <dl class="flex flex-col gap-3">
            <div class="flex flex-wrap items-center gap-3">
              <dt class="text-muted-foreground">On a candidate</dt>
              <dd>
                <Rating rating={{ state: 'Rated', value: 4.6, scorecards: 1 }} scorecards={sampleScorecards} stageName={() => 'Interview'} members={sampleTeam} currentUserId="u1" />
              </dd>
            </div>
            <div class="flex flex-wrap items-center gap-3">
              <dt class="text-muted-foreground">In a table</dt>
              <dd><Rating rating={{ state: 'Rated', value: 3.7, scorecards: 2 }} /></dd>
            </div>
            <div class="flex flex-wrap items-center gap-3">
              <dt class="text-muted-foreground">None yet</dt>
              <dd><Rating rating={{ state: 'NotRated', scorecards: 0 }} /></dd>
            </div>
            <div class="flex flex-wrap items-center gap-3">
              <dt class="text-muted-foreground">Your review first</dt>
              <dd><Rating rating={{ state: 'AfterYourScorecard', scorecards: 0 }} /></dd>
            </div>
          </dl>
        </Panel>
        <Panel title="Answer menu" class="flex-[1_1_320px]">
          <div class="flex flex-wrap gap-2">
            <AnswerMenu groups={applyFormKinds} onselect={() => {}}>
              {#snippet trigger(props)}
                <Button {...props} variant="outline"><PlusIcon aria-hidden="true" />Add a question<ChevronDownIcon aria-hidden="true" /></Button>
              {/snippet}
            </AnswerMenu>
            <AnswerMenu groups={scorecardKinds} value="Rating10" onselect={() => {}}>
              {#snippet trigger(props)}
                <Button {...props} variant="outline" aria-label="Answer: 1 to 10"><StarIcon aria-hidden="true" />1 to 10<ChevronDownIcon aria-hidden="true" /></Button>
              {/snippet}
            </AnswerMenu>
          </div>
        </Panel>
      </div>

      <Panel title="Integrity chip and evidence">
        <div class="flex flex-col gap-4">
          <div class="flex flex-wrap gap-2">
            {#each Object.values(authenticityLevels) as level (level.label)}<IntegrityChip kind="authenticity" {level} />{/each}
            <IntegrityChip kind="authenticity" level={null} compact />
          </div>
          <div class="flex flex-wrap gap-2">
            {#each Object.values(aiAssistanceLevels) as level (level.label)}<IntegrityChip kind="aiAssistance" {level} />{/each}
          </div>
          <LevelScale levels={authenticityOrder.map((id) => authenticityLevels[id])} current={authenticityLevels.VerificationRecommended} name="Authenticity" />
          <LevelScale levels={aiAssistanceOrder.map((id) => aiAssistanceLevels[id])} current={aiAssistanceLevels.NotIndicated} name="AI assistance" />
          <SignalEvidence evidence={quote} />
        </div>
      </Panel>
    </div>
  </div>
</main>
