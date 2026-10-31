import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import Harness from '../../test-support/country-picker-harness.svelte';

const field = () => screen.getByRole('combobox', { name: 'Countries candidates can work from' });
const value = () => screen.getByTestId('value').textContent;

async function search(text: string) {
  await fireEvent.keyDown(field(), { key: text.at(-1) });
  await fireEvent.input(field(), { target: { value: text } });
  return screen.findByRole('listbox');
}

describe('country picker', () => {
  it('finds a country by name and adds it with Enter, without submitting the form', async () => {
    render(Harness);
    const list = await search('irel');
    expect(within(list).getAllByRole('option').map((option) => option.textContent?.trim())).toEqual(['Ireland']);

    await fireEvent.keyDown(field(), { key: 'Enter' });

    await waitFor(() => expect(value()).toBe('IE'));
    expect(screen.getByTestId('submits').textContent).toBe('0');
    expect(within(screen.getByRole('list', { name: 'Chosen countries' })).getByText('Ireland')).toBeInTheDocument();
    expect(field()).toHaveValue('');
    expect(screen.getByRole('status')).toHaveTextContent('Ireland added. 1 country chosen.');
  });

  it('moves through the matches with the arrow keys and keeps the list open for the next country', async () => {
    render(Harness);
    await search('guinea');
    await fireEvent.keyDown(field(), { key: 'ArrowDown' });
    await fireEvent.keyDown(field(), { key: 'Enter' });
    // Equatorial Guinea comes first, so one step down is Guinea.
    await waitFor(() => expect(value()).toBe('GN'));

    const list = await search('united');
    expect(within(list).getByRole('option', { name: 'United Kingdom' })).toBeInTheDocument();
  });

  it('shows the countries it starts with as pills, each removed by its own button', async () => {
    render(Harness, { props: { value: ['GB', 'IE', 'FR'] } });
    const pills = screen.getByRole('list', { name: 'Chosen countries' });
    expect(within(pills).getAllByRole('listitem').map((item) => item.textContent?.trim())).toEqual(['United Kingdom', 'Ireland', 'France']);

    await fireEvent.click(within(pills).getByRole('button', { name: 'Remove Ireland' }));

    expect(value()).toBe('GB,FR');
    await waitFor(() => expect(within(pills).getByRole('button', { name: 'Remove France' })).toHaveFocus());
    expect(screen.getByRole('status')).toHaveTextContent('Ireland removed. 2 countries chosen.');
  });

  it('puts focus back in the field when the last pill goes', async () => {
    render(Harness, { props: { value: ['GB'] } });
    await fireEvent.click(screen.getByRole('button', { name: 'Remove United Kingdom' }));

    expect(value()).toBe('');
    expect(screen.queryByRole('list', { name: 'Chosen countries' })).toBeNull();
    await waitFor(() => expect(field()).toHaveFocus());
  });

  it('marks chosen countries in the list and says when nothing matches', async () => {
    render(Harness, { props: { value: ['IE'] } });
    const list = await search('ireland');
    expect(within(list).getByRole('option', { name: 'Ireland' })).toHaveAttribute('aria-selected', 'true');

    await search('atlantis');
    expect(await screen.findByText('No country matches.')).toBeInTheDocument();
  });

  it('passes axe with the list open', async () => {
    render(Harness, { props: { value: ['GB'] } });
    await search('fra');
    await expectNoAxeViolations(document.body);
  });
});
