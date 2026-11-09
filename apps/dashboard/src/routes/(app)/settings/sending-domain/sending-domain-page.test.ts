import type { OrganizationSendingDomain, SendingDomainDetail } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import { apiStub, user } from '../../../../test-support/fixtures.js';
import WithQueryClient from '../../../../test-support/with-query-client.svelte';
import Page from './+page.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/settings/sending-domain') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

const records: SendingDomainDetail['records'] = [
  { purpose: 'Dkim', type: 'CNAME', host: 's1._domainkey.example.com', value: 's1.domainkey.mail.test', priority: null, state: 'Missing', reason: 'Signs your mail as your domain, so it passes DMARC.' },
  { purpose: 'ReturnPath', type: 'CNAME', host: 'em1.example.com', value: 'return.mail.test', priority: null, state: 'Matches', reason: 'Sets the return path for bounces.' },
  { purpose: 'Replies', type: 'MX', host: 'reply.example.com', value: 'mx.mail.test', priority: 10, state: 'Differs', reason: 'Delivers candidates replies to ScreenHire.' },
];

const domain = (overrides: Partial<SendingDomainDetail> = {}): SendingDomainDetail => ({
  domain: 'example.com',
  status: 'Pending',
  fromAddress: 'hiring@example.com',
  replyHost: 'reply.example.com',
  records,
  createdAt: '2026-11-01T10:00:00+00:00',
  lastCheckedAt: '2026-11-01T11:00:00+00:00',
  verifiedAt: null,
  providerMessage: null,
  ...overrides,
});

const lastUpdated = { at: '2026-11-01T10:00:00+00:00', by: 'Member' as const, userId: null, apiKeyId: null, created: false };

