import fc from 'fast-check';
import { describe, expect, it } from 'vitest';
import { Collector, editKind, fieldIdOf, MAX_FIELDS } from './collector.ts';

describe('fieldIdOf', () => {
  it('knows the form fields and question fields, and nothing else', () => {
    expect(fieldIdOf('fullName')).toBe('fullName');
    expect(fieldIdOf('aiUseDetails')).toBe('aiUseDetails');
    expect(fieldIdOf('q:notice_period')).toBe('notice_period');
    expect(fieldIdOf('leave_this_empty')).toBeNull(); // the honeypot
    expect(fieldIdOf('state')).toBeNull();
    expect(fieldIdOf('upload:resume')).toBeNull();
    expect(fieldIdOf('q:Not An Id')).toBeNull();
  });
});

describe('editKind', () => {
  it('reads only the kind of edit', () => {
    expect(editKind('insertText')).toBe('typed');
    expect(editKind('insertCompositionText')).toBe('typed');
    expect(editKind('insertFromPaste')).toBe('pasted');
    expect(editKind('insertFromDrop')).toBe('pasted');
    expect(editKind('insertReplacementText')).toBe('autofilled');
    expect(editKind(undefined)).toBe('autofilled');
    expect(editKind('deleteContentBackward')).toBe('other');
  });
});

describe('Collector', () => {
  it('counts typed and pasted characters from length growth, and time on the field', () => {
    const collector = new Collector();
    collector.focus('motivation', 0, 1000);
    collector.edit('motivation', 'typed', 1);
    collector.edit('motivation', 'typed', 2);
    collector.edit('motivation', 'pasted', 302);
    collector.edit('motivation', 'other', 290); // deleting counts for nothing
    collector.edit('motivation', 'typed', 291);
    collector.blur('motivation', 61_000);
    collector.hidden();

    expect(collector.aggregates(70_000)).toEqual({
      fields: [
        {
          fieldId: 'motivation',
          pasteCount: 1,
          pastedCharacters: 300,
          typedCharacters: 3,
          focusCount: 1,
          blurCount: 1,
          activeMilliseconds: 60_000,
          autofilled: false,
        },
      ],
      visibilityChanges: 1,
    });
  });

  it('counts the time a field is focused right now, and marks autofill', () => {
    const collector = new Collector();
    collector.focus('email', 0, 0);
    collector.edit('fullName', 'autofilled', 11);

    const { fields } = collector.aggregates(2500);
    expect(fields.find((f) => f.fieldId === 'email')?.activeMilliseconds).toBe(2500);
    expect(fields.find((f) => f.fieldId === 'fullName')).toMatchObject({ autofilled: true, typedCharacters: 0, pastedCharacters: 0 });
  });

  it('never reports more fields than the API takes', () => {
    const collector = new Collector();
    for (let i = 0; i < MAX_FIELDS + 20; i++) collector.focus(`q${i}`, 0, 0);
    expect(collector.aggregates(0).fields).toHaveLength(MAX_FIELDS);
  });

  // Whatever happens to a form, the aggregates are counts within range: no strings but the field ids it was given.
  it('keeps every aggregate a non-negative whole number or a flag (property)', () => {
    const operation = fc.oneof(
      fc.record({ op: fc.constant('focus' as const), field: fc.constantFrom('fullName', 'motivation'), length: fc.nat(10_000), at: fc.nat(1e9) }),
      fc.record({ op: fc.constant('blur' as const), field: fc.constantFrom('fullName', 'motivation'), at: fc.nat(1e9) }),
      fc.record({
        op: fc.constant('edit' as const),
        field: fc.constantFrom('fullName', 'motivation'),
        kind: fc.constantFrom('typed' as const, 'pasted' as const, 'autofilled' as const, 'other' as const),
        length: fc.nat(10_000),
      }),
      fc.record({ op: fc.constant('hidden' as const) }),
    );
    fc.assert(
      fc.property(fc.array(operation, { maxLength: 200 }), fc.nat(1e9), (operations, now) => {
        const collector = new Collector();
        for (const step of operations) {
          if (step.op === 'focus') collector.focus(step.field, step.length, step.at);
          else if (step.op === 'blur') collector.blur(step.field, step.at);
          else if (step.op === 'edit') collector.edit(step.field, step.kind, step.length);
          else collector.hidden();
        }

        const { fields, visibilityChanges } = collector.aggregates(now);
        expect(Number.isInteger(visibilityChanges) && visibilityChanges >= 0).toBe(true);
        for (const field of fields) {
          expect(['fullName', 'motivation']).toContain(field.fieldId);
          for (const [key, value] of Object.entries(field)) {
            if (key === 'fieldId') continue;
            if (key === 'autofilled') expect(typeof value).toBe('boolean');
            else expect(Number.isInteger(value) && (value as number) >= 0).toBe(true);
          }
        }
      }),
    );
  });
});
