<script lang="ts" module>
  /** The line under a question's text: help for a candidate, or what a reviewer looks for. */
  export interface FormBuilderHelp<T> {
    /** The field's name, also its placeholder: "Help text", "What to look for". */
    label: string;
    get: (item: T) => string | null | undefined;
    set: (item: T, value: string | null) => void;
  }
</script>

<script lang="ts" generics="T, K extends string">
  import ChevronDownIcon from '~icons/lucide/chevron-down';
  import CopyIcon from '~icons/lucide/copy';
  import PlusIcon from '~icons/lucide/plus';
  import Rows3Icon from '~icons/lucide/rows-3';
  import Trash2Icon from '~icons/lucide/trash-2';
  import XIcon from '~icons/lucide/x';
  import { tick, type Snippet } from 'svelte';
  import AnswerMenu from '#lib/components/answer-menu.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import Panel from '#lib/components/panel.svelte';
  import ReorderList from '#lib/components/reorder-list.svelte';
  import { Button } from '#lib/components/ui/button/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Separator } from '#lib/components/ui/separator/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import FieldErrors from '#lib/components/workflow/field-errors.svelte';
  import { insertAt, kindIn, type AnswerKind, type AnswerKindGroup, type AnswerOption } from '#lib/form-builder.js';
  import { addSection, fromRows, removeSection, rowsOf, syncSections, type BuilderRow, type SectionDraft } from '#lib/form-sections.js';
  import { idFrom } from '#lib/workflow.js';

  // The form builder (design system section 11), modelled on Google Forms: one panel of questions, each a row that reads
  // as text until it is opened. Only one question is open at a time, marked by an accent bar; it edits its text, its
  // answer kind (the answer menu, top right), its help line and its options, with Duplicate, Delete and Required in its
  // footer. Questions and options reorder with a grip (A7). Removing a question changes nothing until the editor saves,
  // so it is not destructive (section 10).
  //
  // The builder knows nothing of what it edits: application questions, a follow-up questionnaire or a scorecard. The
  // editor that mounts it reads and writes each question through the callbacks below, offers its own answer menu
  // (`groups`), and adds the controls a kind needs through `settings`.
  //
  // A questionnaire may have named sections (`withSections`), each a page on the careers site: section rows in the same
  // list, each starting its section, so dragging a question past one moves it into that section (#lib/form-sections.ts).
  // A scorecard has none.
  let {
    items = $bindable(),
    groups,
    title,
    empty,
    key,
    kindOf,
    withKind,
    labelOf,
    setLabel,
    requiredOf,
    setRequired,
    help,
    optionsOf,
    setOptions,
    newItem,
    duplicate,
    errorsFor,
    problems = [],
    settings,
    note,
    withSections = false,
    sections = $bindable(),
    sectionErrors,
  }: {
    items: T[];
    groups: readonly AnswerKindGroup<K>[];
    /** The panel's title, also the list's accessible name. */
    title: string;
    /** The line shown while there are no questions. */
    empty: string;
    key: (item: T) => string;
    kindOf: (item: T) => K;
    /** The question as another kind, keeping what carries over. */
    withKind: (item: T, kind: K) => T;
    labelOf: (item: T) => string;
    setLabel: (item: T, value: string) => void;
    requiredOf: (item: T) => boolean;
    setRequired: (item: T, value: boolean) => void;
    help?: FormBuilderHelp<T>;
    /** The options of a kind that has them (`AnswerKind.options`). */
    optionsOf: (item: T) => readonly AnswerOption[];
    setOptions: (item: T, options: AnswerOption[]) => void;
    /** A new question of `kind`, with an id no other question has. */
    newItem: (kind: K) => T;
    /** A copy of the question with an id no other question has. */
    duplicate: (item: T) => T;
    /** The problems with one question, shown under it whether it is open or not (W4). */
    errorsFor?: (item: T, index: number) => readonly string[];
    /** The problems with the list as a whole. */
    problems?: readonly string[];
    /** The controls a kind needs beyond the text, help and options, inside the open question. */
    settings?: Snippet<[item: T, index: number]>;
    /** A note under Add a question, such as what a scorecard may ask about. */
    note?: Snippet;
    /** Offer named sections: Add a section, and a row per section in the list. */
    withSections?: boolean;
    /** The sections, or null for none (the questions then split into pages automatically). */
    sections?: SectionDraft[] | null;
    /** The problems with the section at `index`, shown under its row. */
    sectionErrors?: (index: number) => readonly string[];
  } = $props();

  const uid = $props.id();
  let openId = $state<string | null>(null);
  let root = $state<HTMLElement | null>(null);

  // A kind the menu does not offer (a question saved before its kind left this menu) shows by its stored name.
  const kindOfItem = (item: T): Omit<AnswerKind<K>, 'icon'> & { icon?: AnswerKind<K>['icon'] } => {
    const value = kindOf(item);
    return kindIn(groups, value) ?? { value, label: value };
  };
  const nameOf = (item: T) => labelOf(item).trim() || 'Untitled question';
  const sectionName = (section: SectionDraft) => section.name.trim() || 'Untitled section';
  const sectioned = $derived(withSections && (sections?.length ?? 0) > 0);
  const rows = $derived<BuilderRow<T>[]>(withSections ? rowsOf(items, sections, key) : items.map((item) => ({ kind: 'question', item })));
  const rowKey = (row: BuilderRow<T>) => (row.kind === 'section' ? `section:${row.section.id}` : key(row.item));

  // Every change to the list goes through here, so the sections keep holding exactly the questions there are.
  function setItems(next: T[]) {
    if (sectioned && sections) {
      const synced = syncSections(next, sections, key);
      sections = synced.sections;
      items = synced.items;
    } else {
      items = next;
    }
  }

  function reorder(next: BuilderRow<T>[]) {
    if (!withSections) {
      items = next.flatMap((row) => (row.kind === 'question' ? [row.item] : []));
      return;
    }

    const moved = fromRows(next, key);
    sections = moved.sections;
    items = moved.items;
  }

  async function newSection() {
    sections = addSection(sections, items.map(key));
    await tick();
    const last = sections.at(-1);
    if (last) find(`[data-section-name="${attr(last.id)}"]`)?.focus();
  }

  function deleteSection(index: number) {
    if (!sections) return;
    const next = removeSection(sections, index);
    sections = next;
    if (next) items = syncSections(items, next, key).items;
  }

  function renameSection(id: string, name: string) {
    sections = (sections ?? []).map((section) => (section.id === id ? { ...section, name } : section));
  }
  const find = (selector: string) => root?.querySelector<HTMLElement>(selector) ?? null;
  const attr = (id: string) => CSS.escape(id);

  async function open(id: string) {
    openId = id;
    await tick();
    find(`[data-question-text="${attr(id)}"]`)?.focus();
  }

  async function close(id: string) {
    openId = null;
    await tick();
    find(`[data-question-edit="${attr(id)}"]`)?.focus();
  }

  function add(kind: K) {
    const item = newItem(kind);
    setItems([...items, item]);
    void open(key(item));
  }

  function copy(index: number) {
    const original = items[index];
    if (original === undefined) return;
    const item = duplicate(original);
    setItems(insertAt(items, index + 1, item));
    void open(key(item));
  }

  async function remove(index: number) {
    const next = items[index + 1];
    setItems(items.filter((_, i) => i !== index));
    openId = null;
    await tick();
    (next === undefined ? find('[data-question-add]') : find(`[data-question-edit="${attr(key(next))}"]`))?.focus();
  }

  function changeKind(index: number, kind: K) {
    items = items.map((item, i) => (i === index ? withKind(item, kind) : item));
  }

  function addOption(item: T) {
    const options = optionsOf(item);
    const label = `Option ${options.length + 1}`;
    setOptions(item, [...options, { id: idFrom(label, options.map((option) => option.id), 'option'), label }]);
  }

  // The question's text and help line are one line each, drawn as a textarea that grows so long text shows in full at
  // phone width (F5). Enter adds no line break, and a pasted one becomes a space.
  const lineClass = 'min-h-9 resize-none py-1.5 pointer-coarse:min-h-11';
  const flatten = (value: string) => value.replace(/\s*[\r\n]+\s*/g, ' ');
  function oneLine(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.isComposing) event.preventDefault();
  }

  function onrowkeydown(event: KeyboardEvent, id: string) {
    // Escape closes the open question. The answer menu and a picked-up item take their own Escape: the menu sits in a
    // portal and a keyboard drag focuses the list item, both outside this row.
    if (event.key === 'Escape' && !event.defaultPrevented) {
      event.preventDefault();
      void close(id);
    }
  }
