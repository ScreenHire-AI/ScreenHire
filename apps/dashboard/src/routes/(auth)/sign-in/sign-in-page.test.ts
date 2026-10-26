import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import Page from './+page.svelte';
import { load } from './+page.js';

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

  it('is the form template: one h1, then a panel per way to sign in, and passes axe', async () => {
    show(data());

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Sign in');
    expect(screen.getByRole('region', { name: 'Single sign-on' })).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Email link' })).toBeInTheDocument();
    expect(screen.getAllByRole('main')).toHaveLength(1);
    await expectNoAxeViolations(document.body);
  });

  it('says where the link went and moves focus to the new title', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => new Response(null, { status: 202 })));
    show(data());

    await fireEvent.input(screen.getByLabelText('Work email'), { target: { value: 'avery@example.com' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Email me a sign-in link' }));

    const heading = await screen.findByRole('heading', { level: 1, name: 'Check your email' });
    await vi.waitFor(() => expect(heading).toHaveFocus());
    expect(screen.getByRole('status')).toHaveTextContent('avery@example.com');
    expect(screen.queryByRole('region', { name: 'Single sign-on' })).not.toBeInTheDocument();
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
    expect(screen.queryByRole('region', { name: 'Single sign-on' })).not.toBeInTheDocument();
    expect(screen.getByLabelText('Work email')).toBeInTheDocument();
  });

  it('says why a provider sign-in came back', () => {
    show(data({ errorMessage: "We couldn't sign you in with that provider." }));

    expect(screen.getByRole('alert')).toHaveTextContent("We couldn't sign you in with that provider.");
  });

  it('says why a provider sign-in came back when no sign-in method is left to offer', () => {
    show(data({ methods: { providers: [], magicLinkEnabled: false }, errorMessage: "We couldn't sign you in with that provider." }));

    const panel = screen.getByRole('region', { name: 'Sign-in methods' });
    expect(panel).toHaveTextContent("We couldn't sign you in with that provider.");
    expect(panel).toHaveTextContent('Sign-in is not set up on this server yet.');
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

  it('shows no message without an error', async () => {
    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => Response.json(methods)));

    await expect(load(event(''))).resolves.toEqual(expect.objectContaining({ methods, errorMessage: undefined }));
  });
});
