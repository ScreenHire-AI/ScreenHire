import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { goto } from '$app/navigation';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { cancelledPrompt, stubWebAuthn, unstubWebAuthn } from '../../../test-support/webauthn.js';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import Page from './+page.svelte';
import { load } from './+page.js';

// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

type Data = Parameters<typeof Page>[1]['data'];

const data = (overrides: Partial<Data> = {}): Data => ({
  user: null,
  methods: { providers: [{ id: 'dev-sso', displayName: 'Dev SSO' }], magicLinkEnabled: true },
  returnUrl: '/candidates',
  errorMessage: undefined,
  ...overrides,
});

const show = (pageData: Data) => render(WithTooltips, { props: { component: Page, props: { params: {}, data: pageData } } });

describe('sign-in page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is one centred card: the mark, one h1 and a line on new accounts, the providers, then the email link, and passes axe', async () => {
    show(data());

    const card = document.querySelector<HTMLElement>('[data-slot="sign-in-card"]')!;
    expect(screen.getAllByRole('main')).toHaveLength(1);
    expect(screen.getByRole('main')).toContainElement(card);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Sign in to ScreenHire');
    expect(card).toHaveTextContent('New here? Signing in creates your account.');
    expect(within(card).getByRole('link', { name: 'Continue with Dev SSO' })).toBeInTheDocument();
    expect(within(card).getByRole('form', { name: 'Email link' })).toBeInTheDocument();
    // No panels and no brand bar: the card carries the mark (design system section 7).
    expect(screen.queryByRole('region')).not.toBeInTheDocument();
    expect(screen.queryByRole('banner')).not.toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('gives Google and Microsoft their own marks, and any other provider the generic icon', () => {
    show(
      data({
        methods: {
          providers: [
            { id: 'google', displayName: 'Google' },
            { id: 'microsoft', displayName: 'Microsoft' },
            { id: 'dev-sso', displayName: 'Dev SSO' },
          ],
          magicLinkEnabled: true,
        },
      }),
    );

    const google = screen.getByRole('link', { name: 'Continue with Google' });
    const microsoft = screen.getByRole('link', { name: 'Continue with Microsoft' });
    const devSso = screen.getByRole('link', { name: 'Continue with Dev SSO' });
    expect(google.querySelector('[data-slot="google-mark"]')).toHaveAttribute('aria-hidden', 'true');
    expect(microsoft.querySelector('[data-slot="microsoft-mark"]')).toHaveAttribute('aria-hidden', 'true');
    expect(google.querySelector('[data-slot="microsoft-mark"]')).toBeNull();
    // A provider the marks do not know keeps a plain icon, never another company's mark.
    expect(devSso.querySelector('[data-slot$="-mark"]')).toBeNull();
    expect(devSso.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
  });

  it('puts an "or" divider between the providers and the email link, only when both are offered', () => {
    const { unmount } = show(data());

    const divider = screen.getByText('or');
    expect(divider).toHaveAttribute('data-slot', 'or-divider');
    const lastProvider = screen.getByRole('link', { name: 'Continue with Dev SSO' });
    const email = screen.getByLabelText('Work email');
    expect(lastProvider.compareDocumentPosition(divider) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(divider.compareDocumentPosition(email) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    unmount();

    show(data({ methods: { providers: [], magicLinkEnabled: true } }));
    expect(screen.queryByText('or')).not.toBeInTheDocument();
  });

  it('has no divider when only providers are offered', () => {
    show(data({ methods: { providers: [{ id: 'google', displayName: 'Google' }], magicLinkEnabled: false } }));

    expect(screen.getByRole('link', { name: 'Continue with Google' })).toBeInTheDocument();
    expect(screen.queryByText('or')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Work email')).not.toBeInTheDocument();
  });

  it('says where the link went and moves focus to the new title', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 202 })));
    show(data());

    await fireEvent.input(screen.getByLabelText('Work email'), { target: { value: 'avery@example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Email me a sign-in link' }));

    const heading = await screen.findByRole('heading', { level: 1, name: 'Check your email' });
    await vi.waitFor(() => expect(heading).toHaveFocus());
    expect(screen.getByRole('status')).toHaveTextContent('avery@example.com');
    // The same card, with the ways to sign in and the line on new accounts gone.
    expect(document.querySelector('[data-slot="sign-in-card"]')).toContainElement(screen.getByRole('status'));
    expect(screen.queryByRole('link', { name: /Continue with/ })).not.toBeInTheDocument();
    expect(screen.queryByText('New here? Signing in creates your account.')).not.toBeInTheDocument();
  });

  it('sends another link from the same address: back to the form with the email still in it', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 202 })));
    show(data());
    await fireEvent.input(screen.getByLabelText('Work email'), { target: { value: 'avery@example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Email me a sign-in link' }));
    await screen.findByRole('heading', { level: 1, name: 'Check your email' });

    await fireEvent.click(screen.getByRole('button', { name: 'Send another link' }));

    const email = await screen.findByLabelText('Work email');
    expect(email).toHaveValue('avery@example.com');
    await vi.waitFor(() => expect(email).toHaveFocus());
  });

  it('asks for the link to open where this visit was going', async () => {
    const fetchMock = vi.fn<typeof fetch>(async () => new Response(null, { status: 202 }));
    vi.stubGlobal('fetch', fetchMock);
    show(data({ returnUrl: '/oauth/authorize?client_id=abc&state=xyz' }));

    await fireEvent.input(screen.getByLabelText('Work email'), { target: { value: 'avery@example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Email me a sign-in link' }));

    await screen.findByRole('heading', { level: 1, name: 'Check your email' });
    const [input, init] = fetchMock.mock.calls[0]!;
    expect(String(input)).toContain('/v1/auth/magic-link');
    expect(JSON.parse(String(init?.body))).toEqual({ email: 'avery@example.com', returnUrl: '/oauth/authorize?client_id=abc&state=xyz' });
  });

  it('offers each configured provider, returning to where the visitor was going', () => {
    show(data());

    const link = screen.getByRole('link', { name: 'Continue with Dev SSO' });
    expect(link).toHaveAttribute('href', '/v1/auth/challenge/dev-sso?returnUrl=%2Fcandidates');
    expect(link).toHaveAttribute('data-sveltekit-reload');
  });

  it('offers the email link form when the server can send email', () => {
    show(data());

    expect(screen.getByLabelText('Work email')).toHaveAttribute('type', 'email');
    expect(screen.getByRole('button', { name: 'Email me a sign-in link' })).toBeInTheDocument();
  });

  it('hides what is not configured', () => {
    show(data({ methods: { providers: [], magicLinkEnabled: true } }));

    expect(screen.queryByRole('link', { name: /Continue with/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Work email')).toBeInTheDocument();
  });

  it('says why a provider sign-in came back', () => {
    show(data({ errorMessage: "We couldn't sign you in with that provider." }));

    expect(screen.getByRole('alert')).toHaveTextContent("We couldn't sign you in with that provider.");
  });

  it('says why a provider sign-in came back when no sign-in method is left to offer', () => {
    show(data({ methods: { providers: [], magicLinkEnabled: false }, errorMessage: "We couldn't sign you in with that provider." }));

    const card = document.querySelector('[data-slot="sign-in-card"]');
    expect(card).toHaveTextContent("We couldn't sign you in with that provider.");
    expect(card).toHaveTextContent('Sign-in is not set up on this server yet.');
    // Nothing to sign in with, so no line about what signing in does.
    expect(card).not.toHaveTextContent('New here?');
  });
});

describe('sign-in with a passkey', () => {
  const passkeyRoutes = (signIn: () => Response = () => new Response(null, { status: 204 })) =>
    vi.fn<typeof fetch>(async (input, init) => {
      const key = `${init?.method ?? 'GET'} ${String(input)}`;
      if (key === 'POST /v1/auth/passkeys/sign-in/options') return Response.json({ optionsJson: JSON.stringify({ challenge: 'abc' }) });
      if (key === 'POST /v1/auth/passkeys/sign-in') return signIn();
      throw new Error(`No stub for ${key}`);
    });

  afterEach(() => {
    unstubWebAuthn();
    vi.mocked(goto).mockClear();
  });

  it('offers no passkey button in a browser without WebAuthn', () => {
    show(data());

    expect(screen.queryByRole('button', { name: 'Sign in with a passkey' })).not.toBeInTheDocument();
  });

  it('puts the button first among the providers, before the "or" divider, and passes axe', async () => {
    stubWebAuthn();
    show(data());

    const button = screen.getByRole('button', { name: 'Sign in with a passkey' });
    expect(button.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    const provider = screen.getByRole('link', { name: 'Continue with Dev SSO' });
    expect(button.compareDocumentPosition(provider) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(button.compareDocumentPosition(screen.getByText('or')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await expectNoAxeViolations(document.body);
  });

  it('keeps the divider when the passkey is the only thing above the email form, and drops it when it is the only method', () => {
    stubWebAuthn();
    const { unmount } = show(data({ methods: { providers: [], magicLinkEnabled: true } }));
    expect(screen.getByRole('button', { name: 'Sign in with a passkey' })).toBeInTheDocument();
    expect(screen.getByText('or')).toHaveAttribute('data-slot', 'or-divider');
    unmount();

    show(data({ methods: { providers: [], magicLinkEnabled: false } }));
    expect(screen.getByRole('button', { name: 'Sign in with a passkey' })).toBeInTheDocument();
    expect(screen.queryByText('or')).not.toBeInTheDocument();
    expect(screen.queryByText('Sign-in is not set up on this server yet.')).not.toBeInTheDocument();
  });

  it('signs in and goes on to where this visit was going, running the guard again', async () => {
    const credentials = stubWebAuthn();
    vi.stubGlobal('fetch', passkeyRoutes());
    show(data({ returnUrl: '/oauth/authorize?client_id=abc' }));

    await fireEvent.click(screen.getByRole('button', { name: 'Sign in with a passkey' }));

    await vi.waitFor(() => expect(goto).toHaveBeenCalledWith('/oauth/authorize?client_id=abc', { invalidateAll: true }));
    expect(credentials.get).toHaveBeenCalledOnce();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says nothing when the person closes the browser prompt, and lets them try again', async () => {
    const credentials = stubWebAuthn({ get: async () => Promise.reject(cancelledPrompt()) });
    vi.stubGlobal('fetch', passkeyRoutes());
    show(data());

    const button = screen.getByRole('button', { name: 'Sign in with a passkey' });
    await fireEvent.click(button);

    await vi.waitFor(() => expect(credentials.get).toHaveBeenCalledOnce());
    await vi.waitFor(() => expect(button).toBeEnabled());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(goto).not.toHaveBeenCalled();
  });

  it('points to the other ways in when the passkey does not work', async () => {
    stubWebAuthn();
    vi.stubGlobal('fetch', passkeyRoutes(() => Response.json({ errors: {} }, { status: 400 })));
    show(data());

    await fireEvent.click(screen.getByRole('button', { name: 'Sign in with a passkey' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('That passkey did not work. Use another way to sign in.');
    // Above the ways to try again (design system section 7).
    expect(alert.compareDocumentPosition(screen.getByRole('button', { name: 'Sign in with a passkey' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(alert.compareDocumentPosition(screen.getByRole('link', { name: 'Continue with Dev SSO' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(goto).not.toHaveBeenCalled();
    // The other ways are still there.
    expect(screen.getByLabelText('Work email')).toBeInTheDocument();
  });
});

describe('sign-in page load', () => {
  const methods = { providers: [], magicLinkEnabled: true };
  const event = (search: string) => ({ url: new URL(`http://localhost/sign-in${search}`) }) as Parameters<typeof load>[0];

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it.each([
    ['?error=email-unverified', "Your provider didn't confirm your email address"],
    ['?error=something-new', "We couldn't sign you in with that provider."],
    ['?error=constructor', "We couldn't sign you in with that provider."],
    ['?error=__proto__', "We couldn't sign you in with that provider."],
  ])('explains %s with a known message', async (search, message) => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Response.json(methods)));

    const result = await load(event(search));

    expect(result).toEqual(expect.objectContaining({ errorMessage: expect.stringContaining(message) }));
  });

  it('cuts the return address down to a local path', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Response.json(methods)));

    await expect(load(event('?returnUrl=%2Foauth%2Fauthorize%3Fclient_id%3Dabc'))).resolves.toEqual(expect.objectContaining({ returnUrl: '/oauth/authorize?client_id=abc' }));
    await expect(load(event('?returnUrl=https%3A%2F%2Fevil.example.com'))).resolves.toEqual(expect.objectContaining({ returnUrl: '/' }));
  });

  it('shows no message without an error', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Response.json(methods)));

    await expect(load(event(''))).resolves.toEqual(expect.objectContaining({ methods, errorMessage: undefined }));
  });
});
