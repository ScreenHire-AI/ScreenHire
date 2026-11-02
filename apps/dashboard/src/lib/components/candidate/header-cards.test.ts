import type { ApplicationDetail, ApplicationSummary, CandidateDetail } from '@screenhire/api-client';
import { render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import { apiStub, applications } from '../../../test-support/fixtures.js';
import WithQueryClient from '../../../test-support/with-query-client.svelte';
import HeaderCards from './header-cards.svelte';

const candidateId = '01a157d4-0000-7000-8000-000000000204';
const thisApplication = '01a157d4-0000-7000-8000-000000000300';

const detail = {
  id: thisApplication,
  jobId: '01a157d4-0000-7000-8000-000000000002',
  jobTitle: 'Support Engineer',
  candidate: { id: candidateId, fullName: 'Casey Morgan', email: 'casey@example.com', phone: '+44 20 7946 0958', createdAt: '2026-10-01T09:00:00Z' },
  stage: { id: 'screening', name: 'Screening', kind: 'Screening' },
  submittedAt: '2026-10-20T09:00:00Z',
  source: 'CareersSite',
  rating: null,
} as unknown as ApplicationDetail;

const summary = (id: string, jobTitle: string): ApplicationSummary => ({ ...applications[0]!, id, candidateId, jobTitle });

const profile = (others: ApplicationSummary[]): CandidateDetail => ({
  id: candidateId,
  fullName: 'Casey Morgan',
  firstName: 'Casey',
  lastName: 'Morgan',
  email: 'casey@example.com',
  phone: null,
  createdAt: '2026-10-01T09:00:00Z',
  applications: [summary(thisApplication, 'Support Engineer'), ...others],
});

const stubProfile = (others: ApplicationSummary[]) =>
  vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET /v1/candidates/${candidateId}`]: () => Response.json(profile(others)) })));

// `emails` is undefined until the candidate's emails have loaded; `scorecards` is the list the page passes, empty or not.
const show = (patch: Partial<ApplicationDetail> = {}, emails?: unknown[]) =>
  render(WithQueryClient, { props: { component: HeaderCards, props: { detail: { ...detail, ...patch }, emails, scorecards: [] } } });

afterEach(() => vi.unstubAllGlobals());

describe('candidate header cards', () => {
  describe('Contact', () => {
    it('links the email and the phone, each with its own Copy button', async () => {
      stubProfile([]);
      show();

      const contact = screen.getByRole('group', { name: 'Contact' });
      expect(within(contact).getByRole('link', { name: 'casey@example.com' })).toHaveAttribute('href', 'mailto:casey@example.com');
      expect(within(contact).getByRole('link', { name: '+44 20 7946 0958' })).toHaveAttribute('href', 'tel:+44 20 7946 0958');
      expect(within(contact).getByRole('button', { name: 'Copy email address' })).toBeInTheDocument();
      expect(within(contact).getByRole('button', { name: 'Copy phone number' })).toBeInTheDocument();
    });

    it('has no phone line and no Copy button for a phone when the candidate gave none', () => {
      stubProfile([]);
      show({ candidate: { ...detail.candidate, phone: null } });

      const contact = screen.getByRole('group', { name: 'Contact' });
      expect(within(contact).queryByRole('link', { name: /^\+/ })).toBeNull();
      expect(within(contact).queryByRole('button', { name: 'Copy phone number' })).toBeNull();
    });

    it('says why the address is flagged, beside the address', () => {
      stubProfile([]);
      show({ candidate: { ...detail.candidate, emailIssue: 'Bounced' } });

      expect(within(screen.getByRole('group', { name: 'Contact' })).getByText('Email bounced')).toBeInTheDocument();
    });

    it('says last contacted for a saved candidate, by the newest email, and not contacted yet when there is none', () => {
      stubProfile([]);
      const { unmount } = show({ keptInMindAt: '2026-10-29T12:00:00Z' }, [{ sentAt: '2020-01-01T09:00:00Z' }, { sentAt: new Date(Date.now() - 3 * 86_400_000).toISOString() }]);

      expect(screen.getByRole('group', { name: 'Contact' })).toHaveTextContent('Last contacted 3 days ago');
      unmount();

      show({ keptInMindAt: '2026-10-29T12:00:00Z' }, []);
      expect(screen.getByRole('group', { name: 'Contact' })).toHaveTextContent('Not contacted yet');
    });

    it('says nothing about contact until the emails have loaded, and never for an open application', () => {
      stubProfile([]);
      const { unmount } = show({ keptInMindAt: '2026-10-29T12:00:00Z' }, undefined);
      expect(screen.getByRole('group', { name: 'Contact' })).not.toHaveTextContent(/contacted/i);
      unmount();

      show({}, []);
      expect(screen.getByRole('group', { name: 'Contact' })).not.toHaveTextContent(/contacted/i);
    });
  });

  describe('Application', () => {
    it('links the job and says when and where from', () => {
      stubProfile([]);
      show({ sourceSite: null });

      const card = screen.getByRole('group', { name: 'Application' });
      expect(within(card).getByRole('link', { name: 'Support Engineer' })).toHaveAttribute('href', `/jobs/${detail.jobId}`);
      expect(card).toHaveTextContent(/Applied .+ via the careers page$/);
    });

    it('says a recruiter added them, without a source', () => {
      stubProfile([]);
      show({ source: 'Recruiter' });

      expect(screen.getByRole('group', { name: 'Application' })).toHaveTextContent(/Added by a recruiter .+$/);
      expect(screen.getByRole('group', { name: 'Application' })).not.toHaveTextContent('via');
    });
  });

  describe('the third card', () => {
    it('links to the candidate profile before the count arrives, and shows no count yet', async () => {
      vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(() => {})));
      show();

      const link = within(screen.getByRole('group', { name: 'Also applied to' })).getByRole('link', { name: 'Candidate profile' });
      expect(link).toHaveAttribute('href', `/candidates/${candidateId}`);
      expect(screen.getByRole('group', { name: 'Also applied to' })).not.toHaveTextContent(/other job/);
    });

    it.each([
      [[], 'No other jobs'],
      [[summary('a2', 'Senior .NET Engineer')], '1 other job'],
      [[summary('a2', 'Senior .NET Engineer'), summary('a3', 'Product Designer')], '2 other jobs'],
    ])('counts the other applications, leaving this one out: %j', async (others, text) => {
      stubProfile(others);
      show();

      expect(await within(screen.getByRole('group', { name: 'Also applied to' })).findByText(text)).toBeInTheDocument();
    });

    it('keeps the link without a count when the candidate cannot be loaded', async () => {
      const fetchMock = vi.fn(apiStub({ [`GET /v1/candidates/${candidateId}`]: () => Response.json({ title: 'Not found' }, { status: 404 }) }));
      vi.stubGlobal('fetch', fetchMock);
      show();

      await waitFor(() => expect(fetchMock).toHaveBeenCalled());
      const card = screen.getByRole('group', { name: 'Also applied to' });
      expect(within(card).getByRole('link', { name: 'Candidate profile' })).toBeInTheDocument();
      expect(card).not.toHaveTextContent(/other job/);
    });

    it('is the Rating once someone scored them, and never says Not rated yet', async () => {
      stubProfile([]);
      const { container } = show({ rating: { state: 'Rated', value: 4.6, scorecards: 1 } });

      const card = screen.getByRole('group', { name: 'Rating' });
      expect(within(card).getByRole('button', { name: 'Rated 4.6 out of 5 from 1 scorecard' })).toBeInTheDocument();
      expect(screen.queryByRole('group', { name: 'Also applied to' })).toBeNull();
      expect(container).not.toHaveTextContent('Not rated yet');
      await expectNoAxeViolations(container);
    });

    it('is Also applied to, with no rating text at all, while nobody has scored them', () => {
      stubProfile([]);
      const { container } = show({ rating: { state: 'NotRated', scorecards: 0 } });

      expect(screen.getByRole('group', { name: 'Also applied to' })).toBeInTheDocument();
      expect(screen.queryByRole('group', { name: 'Rating' })).toBeNull();
      expect(container).not.toHaveTextContent(/not rated/i);
    });

    it('is the Rating for a reviewer whose own scorecard comes first, in the words the rating uses', () => {
      stubProfile([]);
      show({ rating: { state: 'AfterYourScorecard', scorecards: 0 } });

      expect(within(screen.getByRole('group', { name: 'Rating' })).getByText('Rating after your scorecard')).toBeInTheDocument();
    });
  });
});
