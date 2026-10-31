import type { CurrentUser, TalentPoolMember } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, jobs, user, userAs } from '../../../test-support/fixtures.js';
import { stubViewport } from '../../../test-support/viewport.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

// Signed in as the Owner unless a test changes it.
const appPage = vi.hoisted(() => ({ url: new URL('http://dashboard.test/talent-pool'), data: {} as Record<string, unknown> }));
vi.mock('$app/state', () => ({ page: appPage }));
// The Add to a job sheet leaves for the new application's page through goto.
vi.mock('$app/navigation', () => ({ goto: vi.fn(async () => {}) }));

const supportJobId = '01a157d4-0000-7000-8000-0000000009aa';

const member = (index: number, patch: Partial<TalentPoolMember>): TalentPoolMember => ({
  candidateId: `01a157d4-0000-7000-8000-0000000004${index.toString().padStart(2, '0')}`,
  candidateName: 'Casey Morgan',
  email: 'casey.morgan@example.com',
  applicationId: `01a157d4-0000-7000-8000-0000000005${index.toString().padStart(2, '0')}`,
  jobId: jobs[1]!.id,
  jobTitle: 'Senior .NET Engineer',
  note: null,
  tags: [],
  keptAt: '2026-10-20T12:00:00Z',
  keptByUserId: null,
  permissionEndsAt: '2027-03-15T12:00:00Z',
  lastInvitedJobId: null,
  lastInvitedJobTitle: null,
  lastInvitedAt: null,
  appliedJobIds: [],
  ...patch,
});

const casey = member(0, { note: 'Strong systems thinking, wrong timing.', tags: ['Referral'] });
const jordan = member(1, {
  candidateName: 'Jordan Lee',
  email: 'jordan.lee@example.com',
  jobId: supportJobId,
  jobTitle: 'Support Engineer',
  note: 'Great with customers.',
  tags: ['Warm intro', 'Referral'],
  permissionEndsAt: '2026-12-01T12:00:00Z',
});
const sam = member(2, { candidateName: 'Sam Taylor', email: 'sam.taylor@example.com', note: null, permissionEndsAt: '2027-09-09T12:00:00Z' });
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
      'GET /v1/talent-pool': () => Response.json({ permissionMonths: 12, people: options.who ?? people }),
      'GET /v1/email-templates': () => Response.json({ sendingEnabled: options.sending ?? true, templates: [], variables: [] }),
      'GET /v1/jobs': () => Response.json(jobs),
      ...options.routes,
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  return render(WithQueryClient, { props: { component: Page, props: {} } });
}

const panel = () => screen.findByRole('region', { name: 'People' });
const nameLinks = () => screen.queryAllByRole('link', { name: /^(Casey Morgan|Jordan Lee|Sam Taylor)$/ }).map((link) => link.textContent);
const search = (text: string) => fireEvent.input(screen.getByRole('searchbox', { name: 'Search the talent pool' }), { target: { value: text } });

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

