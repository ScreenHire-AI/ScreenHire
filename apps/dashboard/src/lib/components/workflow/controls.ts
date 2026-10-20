// Native form controls for the workflow builder, styled like the shadcn Input. Native selects and checkboxes, because a
// <fieldset disabled> then turns a whole published version read-only, and they need no script to be accessible.
export const selectClass =
  'h-8 w-full min-w-0 rounded-lg border border-input bg-transparent px-2 text-sm text-foreground outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30';

export const checkboxClass = 'size-4 flex-none accent-primary disabled:cursor-not-allowed';

export const labelClass = 'flex min-w-0 flex-col gap-1 text-[12.5px] font-semibold text-secondary-foreground';

export const kickerClass = 'font-mono text-[10.5px] font-medium tracking-[0.06em] text-muted-foreground uppercase';
