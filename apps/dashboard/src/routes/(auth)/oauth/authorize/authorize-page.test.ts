import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../../test-support/axe.js';
import WithTooltips from '../../../../test-support/with-tooltips.svelte';
import Page from './+page.svelte';
import { load } from './+page.js';

type Data = Parameters<typeof Page>[1]['data'];

const params: Data['params'] = {
  clientId: 'client-1',
  redirectUri: 'https://assistant.example.com/callback',
  responseType: 'code',
  scope: 'read write',
  resource: 'https://api.example.com/v1/mcp',
  codeChallenge: 'challenge-value',
  codeChallengeMethod: 'S256',
  state: 'state-value',
};

const info: NonNullable<Data['info']> = {
  clientName: 'Acme Assistant',
  redirectHost: 'assistant.example.com',
  redirectIsLoopback: false,
  organizationName: 'Brightwater Studio',
  scopes: [
    { scope: 'Read', grantable: true, reason: null },
    { scope: 'Write', grantable: true, reason: null },
    { scope: 'VerificationRead', grantable: false, reason: 'Only an owner can give access to verification results.' },
  ],
};

const data = (overrides: Partial<Data> = {}): Data => ({ user: null, params, info, problem: undefined, ...overrides });
const show = (pageData: Data) => render(WithTooltips, { props: { component: Page, props: { params: {}, data: pageData } } });

const assign = vi.fn();

beforeEach(() => {
  vi.stubGlobal('location', { ...window.location, assign });
});

afterEach(() => {
  vi.unstubAllGlobals();
  assign.mockClear();
});

const decided = (fetchMock: ReturnType<typeof vi.fn<typeof fetch>>) => JSON.parse(String(fetchMock.mock.calls[0]![1]?.body));

describe('OAuth consent page', () => {
  it('names who is asking and where, and offers only Read as on, then passes axe', async () => {
    show(data());

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Connect Acme Assistant to Brightwater Studio');
    expect(screen.getByText('assistant.example.com')).toBeInTheDocument();
    expect(screen.queryByText('This assistant runs on your computer')).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: /^Read Read jobs/ })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /^Read Read jobs/ })).toBeDisabled();
    expect(screen.getByRole('checkbox', { name: /^Write Add notes/ })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: /^Write Add notes/ })).toBeEnabled();
    expect(screen.getByText(/Everything it does shows in the activity under Acme Assistant/)).toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('disables a scope the person does not hold and says why', () => {
    show(data());

    const verification = screen.getByRole('checkbox', { name: /^Verification read/ });
    expect(verification).toBeDisabled();
    expect(verification).not.toBeChecked();
    expect(screen.getByText('Only an owner can give access to verification results.')).toBeInTheDocument();
  });

  it('warns when the assistant runs on this computer', () => {
    show(data({ info: { ...info, redirectHost: '127.0.0.1:53211', redirectIsLoopback: true } }));

    expect(screen.getByText('This assistant runs on your computer')).toBeInTheDocument();
    expect(screen.getByText('127.0.0.1:53211')).toBeInTheDocument();
  });

  it('approves with the scopes ticked and goes to the returned address', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json({ redirectUrl: 'https://assistant.example.com/callback?code=abc&state=state-value' }));
    vi.stubGlobal('fetch', fetchMock);
    show(data({ info: { ...info, scopes: info.scopes.map((offer) => (offer.scope === 'VerificationRead' ? { ...offer, grantable: true, reason: null } : offer)) } }));

    await fireEvent.click(screen.getByRole('checkbox', { name: /^Write Add notes/ }));
    await fireEvent.click(screen.getByRole('checkbox', { name: /^Verification read/ }));
    await fireEvent.click(screen.getByRole('button', { name: 'Approve' }));

    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith('https://assistant.example.com/callback?code=abc&state=state-value'));
    expect(String(fetchMock.mock.calls[0]![0])).toContain('/v1/oauth/authorize');
    expect(decided(fetchMock)).toEqual({ ...params, approve: true, grantedScopes: ['Read', 'Write', 'VerificationRead'] });
  });

  it('approves with Read alone when nothing else is ticked', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json({ redirectUrl: 'https://assistant.example.com/callback?code=abc' }));
    vi.stubGlobal('fetch', fetchMock);
    show(data());

    await fireEvent.click(screen.getByRole('button', { name: 'Approve' }));

    await vi.waitFor(() => expect(assign).toHaveBeenCalled());
    expect(decided(fetchMock)).toMatchObject({ approve: true, grantedScopes: ['Read'] });
  });

  it('denies and goes to the refusal address, with the assistant state passed through', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json({ redirectUrl: 'https://assistant.example.com/callback?error=access_denied&state=state-value' }));
    vi.stubGlobal('fetch', fetchMock);
    show(data());

    await fireEvent.click(screen.getByRole('button', { name: 'Deny' }));

    await vi.waitFor(() => expect(assign).toHaveBeenCalledWith('https://assistant.example.com/callback?error=access_denied&state=state-value'));
    expect(decided(fetchMock)).toMatchObject({ approve: false, state: 'state-value' });
  });

  it('never shows the state or the code challenge', () => {
    show(data());

    expect(document.body).not.toHaveTextContent('state-value');
    expect(document.body).not.toHaveTextContent('challenge-value');
  });

  it('shows a problem with no buttons, moves focus to the title and does not redirect', async () => {
    show(data({ info: undefined, problem: 'This assistant asked to return to an address ScreenHire does not accept.' }));

    const heading = screen.getByRole('heading', { level: 1, name: "This assistant can't be connected" });
    expect(within(screen.getByRole('region', { name: 'Connection request' })).getByRole('alert')).toHaveTextContent('does not accept');
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Deny' })).not.toBeInTheDocument();
    await vi.waitFor(() => expect(heading).toHaveFocus());
    expect(assign).not.toHaveBeenCalled();
    await expectNoAxeViolations(document.body);
  });

  it('keeps the buttons and shows the reason when the answer is refused', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ title: 'Bad Request', status: 400, detail: 'This request has expired. Start again from the assistant.' }, { status: 400 })));
    show(data());

    await fireEvent.click(screen.getByRole('button', { name: 'Approve' }));

    expect(await screen.findByText('This request has expired. Start again from the assistant.')).toBeInTheDocument();
    expect(assign).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Approve' })).toBeEnabled();
  });
});