describe('talent pool page', () => {
  it('lists each kept person with their note, tags, the job they were kept from and when their permission ends', async () => {
    renderPage();
    const table = await screen.findByRole('table', { name: 'Talent pool' });

    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent?.trim())).toEqual([
      '',
      'Candidate',
      'Why',
      'Tags',
      'Kept from',
      'Kept',
      'Permission ends',
      'Actions',
    ]);
    const row = within(table).getByRole('link', { name: 'Casey Morgan' }).closest('tr')!;
    expect(within(row).getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute('href', `/applications/${casey.applicationId}`);
    expect(row).toHaveTextContent('Strong systems thinking, wrong timing.');
    expect(row).toHaveTextContent('Referral');
    expect(row).toHaveTextContent('Senior .NET Engineer');
    expect(within(row).getByText('Mar 15, 2027')).toHaveAttribute('datetime', casey.permissionEndsAt);
    // A row without a note says so to a screen reader instead of leaving an empty cell.
    expect(within(within(table).getByRole('link', { name: 'Sam Taylor' }).closest('tr')!).getByText('No note')).toHaveClass('sr-only');
    expect(screen.getByRole('heading', { level: 1, name: 'Talent pool' }).closest('[data-slot="header-band"]')).toHaveTextContent('3 people');
  });

  it('has no Verification column, filter or chip, since the pool is never a list picked by level (ADR-0004)', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Talent pool' });
    const filters = (await panel()).querySelector<HTMLElement>('[data-filters]')!;

    expect(within(filters).getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Kept from: All', 'Tag: All']);
    expect(screen.queryByRole('columnheader', { name: /verification|authenticity|integrity/i })).toBeNull();
    expect(document.body.textContent).not.toMatch(/Checks pending|Looks genuine|Authenticity/);
  });

  it('opens by name and sorts by the soonest permission end from the column header', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Talent pool' });
    expect(nameLinks()).toEqual(['Casey Morgan', 'Jordan Lee', 'Sam Taylor']);

    await fireEvent.click(screen.getByRole('button', { name: /Permission ends/ }));

    expect(nameLinks()).toEqual(['Jordan Lee', 'Casey Morgan', 'Sam Taylor']);
  });

  it('narrows to what the search matches in the name, email, job or note, and the count follows', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Talent pool' });

    await search('jordan');
    expect(nameLinks()).toEqual(['Jordan Lee']);
    expect(screen.getByRole('heading', { level: 1, name: 'Talent pool' }).closest('[data-slot="header-band"]')).toHaveTextContent('1 person');

    await search('wrong timing');
    expect(nameLinks()).toEqual(['Casey Morgan']);

    await search('support engineer');
    expect(nameLinks()).toEqual(['Jordan Lee']);

    await search('sam.taylor@example.com');
    expect(nameLinks()).toEqual(['Sam Taylor']);
  });

  it('narrows by the job they were kept from, with a count on each job', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Talent pool' });
    const filters = (await panel()).querySelector<HTMLElement>('[data-filters]')!;

    await fireEvent.pointerDown(within(filters).getByRole('button', { name: 'Kept from: All' }), { button: 0, pointerType: 'mouse' });
    const menu = await screen.findByRole('menu');
    expect(within(menu).getAllByRole('menuitemcheckbox').map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'Senior .NET Engineer 2',
      'Support Engineer 1',
    ]);
    await fireEvent.click(within(menu).getByRole('menuitemcheckbox', { name: /^Support Engineer/ }));

    await waitFor(() => expect(nameLinks()).toEqual(['Jordan Lee']));
    expect(within(filters).getByRole('button', { name: 'Kept from: Support Engineer' })).toBeInTheDocument();
  });

  it('narrows by tag, and the tag menu lists each tag once with its count', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Talent pool' });
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

  it('says the pool is empty and where to start, with no filters offered', async () => {
    renderPage({ who: [] });

    expect(await screen.findByText('Nobody has been kept in mind yet.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Clear filters' })).toBeNull();
    expect(screen.queryByRole('button', { name: /^Kept from/ })).toBeNull();
  });

  it('says nobody matches when a search or filter hides everyone, and Clear filters brings them back', async () => {
    renderPage();
    await screen.findByRole('table', { name: 'Talent pool' });

    await search('nobody by this name');
    expect(screen.getByText('Nobody in the talent pool matches these filters.')).toBeInTheDocument();
    expect(screen.queryByText('Nobody has been kept in mind yet.')).toBeNull();

    await fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(nameLinks()).toEqual(['Casey Morgan', 'Jordan Lee', 'Sam Taylor']);

    await chooseFilter(/^Kept from/, /^Support Engineer/);
    await search('casey');
    expect(screen.getByText('Nobody in the talent pool matches these filters.')).toBeInTheDocument();
  });

  it('shows a load failure with a way to retry', async () => {
    renderPage({ routes: { 'GET /v1/talent-pool': () => Response.json({}, { status: 500 }) } });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load the talent pool (HTTP 500).');
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  describe('a row menu', () => {
    it('offers Invite to apply, Add to a job and Remove to someone who may email, add and move candidates', async () => {
      renderPage();
      const menu = await openRowMenu('Casey Morgan');

      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Invite to apply', 'Add to a job', 'Remove from talent pool']);
    });

    it('stays on each list item on a phone, where the record page has none of these actions (section 8)', async () => {
      stubViewport(360);
      renderPage();

      await screen.findByRole('link', { name: 'Casey Morgan' });
      expect(screen.queryByRole('table')).not.toBeInTheDocument();
      const menu = await openRowMenu('Casey Morgan');
      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Invite to apply', 'Add to a job', 'Remove from talent pool']);
    });

    it('leaves out Invite to apply when the deployment sends no email', async () => {
      renderPage({ sending: false });
      const menu = await openRowMenu('Casey Morgan');

      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Add to a job', 'Remove from talent pool']);
    });

    it('offers only Remove to someone who may move candidates but not add or email them', async () => {
      const mover: CurrentUser = { ...user, organization: { ...user.organization!, permissions: ['ViewHiring', 'MoveCandidates'] } };
      renderPage({ as: mover });
      const menu = await openRowMenu('Casey Morgan');

      expect(within(menu).getAllByRole('menuitem').map((item) => item.textContent?.trim())).toEqual(['Remove from talent pool']);
    });

    it('gives a viewer the list and the links but no menu and no checkboxes that act', async () => {
      renderPage({ as: userAs('Viewer') });
      await screen.findByRole('table', { name: 'Talent pool' });

      expect(screen.queryByRole('button', { name: /^Actions for/ })).toBeNull();
      expect(within(screen.getByRole('table', { name: 'Talent pool' })).getByRole('link', { name: 'Casey Morgan' })).toHaveAttribute(
        'href',
        `/applications/${casey.applicationId}`,
      );
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select Casey Morgan' }));
      expect(screen.queryByRole('region', { name: 'Selected people' })).toBeNull();
    });
  });

  describe('the selection', () => {
    it('appears when people are ticked, with Invite to apply, Add to a job and Clear selection', async () => {
      renderPage();
      await screen.findByRole('table', { name: 'Talent pool' });
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
      await screen.findByRole('table', { name: 'Talent pool' });

      await search('o');
      await fireEvent.click(screen.getByRole('checkbox', { name: 'Select everyone shown' }));

      expect(screen.getByRole('region', { name: 'Selected people' })).toHaveTextContent(`${nameLinks().length} selected`);
    });

    it('leaves out Invite to apply when email is not sent, and the whole bar for someone who may do neither', async () => {
      renderPage({ sending: false });
      await screen.findByRole('table', { name: 'Talent pool' });
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
      await screen.findByRole('table', { name: 'Talent pool' });
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

  describe('Remove from talent pool', () => {
    async function openRemoveDialog() {
      const menu = await openRowMenu('Casey Morgan');
      await fireEvent.click(within(menu).getByRole('menuitem', { name: 'Remove from talent pool' }));
      return screen.findByRole('dialog', { name: 'Remove Casey Morgan from the talent pool?' });
    }

    it('asks first, and posts candidateAsked false by default', async () => {
      const post = vi.fn((init: RequestInit | undefined) => Response.json({}));
      renderPage({ routes: { [`POST /v1/talent-pool/${casey.candidateId}/remove`]: post } });

      const dialog = await openRemoveDialog();
      expect(post).not.toHaveBeenCalled();
      expect(within(dialog).getByRole('checkbox', { name: 'They asked to be removed: stop keeping their details' })).not.toBeChecked();
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));

      await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
      expect(JSON.parse(String(post.mock.calls[0]![0]!.body))).toEqual({ candidateAsked: false });
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
      expect(screen.getByText('Casey Morgan left the talent pool.', { selector: '[role="status"]' })).toBeInTheDocument();
    });

    it('posts candidateAsked true when the box for "they asked to be removed" is ticked', async () => {
      const post = vi.fn((init: RequestInit | undefined) => Response.json({}));
      renderPage({ routes: { [`POST /v1/talent-pool/${casey.candidateId}/remove`]: post } });

      const dialog = await openRemoveDialog();
      await fireEvent.click(within(dialog).getByRole('checkbox', { name: 'They asked to be removed: stop keeping their details' }));
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
      renderPage({ routes: { [`POST /v1/talent-pool/${casey.candidateId}/remove`]: () => Response.json({ detail: 'They are not in the pool.' }, { status: 404 }) } });

      const dialog = await openRemoveDialog();
      await fireEvent.click(within(dialog).getByRole('button', { name: 'Remove' }));

      await waitFor(() => expect(within(dialog).getByRole('alert')).toBeInTheDocument());
      expect(screen.getByRole('dialog')).toBeInTheDocument();
    });
  });

  it('has no accessibility violations', async () => {
    const { container } = renderPage();
    await screen.findByRole('table', { name: 'Talent pool' });

    await expectNoAxeViolations(container);
  });

  it('has no accessibility violations in its empty state', async () => {
    const { container } = renderPage({ who: [] });
    await screen.findByText('Nobody has been kept in mind yet.');

    await expectNoAxeViolations(container);
  });
});
