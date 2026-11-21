import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import { goto } from '$app/navigation';
import { preferences } from '#lib/preferences.svelte.js';
import Page from './+page.svelte';

vi.mock('$app/navigation', () => ({ goto: vi.fn(), beforeNavigate: vi.fn(), afterNavigate: vi.fn(), onNavigate: vi.fn() }));

const session = vi.hoisted(() => ({ signOutAndRedirect: vi.fn(async () => {}) }));
vi.mock(import('#lib/session.js'), () => session);

type Data = Parameters<typeof Page>[1]['data'];
const data = { user: { id: 'user-avery', email: 'avery@example.com' } } as Data;
const show = (shown: Data = data) => render(WithTooltips, { props: { component: Page, props: { params: {}, data: shown } } });

const meta = { name: 'ScreenHire', version: '1', environment: 'Development', apiOrigin: 'https://api.example.com', careersUrlPattern: 'https://{tenant}.careers.example.com', trialDays: 14 };

describe('onboarding page', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json(meta)));
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  it('is the form template: the account under the title, the fields in one panel, and passes axe', async () => {
    show();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Set up your organization');
    expect(screen.getByText('avery@example.com')).toBeInTheDocument();
    const panel = screen.getByRole('region', { name: 'Organization' });
    expect(panel).toContainElement(screen.getByLabelText('Organization name'));
    expect(panel).toContainElement(screen.getByRole('button', { name: 'Create organization' }));
    await expectNoAxeViolations(document.body);
  });

  it('fills the careers page address from the name until it is edited', async () => {
    show();

    await fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'Harbour Lane Studio' } });
    expect(screen.getByLabelText('Careers page address')).toHaveValue('harbour-lane-studio');

    await fireEvent.input(screen.getByLabelText('Careers page address'), { target: { value: 'harbour' } });
    await fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'Harbour Lane' } });
    expect(screen.getByLabelText('Careers page address')).toHaveValue('harbour');
  });

  it('shows the full careers address as the name is typed, from the host the API reports', async () => {
    show();

    const hint = () => document.getElementById('slug-hint')!;
    expect(hint()).toHaveTextContent('Lower-case letters, numbers and hyphens');

    await fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'Harbour Lane Studio' } });
    await waitFor(() => expect(hint()).toHaveTextContent('https://harbour-lane-studio.careers.example.com'));

    await fireEvent.input(screen.getByLabelText('Careers page address'), { target: { value: 'harbour' } });
    expect(hint()).toHaveTextContent('https://harbour.careers.example.com');
    await expectNoAxeViolations(document.body);
  });

  it('keeps the plain hint when the API does not answer', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('offline'); }));
    show();

    await fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'Harbour Lane' } });
    expect(document.getElementById('slug-hint')).toHaveTextContent('Lower-case letters, numbers and hyphens');
  });

  it('asks the bell to open itself at the setup steps, once, when the organization is created', async () => {
    show();

    await fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'Harbour Lane' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));

    await waitFor(() => expect(goto).toHaveBeenCalledWith('/', { invalidateAll: true }));
    expect(preferences.takeSetupTour('user-avery')).toBe(true);
    expect(preferences.takeSetupTour('user-avery')).toBe(false);
  });

  it('asks for the name first, empty when the account has none and filled from the provider when it gave one', async () => {
    const { unmount } = show();
    const panel = screen.getByRole('region', { name: 'Organization' });
    expect(within(panel).getAllByRole('textbox').map((box) => box.id)).toEqual(['name', 'organizationName', 'slug']);
    expect(screen.getByLabelText('Your name')).toHaveValue('');
    expect(screen.getByLabelText('Your name')).toBeRequired();
    unmount();

    show({ user: { ...data.user, name: 'Avery Quinn' } } as Data);
    expect(screen.getByLabelText('Your name')).toHaveValue('Avery Quinn');
  });

  it('sends the name with the organization, and shows the problem under it when it is missing', async () => {
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
      String(input).includes('/v1/auth/onboarding') && !JSON.parse(String(init?.body)).name
        ? Response.json({ errors: { name: ['Enter your name.'] } }, { status: 400 })
        : Response.json(meta));
    vi.stubGlobal('fetch', fetch);
    show();

    await fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'Harbour Lane' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));
    expect(await screen.findByText('Enter your name.')).toBeInTheDocument();
    expect(goto).not.toHaveBeenCalled();

    await fireEvent.input(screen.getByLabelText('Your name'), { target: { value: 'Avery Quinn' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Create organization' }));
    await waitFor(() => expect(goto).toHaveBeenCalledWith('/', { invalidateAll: true }));
    const sent = fetch.mock.calls.filter(([url]) => String(url).includes('/v1/auth/onboarding')).map(([, init]) => JSON.parse(String(init?.body)));
    expect(sent.at(-1)).toEqual({ name: 'Avery Quinn', organizationName: 'Harbour Lane', slug: 'harbour-lane' });
  });

  it('says how long the free trial lasts under the button, from the days the API reports', async () => {
    show();

    const button = screen.getByRole('button', { name: 'Create organization' });
    const line = await screen.findByText('Your free trial lasts 14 days and needs no card.');
    expect(button.compareDocumentPosition(line) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await expectNoAxeViolations(document.body);
  });

  it('says nothing about a trial where there is none (billing off) or the days did not arrive', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => Response.json({ ...meta, trialDays: 0 })));
    show();

    await waitFor(() => expect(document.getElementById('slug-hint')).toBeInTheDocument());
    await fireEvent.input(screen.getByLabelText('Organization name'), { target: { value: 'Harbour Lane' } });
    await waitFor(() => expect(document.getElementById('slug-hint')).toHaveTextContent('https://harbour-lane.careers.example.com'));
    expect(screen.queryByText(/free trial/)).not.toBeInTheDocument();
  });

  it('signs out from the header band', async () => {
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
    expect(session.signOutAndRedirect).toHaveBeenCalledOnce();
  });
});
