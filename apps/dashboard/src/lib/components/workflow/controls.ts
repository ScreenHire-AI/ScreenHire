// Native form controls for the workflow builder, styled like the shadcn Input. Native selects and checkboxes, because
// they need no script to be accessible. On touch, selects are 44px high with 16px text and checkboxes grow (F4).
export const selectClass =
  'h-8 pointer-coarse:h-11 pointer-coarse:text-reading w-full min-w-0 rounded-lg border border-input bg-transparent px-2 text-body text-foreground outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30';

export const checkboxClass = 'size-4 pointer-coarse:size-6 flex-none accent-primary disabled:cursor-not-allowed';

export const labelClass = 'flex min-w-0 flex-col gap-1 text-body font-semibold text-secondary-foreground';

export const kickerClass = 'text-caption text-muted-foreground';

/** One item in a panel's list: rows divided by inner rules, never a card inside the panel (S4). */
export const rowClass = 'border-b border-border-subtle py-4 first:pt-0 last:border-0 last:pb-0';

/** A well inside a row (design system section 6): `secondary`, no edge. Its controls stay white so they read as fields. */
export const insetClass = 'flex flex-col gap-2 rounded-lg bg-secondary p-3 [&_input:not([type=checkbox])]:bg-card [&_select]:bg-card';
