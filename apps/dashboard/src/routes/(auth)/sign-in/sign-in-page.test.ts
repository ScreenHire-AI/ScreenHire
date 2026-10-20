import { render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
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

describe('sign-in page', () => {
  it('offers each configured provider, returning to where the visitor was going', () => {
    render(Page, { props: { params: {}, data: data() } });

    const link = screen.getByRole('link', { name: 'Continue with Dev SSO' });
    expect(link).toHaveAttribute('href', '/v1/auth/challenge/dev-sso?returnUrl=%2Fcandidates');
    expect(link).toHaveAttribute('data-sveltekit-reload');
  });

  it('offers the email link form when the server can send email', () => {
    render(Page, { props: { params: {}, data: data() } });

    expect(screen.getByLabelText('Work email')).toHaveAttribute('type', 'email');
    expect(screen.getByRole('button', { name: 'Email me a sign-in link' })).toBeInTheDocument();
  });

  it('hides what is not configured', () => {
    render(Page, { props: { params: {}, data: data({ methods: { providers: [], magicLinkEnabled: true } }) } });

    expect(screen.queryByRole('link', { name: /Continue with/ })).not.toBeInTheDocument();
    expect(screen.queryByText('or')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Work email')).toBeInTheDocument();
  });

  it('says why a provider sign-in came back', () => {
    render(Page, { props: { params: {}, data: data({ errorMessage: "We couldn't sign you in with that provider." }) } });

    expect(screen.getByRole('alert')).toHaveTextContent("We couldn't sign you in with that provider.");
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
