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
  import { cubicInOut } from 'svelte/easing';
  import { prefersReducedMotion } from 'svelte/motion';
  import { SvelteSet } from 'svelte/reactivity';
  import { slide } from 'svelte/transition';
  import AnswerMenu from '#lib/components/answer-menu.svelte';
  import IconButton from '#lib/components/icon-button.svelte';
  import MoreMenu from '#lib/components/more-menu.svelte';
  import Panel from '#lib/components/panel.svelte';
  import ReorderList from '#lib/components/reorder-list.svelte';
  import { Badge } from '#lib/components/ui/badge/index.js';
  import { Button } from '#lib/components/ui/button/index.js';
  import * as DropdownMenu from '#lib/components/ui/dropdown-menu/index.js';
  import { Input } from '#lib/components/ui/input/index.js';
  import { Label } from '#lib/components/ui/label/index.js';
  import { Switch } from '#lib/components/ui/switch/index.js';
  import { Textarea } from '#lib/components/ui/textarea/index.js';
  import { labelClass } from '#lib/components/workflow/controls.js';
  import FieldErrors from '#lib/components/workflow/field-errors.svelte';
  import { choicesSummary, kindIn, type AnswerKind, type AnswerKindGroup, type AnswerOption } from '#lib/form-builder.js';
  import { addSection, fromRows, removeSection, rowsOf, syncSections, type BuilderRow, type SectionDraft } from '#lib/form-sections.js';
  import { idFrom, insertAt } from '#lib/workflow.js';

  // The form builder (design system section 11), modelled on Google Forms: one panel of questions, each a row with one
  // header strip, the same open or closed: the grip, the question, Required and the answer kind as badges, the row menu
  // (Duplicate and Delete, L3) and a chevron. The strip is one button that opens and closes the row, so it never moves or
  // changes height, and a closed row adds its help line and its options ("4 choices: ...") under it. An open row has its full
  // border and the strip's accent fill (S11), and its body is a labelled form. Several rows may be open at once, so each
  // body is a group named after its question; Expand all and Collapse all are in the panel's header. A row closes from its
  // strip or Escape, and slides open and shut (none under reduced motion). A required question wears a Required badge and an
  // optional one nothing (W6). Questions and options reorder with a grip (A7). Removing a question changes nothing until the
  // editor saves, so it is not destructive (section 10).
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
  const openIds = new SvelteSet<string>();
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
  const openCount = $derived(items.filter((item) => openIds.has(key(item))).length);
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

  // A new question opens at its text, which is what to write next. A row opened from its strip keeps the focus there, as a
  // disclosure does, so opening several to read them never moves the person.
  async function openAtText(id: string) {
    openIds.add(id);
    await tick();
    find(`[data-question-text="${attr(id)}"]`)?.focus();
  }

  async function close(id: string) {
    openIds.delete(id);
    await tick();
    find(`[data-question-edit="${attr(id)}"]`)?.focus();
  }

  function toggle(id: string) {
    if (openIds.has(id)) void close(id);
    else openIds.add(id);
  }

  // The button pressed always goes, because it has nothing left to do, so the focus moves to the other one.
  async function setAll(open: boolean) {
    for (const item of items) {
      if (open) openIds.add(key(item));
      else openIds.delete(key(item));
    }
    await tick();
    find(open ? '[data-collapse-all]' : '[data-expand-all]')?.focus();
  }

  function add(kind: K) {
    const item = newItem(kind);
    setItems([...items, item]);
    void openAtText(key(item));
  }

  function copy(index: number) {
    const original = items[index];
    if (original === undefined) return;
    const item = duplicate(original);
    setItems(insertAt(items, index + 1, item));
    void openAtText(key(item));
  }

  async function remove(index: number) {
    const gone = items[index];
    const next = items[index + 1];
    setItems(items.filter((_, i) => i !== index));
    if (gone !== undefined) openIds.delete(key(gone));
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
  const lineClass = 'min-h-9 resize-none py-1.5 max-md:py-1 pointer-coarse:min-h-11';
  const flatten = (value: string) => value.replace(/\s*[\r\n]+\s*/g, ' ');
  function oneLine(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.isComposing) event.preventDefault();
  }

  // The fields slide open and shut. While they slide shut they are already closed: inert and hidden from assistive
  // technology, so a closing question's fields are never reached or read.
  const motion = () => ({ duration: prefersReducedMotion.current ? 0 : 180, easing: cubicInOut });
  // Opening again while it still slides shut reuses the same fields, so opening gives them back.
  function slideOpen(node: HTMLElement) {
    node.inert = false;
    node.removeAttribute('aria-hidden');
    return slide(node, motion());
  }
  function slideShut(node: HTMLElement) {
    node.inert = true;
    node.setAttribute('aria-hidden', 'true');
    return slide(node, motion());
  }

  // Escape closes the open row it is pressed in. A menu sits in a portal, outside the row. Three things inside the row
  // keep their own Escape: a word being composed in a field (an IME), a popover open from a button here (an Info button), and
  // an option picked up with the keyboard, whose list item has the focus.
  function onrowkeydown(event: KeyboardEvent, id: string) {
    if (event.key !== 'Escape' || event.defaultPrevented || event.isComposing) return;
    if ((event.target as Element | null)?.matches('[data-reorder-id], [aria-haspopup="dialog"][aria-expanded="true"]')) return;
    event.preventDefault();
    void close(id);
  }
