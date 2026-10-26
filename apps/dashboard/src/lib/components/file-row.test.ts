import type { ApplicationFileSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import FileRow from './file-row.svelte';

// The real viewer (pdf.js in a sandboxed document) is checked in viewer-document.test.ts; jsdom cannot run it, so the
// frame's side of the conversation is played by the test.
vi.mock('virtual:file-viewer', () => ({ viewerDocument: '<!doctype html><title>viewer</title>' }));

const applicationId = '0198a3c4-0000-7000-8000-000000000001';

function upload(overrides: Partial<ApplicationFileSummary> = {}): ApplicationFileSummary {
  return {
    id: '0198a3c4-0000-7000-8000-0000000000f1',
    questionId: 'resume',
    uploadId: '0198a3c4-0000-7000-8000-0000000000a1',
    fileName: 'avery-example-resume.pdf',
    contentType: 'application/pdf',
    sizeBytes: 48_200,
    scanStatus: 'Clean',
    uploadedAt: '2026-10-20T09:00:00Z',
    ...overrides,
  };
}

const renderRow = (file: ApplicationFileSummary) =>
  render(WithTooltips, { props: { component: FileRow, props: { applicationId, file, description: 'Avery Example, Support engineer' } } });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('file row', () => {
  it('shows the type, name, size, View and Download', async () => {
    renderRow(upload());

    expect(screen.getByText('avery-example-resume.pdf')).toBeInTheDocument();
    expect(screen.getByText('48 kB')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'View avery-example-resume.pdf' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download avery-example-resume.pdf' })).toHaveAttribute('href', `/v1/applications/${applicationId}/files/${upload().id}`);
    await expectNoAxeViolations(document.body);
  });

  it('offers only Download for a type the viewer does not draw', () => {
    renderRow(upload({ fileName: 'headshot.png', contentType: 'image/png' }));

    expect(screen.queryByRole('button', { name: /^View/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download headshot.png' })).toBeInTheDocument();
  });

  it('says why a withheld file is not offered, and offers neither View nor Download', () => {
    renderRow(upload({ scanStatus: 'Infected' }));

    expect(screen.getByText('Unsafe file')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^View/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });

  it('opens the file in a full-window viewer with Close and Download, drawn in a sandboxed frame', async () => {
    const fetch = vi.fn(async () => new Response(new Uint8Array([0x25, 0x50, 0x44, 0x46]), { status: 200 }));
    vi.stubGlobal('fetch', fetch);
    renderRow(upload());

    // A click focuses the button in a browser, so the viewer can give the focus back to it.
    screen.getByRole('button', { name: 'View avery-example-resume.pdf' }).focus();
    await fireEvent.click(screen.getByRole('button', { name: 'View avery-example-resume.pdf' }));

    const viewer = await screen.findByRole('dialog', { name: 'avery-example-resume.pdf' });
    expect(viewer).toHaveClass('inset-0', 'h-dvh', 'max-w-none', 'rounded-none');
    expect(within(viewer).getByText('Avery Example, Support engineer')).toBeInTheDocument();
    expect(within(viewer).getByRole('link', { name: 'Download' })).toHaveAttribute('data-sveltekit-reload');
    const frame = await waitFor(() => within(viewer).getByTitle('Preview of avery-example-resume.pdf'));
    expect(frame).toHaveAttribute('sandbox', 'allow-scripts');
    expect(fetch).toHaveBeenCalledWith(`/v1/applications/${applicationId}/files/${upload().id}`, expect.anything());

    // The frame reports its pages; the viewer says how many.
    window.dispatchEvent(new MessageEvent('message', { data: { type: 'rendered', pageCount: 2 }, source: (frame as HTMLIFrameElement).contentWindow }));
    expect(await within(viewer).findByText('2 pages')).toBeInTheDocument();

    await fireEvent.click(within(viewer).getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole('button', { name: 'View avery-example-resume.pdf' })).toHaveFocus());
  });

  it('fetches nothing until the file is viewed', () => {
    const fetch = vi.fn();
    vi.stubGlobal('fetch', fetch);
    renderRow(upload());

    expect(fetch).not.toHaveBeenCalled();
  });
});
