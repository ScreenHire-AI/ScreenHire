import type { ApplicationIntegrity, ApplicationSource, CurrentUser, IntegrityReasonExplanation, IntegrityReviewEntry } from '@screenhire/api-client';
import { QueryClient } from '@tanstack/svelte-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { invalidatePath } from '#lib/query.js';
import { invalidations } from '#lib/realtime.js';
import { apiStub, user, userAs } from '../../test-support/fixtures.js';
import {
  authenticityAssessmentId,
  botIntegrity,
  genuineIntegrity,
  integrityApplicationId,
  pendingIntegrity,
} from '../../test-support/integrity-fixtures.js';
import IntegrityHarness from '../../test-support/integrity-harness.svelte';
import WithQueryClient from '../../test-support/with-query-client.svelte';

// Who is signed in; a test can switch to a role without the review permission.
const signedIn = vi.hoisted(() => ({ user: null as CurrentUser | null }));
vi.mock('$app/state', () => ({
  page: {
    get data() {
      return { user: signedIn.user ?? user };
    },
    url: new URL('http://dashboard.test/'),
  },
  navigating: { to: null },
}));

const base = `/v1/applications/${integrityApplicationId}/integrity`;

/** Serves the integrity panel from `state`, which a POST stub can replace, and records each POST body. */
function serve(initial: ApplicationIntegrity, posts: Record<string, (body: unknown) => { status: number; body: unknown; next?: ApplicationIntegrity }> = {}) {
  let state = initial;
  const sent: { route: string; body: unknown }[] = [];
  const routes: Record<string, (init: RequestInit | undefined) => Response> = { [`GET ${base}`]: () => Response.json(state) };
  for (const [route, answer] of Object.entries(posts)) {
    routes[route] = (init) => {
      const body = init?.body ? JSON.parse(String(init.body)) : undefined;
      sent.push({ route, body });
      const result = answer(body);
      if (result.next) state = result.next;
      return Response.json(result.body, { status: result.status });
    };
  }
  vi.stubGlobal('fetch', vi.fn(apiStub(routes)));
  return sent;
}

function renderPanel(source?: ApplicationSource) {
  return render(WithQueryClient, { props: { component: IntegrityHarness, props: { applicationId: integrityApplicationId, source } } });
}

const section = (name: string) => screen.getByRole('region', { name });

afterEach(() => {
  vi.unstubAllGlobals();
  signedIn.user = null;
});

const teammate = '01a157d4-0000-7000-8000-0000000000dd';

function explanation(actorUserId: string, overrides: Partial<IntegrityReasonExplanation> = {}): IntegrityReasonExplanation {
  const name = actorUserId === user.id ? 'Avery Quinn' : 'Jordan Park';
  return { actionId: `e-${actorUserId}`, reason: 'KnownFalsePositive', note: null, actorUserId, actorName: name, explainedAt: '2026-10-20T10:00:00Z', ...overrides };
}

/** The bot with some of its authenticity reasons explained, by code. */
function botExplained(explained: Record<string, IntegrityReasonExplanation>, overrides: Partial<ApplicationIntegrity> = {}): ApplicationIntegrity {
  const bot = botIntegrity();
  return {
    ...bot,
    authenticity: { ...bot.authenticity!, signals: bot.authenticity!.signals.map((signal) => ({ ...signal, explained: explained[signal.code] ?? null })) },
    ...overrides,
  };
}

function logEntry(overrides: Partial<IntegrityReviewEntry>): IntegrityReviewEntry {
  return {
    id: 'l1',
    type: 'ReasonExplained',
    assessmentKind: 'Authenticity',
    assessmentId: authenticityAssessmentId,
    dismissReason: null,
    note: null,
    actorUserId: user.id,
    actorName: 'Avery Quinn',
    occurredAt: '2026-10-20T10:00:00Z',
    signalId: 'signal-NET_DATACENTER_IP',
    signalTitle: "Sent from a hosting provider's network",
    explainReason: 'ExpectedForRole',
    ...overrides,
  };
}

