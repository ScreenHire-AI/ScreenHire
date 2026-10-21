import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { beforeNavigate } from '$app/navigation';
import LeaveDialog from './leave-dialog.svelte';

type BeforeNavigate = Parameters<Parameters<typeof beforeNavigate>[0]>[0];

type Guard = (navigation: BeforeNavigate) => void;
const navigation = vi.hoisted(() => ({ guard: null as Guard | null, goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({
  ...(await importOriginal()),
  beforeNavigate: (guard: Guard) => {
    navigation.guard = guard;
  },
  goto: navigation.goto,
}));

const to = new URL('/jobs', location.origin);
function navigate(type = 'link') {
  const cancel = vi.fn();
  navigation.guard!({ type, to: { url: to }, cancel } as unknown as BeforeNavigate);
  return cancel;
}

describe('leave dialog', () => {
  beforeEach(() => {
    navigation.goto.mockClear();
  });

  it('lets a page with nothing unsaved go', () => {
    render(LeaveDialog, { props: { unsaved: false, title: 'Leave without saving this draft?' } });

    expect(navigate()).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('holds the navigation and asks in a dialog, then goes on Leave without saving', async () => {
    render(LeaveDialog, { props: { unsaved: true, title: 'Leave without saving this draft?' } });

    expect(navigate()).toHaveBeenCalledOnce();
    const dialog = await screen.findByRole('dialog', { name: 'Leave without saving this draft?' });
    await fireEvent.click(screen.getByRole('button', { name: 'Leave without saving' }));

    expect(navigation.goto).toHaveBeenCalledWith(to);
    await waitFor(() => expect(dialog).not.toBeInTheDocument());
  });

  it('stays on Keep editing', async () => {
    render(LeaveDialog, { props: { unsaved: true, title: 'Leave without saving this draft?' } });

    navigate();
    await fireEvent.click(await screen.findByRole('button', { name: 'Keep editing' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(navigation.goto).not.toHaveBeenCalled();
  });

  it("leaves closing the tab to the browser's own prompt", () => {
    render(LeaveDialog, { props: { unsaved: true, title: 'Leave without saving this draft?' } });

    expect(navigate('leave')).toHaveBeenCalledOnce();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
