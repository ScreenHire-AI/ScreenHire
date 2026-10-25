import type { QuestionType } from '@screenhire/api-client';
import CircleDotIcon from '@lucide/svelte/icons/circle-dot';
import HashIcon from '@lucide/svelte/icons/hash';
import LinkIcon from '@lucide/svelte/icons/link';
import ListChecksIcon from '@lucide/svelte/icons/list-checks';
import PaperclipIcon from '@lucide/svelte/icons/paperclip';
import TextAlignStartIcon from '@lucide/svelte/icons/text-align-start';
import TextCursorInputIcon from '@lucide/svelte/icons/text-cursor-input';
import ToggleLeftIcon from '@lucide/svelte/icons/toggle-left';
import type { LucideIcon } from '#lib/shell.js';
import { questionTypeLabels } from '#lib/workflow.js';

// The form builder's answer kinds (design system section 11, Form builder and Answer menu). Each editor that mounts the
// builder passes the menu it offers: application questions and follow-up questionnaires below, scorecards their own
// General and Rating groups. A kind's `value` is what the editor stores; the builder never reads it.

/** One answer kind in the answer menu. */
export interface AnswerKind<K extends string = string> {
  value: K;
  label: string;
  icon: LucideIcon;
  /** The question lists options people edit, each with this mark: a circle to pick one, a square to pick any. */
  options?: 'circle' | 'square';
  /** Choices the kind always offers, shown with circle marks and never edited (Yes or no). */
  fixed?: readonly string[];
}

/** A heading in the answer menu and the kinds under it. */
export interface AnswerKindGroup<K extends string = string> {
  label: string;
  kinds: readonly AnswerKind<K>[];
}

/** An option of a choice question: its stable id and what the person sees. */
export interface AnswerOption {
  id: string;
  label: string;
}

const kind = <K extends QuestionType>(value: K, icon: LucideIcon, rest: Partial<AnswerKind<K>> = {}): AnswerKind<K> => ({
  value,
  label: questionTypeLabels[value],
  icon,
  ...rest,
});

/** The General kinds every form offers, scorecards included, in the menu's order. */
export const generalKinds = [
  kind('YesNo', ToggleLeftIcon, { fixed: ['Yes', 'No'] }),
  kind('SingleChoice', CircleDotIcon, { options: 'circle' }),
  kind('MultiChoice', ListChecksIcon, { options: 'square' }),
  kind('ShortText', TextCursorInputIcon),
  kind('LongText', TextAlignStartIcon),
] as const satisfies readonly AnswerKind<QuestionType>[];

const moreKinds = [kind('Number', HashIcon), kind('Url', LinkIcon), kind('File', PaperclipIcon)] as const satisfies readonly AnswerKind<QuestionType>[];

/** The apply form's menu: every kind a question supports. */
export const applyFormKinds: readonly AnswerKindGroup<QuestionType>[] = [
  { label: 'General', kinds: generalKinds },
  { label: 'More', kinds: moreKinds },
];

/** A follow-up questionnaire's menu: its page works without JavaScript and takes no uploads, so no File. */
export const followUpKinds: readonly AnswerKindGroup<QuestionType>[] = [
  { label: 'General', kinds: generalKinds },
  { label: 'More', kinds: moreKinds.filter((more) => more.value !== 'File') },
];

/** The kind `value` names in `groups`, or undefined for a kind the menu does not offer. */
export const kindIn = <K extends string>(groups: readonly AnswerKindGroup<K>[], value: K): AnswerKind<K> | undefined =>
  groups.flatMap((group) => group.kinds).find((candidate) => candidate.value === value);

/** `list` with `item` at `index`. */
export const insertAt = <T>(list: readonly T[], index: number, item: T): T[] => [...list.slice(0, index), item, ...list.slice(index)];

/**
 * An id for a copy of `id`: `{id}_copy`, then `{id}_copy_2` and on, unique among `taken`. The original's part is cut
 * so the id stays within the definition's 40 characters.
 */
export function duplicateId(id: string, taken: Iterable<string>): string {
  const used = new Set(taken);
  const base = `${id.replace(/_copy(_\d+)?$/, '').slice(0, 30)}_copy`;
  if (!used.has(base)) return base;
  for (let n = 2; ; n++) if (!used.has(`${base}_${n}`)) return `${base}_${n}`;
}
