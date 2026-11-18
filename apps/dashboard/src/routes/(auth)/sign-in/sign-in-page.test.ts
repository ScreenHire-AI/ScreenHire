import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { goto } from '$app/navigation';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { cancelledPrompt, fakeCredential, stubWebAuthn, unstubWebAuthn } from '../../../test-support/webauthn.js';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import Page from './+page.svelte';
import { load } from './+page.js';

// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

type Data = Parameters<typeof Page>[1]['data'];

const noLegal = { termsUrl: null, privacyUrl: null, dpaUrl: null };

const data = (overrides: Partial<Data> = {}): Data => ({
  user: null,
  methods: { providers: [{ id: 'dev-sso', displayName: 'Dev SSO' }], magicLinkEnabled: true, legal: noLegal },
  returnUrl: '/candidates',
  errorMessage: undefined,
  ...overrides,
});

const show = (pageData: Data) => render(WithTooltips, { props: { component: Page, props: { params: {}, data: pageData } } });

describe('sign-in page', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is one centred card: the mark, one h1 and a line on new accounts, the email field, then the providers, and passes axe', async () => {
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
          legal: noLegal,
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

  it('puts the "or" divider between the email form and the other ways in, only when both are offered', () => {
    const { unmount } = show(data());

    const divider = screen.getByText('or');
    expect(divider).toHaveAttribute('data-slot', 'or-divider');
    const email = screen.getByLabelText('Work email');
    const provider = screen.getByRole('link', { name: 'Continue with Dev SSO' });
    expect(email.compareDocumentPosition(divider) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(divider.compareDocumentPosition(provider) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    unmount();

    show(data({ methods: { providers: [], magicLinkEnabled: true, legal: noLegal } }));
    expect(screen.queryByText('or')).not.toBeInTheDocument();
  });

  it('has no divider when only providers are offered', () => {
    show(data({ methods: { providers: [{ id: 'google', displayName: 'Google' }], magicLinkEnabled: false, legal: noLegal } }));

    expect(screen.getByRole('link', { name: 'Continue with Google' })).toBeInTheDocument();
    expect(screen.queryByText('or')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Work email')).not.toBeInTheDocument();
  });

  it('says where the link went and moves focus to the new title', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 202 })));
    show(data());

    await fireEvent.input(screen.getByLabelText('Work email'), { target: { value: 'avery@example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

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
    await fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
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
    await fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

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
    expect(screen.getByRole('button', { name: 'Continue' })).toBeInTheDocument();
  });

  it('hides what is not configured', () => {
    show(data({ methods: { providers: [], magicLinkEnabled: true, legal: noLegal } }));

    expect(screen.queryByRole('link', { name: /Continue with/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Work email')).toBeInTheDocument();
  });

  it('says why a provider sign-in came back', () => {
    show(data({ errorMessage: "We couldn't sign you in with that provider." }));

    expect(screen.getByRole('alert')).toHaveTextContent("We couldn't sign you in with that provider.");
  });

  it('says why a provider sign-in came back when no sign-in method is left to offer', () => {
    show(data({ methods: { providers: [], magicLinkEnabled: false, legal: noLegal }, errorMessage: "We couldn't sign you in with that provider." }));

    const card = document.querySelector('[data-slot="sign-in-card"]');
    expect(card).toHaveTextContent("We couldn't sign you in with that provider.");
    expect(card).toHaveTextContent('Sign-in is not set up on this server yet.');
    // Nothing to sign in with, so no line about what signing in does.
    expect(card).not.toHaveTextContent('New here?');
  });
});

describe('the order of the card', () => {
  afterEach(() => {
    unstubWebAuthn();
    vi.unstubAllGlobals();
  });

  // What the card shows, top to bottom: the email field, Continue, the one divider, the passkey, then each provider.
  const order = () =>
    [...document.querySelectorAll<HTMLElement>('[data-slot="sign-in-card"] :is(input, button, a, [data-slot="or-divider"])')].map((element) =>
      element instanceof HTMLInputElement ? 'email field' : (element.textContent ?? '').trim(),
    );

  it.each([
    { email: true, passkey: true, providers: true, expected: ['email field', 'Continue', 'or', 'Sign in with a passkey', 'Continue with Google', 'Continue with Dev SSO'] },
    { email: true, passkey: true, providers: false, expected: ['email field', 'Continue', 'or', 'Sign in with a passkey'] },
    { email: true, passkey: false, providers: true, expected: ['email field', 'Continue', 'or', 'Continue with Google', 'Continue with Dev SSO'] },
    { email: true, passkey: false, providers: false, expected: ['email field', 'Continue'] },
    { email: false, passkey: true, providers: true, expected: ['Sign in with a passkey', 'Continue with Google', 'Continue with Dev SSO'] },
    { email: false, passkey: true, providers: false, expected: ['Sign in with a passkey'] },
    { email: false, passkey: false, providers: true, expected: ['Continue with Google', 'Continue with Dev SSO'] },
    { email: false, passkey: false, providers: false, expected: [] },
  ])('email links $email, passkeys $passkey, providers $providers', ({ email, passkey, providers, expected }) => {
    if (passkey) stubWebAuthn();
    show(
      data({
        methods: {
          providers: providers
            ? [
                { id: 'google', displayName: 'Google' },
                { id: 'dev-sso', displayName: 'Dev SSO' },
              ]
            : [],
          magicLinkEnabled: email,
          legal: noLegal,
        },
      }),
    );

    expect(order()).toEqual(expected);
    // One divider at most, and none when there is only one kind of way in.
    expect(screen.queryAllByText('or')).toHaveLength(email && (passkey || providers) ? 1 : 0);
    expect(screen.queryByText('Sign-in is not set up on this server yet.') !== null).toBe(!email && !passkey && !providers);
  });

  it('gives the email field a hint for a saved passkey, and Continue is the form submit', () => {
    show(data());

    const field = screen.getByLabelText('Work email');
    expect(field).toHaveAttribute('autocomplete', 'email webauthn');
    expect(field).toHaveAttribute('type', 'email');
    const button = screen.getByRole('button', { name: 'Continue' });
    expect(button).toHaveAttribute('type', 'submit');
    expect(button).toHaveClass('w-full');
  });
});

describe('the agreement line', () => {
  const withLegal = (legal: Data['methods']['legal']) =>
    data({ methods: { providers: [{ id: 'dev-sso', displayName: 'Dev SSO' }], magicLinkEnabled: true, legal } });
  const footer = () => document.querySelector<HTMLElement>('[data-slot="sign-in-footer"]');
  // What a sighted person reads: each link also tells a screen reader that it opens a new tab.
  const line = () => footer()?.textContent?.replaceAll(' (opens in a new tab)', '');

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('is left out when no address is configured', async () => {
    show(withLegal(noLegal));

    expect(footer()).toBeNull();
    expect(screen.queryByText(/By continuing/)).not.toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('names only the documents that have an address, each a link', async () => {
    show(withLegal({ termsUrl: 'https://legal.example.com/terms', privacyUrl: null, dpaUrl: 'https://legal.example.com/dpa' }));

    expect(line()).toBe("By continuing, you agree to ScreenHire's Terms of Service and Data Processing Agreement.");
    expect(within(footer()!).getByRole('link', { name: /^Terms of Service/ })).toHaveAttribute('href', 'https://legal.example.com/terms');
    expect(within(footer()!).getByRole('link', { name: /^Data Processing Agreement/ })).toHaveAttribute('href', 'https://legal.example.com/dpa');
    expect(within(footer()!).queryByRole('link', { name: /Privacy Policy/ })).not.toBeInTheDocument();
    await expectNoAxeViolations(document.body);
  });

  it('names one document alone', () => {
    show(withLegal({ termsUrl: null, privacyUrl: 'https://legal.example.com/privacy', dpaUrl: null }));

    expect(line()).toBe("By continuing, you agree to ScreenHire's Privacy Policy.");
  });

  it('names all three, the last after "and", at the foot of the card, and passes axe', async () => {
    show(withLegal({ termsUrl: 'https://legal.example.com/terms', privacyUrl: 'https://legal.example.com/privacy', dpaUrl: 'https://legal.example.com/dpa' }));

    expect(line()).toBe("By continuing, you agree to ScreenHire's Terms of Service, Privacy Policy and Data Processing Agreement.");
    const card = document.querySelector('[data-slot="sign-in-card"]')!;
    expect(card).toContainElement(footer());
    expect(card.lastElementChild).toBe(footer());
    for (const link of within(footer()!).getAllByRole('link')) {
      expect(link).toHaveAttribute('target', '_blank');
      expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'));
    }
    await expectNoAxeViolations(document.body);
  });

  it('is gone once the link has been sent', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 202 })));
    show(withLegal({ termsUrl: 'https://legal.example.com/terms', privacyUrl: null, dpaUrl: null }));

    await fireEvent.input(screen.getByLabelText('Work email'), { target: { value: 'avery@example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

    await screen.findByRole('heading', { level: 1, name: 'Check your email' });
    expect(footer()).toBeNull();
  });
});

describe('a saved passkey in the email field', () => {
  const optionsJson = () => Response.json({ optionsJson: JSON.stringify({ challenge: 'abc' }) });

  afterEach(() => {
    unstubWebAuthn();
    vi.mocked(goto).mockClear();
  });

  it('is waited for quietly where the browser can offer one, and the button ends the wait', async () => {
    const credentials = stubWebAuthn({ conditional: true });
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => optionsJson()));
    show(data());

    await vi.waitFor(() => expect(credentials.get).toHaveBeenCalledOnce());
    const [request] = credentials.get.mock.calls[0]!;
    expect(request).toEqual(expect.objectContaining({ mediation: 'conditional' }));
    const signal = (request as { signal: AbortSignal }).signal;
    expect(signal.aborted).toBe(false);

    await fireEvent.click(screen.getByRole('button', { name: 'Sign in with a passkey' }));

    expect(signal.aborted).toBe(true);
    await vi.waitFor(() => expect(credentials.get).toHaveBeenCalledTimes(2));
    expect(credentials.get.mock.calls[1]![0]).not.toHaveProperty('mediation');
  });

  it('signs in when the person picks one from the field', async () => {
    stubWebAuthn({ conditional: true, get: async () => fakeCredential() });
    vi.stubGlobal(
      'fetch',
      vi.fn<typeof fetch>(async (input, init) =>
        `${init?.method ?? 'GET'} ${String(input)}` === 'POST /v1/auth/passkeys/sign-in' ? new Response(null, { status: 204 }) : optionsJson(),
      ),
    );
    show(data({ returnUrl: '/candidates' }));

    await vi.waitFor(() => expect(goto).toHaveBeenCalledWith('/candidates', { invalidateAll: true }));
  });

  it('is not waited for where the browser cannot offer one, or when there is no email field', async () => {
    const credentials = stubWebAuthn({ conditional: false });
    const { unmount } = show(data());
    await Promise.resolve();
    expect(credentials.get).not.toHaveBeenCalled();
    unmount();

    unstubWebAuthn();
    const withoutField = stubWebAuthn({ conditional: true });
    show(data({ methods: { providers: [], magicLinkEnabled: false, legal: noLegal } }));
    await Promise.resolve();
    expect(withoutField.get).not.toHaveBeenCalled();
  });

  it('stops waiting when the person leaves the page', async () => {
    const credentials = stubWebAuthn({ conditional: true });
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => optionsJson()));
    const { unmount } = show(data());
    await vi.waitFor(() => expect(credentials.get).toHaveBeenCalledOnce());
    const signal = (credentials.get.mock.calls[0]![0] as { signal: AbortSignal }).signal;

    unmount();

    expect(signal.aborted).toBe(true);
    vi.unstubAllGlobals();
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

  it('puts the button first among the providers, after the "or" divider, and passes axe', async () => {
    stubWebAuthn();
    show(data());

    const button = screen.getByRole('button', { name: 'Sign in with a passkey' });
    expect(button.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    const provider = screen.getByRole('link', { name: 'Continue with Dev SSO' });
    expect(button.compareDocumentPosition(provider) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(screen.getByText('or').compareDocumentPosition(button) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await expectNoAxeViolations(document.body);
  });

  it('keeps the divider when the passkey is the only other way below the email form, and drops it when it is the only method', () => {
    stubWebAuthn();
    const { unmount } = show(data({ methods: { providers: [], magicLinkEnabled: true, legal: noLegal } }));
    expect(screen.getByRole('button', { name: 'Sign in with a passkey' })).toBeInTheDocument();
    expect(screen.getByText('or')).toHaveAttribute('data-slot', 'or-divider');
    unmount();

    show(data({ methods: { providers: [], magicLinkEnabled: false, legal: noLegal } }));
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
    expect(alert.compareDocumentPosition(screen.getByLabelText('Work email')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(alert.compareDocumentPosition(screen.getByRole('button', { name: 'Sign in with a passkey' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(alert.compareDocumentPosition(screen.getByRole('link', { name: 'Continue with Dev SSO' })) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(goto).not.toHaveBeenCalled();
    // The other ways are still there.
    expect(screen.getByLabelText('Work email')).toBeInTheDocument();
  });
});

describe('sign-in page load', () => {
  const methods = { providers: [], magicLinkEnabled: true, legal: noLegal };
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
