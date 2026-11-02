import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import SaveCandidateSheet from './save-candidate-sheet.svelte';

afterEach(() => {
  vi.unstubAllGlobals();
});

const ids = ['01a157d4-0000-7000-8000-000000000101', '01a157d4-0000-7000-8000-000000000102'];

function renderSheet(
  props: { applicationIds?: string[]; candidateName?: string; ondone?: (message: string) => void },
  route: (init: RequestInit | undefined) => Response = () => Response.json({ kept: 1, notAllowed: 0, alreadyEnded: 0 }),
) {
  const post = vi.fn(route);
  vi.stubGlobal('fetch', vi.fn(apiStub({ 'POST /v1/talent-pool': post })));
  render(WithQueryClient, { props: { component: SaveCandidateSheet, props: { open: true, applicationIds: ids.slice(0, 1), ...props } } });
  return post;
}

const bodyOf = (post: ReturnType<typeof vi.fn>) => JSON.parse(String(post.mock.calls[0]![0].body));

describe('save candidate sheet', () => {
  it('names the one candidate and says they leave without a rejection and stay saved until removed or they ask to stop', async () => {
    renderSheet({ candidateName: 'Casey Morgan' });
    const sheet = await screen.findByRole('dialog', { name: 'Save Casey Morgan' });

    expect(sheet).toHaveTextContent('They leave this job without a rejection and stay saved until you remove them or they ask to stop.');
    expect(within(sheet).getByLabelText('Note')).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('counts the selection and says only those who allowed it are saved, when it saves several', async () => {
    renderSheet({ applicationIds: ids });
    const sheet = await screen.findByRole('dialog', { name: 'Save 2 candidates' });

    expect(sheet).toHaveTextContent('Only candidates who allowed it are saved.');
  });

  it('posts the application ids and the trimmed note, then reports the result and closes', async () => {
    const ondone = vi.fn();
    const post = renderSheet({ applicationIds: ids.slice(0, 1), candidateName: 'Casey Morgan', ondone });
    const sheet = await screen.findByRole('dialog', { name: 'Save Casey Morgan' });

    await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: '  Strong systems thinking.  ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save candidate' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(bodyOf(post)).toEqual({ applicationIds: [ids[0]], note: 'Strong systems thinking.' });
    await waitFor(() => expect(ondone).toHaveBeenCalledWith('Saved Casey Morgan.'));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
  });

  it('sends a null note when the Note box is empty or blank', async () => {
    const post = renderSheet({ candidateName: 'Casey Morgan' });
    const sheet = await screen.findByRole('dialog');

    await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: '   ' } });
    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save candidate' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(bodyOf(post)).toEqual({ applicationIds: [ids[0]], note: null });
  });

  it('reports how many were saved, who did not allow it and who had already left the job', async () => {
    const ondone = vi.fn();
    const post = renderSheet({ applicationIds: ids, ondone }, () => Response.json({ kept: 1, notAllowed: 1, alreadyEnded: 3 }));
    const sheet = await screen.findByRole('dialog', { name: 'Save 2 candidates' });

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save candidate' }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(bodyOf(post)).toEqual({ applicationIds: ids, note: null });
    await waitFor(() => expect(ondone).toHaveBeenCalledWith('Saved 1. 1 did not allow it. 3 had already left the job.'));
  });

  it('stays open and says why when the API refuses', async () => {
    const ondone = vi.fn();
    renderSheet({ candidateName: 'Casey Morgan', ondone }, () => Response.json({ detail: 'Someone else moved them.' }, { status: 409 }));
    const sheet = await screen.findByRole('dialog');

    await fireEvent.click(within(sheet).getByRole('button', { name: 'Save candidate' }));

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
