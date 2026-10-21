import { clsx, type ClassValue } from 'clsx';
import { extendTailwindMerge } from 'tailwind-merge';
import { createTV } from 'tailwind-variants';

/**
 * The type scale's `--text-*` names in @screenhire/ui. tailwind-merge reads an unknown `text-*` class as a colour, so
 * without them `cn('text-caption text-muted-foreground')` would drop the size. A test keeps this list in step.
 */
export const typeScale = ['page-title', 'panel-title', 'heading', 'body', 'reading', 'caption', 'code', 'candidate-body'];

const twMergeConfig = { extend: { theme: { text: typeScale } } };
const twMerge = extendTailwindMerge(twMergeConfig);

/** tailwind-variants' `tv`, merging with the same configuration as `cn()`. Components import it from here. */
export const tv = createTV({ twMergeConfig });

export function cn(...inputs: ClassValue[]): string {
  return twMerge(clsx(inputs));
}

// Helper types used by shadcn-svelte components.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type WithoutChild<T> = T extends { child?: any } ? Omit<T, 'child'> : T;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export type WithoutChildren<T> = T extends { children?: any } ? Omit<T, 'children'> : T;
export type WithoutChildrenOrChild<T> = WithoutChildren<WithoutChild<T>>;
export type WithElementRef<T, U extends HTMLElement = HTMLElement> = T & { ref?: U | null };
