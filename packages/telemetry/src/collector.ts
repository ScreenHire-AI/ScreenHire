// Per-field session aggregates (verification-engine.md §10.2). What it may know about a field is its name, how long its
// value is, and the kind of edit the browser reports (`inputType`). It never sees key values, the clipboard, the text or
// the time between keys: there is no keyboard listener in this package at all, so none of that can leak by mistake.

/** Counts for one field, in the API's shape (`FieldTelemetryInput`). */
export interface FieldAggregate {
  fieldId: string;
  pasteCount: number;
  pastedCharacters: number;
  typedCharacters: number;
  focusCount: number;
  blurCount: number;
  activeMilliseconds: number;
  autofilled: boolean;
}

/** One cumulative snapshot of a page view, in the API's shape (`TelemetrySnapshotInput`). */
export interface Snapshot {
  sessionToken: string;
  pageViewId: string;
  sequence: number;
  fields: FieldAggregate[];
  page: { visibilityChanges: number; automationMarkers: string[] };
}

/** The most fields one snapshot carries, as the API allows (`TelemetrySnapshot.MaxFields`). */
export const MAX_FIELDS = 110;

const FIXED_FIELDS = new Set(['firstName', 'lastName', 'email', 'phone', 'aiUseDetails']);
const QUESTION_FIELD = /^q:([a-z][a-z0-9_-]{0,39})$/;

/**
 * The field id the API knows for a form control's `name`: the form's own fields, or a question's id from its
 * `q:<id>` name. Anything else (the honeypot, hidden state, uploads) is not measured.
 */
export function fieldIdOf(name: string): string | null {
  if (FIXED_FIELDS.has(name)) return name;
  return QUESTION_FIELD.exec(name)?.[1] ?? null;
}

/** How the browser says text arrived (`InputEvent.inputType`), reduced to what the aggregates count. */
export type EditKind = 'typed' | 'pasted' | 'autofilled' | 'other';

/**
 * Classifies an `input` event by its `inputType` alone. An `input` event that is not an `InputEvent`, or a
 * replacement, is how browsers report autofill.
 */
export function editKind(inputType: string | undefined): EditKind {
  if (inputType === undefined || inputType === '' || inputType === 'insertReplacementText') return 'autofilled';
  if (inputType === 'insertFromPaste' || inputType === 'insertFromDrop' || inputType === 'insertFromPasteAsQuotation') return 'pasted';
  return inputType.startsWith('insert') ? 'typed' : 'other';
}

interface FieldState extends FieldAggregate {
  length: number;
  focusedAt: number | null;
}

/** The aggregates of one page view. Times are milliseconds from the clock the caller passes in. */
export class Collector {
  private readonly fields = new Map<string, FieldState>();
  private visibilityChanges = 0;

  /** The field gained focus; `length` is its value's length now. */
  focus(fieldId: string, length: number, now: number): void {
    const field = this.field(fieldId);
    field.focusCount++;
    field.focusedAt = now;
    field.length = length;
  }

  /** The field lost focus. */
  blur(fieldId: string, now: number): void {
    const field = this.field(fieldId);
    field.blurCount++;
    if (field.focusedAt !== null) field.activeMilliseconds += Math.max(0, now - field.focusedAt);
    field.focusedAt = null;
  }

  /** The field's value changed to `length` characters, the way `kind` says. Only growth counts as entered text. */
  edit(fieldId: string, kind: EditKind, length: number): void {
    const field = this.field(fieldId);
    const added = Math.max(0, length - field.length);
    field.length = length;
    if (kind === 'pasted') {
      field.pasteCount++;
      field.pastedCharacters += added;
    } else if (kind === 'typed') {
      field.typedCharacters += added;
    } else if (kind === 'autofilled' && added > 0) {
      field.autofilled = true;
    }
  }

  /** The browser reports the field as autofilled (`:autofill`). */
  autofilled(fieldId: string): void {
    this.field(fieldId).autofilled = true;
  }

  /** The page was hidden: the candidate switched to another tab or app. Which one is never known. */
  hidden(): void {
    this.visibilityChanges++;
  }

  /** The aggregates so far. A field focused right now counts its time up to `now`. */
  aggregates(now: number): { fields: FieldAggregate[]; visibilityChanges: number } {
    const fields = [...this.fields.values()].slice(0, MAX_FIELDS).map((f) => ({
      fieldId: f.fieldId,
      pasteCount: f.pasteCount,
      pastedCharacters: f.pastedCharacters,
      typedCharacters: f.typedCharacters,
      focusCount: f.focusCount,
      blurCount: f.blurCount,
      activeMilliseconds: Math.round(f.activeMilliseconds + (f.focusedAt === null ? 0 : Math.max(0, now - f.focusedAt))),
      autofilled: f.autofilled,
    }));
    return { fields, visibilityChanges: this.visibilityChanges };
  }

  private field(fieldId: string): FieldState {
    let field = this.fields.get(fieldId);
    if (!field) {
      field = {
        fieldId,
        pasteCount: 0,
        pastedCharacters: 0,
        typedCharacters: 0,
        focusCount: 0,
        blurCount: 0,
        activeMilliseconds: 0,
        autofilled: false,
        length: 0,
        focusedAt: null,
      };
      this.fields.set(fieldId, field);
    }

    return field;
  }
}
