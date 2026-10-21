import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import Page from './+page.svelte';

const session = vi.hoisted(() => ({ signOutAndRedirect: vi.fn(async () => {}) }));
vi.mock(import('#lib/session.js'), () => session);

type Data = Parameters<typeof Page>[1]['data'];
const data = { user: { email: 'avery@example.com' } } as Data;
const show = () => render(WithTooltips, { props: { component: Page, props: { params: {}, data } } });

describe('onboarding page', () => {
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

  it('signs out from the header band', async () => {
    show();

    await fireEvent.click(screen.getByRole('button', { name: 'Sign out' }));
    expect(session.signOutAndRedirect).toHaveBeenCalledOnce();
  });
});
