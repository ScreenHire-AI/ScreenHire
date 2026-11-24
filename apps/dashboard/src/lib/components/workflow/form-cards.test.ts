// The Forms tab's cards (design system section 11, Form cards): two rows each, the name with its question count, then
// when it is sent as a chip with its own icon; the chosen card pressed with the accent fill and its
// full border; a form's menu beside its card's button; New form last.
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import type { TimelineForm } from '#lib/forms-timeline.js';
import FormCards from './form-cards.svelte';

const forms: TimelineForm[] = [
  { id: 'apply', name: 'Application form', when: 'When they apply', whenShort: 'When they apply', moment: 'apply', questions: 6, library: false, equality: false },
  { id: 'equality', name: 'Equality monitoring', when: 'Sent when they apply', whenShort: 'When they apply', moment: 'apply', questions: 4, library: true, equality: true },
  { id: 'screening', name: 'Screening questionnaire', when: 'Sent when they enter Screening', whenShort: 'Entering Screening', moment: 'stage', questions: 1, library: true, equality: false },
  { id: 'availability', name: 'Availability', when: 'Only when sent by hand', whenShort: 'By hand only', moment: 'hand', questions: 3, library: true, equality: false },
];

// The panel's header uses the tooltip provider that the query client wrapper brings.
type Props = { forms: TimelineForm[]; selected: string; onadd?: () => void; onreplace?: (id: string) => void; onremove?: (id: string) => void };
const renderCards = (props: Props) => render(WithQueryClient, { props: { component: FormCards, props } });
const group = () => screen.getByRole('group', { name: 'Forms' });
const cardOf = (name: RegExp) => within(group()).getByRole('button', { name });

describe('form cards', () => {
  it('shows each form as its name and question count, then when it is sent, in a few words with its own icon', async () => {
    const { container } = renderCards({ forms, selected: 'apply', onadd: vi.fn() });

    const screening = cardOf(/Screening questionnaire/);
    expect(screening.querySelector('[data-form-count]')).toHaveTextContent('1 question');
    expect(screening.querySelector('[data-form-when]')).toHaveTextContent('Entering Screening');
    expect(screening.querySelector('[data-form-when]')).toHaveAttribute('data-form-when', 'stage');
    expect(cardOf(/Availability/).querySelector('[data-form-when]')).toHaveAttribute('data-form-when', 'hand');
    expect(cardOf(/Application form/)).toHaveTextContent('6 questions');
    expect(cardOf(/Equality monitoring/)).toHaveTextContent('Counted only');
    expect(cardOf(/Application form/)).not.toHaveTextContent('Counted only');
    await expectNoAxeViolations(container);
  });

  it('presses the chosen card, with the accent fill and its full border, and moves the choice on a click', async () => {
    renderCards({ forms, selected: 'apply' });

    expect(cardOf(/Application form/)).toHaveAttribute('aria-pressed', 'true');
    expect(cardOf(/Application form/)).toHaveClass('bg-accent', 'border', 'border-primary');
    expect(cardOf(/Application form/).className).not.toMatch(/before:|border-[lrse]-/);
    expect(cardOf(/Availability/)).toHaveAttribute('aria-pressed', 'false');
    expect(cardOf(/Availability/)).not.toHaveClass('bg-accent');

    await fireEvent.click(cardOf(/Availability/));

    expect(cardOf(/Availability/)).toHaveAttribute('aria-pressed', 'true');
    expect(cardOf(/Application form/)).toHaveAttribute('aria-pressed', 'false');
  });

  it("has each form's menu beside its card's button, never inside it, and none on the application form", async () => {
    const onreplace = vi.fn();
    const onremove = vi.fn();
    const { container } = renderCards({ forms, selected: 'apply', onreplace, onremove });

    const menus = within(group()).getAllByRole('button', { name: /^Actions for / });
    expect(menus.map((menu) => menu.getAttribute('aria-label'))).toEqual(['Actions for Equality monitoring', 'Actions for Screening questionnaire', 'Actions for Availability']);
    for (const menu of menus) expect(menu.parentElement!.closest('button')).toBeNull();
    expect(container.querySelector('[data-form-card="apply"] [aria-haspopup]')).toBeNull();
    await expectNoAxeViolations(container);

    async function choose(item: string) {
      await fireEvent.pointerDown(within(group()).getByRole('button', { name: 'Actions for Screening questionnaire' }), { button: 0, pointerType: 'mouse' });
      const list = await screen.findByRole('menu');
      expect(within(list).getAllByRole('menuitem').map((each) => each.textContent?.trim())).toEqual(['Replace with another form', 'Remove form']);
      await fireEvent.click(within(list).getByRole('menuitem', { name: item }));
    }
    await choose('Replace with another form');
    await waitFor(() => expect(onreplace).toHaveBeenCalledExactlyOnceWith('screening'));
    await choose('Remove form');
    await waitFor(() => expect(onremove).toHaveBeenCalledExactlyOnceWith('screening'));
    // Neither chooses the card.
    expect(cardOf(/^Application form/)).toHaveAttribute('aria-pressed', 'true');
  });

  it('has no menus where the workflow only reads', () => {
    renderCards({ forms, selected: 'apply' });
    expect(within(group()).queryByRole('button', { name: /^Actions for / })).toBeNull();
  });

  it('ends with New form, a dashed card, only where forms can be added', async () => {
    const onadd = vi.fn();
    const { unmount } = renderCards({ forms, selected: 'apply', onadd });

    const add = within(group()).getByRole('button', { name: 'New form' });
    expect(within(group()).getAllByRole('button').at(-1)).toBe(add);
    expect(add).toHaveClass('border-dashed');
    await fireEvent.click(add);
    expect(onadd).toHaveBeenCalledOnce();
    unmount();

    renderCards({ forms, selected: 'apply' });
    expect(within(group()).queryByRole('button', { name: 'New form' })).toBeNull();
  });
});
