import type { ApplicationIntegrity } from '@screenhire/api-client';
import { QueryClient } from '@tanstack/svelte-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { invalidatePath } from '#lib/query.js';
import { invalidations } from '#lib/realtime.js';
import { apiStub, user } from '../../test-support/fixtures.js';
import {
  authenticityAssessmentId,
  botIntegrity,
  genuineIntegrity,
  integrityApplicationId,
  pendingIntegrity,
} from '../../test-support/integrity-fixtures.js';
import IntegrityHarness from '../../test-support/integrity-harness.svelte';
import WithQueryClient from '../../test-support/with-query-client.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/') }, navigating: { to: null } }));

const base = `/v1/applications/${integrityApplicationId}/integrity`;

/** Serves the integrity panel from `state`, which a POST stub can replace, and records each POST body. */
function serve(initial: ApplicationIntegrity, posts: Record<string, (body: unknown) => { status: number; body: unknown; next?: ApplicationIntegrity }> = {}) {
  let state = initial;
  const sent: { route: string; body: unknown }[] = [];
  const routes: Record<string, (init: RequestInit | undefined) => Response> = { [`GET ${base}`]: () => Response.json(state) };
  for (const [route, answer] of Object.entries(posts)) {
    routes[route] = (init) => {
      const body = JSON.parse(String(init?.body));
      sent.push({ route, body });
      const result = answer(body);
      if (result.next) state = result.next;
      return Response.json(result.body, { status: result.status });
    };
  }
  vi.stubGlobal('fetch', vi.fn(apiStub(routes)));
  return sent;
}

function renderPanel() {
  return render(WithQueryClient, { props: { component: IntegrityHarness, props: { applicationId: integrityApplicationId } } });
}

