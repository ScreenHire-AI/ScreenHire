import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import TagList from './tag-list.svelte';

const show = (props: Record<string, unknown>) => render(WithTooltips, { props: { component: TagList, props } });

describe('tag list, compact (a candidate header)', () => {
  it('shows each tag with its own Remove button, which is hidden until hover, focus or tap but stays in the tab order (F4)', () => {
    show({ compact: true, tags: ['Referral', 'Night shift'], onadd: async () => true, onremove: () => {} });

    const list = screen.getByRole('list', { name: 'Tags' });
    expect(within(list).getAllByRole('listitem')).toHaveLength(2);
    const remove = screen.getByRole('button', { name: 'Remove tag Referral' });
    expect(remove).toHaveClass('opacity-0');
    expect(remove).toHaveClass('focus-visible:opacity-100');
    expect(remove).toHaveClass('pointer-coarse:opacity-100');
    // The button sits beside the pill, never inside it (A5).
    expect(remove.closest('[data-slot="badge"]')).toBeNull();
  });

  it('removes a tag with its button', async () => {
    const onremove = vi.fn();
    show({ compact: true, tags: ['Referral'], onremove });

    await fireEvent.click(screen.getByRole('button', { name: 'Remove tag Referral' }));
    expect(onremove).toHaveBeenCalledWith('Referral');
  });

  it('opens the field in place from Add tag, adds on submit, and closes', async () => {
    const onadd = vi.fn(async () => true);
    show({ compact: true, tags: [], onadd });

    expect(screen.queryByRole('textbox')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Add tag' }));
    const field = screen.getByRole('textbox', { name: 'New tag' });
    expect(field).toHaveFocus();
    await fireEvent.input(field, { target: { value: ' Referral ' } });
    await fireEvent.submit(field.closest('form')!);

    expect(onadd).toHaveBeenCalledWith('Referral');
    await vi.waitFor(() => expect(screen.queryByRole('textbox')).toBeNull());
    expect(screen.getByRole('button', { name: 'Add tag' })).toBeInTheDocument();
  });

  it('closes the field on Escape and says to name the tag first when submitted empty', async () => {
    const onadd = vi.fn(async () => true);
    show({ compact: true, tags: [], onadd });

    await fireEvent.click(screen.getByRole('button', { name: 'Add tag' }));
    await fireEvent.submit(screen.getByRole('textbox', { name: 'New tag' }).closest('form')!);
    expect(screen.getByText('Name the tag first.')).toBeInTheDocument();
    expect(onadd).not.toHaveBeenCalled();

    await fireEvent.keyDown(screen.getByRole('textbox', { name: 'New tag' }), { key: 'Escape' });
    expect(screen.queryByRole('textbox')).toBeNull();
  });

  it('says nothing for a viewer with no tags, and shows the pills only for a viewer with some', () => {
    const { unmount } = show({ compact: true, tags: [] });
    expect(screen.queryByText(/No tags yet/)).toBeNull();
    expect(screen.queryByRole('button')).toBeNull();
    unmount();

    show({ compact: true, tags: ['Referral'] });
    expect(screen.getByText('Referral')).toBeInTheDocument();
    expect(screen.queryByRole('button')).toBeNull();
  });
});

describe('tag list, suggesting the tags the organization has', () => {
  const suggestions = [
    { name: 'Senior engineer', applications: 9 },
    { name: 'Senior', applications: 4 },
    { name: 'Remote', applications: 7 },
  ];

  async function typeIn(text: string) {
    await fireEvent.click(screen.getByRole('button', { name: 'Add tag' }));
    const field = screen.getByRole('combobox', { name: 'New tag' });
    await fireEvent.input(field, { target: { value: text } });
    return field;
  }

  it('lists the matching tags most used first with Create last, and Enter picks the top one', async () => {
    const onadd = vi.fn(async () => true);
    show({ compact: true, tags: ['Remote'], onadd, suggestions });

    const field = await typeIn('sen');
    const options = within(screen.getByRole('listbox', { name: 'Suggested tags' })).getAllByRole('option');
    expect(options.map((option) => option.textContent?.trim())).toEqual(['Senior engineer', 'Senior', 'Create "sen"']);
    expect(field).toHaveAttribute('aria-expanded', 'true');
    expect(field).toHaveAttribute('aria-activedescendant', options[0]!.id);

    await fireEvent.submit(field.closest('form')!);
    expect(onadd).toHaveBeenCalledWith('Senior engineer');
  });

  it('reuses the existing tag for text in another case, and the arrow keys reach Create', async () => {
    const onadd = vi.fn(async () => true);
    show({ compact: true, tags: [], onadd, suggestions });

    const field = await typeIn('SENIOR');
    expect(screen.queryByRole('option', { name: /Create/ })).toBeNull();
    await fireEvent.submit(field.closest('form')!);
    expect(onadd).toHaveBeenLastCalledWith('Senior');

    const again = await typeIn('Night shift');
    await fireEvent.keyDown(again, { key: 'ArrowUp' });
    expect(again).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Create "Night shift"' }).id);
    await fireEvent.submit(again.closest('form')!);
    expect(onadd).toHaveBeenLastCalledWith('Night shift');
  });

  it('opens nothing on an empty field, so Escape closes it, and the down arrow there shows the most used', async () => {
    show({ compact: true, tags: [], onadd: async () => true, suggestions });

    await fireEvent.click(screen.getByRole('button', { name: 'Add tag' }));
    const field = screen.getByRole('combobox', { name: 'New tag' });
    expect(screen.queryByRole('listbox')).toBeNull();
    await fireEvent.keyDown(field, { key: 'ArrowDown' });
    expect(within(screen.getByRole('listbox', { name: 'Suggested tags' })).getAllByRole('option').map((option) => option.textContent?.trim())).toEqual([
      'Senior engineer',
      'Remote',
      'Senior',
    ]);
    await fireEvent.keyDown(field, { key: 'Escape' });
    await fireEvent.keyDown(field, { key: 'Escape' });
    expect(screen.queryByRole('combobox')).toBeNull();
  });

  it('closes the list on Escape and keeps the field open, and a pick by pointer adds it', async () => {
    const onadd = vi.fn(async () => true);
    show({ compact: true, tags: [], onadd, suggestions });

    const field = await typeIn('rem');
    await fireEvent.keyDown(field, { key: 'Escape' });
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(field).toBeInTheDocument();

    await fireEvent.input(field, { target: { value: 're' } });
    await fireEvent.mouseDown(screen.getByRole('option', { name: 'Remote' }));
    expect(onadd).toHaveBeenCalledWith('Remote');
  });

  it('picks the last option still listed when a live update shrinks the list under the highlight', async () => {
    const onadd = vi.fn(async () => true);
    // Rendered without the tooltip harness, so `rerender` reaches the list's own props; with no tags there is no tooltip.
    const { rerender } = render(TagList, { props: { compact: true, tags: [], onadd, suggestions } });

    const field = await typeIn('sen');
    await fireEvent.keyDown(field, { key: 'ArrowUp' });
    expect(screen.getByRole('option', { name: 'Create "sen"' })).toHaveAttribute('aria-selected', 'true');

    // Another member renames both Senior tags away; the refetched list has only Create left.
    await rerender({ suggestions: [{ name: 'Remote', applications: 7 }] });
    expect(screen.getAllByRole('option').map((option) => option.textContent?.trim())).toEqual(['Create "sen"']);
    expect(field).toHaveAttribute('aria-activedescendant', screen.getByRole('option').id);
    await fireEvent.submit(field.closest('form')!);
    expect(onadd).toHaveBeenCalledWith('sen');
  });
});
