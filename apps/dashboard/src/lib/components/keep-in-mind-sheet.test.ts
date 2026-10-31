import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import KeepInMindSheet from './keep-in-mind-sheet.svelte';

afterEach(() => {
  vi.unstubAllGlobals();
});

const ids = ['01a157d4-0000-7000-8000-000000000101', '01a157d4-0000-7000-8000-000000000102'];

function renderSheet(
  props: { applicationIds?: string[]; candidateName?: string; until?: string | null; ondone?: (message: string) => void },
  route: (init: RequestInit | undefined) => Response = () => Response.json({ kept: 1, notAllowed: 0, alreadyEnded: 0 }),
) {
  const post = vi.fn(route);
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'POST /v1/talent-pool': post })));
  render(WithQueryClient, { props: { component: KeepInMindSheet, props: { open: true, applicationIds: ids.slice(0, 1), ...props } } });
  return post;
}

const bodyOf = (post: ReturnType<typeof vi.fn>) => JSON.parse(String(post.mock.calls[0]![0].body));

describe('keep in mind sheet', () => {
  it('names the one candidate and says they leave without a rejection and stay until their permission ends', async () => {
    renderSheet({ candidateName: 'Casey Morgan', until: '2027-03-15T12:00:00Z' });
    const sheet = await screen.findByRole('dialog', { name: 'Keep Casey Morgan in mind' });

    expect(sheet).toHaveTextContent('They leave this job without a rejection and stay in the talent pool until Mar 15, 2027.');
    expect(within(sheet).getByLabelText('Why')).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('counts the selection and says only those who allowed it join, when it keeps several', async () => {
    renderSheet({ applicationIds: ids });
    const sheet = await screen.findByRole('dialog', { name: 'Keep 2 in mind' });

    expect(sheet).toHaveTextContent('Only candidates who allowed it join the talent pool.');
  });

  it('posts the application ids and the trimmed note, then reports the result and closes', async () => {
    const ondone = vi.fn();
    const post = renderSheet({ applicationIds: ids.slice(0, 1), candidateName: 'Casey Morgan', until: '2027-03-15T12:00:00Z', ondone });
    const sheet = await screen.findByRole('dialog', { name: 'Keep Casey Morgan in mind' });

    await fireEvent.input(within(sheet).getByLabelText('Why'), { target: { value: '  Strong systems thinking.  ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Keep in mind' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(bodyOf(post)).toEqual({ applicationIds: [ids[0]], note: 'Strong systems thinking.' });
    await waitFor(() => expect(ondone).toHaveBeenCalledWith('Kept Casey Morgan in mind.'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('sends a null note when the Why box is empty or blank', async () => {
    const post = renderSheet({ candidateName: 'Casey Morgan' });
    const sheet = await screen.findByRole('dialog');

    await fireEvent.input(within(sheet).getByLabelText('Why'), { target: { value: '   ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Keep in mind' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(bodyOf(post)).toEqual({ applicationIds: [ids[0]], note: null });
  });

  it('reports how many were kept, who did not allow it and who had already left the job', async () => {
    const ondone = vi.fn();
    const post = renderSheet({ applicationIds: ids, ondone }, () => Response.json({ kept: 1, notAllowed: 1, alreadyEnded: 3 }));
    const sheet = await screen.findByRole('dialog', { name: 'Keep 2 in mind' });

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Keep in mind' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(bodyOf(post)).toEqual({ applicationIds: ids, note: null });
    await waitFor(() => expect(ondone).toHaveBeenCalledWith('Kept 1 in mind. 1 did not allow it. 3 had already left the job.'));
  });

  it('stays open and says why when the API refuses', async () => {
    const ondone = vi.fn();
    renderSheet({ candidateName: 'Casey Morgan', ondone }, () => Response.json({ detail: 'Someone else moved them.' }, { status: 409 }));
    const sheet = await screen.findByRole('dialog');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Keep in mind' }));

    await waitFor(() => expect(within(sheet).getByRole('alert')).toHaveTextContent('Someone else moved them.'));
    expect(ondone).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('does not post when cancelled', async () => {
    const post = renderSheet({ candidateName: 'Casey Morgan' });
    const sheet = await screen.findByRole('dialog');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(post).not.toHaveBeenCalled();
  });
});
