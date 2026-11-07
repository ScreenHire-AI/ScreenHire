import type { CurrentUser } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, jobs, user, userAs } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import SetupChecklist from './setup-checklist.svelte';

const storageKey = `screenhire:setup-dismissed:${user.organization!.id}`;

beforeEach(() => localStorage.removeItem(storageKey));
afterEach(() => vi.unstubAllGlobals());

const newOrganization = {
  id: user.organization!.id,
  name: 'Brightwater Studio',
  slug: 'brightwater',
  homeRegion: 'eu',
  careersUrl: 'https://careers.example.com/brightwater',
  createdAt: '2026-11-01T09:00:00Z',
  lastUpdated: { at: '2026-11-01T09:00:00Z', by: null, created: true },
  questionsPerPage: 3,
  embedOrigins: [],
};
const noBranding = { brandColor: null, secondColor: null, headingFont: null, logo: null, favicon: null, sharingImage: null, lastUpdated: { at: null, by: 'ScreenHire' } };
const member = (id: string) => ({ id, email: `${id}@example.com`, displayName: id, role: 'Owner' });
const draftJobs = jobs.map((job) => ({ ...job, publishedAt: null }));

interface Answers {
  members?: unknown[];
  invitations?: unknown[];
  branding?: unknown;
  organization?: unknown;
  sendingDomain?: unknown;
  jobs?: unknown[];
}

function renderChecklist(answers: Answers = {}, as: CurrentUser = user) {
  const fetchMock = vi.fn(
    apiStub({
      'GET /v1/organization/members': () => Response.json(answers.members ?? [member('avery')]),
      'GET /v1/organization/invitations': () => Response.json(answers.invitations ?? []),
      'GET /v1/organization/branding': () => Response.json(answers.branding ?? noBranding),
      'GET /v1/organization': () => Response.json(answers.organization ?? newOrganization),
      'GET /v1/organization/sending-domain': () => Response.json(answers.sendingDomain ?? { available: true, sandbox: false, domain: null }),
      'GET /v1/jobs': () => Response.json(answers.jobs ?? draftJobs),
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: SetupChecklist, props: { user: as } } });
  return fetchMock;
}

const panel = () => screen.findByRole('region', { name: 'Set up your organization' });

describe('the setup checklist panel', () => {
  it('shows an Owner the five steps as links to their pages, all open for a new organization', async () => {
    renderChecklist();
    const steps = within(await screen.findByRole('list', { name: 'Setup steps' }));

    expect(steps.getAllByRole('link').map((link) => [link.textContent?.trim(), link.getAttribute('href')])).toEqual([
      ['Invite your team', '/settings/team'],
      ['Set your branding', '/settings/branding'],
      ['Set up your careers site', '/settings/careers-site'],
      ['Add your email domain', '/settings/sending-domain'],
      ['Publish your first job', '/jobs/new'],
    ]);
    expect(steps.queryByText('Done')).toBeNull();
    await expectNoAxeViolations();
  });

  it('ticks a step from the organization data, and says so to a screen reader', async () => {
    renderChecklist({ members: [member('avery'), member('jordan')], branding: { ...noBranding, brandColor: 'teal' } });
    const steps = within(await screen.findByRole('list', { name: 'Setup steps' }));

    await waitFor(() => expect(steps.getAllByText('Done')).toHaveLength(2));
    const team = steps.getByRole('link', { name: 'Invite your team' }).closest('li')!;
    expect(team).toHaveTextContent('Done');
    expect(steps.getByRole('link', { name: 'Publish your first job' }).closest('li')).not.toHaveTextContent('Done');
  });

  it('is an Admin as well, and leaves out the email step where this installation cannot send mail', async () => {
    renderChecklist({ sendingDomain: { available: false, sandbox: false, domain: null } }, userAs('Admin'));
    const steps = within(await screen.findByRole('list', { name: 'Setup steps' }));

    expect(steps.queryByRole('link', { name: 'Add your email domain' })).toBeNull();
  });

  it('goes once every step is done', async () => {
    const fetchMock = renderChecklist({
      members: [member('avery'), member('jordan')],
      branding: { ...noBranding, brandColor: 'teal' },
      organization: { ...newOrganization, embedOrigins: ['https://www.example.com'] },
      sendingDomain: { available: true, sandbox: false, domain: { status: 'Verified' } },
      jobs: [{ ...jobs[0], publishedAt: '2026-11-03T09:00:00Z' }],
    });

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(6));
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Set up your organization' })).toBeNull());
    expect(screen.queryByRole('list', { name: 'Setup steps' })).toBeNull();
  });

  // Preferences remember a dismissal for the page's life, so each of these uses an organization of its own.
  const inOrganization = (id: string): CurrentUser => ({ ...user, organization: { ...user.organization!, id } });

  it('can be dismissed, which stays for this organization in this browser', async () => {
    const dismissing = inOrganization('01a157d4-0000-7000-8000-0000000000d1');
    renderChecklist({}, dismissing);
    await panel();

    await fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));

    expect(screen.queryByRole('region', { name: 'Set up your organization' })).toBeNull();
    expect(localStorage.getItem(`screenhire:setup-dismissed:${dismissing.organization!.id}`)).toBe('on');
    expect(localStorage.getItem(storageKey)).toBeNull();
  });

  it('stays hidden, and asks nothing, once dismissed', async () => {
    const dismissed = inOrganization('01a157d4-0000-7000-8000-0000000000d2');
    localStorage.setItem(`screenhire:setup-dismissed:${dismissed.organization!.id}`, 'on');
    const fetchMock = renderChecklist({}, dismissed);

    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByRole('region', { name: 'Set up your organization' })).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
    localStorage.removeItem(`screenhire:setup-dismissed:${dismissed.organization!.id}`);
  });

  it.each(['Recruiter', 'Viewer'] as const)('is not shown to a %s, who cannot do the steps, and asks nothing', async (role) => {
    const fetchMock = renderChecklist({}, userAs(role));

    await Promise.resolve();
    expect(screen.queryByRole('region', { name: 'Set up your organization' })).toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows nothing while an answer is still on its way, so a finished step never flashes open', async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => (release = resolve));
    vi.stubGlobal(
      'fetch',
      vi.fn(
        apiStub({
          'GET /v1/organization/members': () => Response.json([member('avery')]),
          'GET /v1/organization/invitations': () => Response.json([]),
          'GET /v1/organization/branding': () => Response.json(noBranding),
          'GET /v1/organization': () => Response.json(newOrganization),
          'GET /v1/organization/sending-domain': () => Response.json({ available: true, sandbox: false, domain: null }),
          'GET /v1/jobs': async () => {
            await gate;
            return Response.json(draftJobs);
          },
        }),
      ),
    );
    render(WithQueryClient, { props: { component: SetupChecklist, props: { user } } });

    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByRole('region', { name: 'Set up your organization' })).toBeNull();

    release();
    await panel();
  });
});
