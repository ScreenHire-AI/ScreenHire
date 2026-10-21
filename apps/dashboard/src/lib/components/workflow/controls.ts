// Native form controls for the workflow builder, styled like the shadcn Input. Native selects and checkboxes, because a
// <fieldset disabled> then turns a whole published version read-only, and they need no script to be accessible.
export const selectClass =
  'h-8 pointer-coarse:h-11 max-md:text-reading w-full min-w-0 rounded-lg border border-input bg-transparent px-2 text-body text-foreground outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30';

export const checkboxClass = 'size-4 flex-none accent-primary disabled:cursor-not-allowed';

export const labelClass = 'flex min-w-0 flex-col gap-1 text-body font-semibold text-secondary-foreground';

export const kickerClass = 'text-caption text-muted-foreground';