function renderPage(current: OrganizationSendingDomain, routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: unknown[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/sending-domain': () => Response.json(current),
        'PUT /v1/organization/sending-domain': (init) => {
          bodies.push(JSON.parse(String(init?.body)));
          return Response.json(domain({ records: records.map((record) => ({ ...record, state: 'NotChecked' })), lastCheckedAt: null }));
        },
        ...routes,
      }),
    ),
  );
  render(WithQueryClient, { props: { component: Page, props: {} } });
  return bodies;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('sending domain page', () => {
  it('says so when this deployment has no email provider', async () => {
    renderPage({ available: false, sandbox: false, domain: null });

    expect(await screen.findByText('Sending from your own domain is not set up here')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Add domain' })).not.toBeInTheDocument();
  });

  it('adds a domain and sends it as typed', async () => {
    const bodies = renderPage({ available: true, sandbox: false, domain: null });

    await fireEvent.input(await screen.findByRole('textbox', { name: 'Domain' }), { target: { value: 'example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add domain' }));

    await waitFor(() => expect(bodies).toEqual([{ domain: 'example.com' }]));
  });

  it('takes the domain out of an address someone pasted from the browser bar or an email', async () => {
    const bodies = renderPage({ available: true, sandbox: false, domain: null });

    await fireEvent.input(await screen.findByRole('textbox', { name: 'Domain' }), { target: { value: 'https://www.example.com/careers' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add domain' }));

    await waitFor(() => expect(bodies).toEqual([{ domain: 'example.com' }]));
  });

  it('asks for a domain before it adds one', async () => {
    const bodies = renderPage({ available: true, sandbox: false, domain: null });

    await fireEvent.click(await screen.findByRole('button', { name: 'Add domain' }));

    expect(await screen.findByText('Enter your domain, such as example.com.')).toBeInTheDocument();
    expect(bodies).toEqual([]);
  });

  it('shows the validation problem the API gives for a domain it refuses', async () => {
    renderPage(
      { available: true, sandbox: false, domain: null },
      { 'PUT /v1/organization/sending-domain': () => Response.json({ title: 'x', status: 400, errors: { domain: ['Enter a domain your company owns.'] } }, { status: 400 }) },
    );

    await fireEvent.input(await screen.findByRole('textbox', { name: 'Domain' }), { target: { value: 'gmail.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Add domain' }));

    expect(await screen.findByText('Enter a domain your company owns.')).toBeInTheDocument();
  });

  it('lists each record with what the last check found, and says why the reply record is there', async () => {
    renderPage({ available: true, sandbox: false, domain: domain() });

    expect(await screen.findByRole('heading', { name: 'DKIM key' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Return path' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Replies' })).toBeInTheDocument();
    expect(screen.getByText('Not found')).toBeInTheDocument();
    expect(screen.getByText('Found')).toBeInTheDocument();
    expect(screen.getByText('Different value')).toBeInTheDocument();
    expect(screen.getByText('Records to add')).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Replies host' })).toHaveTextContent('reply.example.com');
    expect(screen.getAllByText('Host')).toHaveLength(records.length);
    expect(screen.getAllByText('Value')).toHaveLength(records.length);
    await expectNoAxeViolations();
  });

  it('numbers the records that share a purpose, so two DKIM keys can be told apart', async () => {
    const second = { ...records[0]!, host: 's2._domainkey.example.com', value: 's2.domainkey.mail.test' };
    renderPage({ available: true, sandbox: false, domain: domain({ records: [records[0]!, second, ...records.slice(1)] }) });

    expect(await screen.findByRole('heading', { name: 'DKIM key 1' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'DKIM key 2' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'DKIM key 2 host' })).toHaveTextContent('s2._domainkey.example.com');
    expect(screen.getByRole('heading', { name: 'Return path' })).toBeInTheDocument();
  });

  it('shows who last changed the domain and opens its history beside the title', async () => {
    renderPage({ available: true, sandbox: false, domain: domain(), lastUpdated });

    expect(await screen.findByRole('button', { name: /History/ })).toBeInTheDocument();
  });

  it('says email still comes from ScreenHire until the domain is verified, then from the domain', async () => {
    renderPage({ available: true, sandbox: false, domain: domain() });
    expect(await screen.findByText('ScreenHire, until the domain is verified')).toBeInTheDocument();
  });

  it('shows the verified domain and the address mail comes from', async () => {
    renderPage({
      available: true,
      sandbox: false,
      domain: domain({ status: 'Verified', verifiedAt: '2026-11-01T11:00:00+00:00', records: records.map((record) => ({ ...record, state: 'Matches' })) }),
    });

    expect(await screen.findByText('Verified')).toBeInTheDocument();
    expect(screen.getByText('hiring@example.com')).toBeInTheDocument();
  });

  it('shows a clear all-good state when the domain is verified and every record is found', async () => {
    const verified = domain({ status: 'Verified', verifiedAt: '2026-11-01T11:00:00+00:00', records: records.map((record) => ({ ...record, state: 'Matches' })) });
    renderPage({ available: true, sandbox: false, domain: verified });

    expect(await screen.findByText(/Every record is in place./)).toHaveTextContent('ScreenHire checks again every day and tells the Owners if it stops.');
    // One quiet line: the instructions and the records are out of the way, and Check records is still there.
    expect(screen.queryByText(/Add these records where your domain is managed/)).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'DKIM key 1' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Check records' })).toBeInTheDocument();
    await expectNoAxeViolations();

    // The records are one press away.
    const show = screen.getByRole('button', { name: 'Show records' });
    expect(show).toHaveAttribute('aria-expanded', 'false');
    // The button names the list it opens, and the list is there while hidden.
    const list = document.getElementById(show.getAttribute('aria-controls') ?? '');
    expect(list).not.toBeNull();
    expect(list).not.toBeVisible();
    await fireEvent.click(show);
    expect(screen.getByRole('button', { name: 'Hide records' })).toHaveAttribute('aria-expanded', 'true');
    expect(list).toBeVisible();
    expect(screen.getAllByText('Found')).toHaveLength(3);
  });

  it('does not promise the daily check in the sandbox, whose DNS is not looked at on its own', async () => {
    const verified = domain({ status: 'Verified', verifiedAt: '2026-11-01T11:00:00+00:00', records: records.map((record) => ({ ...record, state: 'Matches' })) });
    renderPage({ available: true, sandbox: true, domain: verified });

    expect(await screen.findByText(/Every record is in place./)).not.toHaveTextContent('checks again every day');
  });

  it('keeps the instructions, and no all-good line, while a record is still missing', async () => {
    renderPage({ available: true, sandbox: false, domain: domain() });

    expect(await screen.findByText(/Add these records where your domain is managed/)).toBeInTheDocument();
    expect(screen.queryByText(/Every record is in place/)).not.toBeInTheDocument();
  });

  it('shows the email provider\'s reason when the records are in place but it has not confirmed the domain', async () => {
    renderPage({
      available: true,
      sandbox: false,
      domain: domain({ records: records.map((record) => ({ ...record, state: 'Matches' })), providerMessage: 'The CNAME record could not be verified yet.' }),
    });

    expect(await screen.findByText('Waiting for confirmation')).toBeInTheDocument();
    expect(screen.getByText(/The email provider says: The CNAME record could not be verified yet\./)).toBeInTheDocument();
  });

  it('checks the records on request', async () => {
    let checks = 0;
    renderPage(
      { available: true, sandbox: false, domain: domain() },
      {
        'POST /v1/organization/sending-domain/check': () => {
          checks += 1;
          return Response.json(domain({ status: 'Verified', records: records.map((record) => ({ ...record, state: 'Matches' })) }));
        },
      },
    );

    await fireEvent.click(await screen.findByRole('button', { name: 'Check records' }));

    await waitFor(() => expect(checks).toBe(1));
  });
});
