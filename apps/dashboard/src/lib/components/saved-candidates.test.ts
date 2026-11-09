import type { CurrentUser, TalentPoolMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, jobs, user, userAs } from '../../test-support/fixtures.js';
import { editRichText } from '../../test-support/rich-text.js';
import { stubViewport } from '../../test-support/viewport.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import Page from './saved-candidates.svelte';

// Signed in as the Owner unless a test changes it.
const appPage = vi.hoisted(() => ({ url: new URL('http://dashboard.test/candidates?list=saved'), data: {} as Record<string, unknown> }));
vi.mock('$app/state', () => ({ page: appPage, navigating: { to: null } }));
// The Add to a job sheet leaves for the new application's page through goto.
// Superforms (the note sheet) needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const supportJobId = '01a157d4-0000-7000-8000-0000000009aa';

const member = (index: number, patch: Partial<TalentPoolMember>): TalentPoolMember => ({
  candidateId: `01a157d4-0000-7000-8000-0000000004${index.toString().padStart(2, '0')}`,
  candidateName: 'Casey Morgan',
  email: 'casey.morgan@example.com',
  applicationId: `01a157d4-0000-7000-8000-0000000005${index.toString().padStart(2, '0')}`,
  jobId: jobs[1]!.id,
  jobTitle: 'Senior .NET Engineer',
  note: null,
  noteUpdated: null,
  tags: [],
  keptAt: '2026-10-20T12:00:00Z',
  keptByUserId: null,
  rating: { state: 'NotRated' },
  lastContactedAt: null,
  lastInvitedJobId: null,
  lastInvitedJobTitle: null,
  lastInvitedAt: null,
  appliedJobIds: [],
  ...patch,
});

const casey = member(0, {
  note: 'Strong systems thinking, wrong timing.',
  tags: ['Referral'],
  rating: { state: 'Rated', value: 4.2, scorecards: 3 },
  lastContactedAt: '2026-10-28T12:00:00Z',
});
const jordan = member(1, {
  candidateName: 'Jordan Lee',
  email: 'jordan.lee@example.com',
  jobId: supportJobId,
  jobTitle: 'Support Engineer',
  note: 'Great with customers.',
  tags: ['Warm intro', 'Referral'],
  keptAt: '2026-10-22T12:00:00Z',
  rating: { state: 'Rated', value: 4.8, scorecards: 2 },
  lastContactedAt: '2026-10-30T12:00:00Z',
});
const sam = member(2, { candidateName: 'Sam Taylor', email: 'sam.taylor@example.com', note: null, rating: { state: 'AfterYourScorecard' } });
const people = [casey, jordan, sam];

afterEach(() => {
  // The table remembers its sort per browser.
  localStorage.clear();
  appPage.data = {};
  vi.unstubAllGlobals();
});

function renderPage(options: { who?: TalentPoolMember[]; as?: CurrentUser; sending?: boolean; routes?: Parameters<typeof apiStub>[0] } = {}) {
  appPage.data = { user: options.as ?? user };
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/talent-pool': () => Response.json({ people: options.who ?? people }),
      'GET /v1/email-templates': () => Response.json({ sendingEnabled: options.sending ?? true, templates: [], variables: [] }),
      'GET /v1/jobs': () => Response.json(jobs),
      // The list switch's Current and All counts.
      'GET /v1/applications': () => Response.json([]),
      'GET /v1/organization/members': () => Response.json([]),
      ...options.routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return render(WithQueryClient, { props: { component: Page, props: {} } });
}

const panel = () => screen.findByRole('region', { name: 'Candidates' });
const nameLinks = () => screen.queryAllByRole('link', { name: /^(Casey Morgan|Jordan Lee|Sam Taylor)$/ }).map((link) => link.textContent);
const search = (text: string) => fireEvent.input(screen.getByRole('searchbox', { name: 'Search saved candidates' }), { target: { value: text } });

