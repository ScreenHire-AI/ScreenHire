import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { stages } from '../../test-support/fixtures.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import ApplicationMenu from './application-menu.svelte';

const open = () => fireEvent.pointerDown(screen.getByRole('button', { name: 'Actions for Robin Ashby' }), { button: 0, pointerType: 'mouse' });
const renderMenu = (onmove = vi.fn(), currentStageId = stages[0]!.id) =>
  render(WithTooltips, { props: { component: ApplicationMenu, props: { candidateName: 'Robin Ashby', currentStageId, stages, onmove } } });

describe('ApplicationMenu', () => {
  it('is one row menu named for the candidate, not a button that says Move on every row (L3)', () => {
    renderMenu();

    expect(screen.getByRole('button', { name: 'Actions for Robin Ashby' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Move/ })).toBeNull();
  });

  it('lists the stages under Move to, the current one marked and not pickable', async () => {
    renderMenu();
    await open();

    const items = await screen.findAllByRole('menuitem');
    expect(items.map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual(['Applied current', 'Screening', 'Interview', 'Offer']);
    expect(items[0]).toHaveAttribute('aria-disabled', 'true');
  });

  it('moves to the stage that is chosen', async () => {
    const onmove = vi.fn();
    renderMenu(onmove);
    await open();

    await fireEvent.click(await screen.findByRole('menuitem', { name: 'Screening' }));

    expect(onmove).toHaveBeenCalledWith(stages[1]);
  });

  it('marks no stage as current for a saved candidate, who is in none', async () => {
    renderMenu(vi.fn(), '');
    await open();

    const items = await screen.findAllByRole('menuitem');
    expect(items.every((item) => item.getAttribute('aria-disabled') !== 'true')).toBe(true);
  });
});
