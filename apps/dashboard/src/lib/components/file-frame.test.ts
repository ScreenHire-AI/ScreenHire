import type { ApplicationFileSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import FileFrame from './file-frame.svelte';

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
    scanStatus: 'Clean',
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
  vi.restoreAllMocks();
});

describe('file frame', () => {
  it('draws a PDF in a sandboxed frame that gets the bytes and reports its size', async () => {
    const fetch = serveFile();
    const file = resume();
    render(FileFrame, { props: { applicationId, file } });

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
    fromFrame({ type: 'size', height: 900 }, window);
    fromFrame({ type: 'size', height: -1 });
    expect(frame().style.height).not.toBe('900px');

    fromFrame({ type: 'size', height: 1526 });
    fromFrame({ type: 'rendered', pageCount: 2 });
    await waitFor(() => expect(frame().style.height).toBe('1526px'));
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('says so when the frame cannot read the file', async () => {
    serveFile();
    render(FileFrame, { props: { applicationId, file: resume() } });
    await waitFor(() => expect(frame()).toBeInTheDocument());

    fromFrame({ type: 'failed' });

    expect(await screen.findByText('Preview unavailable for this file.')).toBeInTheDocument();
    expect(document.querySelector('iframe')).not.toBeInTheDocument();
  });

  it('offers to try again when the file does not load', async () => {
    const fetch = serveFile(409);
    render(FileFrame, { props: { applicationId, file: resume() } });

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the preview.');
    await fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
  });
});
