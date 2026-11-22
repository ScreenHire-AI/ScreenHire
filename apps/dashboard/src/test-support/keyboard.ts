import { fireEvent } from '@testing-library/svelte';

/**
 * A Tab key press, sent before a test moves the focus to a control: a tooltip opens on keyboard focus, and the focus of a
 * Tab is the one that counts (`ui/tooltip/focus-intent.ts`). jsdom has no default action for Tab, so the test moves the
 * focus itself, in the same task, as the browser would.
 */
export const pressTab = () => fireEvent.keyDown(document.body, { key: 'Tab' });
