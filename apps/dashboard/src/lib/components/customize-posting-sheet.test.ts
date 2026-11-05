import type { PostingCustomization } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, jobId } from '../../test-support/fixtures.js';
import { editRichText } from '../../test-support/rich-text.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import CustomizePostingSheet from './customize-posting-sheet.svelte';

const customizationUrl = `/v1/jobs/${jobId}/postings/adzuna/customization`;
const jobDescription = '<p>Short.</p>';

function customization(overrides: Partial<PostingCustomization> = {}): PostingCustomization {
  return {
    boardId: 'adzuna',
    boardName: 'Adzuna feed',
    title: { override: null, job: 'Senior .NET Engineer' },
    description: { override: null, job: jobDescription },
    changes: [],
    ...overrides,
  };
}

function renderSheet(answer: PostingCustomization, routes: Parameters<typeof apiStub>[0] = {}) {
  const fetchMock = vi.fn(apiStub({ [`GET ${customizationUrl}`]: () => Response.json(answer), ...routes }));
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: CustomizePostingSheet, props: { open: true, jobId, boardId: 'adzuna', boardName: 'Adzuna feed' } } });
  return fetchMock;
}

const openSheet = async () => {
  const sheet = await screen.findByRole('dialog', { name: 'Customize for Adzuna feed' });
  await within(sheet).findByLabelText('Title');
  return sheet;
};

const bodyOf = (fetchMock: ReturnType<typeof vi.fn>, method: string, url: string) => {
  const call = fetchMock.mock.calls.find(([input, init]) => String(input) === url && (init as RequestInit | undefined)?.method === method);
  return call ? JSON.parse(String((call[1] as RequestInit).body)) : undefined;
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('customize posting sheet', () => {
  it("starts with the job's text where the board follows the job, and offers no way back to it", async () => {
    renderSheet(customization());
    const sheet = await openSheet();

    expect(within(sheet).getByLabelText('Title')).toHaveValue('Senior .NET Engineer');
    expect(within(sheet).getByRole('textbox', { name: 'Description' })).toHaveTextContent('Short.');
    expect(within(sheet).queryByRole('button', { name: "Use the job's title" })).toBeNull();
    expect(within(sheet).queryByRole('button', { name: "Use the job's description" })).toBeNull();
    await expectNoAxeViolations();
  });

  it("starts with the board's own text, and each field goes back to the job's on its own", async () => {
    renderSheet(customization({ title: { override: 'Senior engineer (remote)', job: 'Senior .NET Engineer' } }));
    const sheet = await openSheet();

    expect(within(sheet).getByLabelText('Title')).toHaveValue('Senior engineer (remote)');
    expect(within(sheet).queryByRole('button', { name: "Use the job's description" })).toBeNull();

    await fireEvent.click(within(sheet).getByRole('button', { name: "Use the job's title" }));

    expect(within(sheet).getByLabelText('Title')).toHaveValue('Senior .NET Engineer');
    expect(within(sheet).queryByRole('button', { name: "Use the job's title" })).toBeNull();
  });

  it("offers the job's description again after the board's was changed", async () => {
    renderSheet(customization());
    const sheet = await openSheet();

    editRichText(within(sheet).getByRole('textbox', { name: 'Description' }), '<p>A much longer description for this board.</p>');
    await fireEvent.click(await within(sheet).findByRole('button', { name: "Use the job's description" }));

    await waitFor(() => expect(within(sheet).getByRole('textbox', { name: 'Description' })).toHaveTextContent('Short.'));
  });

  it('shows what the job changed since the board was customized', async () => {
    renderSheet(
      customization({ description: { override: '<p>Our own.</p>', job: '<p>New.</p>' }, changes: [{ field: 'Description', before: 'Old', after: 'New' }] }),
    );
    const sheet = await openSheet();

    const changes = sheet.querySelector('[data-customize-changes]')!;
    expect(changes).toHaveTextContent('What changed in the job');
    expect(changes).toHaveTextContent('Description');
    expect(changes).toHaveTextContent('Old');
    expect(changes).toHaveTextContent('New');
  });

  it('previews what the board receives, with what its checks say', async () => {
    const fetchMock = renderSheet(customization(), {
      [`POST ${customizationUrl}/preview`]: () =>
        Response.json({ title: 'Senior .NET Engineer', description: jobDescription, issues: ['Adzuna needs a description of at least 100 characters.'] }),
    });
    const sheet = await openSheet();

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Preview' }));

    await waitFor(() => expect(sheet.querySelector('[data-preview-title]')).toHaveTextContent('Senior .NET Engineer'));
    expect(within(sheet).getByText('Adzuna needs a description of at least 100 characters.')).toBeInTheDocument();
    expect(bodyOf(fetchMock, 'POST', `${customizationUrl}/preview`)).toEqual({ title: 'Senior .NET Engineer', description: jobDescription });
  });

  it('saves the text and posts to this board, then closes', async () => {
    const fetchMock = renderSheet(customization(), { [`PUT ${customizationUrl}`]: () => Response.json({}) });
    const sheet = await openSheet();
    const longer = '<p>A description that is long enough for every board to take it.</p>';
    editRichText(within(sheet).getByRole('textbox', { name: 'Description' }), longer);

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save and post' }));

    await waitFor(() => expect(bodyOf(fetchMock, 'PUT', customizationUrl)).toEqual({ title: 'Senior .NET Engineer', description: longer }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it("keeps the sheet and the person's text open, and says what the board's checks found, when one fails", async () => {
    renderSheet(customization(), {
      [`PUT ${customizationUrl}`]: () =>
        Response.json({ title: 'One or more validation errors occurred.', errors: { board: ['Adzuna needs a description of at least 100 characters.'] } }, { status: 400 }),
    });
    const sheet = await openSheet();
    editRichText(within(sheet).getByRole('textbox', { name: 'Description' }), '<p>Still short.</p>');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save and post' }));

    expect(await within(sheet).findByText('Adzuna needs a description of at least 100 characters.')).toBeInTheDocument();
    expect(within(sheet).getByRole('textbox', { name: 'Description' })).toHaveTextContent('Still short.');
  });
});
