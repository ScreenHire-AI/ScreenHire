import type { ApplicationFileSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import ResumePreview from './resume-preview.svelte';

// The real viewer (pdf.js in a sandboxed document) is built by file-viewer-plugin.ts and checked in
// viewer-document.test.ts; jsdom cannot run it, so the frame's side of the conversation is played by the test.
vi.mock('virtual:file-viewer', () => ({ viewerDocument: '<!doctype html><title>viewer</title>' }));

const applicationId = '0198a3c4-0000-7000-8000-000000000001';
const downloadUrl = (fileId: string) => `/v1/applications/${applicationId}/files/${fileId}`;

function resume(overrides: Partial<ApplicationFileSummary> = {}): ApplicationFileSummary {
  return {
    id: '0198a3c4-0000-7000-8000-0000000000f1',
    questionId: 'resume',
    uploadId: '0198a3c4-0000-7000-8000-0000000000a1',
    fileName: 'avery-example-resume.pdf',
    contentType: 'application/pdf',
    sizeBytes: 48_200,
    scanStatus: 'NotScanned',
    uploadedAt: '2026-10-20T09:00:00Z',
    ...overrides,
  };
}

function serveFile(status = 200) {
  const fetch = vi.fn(async () => new Response(status === 200 ? new Uint8Array([0x25, 0x50, 0x44, 0x46]) : null, { status }));
  vi.stubGlobal('fetch', fetch);
  return fetch;
}

const frame = () => document.querySelector('iframe')!;
const fromFrame = (data: unknown, source: Window | null = frame().contentWindow) =>
  window.dispatchEvent(new MessageEvent('message', { data, source }));

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('resume preview', () => {
  it('shows a PDF in a sandboxed frame that gets the bytes and reports its page count', async () => {
    const fetch = serveFile();
    const file = resume({ scanStatus: 'Clean' });
    render(ResumePreview, { props: { applicationId, file } });

    expect(screen.getByText('avery-example-resume.pdf')).toBeInTheDocument();
    expect(screen.getByText('48 kB')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download avery-example-resume.pdf' })).toHaveAttribute('href', downloadUrl(file.id));
    expect(screen.getByRole('status')).toHaveTextContent('Loading the preview…');

    await waitFor(() => expect(frame()).toBeInTheDocument());
    // allow-scripts alone: with allow-same-origin a srcdoc frame would share the dashboard's origin.
    expect(frame()).toHaveAttribute('sandbox', 'allow-scripts');
    expect(frame()).toHaveAttribute('srcdoc', '<!doctype html><title>viewer</title>');
    expect(frame()).toHaveAttribute('title', 'Preview of avery-example-resume.pdf');
    expect(fetch).toHaveBeenCalledWith(downloadUrl(file.id), expect.anything());

    const post = vi.spyOn(frame().contentWindow!, 'postMessage');
    fromFrame({ type: 'ready' });
    await waitFor(() => expect(post).toHaveBeenCalledOnce());
    const [message, target] = post.mock.calls[0]!;
    expect(message).toMatchObject({ type: 'render' });
    expect(new Uint8Array((message as { bytes: ArrayBuffer }).bytes)).toEqual(new Uint8Array([0x25, 0x50, 0x44, 0x46]));
    expect(target).toBe('*');

    // Only the frame is heard, and only in the shapes it may send.
    fromFrame({ type: 'rendered', pageCount: 9 }, window);
    fromFrame({ type: 'rendered', pageCount: '<b>9</b>' });
    fromFrame({ type: 'size', height: -1 });
    expect(screen.queryByText(/pages?$/)).not.toBeInTheDocument();

    fromFrame({ type: 'size', height: 1526 });
    fromFrame({ type: 'rendered', pageCount: 2 });
    expect(await screen.findByText('2 pages')).toBeInTheDocument();
    expect(frame().style.height).toBe('1526px');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('says so when the frame cannot read the file', async () => {
    serveFile();
    render(ResumePreview, { props: { applicationId, file: resume() } });
    await waitFor(() => expect(frame()).toBeInTheDocument());

    fromFrame({ type: 'failed' });

    expect(await screen.findByText('Preview unavailable for this file.')).toBeInTheDocument();
    expect(document.querySelector('iframe')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download avery-example-resume.pdf' })).toBeInTheDocument();
  });

  it('offers to try again when the file does not load', async () => {
    const fetch = serveFile(409);
    render(ResumePreview, { props: { applicationId, file: resume() } });

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the preview.');
    await fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
  });

  it('has no preview for a type it does not draw, and still offers the file', () => {
    const fetch = serveFile();
    const file = resume({
      fileName: 'avery-example-resume.docx',
      contentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    });
    render(ResumePreview, { props: { applicationId, file } });

    expect(screen.getByText('No preview for this file type.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download avery-example-resume.docx' })).toHaveAttribute('href', downloadUrl(file.id));
    expect(document.querySelector('iframe')).not.toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
  });

  it('holds the whole file name in a tooltip that opens on focus, for a name cut short', async () => {
    serveFile();
    render(ResumePreview, { props: { applicationId, file: resume({ scanStatus: 'Infected' }) } });

    const name = screen.getByText('avery-example-resume.pdf');
    expect(name).toHaveAttribute('tabindex', '0');
    name.focus();
    // jsdom has no layout, so the positioned tooltip stays invisible here; its state and text are what count.
    await waitFor(() => expect(name).toHaveAttribute('data-state', expect.stringMatching(/open/)));
    expect(document.getElementById(name.getAttribute('aria-describedby')!)).toHaveTextContent('avery-example-resume.pdf');
  });

  it.each([
    ['Pending', 'Being scanned for malware'],
    ['Infected', 'Withheld: malware found'],
  ] as const)('shows neither a preview nor Download for a %s file, and says why', (scanStatus, reason) => {
    const fetch = serveFile();
    render(ResumePreview, { props: { applicationId, file: resume({ scanStatus }) } });

    expect(screen.getByText(reason)).toBeInTheDocument();
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
    expect(screen.queryByText('No preview for this file type.')).not.toBeInTheDocument();
    expect(document.querySelector('iframe')).not.toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
  });
});
