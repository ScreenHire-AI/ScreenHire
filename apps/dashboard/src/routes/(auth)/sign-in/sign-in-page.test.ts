import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import Page from './+page.svelte';

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
