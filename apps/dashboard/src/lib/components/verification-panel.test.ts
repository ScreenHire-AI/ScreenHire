import type { ApplicationVerification, CurrentUser, VerificationReasonExplanation } from '@screenhire/api-client';
import { QueryClient } from '@tanstack/svelte-query';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { invalidatePath } from '#lib/query.js';
import { invalidations } from '#lib/realtime.js';
import { apiStub, user, userAs } from '../../test-support/fixtures.js';
import {
  authenticityAssessmentId,
  botVerification,
  checkGroups,
  genuineVerification,
  verificationApplicationId,
  pendingVerification,
} from '../../test-support/verification-fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import VerificationPanel from './verification-panel.svelte';

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

const base = `/v1/applications/${verificationApplicationId}/verification`;

/** Serves the verification tab from `state`, which a POST stub can replace, and records each POST body. */
function serve(initial: ApplicationVerification, posts: Record<string, (body: unknown) => { status: number; body: unknown; next?: ApplicationVerification }> = {}) {
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

function renderPanel() {
  return render(WithQueryClient, { props: { component: VerificationPanel, props: { applicationId: verificationApplicationId } } });
}

const summaryPanel = () => screen.getByRole('region', { name: 'Verification' });
// The summary panel shows once the tab has loaded; until then a skeleton of the same name stands in.
async function assessed() {
  await waitFor(() => expect(document.querySelector('[data-summary]')).toBeInTheDocument());
  return summaryPanel();
}
const group = (name: string) => screen.getByRole('region', { name });
const tile = (key: string) => document.querySelector<HTMLElement>(`[data-group-map] [data-group="${key}"]`)!;
const finding = (code: string) => document.querySelector<HTMLElement>(`[data-signal="${code}"]`)!;
const openFinding = async (code: string) => {
  const row = finding(code);
  await fireEvent.click(row.querySelector('summary')!);
  await waitFor(() => expect(row.querySelector('details')!.open).toBe(true));
  return row;
};

afterEach(() => {
  vi.unstubAllGlobals();
  signedIn.user = null;
});

const teammate = '01a157d4-0000-7000-8000-0000000000dd';

function explanation(actorUserId: string, overrides: Partial<VerificationReasonExplanation> = {}): VerificationReasonExplanation {
  const name = actorUserId === user.id ? 'Avery Quinn' : 'Jordan Park';
  return { actionId: `e-${actorUserId}`, reason: 'KnownFalsePositive', note: null, actorUserId, actorName: name, explainedAt: '2026-10-20T10:00:00Z', ...overrides };
}

/** The bot with some of its authenticity findings explained, by code. */
function botExplained(explained: Record<string, VerificationReasonExplanation>, overrides: Partial<ApplicationVerification> = {}): ApplicationVerification {
  const bot = botVerification();
  return {
    ...bot,
    authenticity: { ...bot.authenticity!, signals: bot.authenticity!.signals.map((signal) => ({ ...signal, explained: explained[signal.code] ?? null })) },
    ...overrides,
  };
}

describe('verification tab', () => {
  it('opens with the next step, then the groups that found something, each finding opening to its evidence and counter-evidence', async () => {
    serve(botVerification());
    renderPanel();

    const summary = await assessed();
    // The level is the page header's mark, shown once: the tab does not spell out the scale or name two assessments.
    expect(screen.queryByRole('region', { name: 'Authenticity' })).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'AI assistance' })).not.toBeInTheDocument();
    expect(screen.queryByRole('img', { name: /of 3/ })).not.toBeInTheDocument();
    // The next step leads, in a callout, with the summary under it, and names the six groups.
    const callout = summary.querySelector('[data-summary]')!;
    expect(callout.querySelector('[data-next-step]')).toHaveTextContent(/^Confirm the candidate is who they say they are/);
    expect(callout).toHaveTextContent('3 observations from network and form use checks back each other up.');
    expect(callout).toHaveClass('border-status-verify-mark');
    // A panel for each group that has findings, in the groups' order, titled by the group and nothing else.
    expect(screen.getAllByRole('heading', { level: 2 }).map((heading) => heading.textContent?.trim())).toEqual(['Verification', 'Network', 'Documents', 'Answers', 'Form use']);
    expect(group('Network').querySelectorAll('svg[data-icon="globe"]')).toHaveLength(0);
    const codes = (name: string) => [...group(name).querySelectorAll('[data-signal]')].map((item) => item.getAttribute('data-signal'));
    expect(codes('Network')).toEqual(['NET_DATACENTER_IP', 'NET_ANON_PROXY']);
    expect(codes('Form use')).toEqual(['AUTO_SUBMIT_VELOCITY', 'AUTO_WEBDRIVER']);
    expect(codes('Documents')).toEqual(['CNT_PROMPT_INJECTION']);
    expect(codes('Answers')).toEqual(['CNT_NEAR_DUPLICATE_ANSWER']);
    // No tag on a finding that counts, and nothing that says "nothing found" outside a tile.
    expect(document.body).not.toHaveTextContent('Counts toward the level');
    expect(group('Network')).not.toHaveTextContent(/nothing found/i);
    // Evidence is drawn by kind, and the counter-evidence is the other side of the argument in its own card.
    expect(finding('AUTO_SUBMIT_VELOCITY').querySelector('[data-evidence-kind="Metric"]')).toHaveTextContent('6 seconds');
    const counter = finding('NET_DATACENTER_IP').querySelector('[data-counter-evidence]')!;
    expect(counter.firstElementChild).toHaveTextContent(/^Counter-evidence$/);
    expect(counter).toHaveTextContent('The candidate gave a phone number in the same country.');
    expect(counter).toHaveTextContent('Phone country: United Kingdom');
    expect(finding('CNT_PROMPT_INJECTION').querySelector('[data-evidence-kind="Quote"]')).toBeInTheDocument();
    expect(finding('CNT_NEAR_DUPLICATE_ANSWER').querySelector('[data-evidence-kind="TextComparison"]')).toBeInTheDocument();
  });

  it('draws a map of the six groups, each with one state, linking to the panel of a group that found something', async () => {
    serve(botVerification());
    renderPanel();

    await assessed();
    const map = screen.getByRole('list', { name: 'Check groups' });
    expect([...map.querySelectorAll('[data-group]')].map((item) => item.getAttribute('data-group'))).toEqual(['Network', 'Contact', 'Documents', 'Answers', 'Duplicates', 'FormUse']);
    expect(tile('Network')).toHaveTextContent('Network 1 found');
    expect(tile('Contact')).toHaveTextContent('Contact details Nothing found');
    expect(tile('Documents')).toHaveTextContent('Documents 1 note, information only');
    expect(tile('Answers')).toHaveTextContent('Answers 1 note, information only');
    expect(tile('Duplicates')).toHaveTextContent('Duplicates Nothing found');
    expect(tile('FormUse')).toHaveTextContent('Form use 2 found');
    // A group that found nothing is a tile and not a link; one that found something links to its panel.
    expect(tile('Contact').querySelector('a')).toBeNull();
    expect(tile('Network').querySelector('a')).toHaveAttribute('href', '#verification-group-network');
    expect(group('Network')).toHaveAttribute('id', 'verification-group-network');
    // A state is its icon, colour and words, always together (T1).
    expect(tile('Network').querySelector('[data-status="verify"], [data-status="review"]')).toBeInTheDocument();
    expect(tile('Contact').querySelector('[data-status-icon]')).toBeInTheDocument();
    // Following a tile moves focus to its panel.
    group('Network').scrollIntoView = vi.fn();
    await fireEvent.click(tile('Network').querySelector('a')!);
    expect(group('Network')).toHaveFocus();
  });

  it('says AI help is information only on its findings, and offers no action or tag on anything else that does not count', async () => {
    serve(botVerification());
    renderPanel();

    await assessed();
    for (const code of ['CNT_PROMPT_INJECTION', 'CNT_NEAR_DUPLICATE_ANSWER']) {
      expect(finding(code).querySelector('[data-information-only]')).toHaveTextContent('Information only');
    }
    // The exception is marked, not the rule: a finding that counts and a context signal (the VPN) carry no tag.
    for (const code of ['NET_DATACENTER_IP', 'AUTO_WEBDRIVER', 'NET_ANON_PROXY']) {
      expect(finding(code).querySelector('[data-information-only]')).toBeNull();
    }
    for (const code of ['CNT_PROMPT_INJECTION', 'CNT_NEAR_DUPLICATE_ANSWER', 'NET_ANON_PROXY']) {
      await openFinding(code);
      expect(within(finding(code)).queryByRole('button', { name: 'Mark explained' })).not.toBeInTheDocument();
    }
    await openFinding('NET_DATACENTER_IP');
    expect(within(finding('NET_DATACENTER_IP')).getByRole('button', { name: 'Mark explained' })).toBeInTheDocument();
  });

  it("shows AI help as its badge and the job's policy, with its summary and next step when it has signs, and no Dismiss", async () => {
    serve(botVerification());
    renderPanel();

    const summary = await assessed();
    const row = summary.querySelector('[data-ai-row]')!;
    expect(within(row as HTMLElement).getByText('Some signs of AI help')).toBeInTheDocument();
    expect(within(row as HTMLElement).getByText(/AI allowed with disclosure/)).toHaveTextContent('Job policy: AI allowed with disclosure');
    // Non-negotiable 1: the AI result's summary and next step are on screen when it has signs.
    expect(row.querySelector('[data-ai-summary]')).toHaveTextContent('Some answers look generated or templated. Ask about the answers in the first conversation.');
    // The declaration has its own place on the page, with the candidate's answers (section 16).
    expect(summary).not.toHaveTextContent(/Declared AI use/);
    // One Dismiss, for the verification result; AI help has none.
    expect(within(summary).getAllByRole('button', { name: 'Dismiss' })).toHaveLength(1);
    expect(row.querySelector('[data-slot="badge"]')!.className).not.toMatch(/status-(review|verify)/);
  });

  it('keeps a calm AI row to its badge and the policy, with the summary for screen readers only', async () => {
    serve(genuineVerification());
    renderPanel();

    const summary = await assessed();
    const row = summary.querySelector<HTMLElement>('[data-ai-row]')!;
    expect(within(row).getByText('No signs of AI help')).toBeInTheDocument();
    expect(within(row).getByText(/AI allowed with disclosure/)).toHaveTextContent('Job policy: AI allowed with disclosure');
    expect(row.querySelector('[data-ai-summary]')).toBeNull();
    expect(row.querySelector('.sr-only')).toHaveTextContent('No indicators. No action needed.');
  });

  it('is one line when nothing was found, with the checks one click away and no empty lines for the groups', async () => {
    serve(genuineVerification());
    renderPanel();

    const summary = await assessed();
    // A calm result is the summary then its next step, quiet, with no map and no panel for any group.
    const line = summary.querySelector('[data-summary]')!;
    expect(line.tagName).toBe('P');
    expect(line).toHaveClass('text-muted-foreground');
    expect(line).toHaveTextContent('Nothing unusual in the checks that ran. No action needed.');
    expect(line.querySelector('[data-next-step]')).toHaveTextContent('No action needed.');
    expect(screen.queryByRole('list', { name: 'Check groups' })).toBeNull();
    expect(screen.getAllByRole('heading', { level: 2 })).toHaveLength(1);
    expect(summary).not.toHaveTextContent(/nothing found/i);
    // What was checked is collapsed and names the groups with their checks, and says nothing about what they mean.
    const checked = summary.querySelector<HTMLDetailsElement>('[data-checked]')!;
    expect(checked.open).toBe(false);
    expect(checked.querySelector('summary')).toHaveTextContent('What was checked');
    const names = [...checked.querySelectorAll('li')].map((item) => item.querySelector('span')?.textContent);
    expect(names).toEqual(['Network:', 'Contact details:', 'Documents:', 'Answers:', 'Duplicates:', 'Form use:']);
    // Nothing to dismiss, and the footer is on the one panel.
    expect(screen.queryByRole('button', { name: /Dismiss/ })).not.toBeInTheDocument();
    expect(summary.querySelector('[data-audit]')).toHaveTextContent(/^Assessed .+Ruleset \S+$/);
    expect(summary).toHaveTextContent('No automated decision has been made. Every action is logged.');
  });

  it('says outright which groups did not run, so a quiet result is never read as a clean one', async () => {
    serve({
      ...genuineVerification(),
      checkGroups: checkGroups({
        Duplicates: { state: 'TurnedOff', note: "Duplicates checks are turned off in this organization's settings." },
        Network: { state: 'NotChecked', note: 'Added by hand, so there was no careers form for these checks to read.' },
        Contact: { note: 'A shared email address was not counted because its domain is on the organization\'s trusted list.' },
      }),
    });
    renderPanel();

    const summary = await assessed();
    const lines = [...summary.querySelectorAll<HTMLElement>('[data-not-run] li')];
    expect(lines.map((item) => item.getAttribute('data-group'))).toEqual(['Network', 'Contact', 'Duplicates']);
    expect(lines[0]).toHaveTextContent('Network Not checked Added by hand, so there was no careers form for these checks to read.');
    expect(lines[1]).toHaveTextContent('Contact details Not counted');
    expect(lines[1]).toHaveTextContent('trusted list');
    expect(lines[2]).toHaveTextContent("Duplicates Turned off Duplicates checks are turned off in this organization's settings.");
    expect(lines[0]!.querySelector('[data-status-icon]')).toBeInTheDocument();
    // What was checked names only the groups that ran.
    const names = [...summary.querySelectorAll('[data-checked] li')].map((item) => item.querySelector('span')?.textContent);
    expect(names).toEqual(['Contact details:', 'Documents:', 'Answers:', 'Form use:']);
  });

  it('shows the map for a calm level that still has notes, with the next step as one quiet line', async () => {
    const calm = genuineVerification();
    serve({
      ...calm,
      aiAssistance: { ...botVerification().aiAssistance!, level: 'NotIndicated', reasonCodes: [] },
      checkGroups: checkGroups({ Documents: { state: 'InformationOnly', notes: 1 }, Answers: { state: 'InformationOnly', notes: 1 } }),
    });
    renderPanel();

    const summary = await assessed();
    expect(summary.querySelector('[data-summary]')!.tagName).toBe('P');
    expect(summary.querySelector('[data-checked]')).toBeNull();
    expect(screen.getByRole('list', { name: 'Check groups' })).toBeInTheDocument();
    expect(group('Documents')).toBeInTheDocument();
    expect(finding('CNT_PROMPT_INJECTION').querySelector('[data-information-only]')).toBeInTheDocument();
    // Nothing counts, so nothing can be explained or dismissed.
    expect(screen.queryByRole('button', { name: /Dismiss/ })).not.toBeInTheDocument();
  });

  it('shows Expand all only for a group with more than one finding, and opens and closes them together', async () => {
    serve(botVerification());
    renderPanel();

    await assessed();
    expect(within(group('Documents')).queryByRole('button', { name: /Expand all|Collapse all/ })).not.toBeInTheDocument();
    expect(within(group('Answers')).queryByRole('button', { name: /Expand all|Collapse all/ })).not.toBeInTheDocument();
    const open = () => [...group('Form use').querySelectorAll<HTMLDetailsElement>('[data-signal] details')];
    expect(open().some((details) => details.open)).toBe(false);
    await fireEvent.click(within(group('Form use')).getByRole('button', { name: 'Expand all' }));
    await waitFor(() => expect(open().every((details) => details.open)).toBe(true));
    await fireEvent.click(within(group('Form use')).getByRole('button', { name: 'Collapse all' }));
    await waitFor(() => expect(open().some((details) => details.open)).toBe(false));
  });

  it('puts the audit line and the decision line last, on the last panel only', async () => {
    serve(botVerification());
    renderPanel();

    await assessed();
    expect(document.querySelectorAll('[data-audit]')).toHaveLength(1);
    expect(group('Form use').querySelector('[data-audit]')).toHaveTextContent(/^Assessed .+Ruleset \S+$/);
    expect(group('Form use')).toHaveTextContent('No automated decision has been made. Every action is logged.');
    expect(summaryPanel().querySelector('[data-audit]')).toBeNull();
  });

  it('shows a trusted-domain or network note in the group it concerns', async () => {
    const bot = botVerification();
    serve({ ...bot, checkGroups: checkGroups({ Network: { state: 'Found', found: 1, notes: 1, note: 'The address is on a network the organization trusts, so no network signal was raised.' }, FormUse: { state: 'Found', found: 2 } }) });
    renderPanel();

    await assessed();
    expect(group('Network')).toHaveTextContent('network the organization trusts');
    expect(group('Form use')).not.toHaveTextContent('trusts');
  });

  it('offers no reject control, no score and no percentage, no review log and no review note', async () => {
    serve(botVerification());
    const { container } = renderPanel();
    await assessed();

    for (const control of [...screen.queryAllByRole('button'), ...screen.queryAllByRole('link')]) {
      expect(control.textContent).not.toMatch(/reject|decline|disqualif/i);
    }
    expect(container.textContent).not.toMatch(/%|score/i);
    expect(container).toHaveTextContent('No automated decision has been made. Every action is logged.');
    expect(screen.queryByRole('region', { name: 'Review log' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Add review note|Request verification/ })).not.toBeInTheDocument();
    expect(document.body).not.toHaveTextContent(/phase 2|review log/i);
  });

  it('dismisses the result with a reason, and says who set it aside; the level and the findings stay', async () => {
    const dismissed: ApplicationVerification = {
      ...botVerification(),
      authenticity: {
        ...botVerification().authenticity!,
        dismissal: { actionId: 'a1', reason: 'VerifiedOutsideScreenHire', note: 'Video call on Monday.', actorUserId: user.id, actorName: 'Avery Quinn', dismissedAt: '2026-10-20T11:00:00Z' },
      },
    };
    const sent = serve(botVerification(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: dismissed, next: dismissed }),
    });
    renderPanel();

    await fireEvent.click(await within(await assessed()).findByRole('button', { name: 'Dismiss' }));
    const dialog = await screen.findByRole('dialog', { name: 'Dismiss this verification result?' });
    expect(dialog).toHaveTextContent('your reason goes in Activity');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Verified outside ScreenHire' }));
    await fireEvent.input(within(dialog).getByLabelText('Note (optional)'), { target: { value: 'Video call on Monday.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/dismiss`, body: { reason: 'VerifiedOutsideScreenHire', note: 'Video call on Monday.' } }]));
    await screen.findByText('Dismissed the verification result: Verified outside ScreenHire.');
    const summary = summaryPanel();
    await waitFor(() => expect(summary.querySelector('[data-dismissal]')).toHaveTextContent('Dismissed by you'));
    expect(summary.querySelector('[data-dismissal]')).toHaveTextContent('Verified outside ScreenHire. "Video call on Monday."');
    expect(within(summary).queryByRole('button', { name: /Dismiss/ })).not.toBeInTheDocument();
    // The evidence stays on record, and the level is not the tab's to change.
    expect(finding('NET_DATACENTER_IP')).toBeInTheDocument();
    expect(document.querySelector('[data-summary]')).not.toHaveClass('border-status-verify-mark');
  });

  it('marks a finding explained, which closes it with its badge and decides nothing', async () => {
    const after = botExplained({ NET_DATACENTER_IP: explanation(user.id, { reason: 'ExpectedForRole', note: 'Remote role.' }) });
    const sent = serve(botVerification(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/explain`]: () => ({ status: 200, body: after, next: after }),
    });
    renderPanel();

    await assessed();
    const row = await openFinding('NET_DATACENTER_IP');
    await fireEvent.click(within(row).getByRole('button', { name: 'Mark explained' }));
    const dialog = await screen.findByRole('dialog', { name: `Mark "Sent from a hosting provider's network" explained?` });
    expect(dialog).toHaveTextContent('The level stays as assessed.');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Expected for this role' }));
    await fireEvent.input(within(dialog).getByLabelText('Note (optional)'), { target: { value: ' Remote role. ' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Mark explained' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/explain`, body: { reason: 'ExpectedForRole', note: 'Remote role.' } }]));
    await screen.findByText(`Marked explained: Sent from a hosting provider's network.`);
    await waitFor(() => expect(finding('NET_DATACENTER_IP')).toHaveAttribute('data-explained'));
    const explained = finding('NET_DATACENTER_IP');
    expect(explained.querySelector('details')!.open).toBe(false);
    expect(within(explained.querySelector('summary')!).getByText('Explained')).toBeInTheDocument();
    // The note sits under the finding it concerns.
    expect(explained.querySelector('[data-explanation] p')).toHaveTextContent(/^You .+: Expected for this role\. "Remote role\."$/);
    expect(within(explained).getByRole('button', { name: 'Undo' })).toBeInTheDocument();
    // The next step, the callout's colour and the one button stay as they were: other findings are still unexplained.
    expect(summaryPanel().querySelector('[data-summary]')).toHaveClass('border-status-verify-mark');
    expect(within(summaryPanel()).getByRole('button', { name: 'Dismiss' })).toBeInTheDocument();
    expect(within(summaryPanel()).queryByRole('button', { name: 'Dismiss all' })).not.toBeInTheDocument();
  });

  it('leaves a finding open when the API refuses its explanation', async () => {
    const sent = serve(botVerification(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/explain`]: () => ({ status: 409, body: { title: 'Already explained', status: 409 } }),
    });
    renderPanel();

    await assessed();
    const row = await openFinding('NET_DATACENTER_IP');
    await fireEvent.click(within(row).getByRole('button', { name: 'Mark explained' }));
    const dialog = await screen.findByRole('dialog', { name: `Mark "Sent from a hosting provider's network" explained?` });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Mark explained' }));

    await waitFor(() => expect(sent).toHaveLength(1));
    await screen.findByText('Someone already explained this finding, or the result changed. The tab now shows the latest.');
    expect(finding('NET_DATACENTER_IP').querySelector('details')!.open).toBe(true);
  });

  it('offers Undo to the author of an explanation only, and undoes it', async () => {
    const before = botExplained({ NET_DATACENTER_IP: explanation(user.id), AUTO_WEBDRIVER: explanation(teammate) });
    const after = botExplained({ AUTO_WEBDRIVER: explanation(teammate) });
    const sent = serve(before, {
      [`POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/undo-explanation`]: () => ({ status: 200, body: after, next: after }),
    });
    renderPanel();

    await assessed();
    expect(finding('AUTO_WEBDRIVER').querySelector('[data-explanation]')).toHaveTextContent(/^Jordan Park /);
    expect(within(finding('AUTO_WEBDRIVER')).queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument();
    await fireEvent.click(within(finding('NET_DATACENTER_IP')).getByRole('button', { name: 'Undo' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/signals/signal-NET_DATACENTER_IP/undo-explanation`, body: undefined }]));
    await screen.findByText(`Undid the explanation: Sent from a hosting provider's network.`);
    await waitFor(() => expect(finding('NET_DATACENTER_IP')).not.toHaveAttribute('data-explained'));
  });

  it('offers Dismiss all once every finding that counts is explained, lists their notes, and dismisses once', async () => {
    // The context finding (the VPN) does not count, so it need not be explained.
    const all = botExplained({
      AUTO_SUBMIT_VELOCITY: explanation(user.id, { actionId: 'e1', reason: 'ConfirmedWithCandidate', note: 'Uses a password manager to fill forms.' }),
      AUTO_WEBDRIVER: explanation(teammate, { actionId: 'e2' }),
      NET_DATACENTER_IP: explanation(user.id, { actionId: 'e3', reason: 'ExpectedForRole', note: 'Remote role.' }),
    });
    const sent = serve(all, {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: all }),
    });
    renderPanel();

    const summary = await assessed();
    expect(within(summary).queryByRole('button', { name: 'Dismiss' })).not.toBeInTheDocument();
    await fireEvent.click(within(summary).getByRole('button', { name: 'Dismiss all' }));
    const dialog = await screen.findByRole('dialog', { name: 'Dismiss this verification result?' });
    const listed = within(dialog).getByRole('list', { name: 'Explained findings' });
    expect(within(listed).getAllByRole('listitem').map((item) => item.querySelector('span')!.textContent)).toEqual([
      'Completed faster than a person could',
      'Browser automation detected',
      "Sent from a hosting provider's network",
    ]);
    expect(listed).toHaveTextContent('Confirmed with the candidate, by you. "Uses a password manager to fill forms."');
    expect(listed).toHaveTextContent('A known false positive, by Jordan Park');
    expect(listed).toHaveTextContent('Expected for this role, by you. "Remote role."');
    // The dismissal keeps its own reason: nothing is chosen from the explanations.
    expect(within(dialog).getByRole('radio', { name: 'The observations do not apply' })).toBeChecked();
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    await waitFor(() => expect(sent).toEqual([{ route: `POST ${base}/assessments/${authenticityAssessmentId}/dismiss`, body: { reason: 'FalsePositive', note: null } }]));
  });

  it('keeps explanations read-only once the result is dismissed', async () => {
    const bot = botExplained({ NET_DATACENTER_IP: explanation(user.id) });
    serve({
      ...bot,
      authenticity: {
        ...bot.authenticity!,
        dismissal: { actionId: 'd1', reason: 'FalsePositive', note: null, actorUserId: user.id, actorName: 'Avery Quinn', dismissedAt: '2026-10-20T11:00:00Z' },
      },
    });
    renderPanel();

    await assessed();
    await openFinding('NET_DATACENTER_IP');
    expect(finding('NET_DATACENTER_IP').querySelector('[data-explanation]')).toHaveTextContent(/^You /);
    expect(screen.queryByRole('button', { name: /Undo|Mark explained|Dismiss/ })).not.toBeInTheDocument();
  });

  it('shows no review controls to a role that does not review verification, and says why', async () => {
    signedIn.user = { ...userAs('Viewer'), id: user.id };
    serve(botExplained({ NET_DATACENTER_IP: explanation(user.id) }));
    renderPanel();

    await assessed();
    await openFinding('AUTO_WEBDRIVER');

    expect(screen.queryByRole('button', { name: /Mark explained|Undo|Dismiss/ })).not.toBeInTheDocument();
    // Read-only says why (E3).
    expect(summaryPanel()).toHaveTextContent('Your role can read these checks; ask a reviewer to explain or dismiss them.');
    expect(finding('NET_DATACENTER_IP').querySelector('[data-explanation]')).toHaveTextContent(/^You /);
  });

  it('asks for a note before dismissing for another reason', async () => {
    const sent = serve(botVerification(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 200, body: botVerification() }),
    });
    renderPanel();

    await fireEvent.click(await within(await assessed()).findByRole('button', { name: 'Dismiss' }));
    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Other reason' }));
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    expect(await within(dialog).findByText('Say why, in a few words, when the reason is "Other".')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Note (required)')).toHaveAttribute('aria-invalid', 'true');
    expect(sent).toEqual([]);
  });

  it('closes and says so when the result was already dismissed or replaced', async () => {
    serve(botVerification(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 409, body: { title: 'Conflict', status: 409 } }),
    });
    renderPanel();

    await fireEvent.click(await within(await assessed()).findByRole('button', { name: 'Dismiss' }));
    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    await screen.findByText('This result was already dismissed, or a newer one replaced it. The tab now shows the latest.');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('keeps the dialog open with a message when the API fails, and opens clean the next time', async () => {
    serve(botVerification(), {
      [`POST ${base}/assessments/${authenticityAssessmentId}/dismiss`]: () => ({ status: 500, body: { title: 'Server error', status: 500 } }),
    });
    renderPanel();

    await fireEvent.click(await within(await assessed()).findByRole('button', { name: 'Dismiss' }));
    let dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('radio', { name: 'Explained by the candidate' }));
    await fireEvent.input(within(dialog).getByLabelText('Note (optional)'), { target: { value: 'Said so on the phone.' } });
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Dismiss with this reason' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent("We couldn't record the dismissal just now. Try again in a minute.");
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await fireEvent.click(within(summaryPanel()).getByRole('button', { name: 'Dismiss' }));
    dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByRole('radio', { name: 'The observations do not apply' })).toBeChecked();
    expect(within(dialog).getByLabelText('Note (optional)')).toHaveValue('');
  });

  it('keeps the evidence on screen when a refetch fails', async () => {
    let failing = false;
    vi.stubGlobal(
      'fetch',
      vi.fn(apiStub({ [`GET ${base}`]: () => (failing ? Response.json({ title: 'Server error', status: 500 }, { status: 500 }) : Response.json(botVerification())) })),
    );
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(WithQueryClient, { props: { component: VerificationPanel, props: { applicationId: verificationApplicationId }, client } });
    await screen.findByRole('region', { name: 'Network' });

    failing = true;
    await invalidatePath(client, '/v1/applications');

    expect(await screen.findByText(/Could not load the verification checks/)).toBeInTheDocument();
    expect(document.querySelector('[data-next-step]')).toHaveTextContent(/^Confirm the candidate/);
  });

  it('turns from pending to assessed when VerificationAssessed arrives, without a reload', async () => {
    let assessed = false;
    vi.stubGlobal('fetch', vi.fn(apiStub({ [`GET ${base}`]: () => Response.json(assessed ? botVerification() : pendingVerification()) })));
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(WithQueryClient, { props: { component: VerificationPanel, props: { applicationId: verificationApplicationId }, client } });
    await screen.findByText(/Checks are still running/);

    // What the shell's live connection does with the event (#lib/realtime.ts).
    assessed = true;
    await Promise.all((invalidations.VerificationAssessed ?? []).map((path) => invalidatePath(client, path)));

    await screen.findByRole('region', { name: 'Network' });
    expect(screen.queryByText(/Checks are still running/)).not.toBeInTheDocument();
  });

  it('says the checks are still running while they are', async () => {
    serve(pendingVerification());
    renderPanel();

    expect(await screen.findByText(/Checks are still running/)).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Check groups' })).not.toBeInTheDocument();
  });

  it('tolerates an API that sends no check groups', async () => {
    serve({ ...genuineVerification(), checkGroups: null });
    renderPanel();

    const summary = await assessed();
    expect(summary.querySelector('[data-summary]')).toHaveTextContent('Nothing unusual in the checks that ran. No action needed.');
  });
});
