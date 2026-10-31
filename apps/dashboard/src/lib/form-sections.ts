// A questionnaire's named sections in the form builder (docs/architecture/workflow-engine.md §4): section rows inside
// the one list of questions, so the builder stays one panel with one open question and one drag zone. A section row
// marks where its section starts; the questions after it, up to the next section row, are its questions. The hiring
// definition keeps the questions as one flat list and the sections as ids beside it, and these functions keep the two in
// step: the flat list is always rebuilt from the sections, in order.
import { idFrom } from '#lib/workflow.js';

/** A section as the hiring definition stores it. */
export interface SectionDraft {
  id: string;
  name: string;
  questionIds: string[];
}

/** One row of the builder's list: a section's header, or a question. */
export type BuilderRow<T> = { kind: 'section'; section: SectionDraft } | { kind: 'question'; item: T };

/** The builder's rows: each section's header, then its questions; just the questions without sections. */
export function rowsOf<T>(items: readonly T[], sections: readonly SectionDraft[] | null | undefined, key: (item: T) => string): BuilderRow<T>[] {
  if (!sections || sections.length === 0) return items.map((item) => ({ kind: 'question', item }));
  const byKey = new Map(items.map((item) => [key(item), item]));
  return sections.flatMap((section): BuilderRow<T>[] => [
    { kind: 'section', section },
    ...section.questionIds.flatMap((id): BuilderRow<T>[] => {
      const item = byKey.get(id);
      return item === undefined ? [] : [{ kind: 'question', item }];
    }),
  ]);
}

/**
 * The questions and sections a new row order says. A question dragged above the first section row joins the first
 * section, so a question is never outside a section while there are sections.
 */
export function fromRows<T>(rows: readonly BuilderRow<T>[], key: (item: T) => string): { items: T[]; sections: SectionDraft[] | null } {
  const sections: SectionDraft[] = [];
  const leading: string[] = [];
  const items = new Map<string, T>();
  for (const row of rows) {
    if (row.kind === 'section') {
      sections.push({ ...row.section, questionIds: [] });
    } else {
      items.set(key(row.item), row.item);
      (sections.at(-1)?.questionIds ?? leading).push(key(row.item));
    }
  }

  if (sections.length === 0) return { items: [...items.values()], sections: null };
  sections[0]!.questionIds.unshift(...leading);
  return { items: sections.flatMap((section) => section.questionIds.map((id) => items.get(id)!)), sections };
}

/**
 * The sections after the question list changed in place (a question added, copied or deleted): deleted questions leave
 * their section, and a new one joins the section of the question before it in `items` (the first section when it is
 * first). Returns the items in the sections' order too.
 */
export function syncSections<T>(
  items: readonly T[],
  sections: readonly SectionDraft[],
  key: (item: T) => string,
): { items: T[]; sections: SectionDraft[] } {
  const ids = items.map(key);
  const present = new Set(ids);
  const next = sections.map((section) => ({ ...section, questionIds: section.questionIds.filter((id) => present.has(id)) }));
  const placed = new Set(next.flatMap((section) => section.questionIds));
  ids.forEach((id, i) => {
    if (placed.has(id) || next.length === 0) return;
    const before = ids.slice(0, i).reverse().find((other) => placed.has(other));
    const home = before === undefined ? next[0]! : next.find((section) => section.questionIds.includes(before))!;
    const at = before === undefined ? 0 : home.questionIds.indexOf(before) + 1;
    home.questionIds.splice(at, 0, id);
    placed.add(id);
  });
  const byKey = new Map(items.map((item) => [key(item), item]));
  return { items: next.flatMap((section) => section.questionIds.map((id) => byKey.get(id)!)), sections: next };
}

/**
 * The sections with one more at the end. The first section a questionnaire gets holds every question it has, and a new
 * empty one follows it, so adding sections never leaves a question outside one.
 */
export function addSection(sections: readonly SectionDraft[] | null | undefined, questionIds: readonly string[]): SectionDraft[] {
  const current = sections ?? [];
  const taken = current.map((section) => section.id);
  const make = (existing: readonly string[], number: number, ids: string[]): SectionDraft => ({
    id: idFrom(`section ${number}`, existing, 'section'),
    name: `Section ${number}`,
    questionIds: ids,
  });
  if (current.length === 0) {
    const first = make(taken, 1, [...questionIds]);
    return [first, make([first.id], 2, [])];
  }

  return [...current, make(taken, current.length + 1, [])];
}

/**
 * The sections without the one at `index`: its questions join the section before it (the one after it, for the first).
 * Deleting the last section leaves none, and the questionnaire goes back to automatic pages.
 */
export function removeSection(sections: readonly SectionDraft[], index: number): SectionDraft[] | null {
  const removed = sections[index];
  if (!removed) return [...sections];
  const rest = sections.filter((_, i) => i !== index);
  if (rest.length === 0) return null;
  const target = index === 0 ? 0 : index - 1;
  return rest.map((section, i) =>
    i !== target
      ? section
      : { ...section, questionIds: index === 0 ? [...removed.questionIds, ...section.questionIds] : [...section.questionIds, ...removed.questionIds] },
  );
}