describe('OAuth consent page load', () => {
  const run = (search: string) =>
    load({
      url: new URL(`http://dashboard.test/oauth/authorize?${search}`),
      parent: async () => ({ user: null }),
    } as unknown as Parameters<typeof load>[0]) as Promise<{ params: Data['params']; info?: unknown; problem?: string }>;

  const query =
    'client_id=client-1&redirect_uri=https%3A%2F%2Fassistant.example.com%2Fcallback&response_type=code&scope=read%20write&resource=https%3A%2F%2Fapi.example.com%2Fv1%2Fmcp&code_challenge=challenge-value&code_challenge_method=S256&state=state-value';

  it('maps the assistant parameters to the API request and returns them for the answer', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => Response.json(info));
    vi.stubGlobal('fetch', fetchMock);

    const result = await run(query);

    const called = new URL(String(fetchMock.mock.calls[0]![0]), 'http://dashboard.test');
    expect(called.pathname).toBe('/v1/oauth/authorization-request');
    expect(Object.fromEntries(called.searchParams)).toEqual({
      clientId: 'client-1',
      redirectUri: 'https://assistant.example.com/callback',
      responseType: 'code',
      scope: 'read write',
      resource: 'https://api.example.com/v1/mcp',
      codeChallenge: 'challenge-value',
      codeChallengeMethod: 'S256',
    });
    expect(result.params).toEqual(params);
    expect(result.info).toEqual(info);
  });

  it('turns a problem response into text for the person', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ detail: 'Unknown assistant.' }, { status: 400 })));

    expect((await run(query)).problem).toBe('Unknown assistant.');
  });

  it('words a refusal that comes without detail', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 403 })));

    expect((await run(query)).problem).toMatch(/needs a role that manages API keys and sees every job/);
  });
});
