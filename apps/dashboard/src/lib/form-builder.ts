import type { QuestionType, ScorecardQuestionType } from '@screenhire/api-client';
import CircleDotIcon from '~icons/lucide/circle-dot';
import HashIcon from '~icons/lucide/hash';
import LinkIcon from '~icons/lucide/link';
import ListChecksIcon from '~icons/lucide/list-checks';
import PaperclipIcon from '~icons/lucide/paperclip';
import StarIcon from '~icons/lucide/star';
import TextAlignStartIcon from '~icons/lucide/text-align-start';
import TextCursorInputIcon from '~icons/lucide/text-cursor-input';
import ToggleLeftIcon from '~icons/lucide/toggle-left';
import { ratingKinds } from '#lib/scorecards.js';
import type { LucideIcon } from '#lib/shell.js';
import { questionTypeLabels, scorecardQuestionTypeLabels } from '#lib/workflow.js';

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

/**
 * A scorecard's menu: the General kinds and the Rating group, each rating with a star (section 11, Answer menu). A
 * Rating question is one the candidate's rating averages; choosing the group is choosing that, so there is no switch.
 */
export const scorecardKinds: readonly AnswerKindGroup<ScorecardQuestionType>[] = [
  { label: 'General', kinds: generalKinds },
  {
    label: 'Rating',
    kinds: ratingKinds.map((value) => ({
      value,
      label: scorecardQuestionTypeLabels[value],
      icon: StarIcon,
      ...(value === 'RatingYesNo' ? { fixed: ['Yes', 'No'] } : {}),
    })),
  },
];

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
