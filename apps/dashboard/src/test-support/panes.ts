/**
 * Whether an element is in the pane an Edit / Preview layout hides where the editor and its preview do not both fit
 * (`EditPreviewLayout`). jsdom has no container queries, so a test sees the narrow layout: one pane, the other hidden.
 */
export const inHiddenPane = (element: Element | null) => Boolean(element?.closest('[data-edit-pane].hidden, [data-preview-pane].hidden'));
