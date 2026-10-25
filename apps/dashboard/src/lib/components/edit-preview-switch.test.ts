import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/svelte';
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

  it('opens the preview in a named second window, carrying its own channel', async () => {
    render(PreviewHarness);

    await fireEvent.click(screen.getByRole('button', { name: 'Open in a new window' }));

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
    await fireEvent.click(screen.getByRole('button', { name: 'Open in a new window' }));

    // The window starts after the editor: it asks for the current value and gets it.
    render(PreviewSinkHarness, { props: { url: windowUrl() } });
    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('What is your notice period?'));

    await fireEvent.input(screen.getByLabelText('Question'), { target: { value: 'When could you start?' } });

    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('When could you start?'));
  });

  it('tells the window when the editor goes away', async () => {
    const editor = render(PreviewHarness);
    await fireEvent.click(screen.getByRole('button', { name: 'Open in a new window' }));
    render(PreviewSinkHarness, { props: { url: windowUrl() } });
    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('What is your notice period?'));

    editor.unmount();

    expect(await screen.findByText('Editor closed')).toBeInTheDocument();
  });

  it('never crosses two editors: each window follows only its own', async () => {
    render(PreviewHarness);
    render(PreviewHarness);
    const [first, second] = screen.getAllByRole('button', { name: 'Open in a new window' });
    await fireEvent.click(first!);
    const firstUrl = windowUrl();
    await fireEvent.click(second!);
    render(PreviewSinkHarness, { props: { url: firstUrl } });
    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('What is your notice period?'));

    await fireEvent.input(screen.getAllByLabelText('Question')[1]!, { target: { value: 'Second editor' } });
    await fireEvent.input(screen.getAllByLabelText('Question')[0]!, { target: { value: 'First editor' } });

    await waitFor(() => expect(screen.getByTestId('window')).toHaveTextContent('First editor'));
    expect(firstUrl.searchParams.get('channel')).not.toBe(windowUrl().searchParams.get('channel'));
  });
});
