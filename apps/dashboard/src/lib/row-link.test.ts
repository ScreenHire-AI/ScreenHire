import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { rowClick, rowLink } from './row-link.js';

// A table row whose name cell holds the record's control, and the cells a real row has around it: plain text, a cut
// value with a tooltip, a checkbox in its label, another link, a menu's button and a text field.
function makeRow(control: 'link' | 'button' | 'none' = 'link') {
  const table = document.createElement('table');
  table.innerHTML = `<tbody><tr>
    <td data-cell="select"><label data-label><input type="checkbox" aria-label="Select Casey Morgan" /></label></td>
    <td data-cell="name">${
      control === 'link'
        ? '<a href="/applications/1" data-row-link>Casey Morgan</a>'
        : control === 'button'
          ? '<button type="button" data-row-link>Casey Morgan</button>'
          : '<span>Casey Morgan</span>'
    }</td>
    <td data-cell="text"><span data-text>Senior .NET Engineer</span></td>
    <td data-cell="hint"><span data-tooltip-trigger tabindex="0">Remote (United Kingdom, Ireland)</span></td>
    <td data-cell="job"><a href="/jobs/1" data-other-link>Job</a></td>
    <td data-cell="menu"><button type="button" aria-haspopup="menu" data-menu>More</button><span role="menuitem" data-item>Move</span></td>
    <td data-cell="note"><input type="text" data-field /><span data-row-ignore><span data-ignored>Own click</span></span></td>
  </tr></tbody>`;
  document.body.append(table);
  const row = table.querySelector('tr')!;
  const find = <T extends Element = HTMLElement>(selector: string) => row.querySelector<T & HTMLElement>(selector)!;
  return { row, find, cleanup: rowLink(row) as () => void };
}

