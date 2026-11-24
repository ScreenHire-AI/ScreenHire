import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import PreviewHarness from '../../test-support/preview-harness.svelte';
import PreviewSinkHarness from '../../test-support/preview-sink-harness.svelte';

let open: ReturnType<typeof vi.fn>;
beforeEach(() => {
  open = vi.fn();
  vi.stubGlobal('open', open);
});
// Unmounting closes every BroadcastChannel the editors and windows opened, so none outlives its test.
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

// The window opens from the Preview panel's header, so each editor is on Preview to open it.
async function popOut(index = 0) {
  await fireEvent.click(screen.getAllByRole('button', { name: 'Preview' })[index]!);
  await fireEvent.click(screen.getAllByRole('button', { name: 'Open in a new window' }).at(-1)!);
}
const windowUrl = () => open.mock.calls.at(-1)![0] as URL;

describe('edit / preview switch', () => {
  it('starts on Edit and swaps the editor for the preview in place', async () => {
    render(PreviewHarness);

    expect(screen.getByRole('group', { name: 'View' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByLabelText('Question')).toBeInTheDocument();

    await fireEvent.click(screen.getByRole('button', { name: 'Preview' }));

    expect(screen.getByRole('button', { name: 'Preview' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.queryByLabelText('Question')).not.toBeInTheDocument();
    expect(screen.getByTestId('preview')).toHaveTextContent('What is your notice period?');
    await expectNoAxeViolations(document.body);
  });

  it('holds exactly two choices, and the Preview panel opens the preview in its own window, in words', async () => {
    render(PreviewHarness);
    expect(within(screen.getByRole('group', { name: 'View' })).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Edit', 'Preview']);
    expect(screen.queryByRole('button', { name: 'Open in a new window' })).toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: 'Preview' }));

    const panel = screen.getByRole('region', { name: 'Preview' });
    expect(within(panel).getByText('Open in a new window')).toBeVisible();
    expect(within(screen.getByRole('group', { name: 'View' })).getAllByRole('button')).toHaveLength(2);
  });

  it('opens the preview in a named second window, carrying its own channel', async () => {
    render(PreviewHarness);

    await popOut();

    expect(open).toHaveBeenCalledOnce();
    const [url, name, features] = open.mock.calls[0]!;
    expect((url as URL).pathname).toBe('/preview/form');
    const channel = (url as URL).searchParams.get('channel');
    expect(channel).toMatch(/^[0-9a-f-]{36}$/);
    expect(name).toBe(`preview-${channel}`);
    expect(features).toContain('popup');
  });

  it('keeps the preview window in step with each edit', async () => {
    render(PreviewHarness);
    await popOut();
    await fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

    // The window starts after the editor: it asks for the current value and gets it.
    render(PreviewSinkHarness, { props: { url: windowUrl() } });
    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('What is your notice period?'));

    await fireEvent.input(screen.getByLabelText('Question'), { target: { value: 'When could you start?' } });

    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('When could you start?'));
  });

  it('tells the window when the editor goes away', async () => {
    const editor = render(PreviewHarness);
    await popOut();
    render(PreviewSinkHarness, { props: { url: windowUrl() } });
    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('What is your notice period?'));

    editor.unmount();

    expect(await screen.findByText('Editor closed')).toBeInTheDocument();
  });

  it('never crosses two editors: each window follows only its own', async () => {
    render(PreviewHarness);
    render(PreviewHarness);
    await popOut(0);
    const firstUrl = windowUrl();
    await popOut(1);
    for (const edit of screen.getAllByRole('button', { name: 'Edit' })) await fireEvent.click(edit);
    render(PreviewSinkHarness, { props: { url: firstUrl } });
    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('What is your notice period?'));

    await fireEvent.input(screen.getAllByLabelText('Question')[1]!, { target: { value: 'Second editor' } });
    await fireEvent.input(screen.getAllByLabelText('Question')[0]!, { target: { value: 'First editor' } });

    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('First editor'));
    expect(firstUrl.searchParams.get('channel')).not.toBe(windowUrl().searchParams.get('channel'));
  });
});
