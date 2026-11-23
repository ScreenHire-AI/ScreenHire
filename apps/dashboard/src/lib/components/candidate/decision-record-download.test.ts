// The decision record download on a candidate's Activity tab: one press reads the application's record from the API and
// saves it as a file named for the application, and a refusal says so in one line instead of saving nothing in silence.
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import DecisionRecordDownload from './decision-record-download.svelte';

const applicationId = '01a157d4-0000-7000-8000-000000000601';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

function renderDownload() {
  render(WithQueryClient, { props: { component: DecisionRecordDownload, props: { applicationId } } });
  return screen.getByRole('button', { name: 'Download decision record' });
}

describe('decision record download', () => {
  it('saves the record the API returns as a file named for the application', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET /v1/applications/${applicationId}/decision-record`]: () => Response.json({ applicationId, jobTitle: 'Designer' }) })));
    const saved: { name: string; blob: Blob }[] = [];
    vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => (saved.push({ name: '', blob: blob as Blob }), 'blob:record'));
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      saved[saved.length - 1]!.name = this.download;
    });

    await fireEvent.click(renderDownload());

    await waitFor(() => expect(click).toHaveBeenCalledOnce());
    expect(saved[0]!.name).toBe(`decision-record-${applicationId}.json`);
    expect(JSON.parse(await saved[0]!.blob.text())).toEqual({ applicationId, jobTitle: 'Designer' });
  });

  it('says the record could not be downloaded when the API refuses it', async () => {
    vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET /v1/applications/${applicationId}/decision-record`]: () => new Response(null, { status: 404 }) })));
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click');

    await fireEvent.click(renderDownload());

    // The alert line is always in the page, empty until there is a message (FailureText).
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('The decision record could not be downloaded'));
    expect(click).not.toHaveBeenCalled();
  });
});