</script>

<div bind:this={root} class="@container min-w-0">
  <Panel {title} count={items.length} flush>
    {#if problems.length > 0}
      <div class="px-4.5 pt-4 max-md:px-4"><FieldErrors id={`${uid}-problems`} messages={problems} /></div>
    {/if}
    {#if items.length === 0}
      <p class="px-4.5 py-4 text-muted-foreground max-md:px-4">{empty}</p>
    {/if}
    <ReorderList
      items={rows}
      key={rowKey}
      label={title}
      itemLabel={(row) => (row.kind === 'section' ? `section ${sectionName(row.section)}` : nameOf(row.item))}
      onreorder={reorder}
      class="divide-y divide-border-subtle [&>li:last-child>div]:rounded-b-panel"
    >
      {#snippet item(row, grip)}
        {#if row.kind === 'section'}
          {@const section = row.section}
          {@const sectionIndex = (sections ?? []).findIndex((each) => each.id === section.id)}
          {@const problems = sectionErrors?.(sectionIndex) ?? []}
          {@const problemsId = `${uid}-section-errors-${sectionIndex}`}
          <div data-section-row={section.id} class="flex min-w-0 flex-col gap-1 bg-muted py-2 pr-2 pl-2.5">
            <div class="flex min-w-0 items-center gap-2">
              {@render grip()}
              <Input
                data-section-name={section.id}
                aria-label={`Section ${sectionIndex + 1} name`}
                value={section.name}
                oninput={(event) => renameSection(section.id, event.currentTarget.value)}
                maxlength={80}
                aria-invalid={problems.length > 0 || undefined}
                aria-describedby={problems.length > 0 ? problemsId : undefined}
                class="min-w-0 flex-1 bg-card font-semibold"
              />
              <span class="shrink-0 text-muted-foreground">
                {section.questionIds.length === 1 ? '1 question' : `${section.questionIds.length} questions`}
              </span>
              <IconButton label={`Delete section ${sectionName(section)}`} onclick={() => deleteSection(sectionIndex)}><Trash2Icon /></IconButton>
            </div>
            {#if problems.length > 0}<div class="pl-10"><FieldErrors id={problemsId} messages={problems} /></div>{/if}
          </div>
        {:else}
        {@const question = row.item}
        {@const index = items.indexOf(question)}
        {@const id = key(question)}
        {@const kind = kindOfItem(question)}
        {@const errors = errorsFor?.(question, index) ?? []}
        {@const errorsId = `${uid}-errors-${index}`}
        {#if openId === id}
          <!-- svelte-ignore a11y_no_static_element_interactions -->
          <div data-question-row={id} data-open class="relative flex min-w-0 gap-2 bg-card py-3 pr-4 pl-2.5" onkeydown={(event) => onrowkeydown(event, id)}>
            <span aria-hidden="true" class="absolute inset-y-0 left-0 w-1 bg-primary"></span>
            <span class="pt-0.5">{@render grip()}</span>
            <div class="flex min-w-0 flex-1 flex-col gap-3">
              <div class="flex min-w-0 flex-wrap items-start gap-2">
                <Textarea
                  data-question-text={id}
                  aria-label={`Question ${index + 1}`}
                  rows={1}
                  value={labelOf(question)}
                  onkeydown={oneLine}
                  oninput={(event) => setLabel(question, flatten(event.currentTarget.value))}
                  maxlength={300}
                  aria-invalid={errors.length > 0 || undefined}
                  aria-describedby={errors.length > 0 ? errorsId : undefined}
                  class={[lineClass, 'flex-[1_1_14rem] font-semibold']}
                />
                <AnswerMenu {groups} value={kind.value} onselect={(next) => changeKind(index, next)}>
                  {#snippet trigger(props)}
                    {@const Icon = kind.icon}
                    <Button {...props} variant="outline" aria-label={`Answer: ${kind.label}`} class="shrink-0">
                      {#if Icon}<Icon aria-hidden="true" />{/if}{kind.label}<ChevronDownIcon aria-hidden="true" />
                    </Button>
                  {/snippet}
                </AnswerMenu>
              </div>
              {#if help}
                <Textarea
                  aria-label={help.label}
                  placeholder={help.label}
                  rows={1}
                  value={help.get(question) ?? ''}
                  onkeydown={oneLine}
                  oninput={(event) => help?.set(question, flatten(event.currentTarget.value) || null)}
                  maxlength={500}
                  class={lineClass}
                />
              {/if}
              {#if kind.options}
                {@const mark = kind.options}
                {@const options = optionsOf(question)}
                <div class="flex flex-col gap-1">
                  <ReorderList
                    items={options}
                    key={(option) => option.id}
                    label={`Options for ${nameOf(question)}`}
                    itemLabel={(option) => option.label || 'Untitled option'}
                    onreorder={(next) => setOptions(question, next)}
                    class="gap-1"
                  >
                    {#snippet item(option, optionGrip)}
                      {@const optionIndex = options.indexOf(option)}
                      <div class="flex min-w-0 items-center gap-2 bg-card">
                        {@render optionGrip()}
                        {@render markOf(mark)}
                        <Input
                          aria-label={`Option ${optionIndex + 1}`}
                          value={option.label}
                          oninput={(event) => {
                            const label = event.currentTarget.value;
                            setOptions(question, options.map((existing) => (existing.id === option.id ? { ...existing, label } : existing)));
                          }}
                          maxlength={200}
                        />
                        <IconButton
                          label={`Remove option ${option.label || optionIndex + 1}`}
                          onclick={() => setOptions(question, options.filter((existing) => existing.id !== option.id))}><XIcon /></IconButton
                        >
                      </div>
                    {/snippet}
                  </ReorderList>
                  <div class="flex items-center gap-2 pl-10 pointer-coarse:pl-13">
                    {@render markOf(mark)}
                    <Button variant="outline" size="sm" onclick={() => addOption(question)}><PlusIcon aria-hidden="true" />Add option</Button>
                  </div>
                </div>
              {:else if kind.fixed}
                <ul class="flex flex-col gap-1.5" aria-label="Choices">
                  {#each kind.fixed as choice (choice)}<li class="flex items-center gap-2">{@render markOf('circle')}{choice}</li>{/each}
                </ul>
              {/if}
              {@render settings?.(question, index)}
              <FieldErrors id={errorsId} messages={errors} />
              <div class="flex flex-wrap items-center justify-end gap-1">
                <IconButton label={`Duplicate ${nameOf(question)}`} onclick={() => copy(index)}><CopyIcon /></IconButton>
                <IconButton label={`Delete ${nameOf(question)}`} onclick={() => remove(index)}><Trash2Icon /></IconButton>
                <Separator orientation="vertical" class="mx-2 h-6 data-vertical:self-center" />
                <span class="flex items-center gap-2 pointer-coarse:min-h-11">
                  <Switch
                    id={`${uid}-required-${index}`}
                    checked={requiredOf(question)}
                    onCheckedChange={(value) => setRequired(question, value)}
                  />
                  <Label for={`${uid}-required-${index}`} class="font-semibold">Required</Label>
                </span>
              </div>
            </div>
          </div>
        {:else}
          <div data-question-row={id} class="flex min-w-0 items-start gap-2 bg-card py-2 pr-2 pl-2.5">
            <span class="pt-1">{@render grip()}</span>
            <div class="flex min-w-0 flex-1 flex-col gap-1">
              <button
                type="button"
                data-question-edit={id}
                aria-expanded="false"
                aria-describedby={errors.length > 0 ? errorsId : undefined}
                onclick={() => open(id)}
                class="flex min-w-0 flex-col gap-1 rounded-lg px-2 py-2 text-left hover:bg-muted pointer-coarse:min-h-11 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
              >
                <span class="flex min-w-0 items-baseline gap-3">
                  <span class="min-w-0 flex-1 font-semibold break-words">
                    <span class="sr-only">Edit question {index + 1}:</span>{' '}{nameOf(question)}{#if requiredOf(question)}<span aria-hidden="true" class="ml-1">*</span><span class="sr-only">, required</span>{/if}
                  </span>
                  <span class="flex shrink-0 items-center gap-1.5 self-start text-muted-foreground">
                    {#if kind.icon}{@const Icon = kind.icon}<Icon aria-hidden="true" />{/if}{kind.label}
                  </span>
                </span>
                {#if help?.get(question)}<span class="block text-muted-foreground">{help.get(question)}</span>{/if}
                {#if kind.options}
                  {#each optionsOf(question) as option (option.id)}
                    {' '}<span class="flex min-w-0 items-center gap-2">{@render markOf(kind.options)}<span class="min-w-0 truncate">{option.label}</span></span>
                  {/each}
                {/if}
              </button>
              {#if errors.length > 0}<div class="px-2"><FieldErrors id={errorsId} messages={errors} /></div>{/if}
            </div>
          </div>
        {/if}
        {/if}
      {/snippet}
    </ReorderList>
    {#snippet footer()}
      <div class="flex flex-col gap-3">
        <div class="flex flex-wrap gap-2">
          <AnswerMenu {groups} onselect={add}>
            {#snippet trigger(props)}
              <Button {...props} data-question-add variant="outline"><PlusIcon aria-hidden="true" />Add a question<ChevronDownIcon aria-hidden="true" /></Button>
            {/snippet}
          </AnswerMenu>
          {#if withSections}
            <Button variant="outline" data-section-add onclick={newSection}><Rows3Icon aria-hidden="true" />Add a section</Button>
          {/if}
        </div>
        {@render note?.()}
      </div>
    {/snippet}
  </Panel>
</div>

{#snippet markOf(mark: 'circle' | 'square')}
  <span aria-hidden="true" class={['size-4 shrink-0 border-2 border-muted-foreground', mark === 'circle' ? 'rounded-full' : 'rounded-sm']}></span>
{/snippet}
