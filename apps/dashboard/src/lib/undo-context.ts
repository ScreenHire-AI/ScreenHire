import { getContext, setContext } from 'svelte';
import type { UndoOffer } from '#lib/undo.svelte.js';

// The app shell owns the one undo offer, shown by `UndoNotice` at the bottom of the window. A page or component that
// changes something it can put back (the empty step a workflow drops) reaches it here. Outside the shell, as in a
// component test, there is none: the change still happens, with nothing to undo it from.
const key = Symbol('undo-offer');

export const setUndo = (offer: UndoOffer): void => {
  setContext(key, offer);
};

export const getUndo = (): UndoOffer | undefined => getContext<UndoOffer | undefined>(key);
