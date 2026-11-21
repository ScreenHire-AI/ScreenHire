import { cleanup, fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import ExpandableRow from './expandable-row.svelte';

// The person's reduced-motion setting, as `svelte/motion` reports it.
const motion = vi.hoisted(() => ({ current: false }));
vi.mock('svelte/motion', async (original) => ({ ...(await original<typeof import('svelte/motion')>()), prefersReducedMotion: motion }));

const text = (html: string) => createRawSnippet(() => ({ render: () => html }));

afterEach(() => {
  cleanup();
  motion.current = false;
  vi.restoreAllMocks();
});

// Design system A17: what a row shows slides open, and appears at once with reduced motion.
describe('ExpandableRow motion', () => {
  it.each([
    ['slides for 180ms', false, [180]],
    ['appears at once with reduced motion', true, []],
  ])('opening %s', async (_, reduced, durations) => {
    motion.current = reduced;
    const animate = vi.spyOn(Element.prototype, 'animate');
    render(ExpandableRow, { props: { id: 'row', summary: text('<span>Interview</span>'), children: text('<p>Hi Casey</p>') } });

    await fireEvent.click(screen.getByRole('button', { name: 'Interview' }));

    expect(await screen.findByText('Hi Casey')).toBeInTheDocument();
    // Svelte runs a zero-length animation for the transition's delay before the slide itself.
    const slides = animate.mock.calls.map(([, options]) => (typeof options === 'object' ? options.duration : options)).filter(Boolean);
    expect(slides).toEqual(durations);
  });
});
