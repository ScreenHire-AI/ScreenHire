// Native form controls for the workflow builder, styled like the shadcn Input. Native selects and checkboxes, because
// they need no script to be accessible. On touch, selects are 44px high with 16px text, and checkboxes grow inside
// 44px-high labels (F4).
export const selectClass =
  'h-9 pointer-coarse:h-11 pointer-coarse:text-reading max-md:text-reading w-full min-w-0 rounded-lg border border-field-edge bg-transparent px-2 text-body text-foreground  focus-visible:border-ring   aria-invalid:border-destructive disabled:opacity-50 dark:bg-input/30';

export const checkboxClass = 'size-4 pointer-coarse:size-6 flex-none accent-primary';

/** A field's label that stands apart from its control, with an Info button beside it (a button never sits inside a label). */
export const fieldLabelClass = 'text-body font-semibold text-secondary-foreground';

export const labelClass = 'flex min-w-0 flex-col gap-1 text-body font-semibold text-secondary-foreground';

/** One item in a panel's list (S4). */
export const rowClass = 'border-b border-border-subtle py-4 first:pt-0 last:border-0 last:pb-0';

/** A well inside a row (design system section 6). Its controls stay white so they read as fields. */
export const insetClass = 'flex flex-col gap-2 rounded-lg bg-secondary p-3 [&_input:not([type=checkbox])]:bg-card [&_select]:bg-card';

/**
 * A chip that adds a part to an automation: the sentence's "another condition" and "another action", and Rules' Add condition
 * and Add action. Dashed, so it reads as a place for something new beside the parts already there.
 */
export const addChipClass =
  'inline-flex max-w-full min-w-0 items-center gap-1 rounded-lg border border-dashed border-input bg-card px-2 py-0.5 align-middle text-left leading-6 font-semibold text-muted-foreground shadow-surface min-h-8 pointer-coarse:min-h-11 hover:bg-accent hover:text-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring';

/** One of Rules' three parts, When, If and Then: a well like the sentence's, its controls white (section 6). */
export const ruleSectionClass = 'flex min-w-0 flex-col gap-3 rounded-lg bg-secondary p-3 [&_input:not([type=checkbox])]:bg-card [&_select]:bg-card';

/** When, If and Then, in the Sentence and in Rules: a muted pill with no edge, so it never reads as one of the chips around it. */
export const keywordClass = 'border-transparent bg-foreground/10 font-semibold text-secondary-foreground';
