import fc from 'fast-check';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { startTelemetry, type Telemetry, type TelemetryOptions } from './index.ts';

const TOKEN = 'session-token-from-the-api';
const PAGE_VIEW = '0199e8a0-0000-7000-8000-00000000aaaa';
const ENDPOINT = '/brightwater/telemetry';

const FIELD_KEYS = ['fieldId', 'pasteCount', 'pastedCharacters', 'typedCharacters', 'focusCount', 'blurCount', 'activeMilliseconds', 'autofilled'];

let sent: string[];
let clock: number;
let running: Telemetry | null;

beforeEach(() => {
  sent = [];
  clock = 0;
  running = null;
  document.body.innerHTML = `
    <form>
      <input type="checkbox" name="accommodationRequested" />
      <input type="text" name="firstName" />
      <textarea name="q:motivation"></textarea>
      <input type="text" name="leave_this_empty" />
      <input type="hidden" name="state" value="{}" />
      <input type="hidden" name="telemetry" />
      <input type="hidden" name="deviceFingerprint" />
      <input type="hidden" name="automationMarkers" />
    </form>`;
});

afterEach(() => running?.stop());

const form = () => document.querySelector('form')!;
const field = (name: string) => form().elements.namedItem(name) as HTMLInputElement | HTMLTextAreaElement;
const start = (options: Partial<TelemetryOptions> = {}) =>
  (running = startTelemetry(form(), {
    endpoint: ENDPOINT,
    sessionToken: TOKEN,
    pageViewId: PAGE_VIEW,
    now: () => clock,
    send: (endpoint, body) => {
      expect(endpoint).toBe(ENDPOINT);
      sent.push(body);
    },
    ...options,
  }));

const focus = (element: Element) => element.dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
const blur = (element: Element) => element.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));

// As a browser reports typing: key events, then an input event per character with the character in `data`.
function type(element: HTMLInputElement | HTMLTextAreaElement, text: string) {
  for (const character of text) {
    element.dispatchEvent(new KeyboardEvent('keydown', { key: character, bubbles: true }));
    element.value += character;
    element.dispatchEvent(new InputEvent('input', { inputType: 'insertText', data: character, bubbles: true }));
    element.dispatchEvent(new KeyboardEvent('keyup', { key: character, bubbles: true }));
    clock += 120;
  }
}

function paste(element: HTMLInputElement | HTMLTextAreaElement, text: string) {
  element.value += text;
  element.dispatchEvent(new InputEvent('input', { inputType: 'insertFromPaste', data: text, bubbles: true }));
}

function deleteBackward(element: HTMLInputElement | HTMLTextAreaElement, count: number) {
  element.value = element.value.slice(0, Math.max(0, element.value.length - count));
  element.dispatchEvent(new InputEvent('input', { inputType: 'deleteContentBackward', bubbles: true }));
}

function hidePage() {
  Object.defineProperty(document, 'visibilityState', { value: 'hidden', configurable: true });
  document.dispatchEvent(new Event('visibilitychange'));
  Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true });
}

const submit = () => form().dispatchEvent(new Event('submit', { cancelable: true }));
const switchOn = () => {
  const toggle = field('accommodationRequested') as HTMLInputElement;
  toggle.checked = true;
  toggle.dispatchEvent(new Event('change', { bubbles: true }));
};

/** Every string a payload may hold: the session, the page view, field ids and automation markers. */
function expectOnlyCounts(json: string) {
  const payload = JSON.parse(json) as Record<string, unknown>;
  expect(Object.keys(payload).sort()).toEqual(['fields', 'page', 'pageViewId', 'sequence', 'sessionToken']);
  expect(payload.sessionToken).toBe(TOKEN);
  expect(payload.pageViewId).toBe(PAGE_VIEW);
  expect(Number.isInteger(payload.sequence)).toBe(true);
  for (const each of payload.fields as Record<string, unknown>[]) {
    expect(Object.keys(each).sort()).toEqual([...FIELD_KEYS].sort());
    expect(['firstName', 'motivation']).toContain(each.fieldId);
    for (const key of FIELD_KEYS.filter((k) => k !== 'fieldId' && k !== 'autofilled')) expect(Number.isInteger(each[key])).toBe(true);
    expect(typeof each.autofilled).toBe('boolean');
  }

  const page = payload.page as Record<string, unknown>;
  expect(Object.keys(page).sort()).toEqual(['automationMarkers', 'visibilityChanges']);
  for (const marker of page.automationMarkers as string[]) {
    expect(['webdriver', 'headless', 'phantom', 'selenium', 'nightmare', 'chromedriver']).toContain(marker);
  }
}