const reasonRow = (code: string) => section('Authenticity').querySelector<HTMLElement>(`[data-signal="${code}"]`)!;

describe('integrity panel', () => {
  it('shows each assessment with its summary, next step, reasons with evidence, context and counter-evidence', async () => {
    serve(botIntegrity());
    renderPanel();

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(within(authenticity).getByRole('img', { name: 'Authenticity: Verification suggested, the highest of 3' })).toBeInTheDocument();
    expect(authenticity).toHaveTextContent('3 observations from network and automation checks back each other up.');
    expect(authenticity.querySelector('[data-next-step]')).toHaveTextContent(/^Confirm the candidate is who they say they are/);
    // The next step leads, in a callout, with the summary under it; no labels, and no heading over the reasons.
    expect(authenticity.querySelector('[data-summary]')?.firstElementChild?.nextElementSibling?.firstElementChild).toHaveAttribute('data-next-step');
    expect(authenticity.querySelector('[data-summary] dt')).toBeNull();
    expect(within(authenticity).getByRole('list', { name: 'Reasons' })).toBeInTheDocument();
    const codes = [...authenticity.querySelectorAll('[data-signal]')].map((item) => item.getAttribute('data-signal'));
    // Grouped by the check that found them, so the order follows the checks, not the list the API sends.
    expect(codes.toSorted()).toEqual(['AUTO_SUBMIT_VELOCITY', 'AUTO_WEBDRIVER', 'NET_ANON_PROXY', 'NET_DATACENTER_IP']);
    const context = authenticity.querySelector('[data-check="context"]')!;
    expect(within(context as HTMLElement).getByRole('heading', { name: 'Context' })).toBeInTheDocument();
    expect(context).toHaveTextContent('Not counted toward the level');
    // When it was assessed and under which rules: audit details, on the panel's last line.
    expect(authenticity.querySelector('[data-audit]')).toHaveTextContent(/^Assessed .+Ruleset \S+$/);
    expect(authenticity.querySelector('[data-signal="AUTO_SUBMIT_VELOCITY"] [data-evidence-kind="Metric"]')).toHaveTextContent('6 seconds');
    const counter = authenticity.querySelector('[data-signal="NET_DATACENTER_IP"] [data-counter-evidence]')!;
    // The other side of the argument, in its own card under its label.
    expect(counter.firstElementChild).toHaveTextContent(/^Counter-evidence$/);
    expect(counter).toHaveTextContent('The candidate gave a phone number in the same country.');
    expect(counter).toHaveTextContent('Phone country: United Kingdom');
  });

  it('groups the reasons under the check that found them, marking the checks that fed the level (W6)', async () => {
    serve(botIntegrity());
    renderPanel();

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    const checks = [...authenticity.querySelectorAll<HTMLElement>('[data-check]')];
    expect(checks.map((check) => check.querySelector('h3')?.textContent?.trim())).toEqual(['Network', 'Identity', 'Automation', 'Content', 'Context']);
    const network = checks.find((check) => check.dataset.check === 'Network')!;
    const identity = checks.find((check) => check.dataset.check === 'Identity')!;
    expect(network).toHaveTextContent('1 signal');
    expect(network.querySelector('svg.lucide-triangle-alert')).toBeInTheDocument();
    // Its reason sits under it and does not repeat the check's name.
    const reason = network.querySelector('[data-signal="NET_DATACENTER_IP"] summary')!;
    expect(reason).not.toHaveTextContent('Network');
    expect(reason.querySelector('svg.lucide-chevron-down')).toBeInTheDocument();
    expect(identity).toHaveTextContent('Nothing found');
    expect(identity.querySelector('svg.lucide-triangle-alert')).not.toBeInTheDocument();
    // There is no second list of the same reasons.
    expect(authenticity.querySelectorAll('[data-signal="NET_DATACENTER_IP"]')).toHaveLength(1);
  });

  // A recruiter added them by hand: no careers form, so no network address, session or form telemetry (section 16).
  it('says which checks had nothing to look at for a candidate added by hand, and marks them Not checked', async () => {
    const bot = botIntegrity();
    const duplicate = { ...bot.authenticity!.signals[0]!, id: 'signal-duplicate', code: 'ID_DUPLICATE_EMAIL', title: 'Email used on another application', category: 'Identity' as const };
    serve({ ...bot, authenticity: { ...bot.authenticity!, level: 'ReviewSuggested', reasonCodes: [duplicate.code], signals: [duplicate] } });
    renderPanel('Recruiter');

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(authenticity.querySelector('[data-not-checked]')).toHaveTextContent(
      'Added by hand, so the network, automation and form activity checks had nothing to look at.',
    );
    const check = (name: string) => authenticity.querySelector<HTMLElement>(`[data-check="${name}"]`)!;
    expect(check('Network')).toHaveTextContent('Not checked');
    expect(check('Automation')).toHaveTextContent('Not checked');
    expect(check('Content')).toHaveTextContent('Nothing found');
    expect(screen.getByRole('region', { name: 'AI assistance' }).querySelector('[data-not-checked]')).toBeNull();
  });

  it('keeps a calm hand-added result to its summary and one quiet line', async () => {
    serve(genuineIntegrity());
    renderPanel('Recruiter');

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(authenticity.querySelector('[data-not-checked]')).toHaveTextContent(/^Added by hand/);
    expect(authenticity.querySelector('[data-checks]')).toBeNull();
  });

  it('never says a careers applicant had checks that could not run', async () => {
    serve(genuineIntegrity());
    renderPanel('CareersSite');

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(authenticity.querySelector('[data-not-checked]')).toBeNull();
    expect(authenticity).not.toHaveTextContent('Not checked');
  });

  it('expands every reason at once and closes them again', async () => {
    serve(botIntegrity());
    renderPanel();

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    const reasons = () => [...authenticity.querySelectorAll('[data-signal] details')] as HTMLDetailsElement[];
    expect(reasons().some((reason) => reason.open)).toBe(false);
    await fireEvent.click(within(authenticity).getAllByRole('button', { name: 'Expand all' })[0]!);
    await waitFor(() => expect(reasons().slice(0, 3).every((reason) => reason.open)).toBe(true));
    await fireEvent.click(within(authenticity).getAllByRole('button', { name: 'Collapse all' })[0]!);
    await waitFor(() => expect(reasons().slice(0, 3).some((reason) => reason.open)).toBe(false));
  });

  it("shows AI assistance as information, with the job policy and not the candidate's declaration", async () => {
    serve(botIntegrity());
    renderPanel();

    const ai = await screen.findByRole('region', { name: 'AI assistance' });
    expect(within(ai).getByRole('heading', { level: 2 })).toHaveTextContent('AI assistance');
    expect(within(ai).getByRole('img', { name: 'AI assistance: Some signs of AI help, the middle of 3' })).toBeInTheDocument();
    expect(ai).toHaveTextContent('Informational');
    // The job's policy is a pill in the header, beside the level.
    expect(within(ai).getByText(/AI allowed with disclosure/)).toHaveTextContent('Job policy: AI allowed with disclosure');
    expect(ai.querySelector('[data-audit]')).toHaveTextContent(/^Assessed .+Ruleset \S+$/);
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
    expect(container).toHaveTextContent('No automated decision has been made. Every action is logged.');
  });

  it('hides request verification until it exists', async () => {
    serve(botIntegrity());
    renderPanel();
    await screen.findByRole('region', { name: 'Authenticity' });

    expect(screen.queryByRole('button', { name: /Request verification/ })).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/phase 2/i);
  });

  it('says why, then the next step: quiet when calm, semibold when the level needs a look', async () => {
    serve(genuineIntegrity());
    const { unmount } = renderPanel();
    const calm = await screen.findByRole('region', { name: 'Authenticity' });
    // A calm result is one quiet line: the summary, then its next step, which every result carries.
    const line = calm.querySelector('[data-summary]')!;
    expect(line.tagName).toBe('P');
    expect(line).toHaveClass('text-muted-foreground');
    expect(line.querySelector('[data-next-step]')).toHaveTextContent('No action needed.');
    // Its summary already says nothing was found, so there is no list of empty checks under it (W7).
    expect(within(calm).queryByRole('list', { name: 'Reasons' })).toBeNull();
    unmount();

    serve(botIntegrity());
    renderPanel();
    const concern = await screen.findByRole('region', { name: 'Authenticity' });
    // A level that needs a look leads with its next step, semibold, in a callout edged in the level's colour.
    const callout = concern.querySelector('[data-summary]')!;
    expect(callout).toHaveClass('border-status-verify-mark');
    expect(callout.querySelector('[data-next-step]')).toHaveClass('font-semibold');
    expect(callout).toHaveTextContent(botIntegrity().authenticity!.summary);
  });

  it('dismisses an assessment with a reason, which lands in the review log', async () => {
    const dismissed: ApplicationIntegrity = {
      ...botIntegrity(),
      authenticity: {
        ...botIntegrity().authenticity!,
        dismissal: { actionId: 'a1', reason: 'VerifiedOutsideScreenHire', note: 'Video call on Monday.', actorUserId: user.id, actorName: 'Avery Quinn', dismissedAt: '2026-10-20T11:00:00Z' },
      },
      reviewLog: [
        { id: 'a1', type: 'Dismissed', assessmentKind: 'Authenticity', assessmentId: authenticityAssessmentId, dismissReason: 'VerifiedOutsideScreenHire', note: 'Video call on Monday.', actorUserId: user.id, actorName: 'Avery Quinn', occurredAt: '2026-10-20T11:00:00Z', signalId: null, signalTitle: null, explainReason: null },
      ],
    };
    const sent = serve(botIntegrity(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: dismissed, next: dismissed }),
    });
    renderPanel();

    await fireEvent.click(await within(await screen.findByRole('region', { name: 'Authenticity' })).findByRole('button', { name: 'Dismiss' }));
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
    // The level stays as assessed; the panel says who dismissed it and the button is gone.
    const authenticity = section('Authenticity');
    expect(within(authenticity).getByRole('img', { name: /Authenticity: Verification suggested/ })).toBeInTheDocument();
    expect(authenticity).toHaveTextContent(/Dismissed by you/);
    expect(within(authenticity).queryByRole('button', { name: /Dismiss/ })).not.toBeInTheDocument();
  });

  it('marks a reason explained, which closes it with its badge and lands in the review log, and decides nothing', async () => {
    const after = botExplained(
      { NET_DATACENTER_IP: explanation(user.id, { reason: 'ExpectedForRole', note: 'Remote role.' }) },
      { reviewLog: [logEntry({ note: 'Remote role.' })] },
    );
    const sent = serve(botIntegrity(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/explain`]: () => ({ status: 200, body: after, next: after }),
    });
    renderPanel();

    await screen.findByRole('region', { name: 'Authenticity' });
    const row = reasonRow('NET_DATACENTER_IP');
    await fireEvent.click(row.querySelector('summary')!);
    await waitFor(() => expect(row.querySelector('details')!.open).toBe(true));
    await fireEvent.click(within(row).getByRole('button', { name: 'Mark explained' }));
    const dialog = await screen.findByRole('dialog', { name: `Mark "Sent from a hosting provider's network" explained?` });
    expect(dialog).toHaveTextContent('The level stays as assessed.');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Expected for this role' }));
    await fireEvent.input(within(dialog).getByLabelText('Note (optional)'), { target: { value: ' Remote role. ' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Mark explained' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/explain`, body: { reason: 'ExpectedForRole', note: 'Remote role.' } }]));
    await screen.findByText(`Marked explained: Sent from a hosting provider's network. It is in the review log.`);
    await waitFor(() => expect(reasonRow('NET_DATACENTER_IP')).toHaveAttribute('data-explained'));
    const explained = reasonRow('NET_DATACENTER_IP');
    expect(explained.querySelector('details')!.open).toBe(false);
    expect(within(explained.querySelector('summary')!).getByText('Explained')).toBeInTheDocument();
    expect(explained.querySelector('[data-explanation] p')).toHaveTextContent(/^You .+: Expected for this role\. "Remote role\."$/);
    expect(within(explained).getByRole('button', { name: 'Undo' })).toBeInTheDocument();
    expect(within(section('Review log')).getByText(`Explained Sent from a hosting provider's network: Expected for this role`)).toBeInTheDocument();
    // The level, and the one button, stay as they were: other reasons are still unexplained.
    const authenticity = section('Authenticity');
    expect(within(authenticity).getByRole('img', { name: /Authenticity: Verification suggested/ })).toBeInTheDocument();
    expect(within(authenticity).getByRole('button', { name: 'Dismiss' })).toBeInTheDocument();
    expect(within(authenticity).queryByRole('button', { name: 'Dismiss all' })).not.toBeInTheDocument();
  });

  it('offers Undo to the author of an explanation only, and undoes it', async () => {
    const before = botExplained({ NET_DATACENTER_IP: explanation(user.id), AUTO_WEBDRIVER: explanation(teammate) });
    const after = botExplained(
      { AUTO_WEBDRIVER: explanation(teammate) },
      { reviewLog: [logEntry({}), logEntry({ id: 'l2', type: 'ExplanationUndone', explainReason: null, occurredAt: '2026-10-20T10:05:00Z' })] },
    );
    const sent = serve(before, {
      [`POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/undo-explanation`]: () => ({ status: 200, body: after, next: after }),
    });
    renderPanel();

    await screen.findByRole('region', { name: 'Authenticity' });
    expect(reasonRow('AUTO_WEBDRIVER').querySelector('[data-explanation]')).toHaveTextContent(/^Jordan Park /);
    expect(within(reasonRow('AUTO_WEBDRIVER')).queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument();
    await fireEvent.click(within(reasonRow('NET_DATACENTER_IP')).getByRole('button', { name: 'Undo' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/undo-explanation`, body: undefined }]));
    await screen.findByText(`Undid the explanation: Sent from a hosting provider's network.`);
    await waitFor(() => expect(reasonRow('NET_DATACENTER_IP')).not.toHaveAttribute('data-explained'));
    expect(within(section('Review log')).getByText(`Undid the explanation of Sent from a hosting provider's network`)).toBeInTheDocument();
  });

  it('offers Dismiss all once every reason that counts is explained, lists their notes, and dismisses once', async () => {
    // The context reason (the VPN) does not count, so it need not be explained.
    const all = botExplained({
      AUTO_SUBMIT_VELOCITY: explanation(user.id, { actionId: 'e1', reason: 'ConfirmedWithCandidate', note: 'Uses a password manager to fill forms.' }),
      AUTO_WEBDRIVER: explanation(teammate, { actionId: 'e2' }),
      NET_DATACENTER_IP: explanation(user.id, { actionId: 'e3', reason: 'ExpectedForRole', note: 'Remote role.' }),
    });
    const sent = serve(all, {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: all }),
    });
    renderPanel();

    const authenticity = await screen.findByRole('region', { name: 'Authenticity' });
    expect(within(authenticity).queryByRole('button', { name: 'Dismiss' })).not.toBeInTheDocument();
    await fireEvent.click(within(authenticity).getByRole('button', { name: 'Dismiss all' }));
    const dialog = await screen.findByRole('dialog', { name: 'Dismiss this authenticity assessment?' });
    const listed = within(dialog).getByRole('list', { name: 'Explained reasons' });
    expect(within(listed).getAllByRole('listitem')).toHaveLength(3);
    expect(listed).toHaveTextContent('Confirmed with the candidate, by you. "Uses a password manager to fill forms."');
    expect(listed).toHaveTextContent('A known false positive, by Jordan Park');
    expect(listed).toHaveTextContent('Expected for this role, by you. "Remote role."');
    // The dismissal keeps its own reason: nothing is chosen from the explanations.
    expect(within(dialog).getByRole('radio', { name: 'The observations do not apply' })).toBeChecked();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/dismiss`, body: { reason: 'FalsePositive', note: null } }]));
  });

  it('keeps explanations read-only once the assessment is dismissed', async () => {
    const bot = botExplained({ NET_DATACENTER_IP: explanation(user.id) });
    serve({
      ...bot,
      authenticity: {
        ...bot.authenticity!,
        dismissal: { actionId: 'd1', reason: 'FalsePositive', note: null, actorUserId: user.id, actorName: 'Avery Quinn', dismissedAt: '2026-10-20T11:00:00Z' },
      },
    });
    renderPanel();

    await screen.findByRole('region', { name: 'Authenticity' });
    expect(reasonRow('NET_DATACENTER_IP').querySelector('[data-explanation]')).toHaveTextContent(/^You /);
    expect(within(section('Authenticity')).queryByRole('button', { name: /Undo|Mark explained|Dismiss/ })).not.toBeInTheDocument();
  });

  it('shows no review controls to a role that does not review verification', async () => {
    signedIn.user = { ...userAs('Viewer'), id: user.id };
    serve(botExplained({ NET_DATACENTER_IP: explanation(user.id) }));
    renderPanel();

    await screen.findByRole('region', { name: 'Authenticity' });
    await fireEvent.click(within(section('Authenticity')).getAllByRole('button', { name: 'Expand all' })[0]!);
    await waitFor(() => expect(reasonRow('AUTO_WEBDRIVER').querySelector('details')!.open).toBe(true));

    expect(screen.queryByRole('button', { name: /Mark explained|Undo|Dismiss|Add review note/ })).not.toBeInTheDocument();
    // Read-only says why (E3).
    expect(section('Authenticity')).toHaveTextContent('Your role can read these checks. A reviewer explains reasons and dismisses.');
    expect(reasonRow('NET_DATACENTER_IP').querySelector('[data-explanation]')).toHaveTextContent(/^You /);
  });

  it('asks for a note before dismissing for another reason', async () => {
    const sent = serve(botIntegrity(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: botIntegrity() }),
    });
    renderPanel();

    await fireEvent.click(await within(await screen.findByRole('region', { name: 'Authenticity' })).findByRole('button', { name: 'Dismiss' }));
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

    await fireEvent.click(await within(await screen.findByRole('region', { name: 'Authenticity' })).findByRole('button', { name: 'Dismiss' }));
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

    await fireEvent.click(await within(await screen.findByRole('region', { name: 'Authenticity' })).findByRole('button', { name: 'Dismiss' }));
    let dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Explained by the candidate' }));
    await fireEvent.input(within(dialog).getByLabelText('Note (optional)'), { target: { value: 'Said so on the phone.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent("We couldn't record the dismissal just now. Try again in a minute.");
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await fireEvent.click(within(section('Authenticity')).getByRole('button', { name: 'Dismiss' }));
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

    expect(await screen.findByText(/Could not load the verification checks/)).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Authenticity' }).querySelector('[data-next-step]')).toHaveTextContent(/^Confirm the candidate/);
  });

  it('adds a note to the review log', async () => {
    const noted: ApplicationIntegrity = {
      ...botIntegrity(),
      reviewLog: [{ id: 'n1', type: 'NoteAdded', assessmentKind: null, assessmentId: null, dismissReason: null, note: 'Asked for references.', actorUserId: 'someone-else', actorName: 'Jordan Park', occurredAt: '2026-10-20T11:00:00Z', signalId: null, signalTitle: null, explainReason: null }],
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
    expect(within(authenticity).getByRole('img', { name: 'Authenticity: No concerns, the lowest of 3' })).toBeInTheDocument();
    expect(authenticity.querySelector('[data-next-step]')).toHaveTextContent('No action needed.');
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
    expect(within(authenticity).getByRole('img', { name: /Authenticity: Verification suggested/ })).toBeInTheDocument();
    expect(screen.queryByText(/Checks are still running/)).not.toBeInTheDocument();
  });

  it('says the checks are still running while they are', async () => {
    serve(pendingIntegrity());
    renderPanel();

    expect(await screen.findByText(/Checks are still running/)).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Authenticity' })).not.toBeInTheDocument();
  });
});
