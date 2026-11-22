import { describe, expect, it, vi } from 'vitest';
import { chordTimeoutMs, createChordHandler, isTypingTarget, opensActivity, shortcutGroups } from './shortcuts.js';

function press(key: string, target: EventTarget = document.body, init: KeyboardEventInit = {}) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, ...init });
  Object.defineProperty(event, 'target', { value: target });
  return event;
}

describe('createChordHandler', () => {
  it('goes to the destination for G then its letter', () => {
    const go = vi.fn();
    const handle = createChordHandler({ j: '/', c: '/candidates' }, go, () => 1000);

    expect(handle(press('g'))).toBe(true);
    expect(handle(press('c'))).toBe(true);

    expect(go).toHaveBeenCalledWith('/candidates');
  });

  it('ignores the letter alone, and after the chord times out', () => {
    let clock = 0;
    const go = vi.fn();
    const handle = createChordHandler({ j: '/' }, go, () => clock);

    expect(handle(press('j'))).toBe(false);
    handle(press('g'));
    clock = chordTimeoutMs + 1;
    expect(handle(press('j'))).toBe(false);

    expect(go).not.toHaveBeenCalled();
  });

  it('leaves key presses in text fields and with modifiers alone', () => {
    const go = vi.fn();
    const handle = createChordHandler({ j: '/' }, go, () => 0);
    const input = document.createElement('input');

    handle(press('g', input));
    handle(press('j', input));
    handle(press('g', document.body, { ctrlKey: true }));
    handle(press('j'));

    expect(go).not.toHaveBeenCalled();
  });
});

describe('isTypingTarget', () => {
  it('is true inside text fields and dialogs, false on links and checkboxes', () => {
    const field = document.createElement('textarea');
    const dialog = document.createElement('div');
    dialog.setAttribute('role', 'dialog');
    const inside = document.createElement('button');
    dialog.append(inside);
    const checkbox = Object.assign(document.createElement('input'), { type: 'checkbox' });

    expect(isTypingTarget(field)).toBe(true);
    expect(isTypingTarget(inside)).toBe(true);
    expect(isTypingTarget(document.createElement('a'))).toBe(false);
    expect(isTypingTarget(checkbox)).toBe(false);
  });

  // The rich text editor is a contenteditable; a list item or bold text inside it is where the key press starts.
  it('is true anywhere inside the rich text editor', () => {
    const editor = document.createElement('div');
    editor.setAttribute('contenteditable', 'true');
    const bold = document.createElement('strong');
    editor.append(bold);

    expect(isTypingTarget(editor)).toBe(true);
    expect(isTypingTarget(bold)).toBe(true);
  });
});

describe('the Activity shortcut', () => {
  const key = (init: KeyboardEventInit, target: EventTarget = document.body) => {
    const event = new KeyboardEvent('keydown', init);
    Object.defineProperty(event, 'target', { value: target });
    return event;
  };

  it('is A alone, outside text fields, and listed for the ? dialog as a single key the switch turns off', () => {
    expect(opensActivity(key({ key: 'a' }))).toBe(true);
    expect(opensActivity(key({ key: 'A', shiftKey: true }))).toBe(false);
    expect(opensActivity(key({ key: 'a', ctrlKey: true }))).toBe(false);
    expect(opensActivity(key({ key: 'a' }, document.createElement('input')))).toBe(false);
    const anywhere = shortcutGroups([]).find((group) => group.label === 'Anywhere')!;
    expect(anywhere.shortcuts).toContainEqual({ label: "Open the workspace's activity", keys: ['A'], singleKey: true });
  });
});
