import { page } from '$app/state';
import type { Attachment } from 'svelte/attachments';

/**
 * Where a link into a record lands (design system section 11, Activity): the address's hash names one thing on the page,
 * `#email-<id>`, `#scorecard-<id>`, `#follow-up-<id>`, `#interview`, and the thing scrolls into view, or opens, once the
 * data that draws it has loaded. The Activity feed in the bell, the candidate's Activity tab and the toasts link this way
 * (`activityHref` in `#lib/activity.ts`).
 */

/** The id after `#<prefix>` in the address's hash, or undefined when the hash names something else. */
export function linkedId(prefix: string): string | undefined {
  const hash = page.url.hash;
  return hash.startsWith(`#${prefix}`) && hash.length > prefix.length + 1 ? decodeURIComponent(hash.slice(prefix.length + 1)) : undefined;
}

/**
 * Scrolls the element into view when the hash names `anchor`: when it mounts, which is when its data has loaded, and
 * when a later link on the same page names it. The element also needs `id={anchor}`, so the address still means it.
 */
export function linkedTo(anchor: string): Attachment<HTMLElement> {
  return (element) => {
    if (page.url.hash !== `#${anchor}`) return;
    // After the browser has laid out what mounted with it, so the element lands where it will stay.
    const frame = requestAnimationFrame(() => element.scrollIntoView({ block: 'center' }));
    return () => cancelAnimationFrame(frame);
  };
}
