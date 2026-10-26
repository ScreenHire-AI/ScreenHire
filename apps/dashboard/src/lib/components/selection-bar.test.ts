import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { stages } from '../../test-support/fixtures.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import SelectionBar from './selection-bar.svelte';

function renderBar(props: Partial<{ onemail: () => void }> = {}) {
  const handlers = { onmove: vi.fn(), onclear: vi.fn() };
  const view = render(WithTooltips, { props: { component: SelectionBar, props: { count: 2, stages, ...handlers, ...props } } });
  return { ...view, ...handlers };
}

describe('selection bar', () => {
  it('says how many are selected once, and names the count on its buttons only for screen readers (W4)', async () => {
    const { container } = renderBar({ onemail: vi.fn() });
    const bar = screen.getByRole('region', { name: 'Selected candidates' });

    expect(bar).toHaveTextContent('2 selected');
    expect(within(bar).getByRole('button', { name: 'Move 2 selected…' })).toHaveTextContent(/^Move$/);
    expect(within(bar).getByRole('button', { name: 'Email 2 selected' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('moves the selection to a stage it offers, and clears it', async () => {
    const { onmove, onclear } = renderBar();

    await fireEvent.pointerDown(screen.getByRole('button', { name: 'Move 2 selected…' }), { button: 0, pointerType: 'mouse' });
    expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.trim())).toEqual(stages.map((stage) => stage.name));
    await fireEvent.click(screen.getByRole('menuitem', { name: 'Screening' }));
    expect(onmove).toHaveBeenCalledWith(stages.find((stage) => stage.name === 'Screening'));

    await fireEvent.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(onclear).toHaveBeenCalled();
  });

  it('offers no Email when the page leaves it out, as it does under an integrity filter (ADR-0004)', () => {
    renderBar();

    expect(screen.queryByRole('button', { name: /^Email/ })).toBeNull();
  });
});
