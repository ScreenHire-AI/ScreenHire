import type { Attachment } from 'svelte/attachments';

// A table row opens its record (design system L5). The record's real control stays in the row's name cell, marked
// `data-row-link`: a link to the record's page, or the button that opens its sheet. It is the row's one tab stop, so
// keyboards and screen readers use it as they would any link. `rowLink` makes a click anywhere else on the row follow
// it, as a mouse user expects, and leaves alone what has its own click: another link, a button, a menu, the checkbox and
// its cell, a text field, and a click that ends a text selection or closes an open menu. Ctrl, Cmd, Shift or a middle
// click on a row whose control is a link opens the record in a new tab, as the link itself would.
//
// A row that expands in place is not this: it carries `data-row-opens` and toggles itself (A9).

/** Elements with a click of their own. A click on one, or inside one, is theirs, not the row's. */
const ownClick = [
  'a',
  'button',
  'input',
  'select',
  'textarea',
  'label',
  'summary',
  '[contenteditable=""]',
  '[contenteditable="true"]',
  '[role="button"]',
  '[role="link"]',
  '[role="checkbox"]',
  '[role="switch"]',
  '[role="menuitem"]',
  '[role="option"]',
  '[data-row-ignore]',
].join(', ');

/** A layer that a click outside closes. The click that closes it is not a click on the row. */
const openLayer = ':is([role="menu"], [role="listbox"], [role="dialog"], [role="alertdialog"])[data-state="open"]';

/**
 * The checkbox's `<label>` in a row's select cell (and the header's Select all): it fills the cell, which must be
 * positioned (`relative` or `sticky`), so the whole cell selects and a click in its padding never opens the record. The
 * cell is at least 48px tall and 50px wide, 70px on touch (F4).
 */
export const selectCellLabel = 'absolute inset-0 grid place-items-center';

/** What a click on the row does: press the row's control (or the checkbox of the cell it lands in), in this tab or a new one, or nothing. */
export type RowClick = { control: HTMLElement; newTab: boolean } | null;

/** Whether the person has selected text inside the row: a drag to copy a cell ends in a click that must not navigate. */
function selectsText(row: HTMLElement): boolean {
  const selection = row.ownerDocument.defaultView?.getSelection();
  if (!selection || selection.isCollapsed || selection.toString().trim() === '') return false;
  return (selection.anchorNode !== null && row.contains(selection.anchorNode)) || (selection.focusNode !== null && row.contains(selection.focusNode));
}

/**
 * Decides what a click (or a middle button's `auxclick`) on `row` does. Pure apart from reading the selection, so the
 * unit test can call it with any target and modifier.
 */
export function rowClick(event: MouseEvent, row: HTMLElement): RowClick {
  if (event.defaultPrevented || (event.button !== 0 && event.button !== 1)) return null;
  const target = event.target instanceof Element ? event.target : null;
  if (!target || !row.contains(target)) return null;
  const own = target.closest(ownClick);
  if (own && row.contains(own)) return null;
  // A tap on cut text opens its tooltip (Hint, F4), because a touch screen has no hover to show it.
  if ((event as PointerEvent).pointerType === 'touch' && target.closest('[data-tooltip-trigger]')) return null;
  if (selectsText(row)) return null;
  // The checkbox's cell is all its own: a click in it selects, wherever the label stops (L5).
  const checkbox = target.closest('td, th')?.querySelector<HTMLInputElement>('input[type="checkbox"]');
  if (checkbox) return checkbox.disabled ? null : { control: checkbox, newTab: false };
  const control = row.querySelector<HTMLElement>('[data-row-link]');
  if (!control || control.matches(':disabled, [aria-disabled="true"]')) return null;
  const isLink = control instanceof HTMLAnchorElement && control.href !== '';
  const newTab = event.button === 1 || event.ctrlKey || event.metaKey || event.shiftKey;
  // A button opens a sheet on this page: there is no new tab to open, and a middle click means nothing to it.
  if (!isLink) return event.button === 1 ? null : { control, newTab: false };
  return { control, newTab };
}

/**
 * The row's click, as `{@attach rowLink}` on a `<tr>` whose name cell holds a `data-row-link` control. The row gets
 * `data-row-click`, which the theme gives the hand cursor while the control is there (A10); the row keeps its own
 * `hover:bg-muted`.
 */
export const rowLink: Attachment<HTMLElement> = (row) => {
  const document = row.ownerDocument;
  let layerWasOpen = false;

  const onpointerdown = (event: PointerEvent) => {
    // An open menu closes on this press (bits-ui waits a moment, or for the click on touch), so look now.
    layerWasOpen = document.querySelector(openLayer) !== null;
    // A middle press outside a control would start the browser's autoscroll, not open the record.
    if (event.button === 1 && rowClick(event, row)) event.preventDefault();
  };

  const follow = (event: MouseEvent) => {
    if (layerWasOpen) {
      layerWasOpen = false;
      return;
    }
    const click = rowClick(event, row);
    if (!click) return;
    if (click.newTab) {
      event.preventDefault();
      document.defaultView?.open((click.control as HTMLAnchorElement).href, '_blank', 'noopener');
    } else {
      // The control's own click, so SvelteKit's router takes the link and a button runs its handler. It bubbles back
      // here with the control as its target, which `rowClick` leaves alone.
      click.control.click();
    }
  };

  const onauxclick = (event: MouseEvent) => {
    if (event.button === 1) follow(event);
  };

  row.setAttribute('data-row-click', '');
  row.addEventListener('pointerdown', onpointerdown);
  row.addEventListener('click', follow);
  row.addEventListener('auxclick', onauxclick);
  return () => {
    row.removeAttribute('data-row-click');
    row.removeEventListener('pointerdown', onpointerdown);
    row.removeEventListener('click', follow);
    row.removeEventListener('auxclick', onauxclick);
  };
};