const section = (name: string) => screen.getByRole('region', { name });

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('integrity panel', () => {
  it('shows each assessment with its summary, next step, reasons with evidence, context and counter-evidence', async () => {
    serve(botIntegrity());
    renderPanel();

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(within(authenticity).getByText('Authenticity: Verification recommended')).toBeInTheDocument();
    expect(authenticity).toHaveTextContent('3 observations from network and automation checks back each other up.');
    expect(authenticity).toHaveTextContent('Suggested next step: Confirm the candidate is who they say they are');
    expect(within(authenticity).getByRole('heading', { name: 'Reasons 3' })).toBeInTheDocument();
    const codes = [...authenticity.querySelectorAll('[data-signal]')].map((item) => item.getAttribute('data-signal'));
    expect(codes).toEqual(['AUTO_SUBMIT_VELOCITY', 'AUTO_WEBDRIVER', 'NET_DATACENTER_IP', 'NET_ANON_PROXY']);
    expect(within(authenticity).getByRole('heading', { name: 'Context, not counted toward the level' })).toBeInTheDocument();
    expect(authenticity.querySelector('[data-signal="AUTO_SUBMIT_VELOCITY"] [data-evidence-kind="Metric"]')).toHaveTextContent('6 seconds');
    const counter = authenticity.querySelector('[data-signal="NET_DATACENTER_IP"] [data-counter-evidence]')!;
    expect(counter).toHaveTextContent('Counter-evidence: The candidate gave a phone number in the same country.');
    expect(counter).toHaveTextContent('Phone country: United Kingdom');
  });

  it("shows AI assistance as information, with the job policy and not the candidate's declaration", async () => {
    serve(botIntegrity());
    renderPanel();

    const ai = await screen.findByRole('region', { name: 'AI assistance' });
    expect(within(ai).getByRole('heading', { level: 3 })).toHaveTextContent('AI assistance');
    expect(ai).toHaveTextContent('Informational');
    expect(ai).toHaveTextContent('Job policy: Allowed with disclosure');
    // The declaration has its own place on the page, in Declarations (section 16).
    expect(ai).not.toHaveTextContent(/Declared AI use/);
    expect(ai.querySelector('[data-evidence-kind="Quote"]')).toBeInTheDocument();
    expect(ai.querySelector('[data-evidence-kind="TextComparison"]')).toBeInTheDocument();
    expect(ai.querySelector('[data-slot="badge"]')!.className).not.toMatch(/status-(review|verify)/);
  });

  it('offers no reject control, no score and no percentage, and says no decision was made', async () => {
    serve(botIntegrity());
    const { container } = renderPanel();
    await screen.findByRole('region', { name: 'Authenticity' });

    for (const control of [...screen.queryAllByRole('button'), ...screen.queryAllByRole('link')]) {
      expect(control.textContent).not.toMatch(/reject|decline|disqualif/i);
    }
    expect(container.textContent).not.toMatch(/%|score/i);
    expect(section('Integrity')).toHaveTextContent('No automated decision has been made. Every action is logged.');
  });

  it('hides request verification until it exists', async () => {
    serve(botIntegrity());
    renderPanel();
    await screen.findByRole('region', { name: 'Authenticity' });

    expect(screen.queryByRole('button', { name: /Request verification/ })).not.toBeInTheDocument();
    expect(section('Integrity')).not.toHaveTextContent(/phase 2/i);
  });

  it('keeps a calm next step as a quiet line and a concern in an inset', async () => {
    serve(genuineIntegrity());
    const { unmount } = renderPanel();
    const calm = await screen.findByRole('region', { name: 'Authenticity' });
    expect(within(calm).getByText('Suggested next step: No action needed.')).not.toHaveClass('bg-secondary');
    unmount();

    serve(botIntegrity());
    renderPanel();
    const concern = await screen.findByRole('region', { name: 'Authenticity' });
    expect(within(concern).getByText(/Confirm the candidate is who they say they are/)).toHaveClass('bg-secondary');
  });

  it('dismisses an assessment with a reason, which lands in the review log', async () => {
    const dismissed: ApplicationIntegrity = {
      ...botIntegrity(),
      authenticity: {
        ...botIntegrity().authenticity!,
        dismissal: { actionId: 'a1', reason: 'VerifiedOutsideScreenHire', note: 'Video call on Monday.', actorUserId: user.id, actorName: 'Avery Quinn', dismissedAt: '2026-10-20T11:00:00Z' },
      },
      reviewLog: [
        { id: 'a1', type: 'Dismissed', assessmentKind: 'Authenticity', assessmentId: authenticityAssessmentId, dismissReason: 'VerifiedOutsideScreenHire', note: 'Video call on Monday.', actorUserId: user.id, actorName: 'Avery Quinn', occurredAt: '2026-10-20T11:00:00Z' },
      ],
    };
    const sent = serve(botIntegrity(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: dismissed, next: dismissed }),
    });
    renderPanel();

    await fireEvent.click(await screen.findByRole('button', { name: 'Dismiss authenticity with a reason' }));
    const dialog = await screen.findByRole('dialog', { name: 'Dismiss this authenticity assessment?' });
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Verified outside ScreenHire' }));
    await fireEvent.input(within(dialog).getByLabelText('Note (optional)'), { target: { value: 'Video call on Monday.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/dismiss`, body: { reason: 'VerifiedOutsideScreenHire', note: 'Video call on Monday.' } }]));
    await screen.findByText(/Dismissed authenticity: Verified outside ScreenHire. The decision is in the review log./);
    const log = await screen.findByRole('region', { name: 'Review log' });
    await within(log).findByText(/Dismissed authenticity: Verified outside ScreenHire/);
    expect(log).toHaveTextContent('by you');
    expect(log).toHaveTextContent('"Video call on Monday."');
    // The level stays as assessed; the chip says it was dismissed and the button is gone.
    const authenticity = section('Authenticity');
    expect(within(authenticity).getByText('Authenticity: Verification recommended, dismissed after review')).toBeInTheDocument();
    expect(within(authenticity).queryByRole('button', { name: /Dismiss/ })).not.toBeInTheDocument();
  });

  it('asks for a note before dismissing for another reason', async () => {
    const sent = serve(botIntegrity(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: botIntegrity() }),
    });
    renderPanel();

    await fireEvent.click(await screen.findByRole('button', { name: 'Dismiss authenticity with a reason' }));
    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Other reason' }));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    expect(await within(dialog).findByText('Say why, in a few words, when the reason is "Other".')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Note (required)')).toHaveAttribute('aria-invalid', 'true');
    expect(sent).toEqual([]);
  });

  it('closes and says so when the assessment was already dismissed or replaced', async () => {
    serve(botIntegrity(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 409, body: { title: 'Conflict', status: 409 } }),
    });
    renderPanel();

    await fireEvent.click(await screen.findByRole('button', { name: 'Dismiss authenticity with a reason' }));
    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    await screen.findByText('This assessment was already dismissed, or a newer one replaced it. The panel now shows the latest.');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('keeps the dialog open with a message when the API fails, and opens clean the next time', async () => {
    serve(botIntegrity(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 500, body: { title: 'Server error', status: 500 } }),
    });
    renderPanel();

    await fireEvent.click(await screen.findByRole('button', { name: 'Dismiss authenticity with a reason' }));
    let dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Explained by the candidate' }));
    await fireEvent.input(within(dialog).getByLabelText('Note (optional)'), { target: { value: 'Said so on the phone.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent("We couldn't record the dismissal just now. Try again in a minute.");
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await fireEvent.click(screen.getByRole('button', { name: 'Dismiss authenticity with a reason' }));
    dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('radio', { name: 'The observations do not apply' })).toBeChecked();
    expect(within(dialog).getByLabelText('Note (optional)')).toHaveValue('');
  });

  it('shows a rejected note under the field', async () => {
    serve(botIntegrity(), {
      [`POST ${base}/notes`]: () => ({
        status: 400,
        body: { title: 'One or more validation errors occurred.', status: 400, errors: { note: ['The note is too long.'] } },
      }),
    });
    renderPanel();

    await fireEvent.click(await screen.findByRole('button', { name: 'Add review note' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a review note' });
    await fireEvent.input(within(dialog).getByLabelText('Note'), { target: { value: 'A note.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add review note' }));

    expect(await within(dialog).findByText('The note is too long.')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Note')).toHaveAttribute('aria-invalid', 'true');
  });

  it('keeps the evidence on screen when a refetch fails', async () => {
    let failing = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(apiStub({ [`GET ${base}`]: () => (failing ? Response.json({ title: 'Server error', status: 500 }, { status: 500 }) : Response.json(botIntegrity())) })),
    );
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(WithQueryClient, { props: { component: IntegrityHarness, props: { applicationId: integrityApplicationId }, client } });
    await screen.findByRole('region', { name: 'Authenticity' });

    failing = true;
    await invalidatePath(client, '/v1/applications');

    expect(await screen.findByText(/Could not load the integrity checks/)).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Authenticity' })).toHaveTextContent('Suggested next step');
  });

  it('adds a note to the review log', async () => {
    const noted: ApplicationIntegrity = {
      ...botIntegrity(),
      reviewLog: [{ id: 'n1', type: 'NoteAdded', assessmentKind: null, assessmentId: null, dismissReason: null, note: 'Asked for references.', actorUserId: 'someone-else', actorName: 'Jordan Park', occurredAt: '2026-10-20T11:00:00Z' }],
    };
    const sent = serve(botIntegrity(), { [`POST ${base}/notes`]: () => ({ status: 200, body: noted, next: noted }) });
    renderPanel();

    await fireEvent.click(await screen.findByRole('button', { name: 'Add review note' }));
    const dialog = await screen.findByRole('dialog', { name: 'Add a review note' });
    await fireEvent.input(within(dialog).getByLabelText('Note'), { target: { value: '  Asked for references. ' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Add review note' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/notes`, body: { note: 'Asked for references.' } }]));
    const log = screen.getByRole('region', { name: 'Review log' });
    await within(log).findByText('"Asked for references."');
    expect(log).toHaveTextContent('Note by Jordan Park');
  });

  it('has nothing to dismiss when there are no concerns', async () => {
    serve(genuineIntegrity());
    renderPanel();

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(within(authenticity).getByText('Authenticity: No concerns')).toBeInTheDocument();
    expect(authenticity).toHaveTextContent('Suggested next step: No action needed.');
    expect(screen.queryByRole('button', { name: /Dismiss/ })).not.toBeInTheDocument();
    expect(within(section('Review log')).getByText('No review actions yet.')).toBeInTheDocument();
  });

  it('turns from pending to assessed when IntegrityAssessed arrives, without a reload', async () => {
    let assessed = false;
    vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET ${base}`]: () => Response.json(assessed ? botIntegrity() : pendingIntegrity()) })));
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(WithQueryClient, { props: { component: IntegrityHarness, props: { applicationId: integrityApplicationId }, client } });
    await screen.findByText(/Checks are still running/);

    // What the shell's live connection does with the event (#lib/realtime.ts).
    assessed = true;
    await Promise.all((invalidations.IntegrityAssessed ?? []).map((path) => invalidatePath(client, path)));

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(within(authenticity).getByText('Authenticity: Verification recommended')).toBeInTheDocument();
    expect(screen.queryByText(/Checks are still running/)).not.toBeInTheDocument();
  });

  it('says the checks are still running while they are', async () => {
    serve(pendingIntegrity());
    renderPanel();

    expect(await screen.findByText(/Checks are still running/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Authenticity' })).not.toBeInTheDocument();
  });
});