async function chooseFilter(trigger: RegExp, option: RegExp) {
  const filters = (await panel()).querySelector<HTMLElement>('[data-filters]')!;
  await fireEvent.pointerDown(within(filters).getByRole('button', { name: trigger }), { button: 0, pointerType: 'mouse' });
  const menu = await screen.findByRole('menu');
  await fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: option }));
  await fireEvent.keyDown(menu, { key: 'Escape' });
}

async function openRowMenu(name: string) {
  await fireEvent.pointerDown(await screen.findByRole('button', { name: `Actions for ${name}` }), { button: 0, pointerType: 'mouse' });
  return screen.findByRole('menu');
}

describe('saved candidates page', () => {
  it('lists each saved person with their rating, note, tags, the job they were saved from, when, and when they were last contacted', async () => {
    renderPage();
    const table = await screen.findByRole('table', { name: 'Saved candidates' });

    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual([
      '',
      'Candidate',
      'Rating',
      'Note',
      'Tags',
      'Saved from',
      'Saved',
      'Last contacted',
      'Actions',
    ]);
    const row = within(table).getByRole('link', { name: 'Casey Morgan' }).closest('tr')!;
    expect(within(row).getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute('href', `/applications/${casey.applicationId}`);
    expect(row).toHaveTextContent('Strong systems thinking, wrong timing.');
    expect(row).toHaveTextContent('Referral');
    expect(row).toHaveTextContent('Senior .NET Engineer');
    expect(within(row).getByText('Oct 20, 2026')).toHaveAttribute('datetime', casey.keptAt);
    expect(within(row).getByText('Oct 28, 2026')).toHaveAttribute('datetime', casey.lastContactedAt);
    // There is no permission end any more: a saved candidate stays until the team removes them or they ask to stop.
    expect(screen.queryByRole('columnheader', { name: /permission/i })).toBeNull();
    // The list's size is on its choice beside the title, and the panel says it again only when a search or filter narrows it (W7).
    expect(within(screen.getByRole('group', { name: 'List' })).getByRole('link', { name: 'Saved 3' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Candidates' }).querySelector('header [data-slot="badge"]')).toBeNull();
  });

  it('shows the rating from the scorecards, and "After yours" while the reviewer owes their own scorecard', async () => {
    renderPage();
    const table = await screen.findByRole('table', { name: 'Saved candidates' });

    const rated = within(table).getByRole('link', { name: 'Casey Morgan' }).closest('tr')!;
    expect(within(rated).getByText('4.2')).toBeInTheDocument();
    expect(within(rated).getByText('out of 5, from 3 scorecards')).toBeInTheDocument();
    // The number only, no stars (L1).
    expect(rated.querySelector('svg[data-icon="lucide-star"], [class*="fill-current"]')).toBeNull();
    const gated = within(table).getByRole('link', { name: 'Sam Taylor' }).closest('tr')!;
    expect(within(gated).getByText('After yours')).toBeInTheDocument();
    expect(within(gated).queryByText(/\d\.\d/)).toBeNull();
  });

  it('says when someone has never been contacted, and shows a note-less row to a screen reader', async () => {
    renderPage();
    const table = await screen.findByRole('table', { name: 'Saved candidates' });
    const row = within(table).getByRole('link', { name: 'Sam Taylor' }).closest('tr')!;

    expect(within(row).getByText('Never')).toBeInTheDocument();
    expect(within(row).getByText('Never contacted')).toHaveClass('sr-only');
    // No note: the cell shows nothing (L1), and the button that adds one says so to a screen reader only.
    expect(within(row).getByRole('button', { name: /Edit note for Sam Taylor$/ })).toHaveTextContent('Add a note');
    expect(within(row).getByText('Add a note')).toHaveClass('sr-only');
  });

  it('has no Verification column, filter or chip, since the pool is never a list picked by level (ADR-0004)', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });
    const filters = (await panel()).querySelector<HTMLElement>('[data-filters]')!;

    expect(within(filters).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Saved from: All', 'Tag: All']);
    expect(screen.queryByRole('columnheader', { name: /integrity|authenticity|verification/i })).toBeNull();
    expect(document.body.textContent).not.toMatch(/Checks pending|Looks genuine|Authenticity/);
  });

  it('opens by name and sorts by rating, highest first, with people who have no number last', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });
    expect(nameLinks()).toEqual(['Casey Morgan', 'Jordan Lee', 'Sam Taylor']);

    await fireEvent.click(screen.getByRole('button', { name: /^Rating/ }));
    expect(nameLinks()).toEqual(['Jordan Lee', 'Casey Morgan', 'Sam Taylor']);

    await fireEvent.click(screen.getByRole('button', { name: /^Rating/ }));
    expect(nameLinks()).toEqual(['Casey Morgan', 'Jordan Lee', 'Sam Taylor']);
  });

  it('sorts by last contacted, newest first, with people nobody has emailed last', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });

    await fireEvent.click(screen.getByRole('button', { name: /^Last contacted/ }));

    expect(nameLinks()).toEqual(['Jordan Lee', 'Casey Morgan', 'Sam Taylor']);
  });

  it('narrows to what the search matches in the name, email, job or note, and the count follows', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });

    await search('jordan');
    expect(nameLinks()).toEqual(['Jordan Lee']);
    expect(within(screen.getByRole('region', { name: 'Candidates' }).querySelector('header')!).getByText('1', { selector: '[data-slot="badge"]' })).toBeInTheDocument();

    await search('wrong timing');
    expect(nameLinks()).toEqual(['Casey Morgan']);

    await search('support engineer');
    expect(nameLinks()).toEqual(['Jordan Lee']);

    await search('sam.taylor@example.com');
    expect(nameLinks()).toEqual(['Sam Taylor']);
  });

  it('narrows by the job they were saved from, with a count on each job', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });
    const filters = (await panel()).querySelector<HTMLElement>('[data-filters]')!;

    await fireEvent.pointerDown(within(filters).getByRole('button', { name: 'Saved from: All' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitemcheckbox').map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Senior .NET Engineer 2',
      'Support Engineer 1',
    ]);
    await fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /^Support Engineer/ }));

    await waitFor(() => expect(nameLinks()).toEqual(['Jordan Lee']));
    expect(within(filters).getByRole('button', { name: 'Saved from: Support Engineer' })).toBeInTheDocument();
  });

  it('narrows by tag, and the tag menu lists each tag once with its count', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });
    const filters = (await panel()).querySelector<HTMLElement>('[data-filters]')!;

    await fireEvent.pointerDown(within(filters).getByRole('button', { name: 'Tag: All' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitemcheckbox').map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Referral 2',
      'Warm intro 1',
    ]);
    await fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /^Warm intro/ }));

    await waitFor(() => expect(nameLinks()).toEqual(['Jordan Lee']));
  });

  it('says nobody has been saved and offers no filters, with no filters offered', async () => {
    renderPage({ who: [] });

    expect(await screen.findByText('Nobody saved yet')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Saved from/ })).toBeNull();
  });

  it('says nobody matches when a search or filter hides everyone, and Clear filters brings them back', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });

    await search('nobody by this name');
    expect(screen.getByText('Nobody in saved candidates matches these filters.')).toBeInTheDocument();
    expect(screen.queryByText('Nobody saved yet')).toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(nameLinks()).toEqual(['Casey Morgan', 'Jordan Lee', 'Sam Taylor']);

    await chooseFilter(/^Saved from/, /^Support Engineer/);
    await search('casey');
    expect(screen.getByText('Nobody in saved candidates matches these filters.')).toBeInTheDocument();
  });

  it('shows a load failure with a way to retry', async () => {
    renderPage({ routes: { 'GET /v1/talent-pool': () => Response.json({}, { status: 500 }) } });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load saved candidates (HTTP 500).');
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  describe('a row menu', () => {
    it('offers Invite to apply, Add to a job, Message and Remove to someone who may email, add and move candidates', async () => {
      renderPage();
      const menu = await openRowMenu('Casey Morgan');

      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual([
        'Invite to apply',
        'Add to a job',
        'Message',
        'Edit note',
        'Remove from saved',
      ]);
    });

    it('has no Message button down the Last contacted column: the action repeats on every row, so it is in the menu', async () => {
      renderPage();
      await screen.findByRole('table', { name: 'Saved candidates' });

      expect(screen.queryByRole('button', { name: /^Message / })).toBeNull();
    });

    it('stays on each list item on a phone, where the record page has none of these actions (section 8)', async () => {
      stubViewport(360);
      renderPage();

      await screen.findByRole('link', { name: 'Casey Morgan' });
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      const menu = await openRowMenu('Casey Morgan');
      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual([
        'Invite to apply',
        'Add to a job',
        'Message',
        'Edit note',
        'Remove from saved',
      ]);
      expect(screen.getByText('Last contacted Oct 28, 2026')).toBeInTheDocument();
      expect(screen.getByText('Not contacted yet')).toBeInTheDocument();
    });

    it('leaves out Invite to apply when the deployment sends no email', async () => {
      renderPage({ sending: false });
      const menu = await openRowMenu('Casey Morgan');

      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Add to a job', 'Edit note', 'Remove from saved']);
    });

    it('offers only Edit note and Remove to someone who may move candidates but not add or email them', async () => {
      const mover: CurrentUser = { ...user, organization: { ...user.organization!, permissions: ['ViewHiring', 'MoveCandidates'] } };
      renderPage({ as: mover });
      const menu = await openRowMenu('Casey Morgan');

      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Edit note', 'Remove from saved']);
    });

    it('gives a viewer the list and the links but no menu and no checkboxes that act', async () => {
      renderPage({ as: userAs('Viewer') });
      await screen.findByRole('table', { name: 'Saved candidates' });

      expect(screen.queryByRole('button', { name: /^Actions for/ })).toBeNull();
      expect(within(screen.getByRole('table', { name: 'Saved candidates' })).getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute(
        'href',
        `/applications/${casey.applicationId}`,
      );
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));
      expect(screen.queryByRole('region', { name: 'Selected people' })).toBeNull();
    });
  });

  describe('Message', () => {
    const emailRoutes = (sends: unknown[]) => ({
      'POST /v1/emails': (init: RequestInit | undefined) => {
        sends.push(JSON.parse(String(init?.body)));
        return Response.json({ queued: 1, skipped: 0 }, { status: 202 });
      },
    });

    it('opens the email composer for that person from the row menu, and sends to their saved-from application', async () => {
      const sends: unknown[] = [];
      renderPage({ routes: emailRoutes(sends) });
      await screen.findByRole('table', { name: 'Saved candidates' });

      const menu = await openRowMenu('Casey Morgan');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Message' }));

      const sheet = await screen.findByRole('dialog', { name: 'Email Casey Morgan' });
      await fireEvent.input(within(sheet).getByLabelText('Subject'), { target: { value: 'A quick question' } });
      editRichText(within(sheet).getByRole('textbox', { name: 'Message' }), '<p>Hi there</p>');
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Send' }));

      await waitFor(() => expect(sends).toHaveLength(1));
      expect(sends[0]).toMatchObject({ applicationIds: [casey.applicationId], subject: 'A quick question' });
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    });

    it('is not offered when the deployment sends no email', async () => {
      renderPage({ sending: false });
      const menu = await openRowMenu('Casey Morgan');

      expect(within(menu).queryByRole('menuitem', { name: 'Message' })).toBeNull();
    });

    it('is not offered to someone who may not email candidates', async () => {
      renderPage({ as: userAs('Viewer') });
      await screen.findByRole('table', { name: 'Saved candidates' });

      expect(screen.queryByRole('menuitem', { name: 'Message' })).toBeNull();
    });

    it('opens from the row menu on a phone', async () => {
      stubViewport(360);
      renderPage();
      const menu = await openRowMenu('Casey Morgan');

      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Message' }));

      expect(await screen.findByRole('dialog', { name: 'Email Casey Morgan' })).toBeInTheDocument();
    });
  });

  describe('the note', () => {
    it('opens the edit sheet when the note is pressed, with the current note in it', async () => {
      renderPage();
      await screen.findByRole('table', { name: 'Saved candidates' });

      await fireEvent.click(screen.getByRole('button', { name: /Edit note for Casey Morgan$/ }));

      const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });
      expect(within(sheet).getByLabelText('Note')).toHaveValue('Strong systems thinking, wrong timing.');
    });

    it('opens the same sheet from the row menu, and saves the new note and announces it', async () => {
      const put = vi.fn((init: RequestInit | undefined) => new Response(null, { status: 204 }));
      renderPage({ routes: { [`PUT /v1/talent-pool/${casey.candidateId}/note`]: put } });
      const menu = await openRowMenu('Casey Morgan');

      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Edit note' }));
      const sheet = await screen.findByRole('dialog', { name: 'Edit note for Casey Morgan' });
      await fireEvent.input(within(sheet).getByLabelText('Note'), { target: { value: 'Ready to talk now.' } });
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Save note' }));

      await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
      expect(JSON.parse(String(put.mock.calls[0]![0]!.body))).toEqual({ note: 'Ready to talk now.' });
      expect(await screen.findByText('Saved the note for Casey Morgan.', { selector: '[role="status"]' })).toBeInTheDocument();
    });

    it('is plain text, not a button, for someone who may not move candidates', async () => {
      renderPage({ as: userAs('Viewer') });
      const table = await screen.findByRole('table', { name: 'Saved candidates' });

      expect(screen.queryByRole('button', { name: /^Edit note for/ })).toBeNull();
      expect(within(table).getByText('Strong systems thinking, wrong timing.')).toBeInTheDocument();
    });
  });

  describe('the selection', () => {
    it('appears when people are ticked, with Invite to apply, Add to a job and Clear selection', async () => {
      renderPage();
      await screen.findByRole('table', { name: 'Saved candidates' });
      expect(screen.queryByRole('region', { name: 'Selected people' })).toBeNull();

      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Sam Taylor' }));

      const bar = screen.getByRole('region', { name: 'Selected people' });
      expect(bar).toHaveTextContent('2 selected');
      expect(within(bar).getByRole('button', { name: 'Invite to apply, 2 selected' })).toBeInTheDocument();
      expect(within(bar).getByRole('button', { name: 'Add to a job, 2 selected' })).toBeInTheDocument();
      await expectNoAxeViolations(bar);

      await fireEvent.click(within(bar).getByRole('button', { name: 'Clear selection' }));
      expect(screen.queryByRole('region', { name: 'Selected people' })).toBeNull();
    });

    it('selects everyone shown from the header checkbox, and only the rows a search leaves', async () => {
      renderPage();
      await screen.findByRole('table', { name: 'Saved candidates' });

      await search('o');
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select everyone shown' }));

      expect(screen.getByRole('region', { name: 'Selected people' })).toHaveTextContent(`${nameLinks().length} selected`);
    });

    it('leaves out Invite to apply when email is not sent, and the whole bar for someone who may do neither', async () => {
      renderPage({ sending: false });
      await screen.findByRole('table', { name: 'Saved candidates' });
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));

      const bar = screen.getByRole('region', { name: 'Selected people' });
      expect(within(bar).queryByRole('button', { name: /^Invite/ })).toBeNull();
      expect(within(bar).getByRole('button', { name: 'Add to a job, 1 selected' })).toBeInTheDocument();
    });

    it('opens the invitation sheet for the selection and sends the chosen job to everyone ticked', async () => {
      const invitePost = vi.fn((init: RequestInit | undefined) => Response.json({ queued: 2, skipped: 0 }));
      renderPage({
        routes: {
          'POST /v1/talent-pool/invitations': invitePost,
          'POST /v1/talent-pool/invitations/preview': () => Response.json({ subject: 'Apply', html: '<p>Hi</p>', text: 'Hi' }),
        },
      });
      await screen.findByRole('table', { name: 'Saved candidates' });
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Sam Taylor' }));
      await fireEvent.click(screen.getByRole('button', { name: 'Invite to apply, 2 selected' }));

      const sheet = await screen.findByRole('dialog', { name: 'Invite 2 to apply' });
      await fireEvent.click(within(sheet).getByRole('button', { name: 'Send 2 invitations' }));

      await waitFor(() => expect(invitePost).toHaveBeenCalled());
      expect(JSON.parse(String(invitePost.mock.calls[0]![0]!.body))).toEqual({ candidateIds: [casey.candidateId, sam.candidateId], jobId: jobs[1]!.id });
      expect(await screen.findByText('Sending 2 invitations.', { selector: '[role="status"]' })).toBeInTheDocument();
    });
  });

  describe('Remove from saved', () => {
    async function openRemoveDialog() {
      const menu = await openRowMenu('Casey Morgan');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from saved' }));
      return screen.findByRole('dialog', { name: 'Remove Casey Morgan from saved candidates?' });
    }

    it('asks first, and posts candidateAsked false by default', async () => {
      const post = vi.fn((init: RequestInit | undefined) => Response.json({}));
      renderPage({ routes: { [`POST /v1/talent-pool/${casey.candidateId}/remove`]: post } });

      const dialog = await openRemoveDialog();
      expect(post).not.toHaveBeenCalled();
      expect(within(dialog).getByRole('checkbox', { name: 'They asked to be removed, so stop keeping their details' })).not.toBeChecked();
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));

      await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
      expect(JSON.parse(String(post.mock.calls[0]![0]!.body))).toEqual({ candidateAsked: false });
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
      expect(screen.getByText('Casey Morgan was removed from saved candidates.', { selector: '[role="status"]' })).toBeInTheDocument();
    });

    it('posts candidateAsked true when the box for "they asked to be removed" is ticked', async () => {
      const post = vi.fn((init: RequestInit | undefined) => Response.json({}));
      renderPage({ routes: { [`POST /v1/talent-pool/${casey.candidateId}/remove`]: post } });

      const dialog = await openRemoveDialog();
      await fireEvent.click(within(dialog).getByRole('checkbox', { name: 'They asked to be removed, so stop keeping their details' }));
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));

      await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
      expect(JSON.parse(String(post.mock.calls[0]![0]!.body))).toEqual({ candidateAsked: true });
    });

    it('does not post when the person keeps them, and the box starts unticked the next time', async () => {
      const post = vi.fn((init: RequestInit | undefined) => Response.json({}));
      renderPage({ routes: { [`POST /v1/talent-pool/${casey.candidateId}/remove`]: post } });

      let dialog = await openRemoveDialog();
      await fireEvent.click(within(dialog).getByRole('checkbox', { name: /They asked to be removed/ }));
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Keep them' }));
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
      expect(post).not.toHaveBeenCalled();

      dialog = await openRemoveDialog();
      expect(within(dialog).getByRole('checkbox', { name: /They asked to be removed/ })).not.toBeChecked();
    });

    it('says why when removing fails and keeps the dialog open', async () => {
      renderPage({ routes: { [`POST /v1/talent-pool/${casey.candidateId}/remove`]: () => Response.json({ detail: 'They are not saved.' }, { status: 404 }) } });

      const dialog = await openRemoveDialog();
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));

      await waitFor(() => expect(within(dialog).getByRole('alert')).toBeInTheDocument());
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });
  });

  it('has no accessibility violations', async () => {
    const { container } = renderPage();
    await screen.findByRole('table', { name: 'Saved candidates' });

    await expectNoAxeViolations(container);
  });

  it('has no accessibility violations in its empty state', async () => {
    const { container } = renderPage({ who: [] });
    await screen.findByText('Nobody saved yet');

    await expectNoAxeViolations(container);
  });
});