describe('startTelemetry', () => {
  it('sends nothing for a page view that measured nothing', () => {
    start();
    focus(form().querySelector('input[type=checkbox]')!); // pressing a control, typing nothing
    window.dispatchEvent(new Event('pagehide'));
    submit();

    expect(sent).toEqual([]);
    expect((field('telemetry') as HTMLInputElement).value).toBe('');
  });

  it('records nothing on the notice step until the candidate starts on the form', () => {
    start();
    hidePage();
    window.dispatchEvent(new Event('pagehide'));
    expect(sent).toEqual([]);

    focus(field('firstName'));
    type(field('firstName') as HTMLInputElement, 'Casey');
    hidePage();
    expect(sent).toHaveLength(1);
  });

  it('does not start when the candidate reaches the assistive-technology switch', () => {
    start();
    focus(field('accommodationRequested'));
    hidePage();
    window.dispatchEvent(new Event('pagehide'));

    expect(sent).toEqual([]);
  });

  it('sends counts and never what was typed or pasted (fixture)', () => {
    start();
    const name = field('firstName');
    const answer = field('q:motivation');
    focus(name);
    type(name, 'Casey Morgan');
    blur(name);
    focus(answer);
    type(answer, 'I like small teams. ');
    paste(answer, 'Prepared paragraph about distributed systems and on-call rotations.');
    deleteBackward(answer, 5);
    blur(answer);
    focus(field('leave_this_empty')); // the honeypot is never measured
    type(field('leave_this_empty') as HTMLInputElement, 'bot');
    hidePage();

    expect(sent).toHaveLength(1);
    const body = sent[0]!;
    expectOnlyCounts(body);
    for (const secret of ['Casey', 'Morgan', 'small teams', 'Prepared', 'distributed', 'bot', 'insertText', 'insertFromPaste']) {
      expect(body).not.toContain(secret);
    }

    const payload = JSON.parse(body);
    expect(payload.fields).toEqual([
      expect.objectContaining({ fieldId: 'firstName', typedCharacters: 12, pastedCharacters: 0, pasteCount: 0, focusCount: 1 }),
      expect.objectContaining({ fieldId: 'motivation', typedCharacters: 20, pastedCharacters: 67, pasteCount: 1, focusCount: 1 }),
    ]);
    expect(payload.page.visibilityChanges).toBe(1);
  });

  it('sends a page view once per change, with a rising sequence', () => {
    start({ acknowledged: true });
    focus(field('firstName'));
    type(field('firstName'), 'Jo');
    hidePage();
    hidePage();
    window.dispatchEvent(new Event('pagehide'));

    // Each hide is one more switch away; leaving the page after that has nothing new to send.
    const sequences = sent.map((body) => JSON.parse(body).sequence as number);
    expect(sequences).toHaveLength(2);
    expect(sequences[1]).toBeGreaterThan(sequences[0]!);
    expect(sent.map((body) => JSON.parse(body).page.visibilityChanges)).toEqual([1, 2]);
  });

  it('puts the final snapshot and the markers in the form on submit, and sends the page view once on leaving', () => {
    start({ acknowledged: true });
    focus(field('q:motivation'));
    type(field('q:motivation'), 'Because.');
    submit();

    const final = (field('telemetry') as HTMLInputElement).value;
    expectOnlyCounts(final);
    expect(JSON.parse(final).fields[0]).toMatchObject({ fieldId: 'motivation', typedCharacters: 8 });
    hidePage(); // leaving for the next page: not a switch away
    window.dispatchEvent(new Event('pagehide'));

    expect(sent).toHaveLength(1);
    const beacon = JSON.parse(sent[0]!);
    expect(beacon.page.visibilityChanges).toBe(0);
    expect(beacon.fields).toEqual(JSON.parse(final).fields);
    expect(beacon.sequence).toBeGreaterThan(JSON.parse(final).sequence);
  });

  it('with the assistive-technology switch on, forgets everything and sends nothing', () => {
    start();
    focus(field('firstName'));
    type(field('firstName'), 'Ari Moss');
    switchOn();
    type(field('q:motivation'), 'More text after switching it on.');
    hidePage();
    window.dispatchEvent(new Event('pagehide'));
    submit();

    expect(sent).toEqual([]);
    expect((field('telemetry') as HTMLInputElement).value).toBe('');
    expect((field('automationMarkers') as HTMLInputElement).value).toBe('');
    expect(running!.snapshot()).toBeNull();
  });

  it('marks autofill and counts no typing for it', () => {
    start({ acknowledged: true });
    const name = field('firstName');
    name.value = 'Casey Morgan';
    name.dispatchEvent(new Event('input', { bubbles: true })); // how browsers report autofill
    hidePage();

    expect(JSON.parse(sent[0]!).fields).toEqual([expect.objectContaining({ fieldId: 'firstName', autofilled: true, typedCharacters: 0 })]);
  });

  // Property: whatever is typed, pasted or deleted, every payload is counts and allowed ids only, and no run of two
  // characters of the content appears in it. Content interleaves a marker character that never occurs otherwise.
  it('never leaks content, whatever the candidate does (property)', () => {
    const MARK = '☃';
    const edit = fc.record({
      kind: fc.constantFrom('type' as const, 'paste' as const, 'delete' as const, 'hide' as const, 'switch-field' as const),
      text: fc.string({ minLength: 1, maxLength: 12 }).map((text) => [...text].join(MARK) + MARK),
    });
    fc.assert(
      fc.property(fc.array(edit, { minLength: 1, maxLength: 40 }), (edits) => {
        sent = [];
        running?.stop();
        (field('telemetry') as HTMLInputElement).value = '';
        field('firstName').value = '';
        field('q:motivation').value = '';
        start({ acknowledged: true });
        let current = field('firstName');
        focus(current);
        for (const step of edits) {
          if (step.kind === 'type') type(current, step.text);
          else if (step.kind === 'paste') paste(current, step.text);
          else if (step.kind === 'delete') deleteBackward(current, step.text.length);
          else if (step.kind === 'hide') hidePage();
          else {
            blur(current);
            current = current.name === 'firstName' ? field('q:motivation') : field('firstName');
            focus(current);
          }
        }

        submit();
        const bodies = [...sent, (field('telemetry') as HTMLInputElement).value];
        for (const body of bodies) {
          expectOnlyCounts(body);
          expect(body).not.toContain(MARK);
        }
      }),
      { numRuns: 200 },
    );
  });
});