</script>

<div bind:this={root} class="@container min-w-0">
  <Panel {title} count={items.length} flush actions={items.length > 1 ? expandAll : undefined}>
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
      class="[&>li]:-mt-px [&>li:last-child]:-mb-px"
    >
      {#snippet item(row, grip)}
        {#if row.kind === 'section'}
          {@const section = row.section}
          {@const sectionIndex = (sections ?? []).findIndex((each) => each.id === section.id)}
          {@const problems = sectionErrors?.(sectionIndex) ?? []}
          {@const problemsId = `${uid}-section-errors-${sectionIndex}`}
          <div data-section-row={section.id} class="flex min-w-0 flex-col gap-1 border border-x-transparent border-y-border-subtle bg-muted py-2 pr-2 pl-2.5">
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
        {@const isOpen = openIds.has(id)}
        {@const helpLine = help?.get(question)}
        <!-- Neighbours share one hairline (each item overlaps the one before by 1px), and an open row's border is drawn over it. -->
        <!-- svelte-ignore a11y_no_static_element_interactions -->
        <div
          data-question-row={id}
          data-open={isOpen || undefined}
          class={['relative flex min-w-0 flex-col border bg-card', isOpen ? 'z-1 border-primary' : 'border-x-transparent border-y-border-subtle has-[[data-question-edit]:hover]:bg-muted/40']}
          onkeydown={isOpen ? (event) => onrowkeydown(event, id) : undefined}
        >
          <!-- The strip is the same open or closed, so it never moves. Its button's box stretches over the strip, and over the
               whole row while it is closed, under the grip and the row menu (A9). What is under it starts where its words do. -->
          <div data-question-header class={['flex min-w-0 items-start gap-2 py-2 pr-2 pl-2.5', isOpen && 'relative bg-accent text-accent-foreground']}>
            <span class="relative z-10 pt-0.5 pointer-coarse:pt-0">{@render grip()}</span>
            <button
              type="button"
              data-question-edit={id}
              aria-expanded={isOpen}
              aria-controls={isOpen ? `${uid}-body-${index}` : undefined}
              aria-describedby={errors.length > 0 ? errorsId : undefined}
              onclick={() => toggle(id)}
              class="flex min-h-9 min-w-0 flex-1 flex-wrap items-center gap-x-3 gap-y-1.5 px-2 py-2 text-left after:absolute after:inset-0 pointer-coarse:min-h-11 pointer-coarse:py-3 focus-visible:outline-none focus-visible:after:outline-2 focus-visible:after:-outline-offset-2 focus-visible:after:outline-ring"
            >
              <span class="min-w-0 flex-[1_1_14rem] font-semibold break-words">
                <span class="sr-only">Edit question {index + 1}:</span>{' '}<span id={`${uid}-name-${index}`}>{nameOf(question)}</span>
              </span>{' '}
              <span data-question-badges class="flex min-w-0 flex-wrap items-center gap-1.5">
                <!-- On the open strip's accent fill a badge turns white, as a count does on a pressed toggle (T4). -->
                {#if requiredOf(question)}<Badge variant="muted" class={isOpen ? 'bg-card' : undefined}>Required</Badge>{' '}{/if}
                <Badge variant="muted" class={isOpen ? 'bg-card' : undefined}>{#if kind.icon}{@const Icon = kind.icon}<Icon aria-hidden="true" />{/if}{kind.label}</Badge>
              </span>
            </button>
            <span class="relative z-10 shrink-0">{@render rowMenu(nameOf(question), index)}</span>
            <!-- The chevron takes no press of its own: it is part of the strip's button. -->
            <span aria-hidden="true" data-question-chevron class="pointer-events-none flex size-9 shrink-0 items-center justify-center text-muted-foreground pointer-coarse:size-11">
              <ChevronDownIcon class={['size-4 transition-transform duration-180 ease-in-out motion-reduce:transition-none', isOpen && 'rotate-180']} />
            </span>
          </div>
          {#if errors.length > 0}
            <div class={['pr-4 pb-3 pl-14.5 pointer-coarse:pl-17.5', isOpen && 'pt-3']}><FieldErrors id={errorsId} messages={errors} /></div>
          {/if}
          {#if !isOpen && (helpLine || kind.options)}
            <div data-question-summary class="flex min-w-0 flex-col gap-1 pr-4 pb-3 pl-14.5 text-muted-foreground pointer-coarse:pl-17.5" transition:slide={motion()}>
              {#if helpLine}<span class="break-words">{helpLine}</span>{/if}
              {#if kind.options}
                <span data-question-choices class="flex min-w-0 items-center gap-2">
                  {@render markOf(kind.options)}<span class="min-w-0 truncate">{choicesSummary(optionsOf(question))}</span>
                </span>
              {/if}
            </div>
          {/if}
          {#if isOpen}
            <div
              id={`${uid}-body-${index}`}
              role="group"
              aria-labelledby={`${uid}-name-${index}`}
              data-question-body
              class="border-t border-border-subtle px-4 py-4 @md:pl-14.5 pointer-coarse:@md:pl-17.5"
              in:slideOpen
              out:slideShut
            >
              <!-- The form keeps its 688px in a wide row (S6), while the strip runs the row's whole width. -->
              <div class="flex max-w-form min-w-0 flex-col gap-4">
                <!-- Where the pane is wide, Answer type and Required sit beside the fields they follow; they stack where it is not. -->
                <div class="grid min-w-0 gap-x-4 gap-y-3 @2xl:grid-cols-[minmax(0,1fr)_14rem]">
                  <div class={labelClass}>
                    <label for={`${uid}-text-${index}`}>Question{' '}<span class="sr-only">{index + 1}</span></label>
                    <Textarea
                      id={`${uid}-text-${index}`}
                      data-question-text={id}
                      rows={1}
                      value={labelOf(question)}
                      onkeydown={oneLine}
                      oninput={(event) => setLabel(question, flatten(event.currentTarget.value))}
                      maxlength={300}
                      aria-invalid={errors.length > 0 || undefined}
                      aria-describedby={errors.length > 0 ? errorsId : undefined}
                      class={lineClass}
                    />
                  </div>
                  <div class={labelClass}>
                    <span aria-hidden="true">Answer type</span>
                    <AnswerMenu {groups} value={kind.value} onselect={(next) => changeKind(index, next)}>
                      {#snippet trigger(props)}
                        {@const Icon = kind.icon}
                        <Button {...props} variant="outline" size="lg" aria-label={`Answer type: ${kind.label}`} class="justify-start">
                          {#if Icon}<Icon aria-hidden="true" />{/if}<span class="min-w-0 flex-1 truncate text-left">{kind.label}</span><ChevronDownIcon aria-hidden="true" />
                        </Button>
                      {/snippet}
                    </AnswerMenu>
                  </div>
                  {#if help}
                    <div class={labelClass}>
                      <label for={`${uid}-help-${index}`}>{help.label}</label>
                      <Textarea
                        id={`${uid}-help-${index}`}
                        rows={1}
                        value={help.get(question) ?? ''}
                        onkeydown={oneLine}
                        oninput={(event) => help?.set(question, flatten(event.currentTarget.value) || null)}
                        maxlength={500}
                        class={lineClass}
                      />
                    </div>
                  {/if}
                  <span class="flex min-h-9 items-center gap-2 self-end pointer-coarse:min-h-11">
                    <Switch
                      id={`${uid}-required-${index}`}
                      checked={requiredOf(question)}
                      onCheckedChange={(value) => setRequired(question, value)}
                    />
                    <Label for={`${uid}-required-${index}`} class="font-semibold">Required</Label>
                  </span>
                </div>
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
              </div>
            </div>
          {/if}
        </div>
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

<!-- Both show while some rows are open and some are not; a narrow header has room for one, Collapse all while any is open. -->
{#snippet expandAll()}
  {#if openCount > 0}<Button variant="outline" data-collapse-all onclick={() => setAll(false)}>Collapse all</Button>{/if}
  {#if openCount < items.length}
    <Button variant="outline" data-expand-all class={openCount > 0 ? '@max-sm/panel:hidden' : undefined} onclick={() => setAll(true)}>Expand all</Button>
  {/if}
{/snippet}

{#snippet rowMenu(name: string, index: number)}
  <MoreMenu label={`Actions for ${name}`} quiet>
    <DropdownMenu.Item onSelect={() => copy(index)}><CopyIcon aria-hidden="true" />Duplicate</DropdownMenu.Item>
    <DropdownMenu.Item onSelect={() => remove(index)}><Trash2Icon aria-hidden="true" />Delete</DropdownMenu.Item>
  </MoreMenu>
{/snippet}

{#snippet markOf(mark: 'circle' | 'square')}
  <span aria-hidden="true" class={['size-4 shrink-0 border-2 border-muted-foreground', mark === 'circle' ? 'rounded-full' : 'rounded-sm']}></span>
{/snippet}