function click(target: Element, init: MouseEventInit = {}, type = 'click') {
  const event = new MouseEvent(type, { bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

let open: ReturnType<typeof vi.spyOn>;
beforeEach(() => {
  open = vi.spyOn(window, 'open').mockReturnValue(null);
});
afterEach(() => {
  vi.restoreAllMocks();
  window.getSelection()?.removeAllRanges();
  document.body.innerHTML = '';
});

describe('rowLink', () => {
  it('follows the row link from any plain cell, in this tab', () => {
    const { find } = makeRow();
    const follow = vi.spyOn(find<HTMLAnchorElement>('[data-row-link]'), 'click').mockImplementation(() => {});
    click(find('[data-text]'));
    click(find('[data-cell="note"]'));
    expect(follow).toHaveBeenCalledTimes(2);
    expect(open).not.toHaveBeenCalled();
  });

  it('marks the row for the theme and clears the mark when it is removed', () => {
    const { row, cleanup } = makeRow();
    expect(row).toHaveAttribute('data-row-click');
    cleanup();
    expect(row).not.toHaveAttribute('data-row-click');
  });

  it.each([
    ['the row link itself', '[data-row-link]'],
    ['another link', '[data-other-link]'],
    ['the checkbox', 'input[type=checkbox]'],
    ["the checkbox's label, which fills its cell", '[data-label]'],
    ["a menu's button", '[data-menu]'],
    ['a menu item', '[data-item]'],
    ['a text field', '[data-field]'],
    ['something marked data-row-ignore', '[data-ignored]'],
  ])('leaves a click on %s alone', (_name, selector) => {
    const { row, find } = makeRow();
    const event = new MouseEvent('click', { bubbles: true });
    Object.defineProperty(event, 'target', { value: find(selector) });
    expect(rowClick(event, row)).toBeNull();
  });

  it('does not follow a link when the click lands on another control', () => {
    const { find } = makeRow();
    const follow = vi.spyOn(find<HTMLAnchorElement>('[data-row-link]'), 'click').mockImplementation(() => {});
    find<HTMLInputElement>('input[type=checkbox]').click();
    click(find('[data-menu]'));
    expect(follow).not.toHaveBeenCalled();
    expect(find<HTMLInputElement>('input[type=checkbox]').checked).toBe(true);
  });

  it("selects from anywhere in the checkbox's cell, and never opens the record from it", () => {
    const { find } = makeRow();
    const follow = vi.spyOn(find<HTMLAnchorElement>('[data-row-link]'), 'click').mockImplementation(() => {});
    const box = find<HTMLInputElement>('input[type=checkbox]');
    click(find('[data-cell="select"]'));
    expect(box.checked).toBe(true);
    click(find('[data-cell="select"]'), { ctrlKey: true });
    expect(box.checked).toBe(false);
    box.disabled = true;
    click(find('[data-cell="select"]'));
    expect(box.checked).toBe(false);
    expect(follow).not.toHaveBeenCalled();
    expect(open).not.toHaveBeenCalled();
  });

  it.each([
    ['Ctrl', { ctrlKey: true }],
    ['Cmd', { metaKey: true }],
    ['Shift', { shiftKey: true }],
  ])('opens the record in a new tab on %s click', (_name, init) => {
    const { find } = makeRow();
    const follow = vi.spyOn(find<HTMLAnchorElement>('[data-row-link]'), 'click').mockImplementation(() => {});
    const event = click(find('[data-text]'), init);
    expect(open).toHaveBeenCalledWith('http://localhost:3000/applications/1', '_blank', 'noopener');
    expect(event.defaultPrevented).toBe(true);
    expect(follow).not.toHaveBeenCalled();
  });

  it('opens the record in a new tab on a middle click, and stops the autoscroll its press would start', () => {
    const { find } = makeRow();
    const press = click(find('[data-text]'), { button: 1 }, 'pointerdown');
    click(find('[data-text]'), { button: 1 }, 'auxclick');
    expect(press.defaultPrevented).toBe(true);
    expect(open).toHaveBeenCalledWith('http://localhost:3000/applications/1', '_blank', 'noopener');
  });

  it('ignores a right click', () => {
    const { row, find } = makeRow();
    const event = new MouseEvent('auxclick', { button: 2 });
    Object.defineProperty(event, 'target', { value: find('[data-text]') });
    expect(rowClick(event, row)).toBeNull();
    click(find('[data-text]'), { button: 2 }, 'auxclick');
    expect(open).not.toHaveBeenCalled();
  });

  it("presses a sheet's button, with or without a modifier, and leaves a middle click alone", () => {
    const { find } = makeRow('button');
    const button = find<HTMLButtonElement>('[data-row-link]');
    const press = vi.fn();
    button.addEventListener('click', press);
    click(find('[data-text]'));
    click(find('[data-text]'), { ctrlKey: true });
    click(find('[data-text]'), { button: 1 }, 'auxclick');
    // Each row click presses the button once; the press bubbles back to the row and is left alone.
    expect(press).toHaveBeenCalledTimes(2);
    expect(open).not.toHaveBeenCalled();
  });

  it('does nothing on a row with no control, or a disabled one', () => {
    const { row, find } = makeRow('none');
    const event = new MouseEvent('click');
    Object.defineProperty(event, 'target', { value: find('[data-text]') });
    expect(rowClick(event, row)).toBeNull();

    const other = makeRow('button');
    const button = other.find<HTMLButtonElement>('[data-row-link]');
    button.disabled = true;
    const press = vi.fn();
    button.addEventListener('click', press);
    click(other.find('[data-text]'));
    expect(press).not.toHaveBeenCalled();
  });

  it('leaves a click that ends a text selection in the row alone', () => {
    const { find } = makeRow();
    const follow = vi.spyOn(find<HTMLAnchorElement>('[data-row-link]'), 'click').mockImplementation(() => {});
    const range = document.createRange();
    range.selectNodeContents(find('[data-text]'));
    window.getSelection()!.addRange(range);
    click(find('[data-text]'));
    expect(follow).not.toHaveBeenCalled();

    window.getSelection()!.removeAllRanges();
    click(find('[data-text]'));
    expect(follow).toHaveBeenCalledTimes(1);
  });

  it('leaves alone the click that closes an open menu', () => {
    const { find } = makeRow();
    const follow = vi.spyOn(find<HTMLAnchorElement>('[data-row-link]'), 'click').mockImplementation(() => {});
    const menu = document.createElement('div');
    menu.setAttribute('role', 'menu');
    menu.dataset.state = 'open';
    document.body.append(menu);
    click(find('[data-text]'), {}, 'pointerdown');
    // The menu closes between the press and the click, as bits-ui does.
    menu.remove();
    click(find('[data-text]'));
    expect(follow).not.toHaveBeenCalled();

    click(find('[data-text]'), {}, 'pointerdown');
    click(find('[data-text]'));
    expect(follow).toHaveBeenCalledTimes(1);
  });

  it("lets a tap on cut text open its tooltip, while a mouse click on it follows the row", () => {
    const { row, find } = makeRow();
    const tap = new MouseEvent('click', { bubbles: true });
    Object.defineProperty(tap, 'target', { value: find('[data-tooltip-trigger]') });
    Object.defineProperty(tap, 'pointerType', { value: 'touch' });
    expect(rowClick(tap, row)).toBeNull();

    const mouse = new MouseEvent('click', { bubbles: true });
    Object.defineProperty(mouse, 'target', { value: find('[data-tooltip-trigger]') });
    Object.defineProperty(mouse, 'pointerType', { value: 'mouse' });
    expect(rowClick(mouse, row)).toEqual({ control: find('[data-row-link]'), newTab: false });
  });
});
