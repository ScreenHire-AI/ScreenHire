import type { Attachment } from 'svelte/attachments';

/**
 * Reports whether an element's text is cut: ending in an ellipsis, or visually hidden in a narrow container. A
 * control's tooltip uses it to show its label only while the label is not readable (design system F5).
 */
export function watchTruncation(oncut: (cut: boolean) => void): Attachment<HTMLElement> {
  return (element) => {
    const measure = () => oncut(element.scrollWidth > element.clientWidth);
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(element);
    return () => observer.disconnect();
  };
}
