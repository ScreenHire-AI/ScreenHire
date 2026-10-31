import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import CodeBlock from './code-block.svelte';

afterEach(() => vi.unstubAllGlobals());

describe('CodeBlock', () => {
  it('copies the code and says so', async () => {
    const writeText = vi.fn(async () => {});
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText } });
    render(CodeBlock, { props: { code: '<script src="https://careers.example/embed.js"></script>', label: 'Embed code' } });

    expect(screen.getByLabelText('Embed code')).toHaveTextContent('<script src="https://careers.example/embed.js"></script>');
    await fireEvent.click(screen.getByRole('button', { name: 'Copy' }));

    expect(writeText).toHaveBeenCalledWith('<script src="https://careers.example/embed.js"></script>');
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Embed code copied');
  });

  it('selects the code for a manual copy when there is no clipboard', async () => {
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: vi.fn(async () => Promise.reject(new Error('denied'))) } });
    render(CodeBlock, { props: { code: 'npm run build', label: 'Command' } });

    await fireEvent.click(screen.getByRole('button', { name: 'Copy' }));

    await vi.waitFor(() => expect(window.getSelection()?.toString()).toBe('npm run build'));
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument();
  });
});
