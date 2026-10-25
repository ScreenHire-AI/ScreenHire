import { setKeyboardDragTrigger } from 'svelte-dnd-action';

// svelte-dnd-action's settings are global to the document, so they are set once here for every drag zone: the pipeline
// board's stages and the reorder lists. Space picks an item up for a keyboard drag; Enter opens it, or a grip's Move
// menu, as it does everywhere else in the dashboard.
setKeyboardDragTrigger('space');
