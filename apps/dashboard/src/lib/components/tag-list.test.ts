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
