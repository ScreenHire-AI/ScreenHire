import { StageKind } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { applications, now } from '../test-support/fixtures.js';
import { botVerification, summary } from '../test-support/verification-fixtures.js';
import type { CheckGroupView } from '@screenhire/api-client';
import {
  aiMarkOf,
  bulkMoveStages,
  findingsByGroup,
  groupAnchor,
  groupDidNotRun,
  groupStateText,
  matchesVerificationFilter,
  needsReview,
  noVerificationFilter,
  overviewStepOf,
  stateOf,
  verificationFilterOptions,
  verificationOverview,
  verificationRank,
  verificationRecommended,
  verificationStateOf,
} from './verification.js';
import { findSavedView } from './saved-views.js';

const [clear, review, verify] = [
  { ...applications[0]!, verification: summary() },
  { ...applications[1]!, verification: summary({ authenticity: 'ReviewSuggested', authenticityReasons: ['A', 'B'], aiAssistance: 'SomeIndicators' }) },
  { ...applications[2]!, verification: summary({ authenticity: 'VerificationRecommended', authenticityReasons: ['A', 'B', 'C'] }) },
];
const pending = { ...applications[0]!, id: 'pending', verification: null };
const dismissed = { ...verify, id: 'dismissed', verification: { ...verify.verification, authenticityDismissed: true } };

describe('verification overview', () => {
  it('counts each authenticity level, the pending ones and the dismissed ones, adding up to the total', () => {
    expect(verificationOverview([clear, review, verify, pending, dismissed])).toEqual({
      total: 5,
      pending: 1,
      levels: { NoConcerns: 1, ReviewSuggested: 1, VerificationRecommended: 2 },
      dismissed: 1,
    });
  });
});

describe('verification filter', () => {
  it('shows every row without a filter', () => {
    expect([clear, review, verify, pending].filter((a) => matchesVerificationFilter(a, noVerificationFilter))).toHaveLength(4);
  });

  it('keeps one authenticity level, or only the pending rows', () => {
    expect([clear, review, verify, pending].filter((a) => matchesVerificationFilter(a, { ...noVerificationFilter, authenticity: 'ReviewSuggested' }))).toEqual([review]);
    expect([clear, review, verify, pending].filter((a) => matchesVerificationFilter(a, { ...noVerificationFilter, authenticity: 'Pending' }))).toEqual([pending]);
  });

  it('never filters by AI assistance, which is information (a row with signs of AI help shows under its own state)', () => {
    const rows = [clear, review, verify, pending];
    expect(rows.filter((a) => matchesVerificationFilter(a, { authenticity: 'ReviewSuggested' }))).toEqual([review]);
    expect(Object.keys(noVerificationFilter)).toEqual(['authenticity']);
  });

  it('lists its choices by their one word, then Pending', () => {
    expect(verificationFilterOptions.map((option) => option.word)).toEqual(['Clear', 'Review', 'Verify', 'Pending']);
  });
});

describe('verification state', () => {
  it('names each level by one word and keeps the long name for screen readers', () => {
    expect([clear, review, verify].map((a) => stateOf(a.verification))).toMatchObject([
      { kind: 'clear', word: 'Clear', label: 'No concerns', steps: 0 },
      { kind: 'review', word: 'Review', label: 'Review suggested', steps: 2 },
      { kind: 'verify', word: 'Verify', label: 'Verification suggested', steps: 3 },
    ]);
  });

  it('is Pending without a summary, and Dismissed once a reviewer set a level aside, which keeps the level it had', () => {
    expect(stateOf(null)).toMatchObject({ kind: 'pending', word: 'Pending' });
    expect(stateOf(dismissed.verification)).toMatchObject({ kind: 'dismissed', word: 'Dismissed', assessedAs: 'VerificationRecommended' });
    expect(stateOf(dismissed.verification).label).toBe('Verification suggested, dismissed after review');
    // The lowest level has nothing to dismiss.
    expect(verificationStateOf('NoConcerns', true).kind).toBe('clear');
  });

  it('shows the AI mark only for Some and Strong signs', () => {
    expect(aiMarkOf(clear.verification)).toBeUndefined();
    expect(aiMarkOf(review.verification)).toBe('SomeIndicators');
    expect(aiMarkOf(summary({ aiAssistance: 'StrongIndicators' }))).toBe('StrongIndicators');
    expect(aiMarkOf(null)).toBeUndefined();
  });

  it('gives the Overview a sentence only for Review and Verify, and never speaks of rejecting', () => {
    expect(overviewStepOf(stateOf(verify.verification))).toBe('Confirm who they are on a short video call before interviewing.');
    const sentences = [clear, review, verify, pending, dismissed].map((a) => overviewStepOf(stateOf(a.verification)));
    expect(sentences.filter(Boolean)).toHaveLength(2);
    expect(sentences.join(' ')).not.toMatch(/reject/i);
  });
});

describe('the Verification column sort order', () => {
  it('ranks Verify, Review, Dismissed, Clear, then Pending last', () => {
    const ranked = [clear, review, verify, pending, dismissed].toSorted((a, b) => (verificationRank(b) ?? -1) - (verificationRank(a) ?? -1)).map((a) => stateOf(a.verification).kind);
    expect(ranked).toEqual(['verify', 'review', 'dismissed', 'clear', 'pending']);
    expect(verificationRank(pending)).toBeUndefined();
  });

  it('ignores AI assistance', () => {
    const aiOnly = { ...clear, verification: summary({ aiAssistance: 'StrongIndicators', aiAssistanceReasons: ['X'] }) };
    expect(verificationRank(aiOnly)).toBe(verificationRank(clear));
  });
});

describe('verification saved views', () => {
  it('"Needs review" holds authenticity above its lowest level, until a reviewer dismisses it', () => {
    expect([clear, review, verify, pending, dismissed].filter(needsReview)).toEqual([review, verify]);
    expect([clear, review, verify, pending, dismissed].filter((a) => findSavedView('needs-review')!.matches(a, now))).toEqual([review, verify]);
  });

  it('"Verification suggested" holds the highest level only', () => {
    expect([clear, review, verify, pending, dismissed].filter(verificationRecommended)).toEqual([verify]);
    expect(findSavedView('verification-recommended')!.status).toBe('verification');
  });

  it('ignores AI assistance, which is information rather than a queue', () => {
    const aiOnly = { ...clear, verification: summary({ aiAssistance: 'StrongIndicators', aiAssistanceReasons: ['X'] }) };
    expect(needsReview(aiOnly)).toBe(false);
  });
});

describe('bulk moves under a verification filter', () => {
  const stages = Object.values(StageKind).map((kind) => ({ id: kind.toLowerCase(), name: kind, kind }));

  it('offers every stage when no verification filter picks the rows', () => {
    expect(bulkMoveStages(stages, noVerificationFilter)).toEqual(stages);
  });

  it('never offers a stage that ends the candidacy while a verification filter is set (ADR-0004)', () => {
    for (const filter of [
      { ...noVerificationFilter, authenticity: 'VerificationRecommended' },
      { ...noVerificationFilter, authenticity: 'ReviewSuggested' },
      { ...noVerificationFilter, authenticity: 'NoConcerns' },
      { ...noVerificationFilter, authenticity: 'Pending' },
    ] as const) {
      const kinds = bulkMoveStages(stages, filter).map((stage) => stage.kind);
      expect(kinds).not.toContain('Rejected');
      expect(kinds).not.toContain('Withdrawn');
      expect(kinds).toContain('Interview');
    }
  });
});

describe('check groups', () => {
  const group = (overrides: Partial<CheckGroupView>): CheckGroupView => ({
    group: 'Network',
    label: 'Network',
    state: 'NothingFound',
    found: 0,
    notes: 0,
    checked: 'hosting providers',
    note: null,
    ...overrides,
  });

  it('words each state in a few words that say nothing about the person', () => {
    expect(groupStateText(group({ state: 'Found', found: 2 }))).toBe('2 found');
    expect(groupStateText(group({ state: 'InformationOnly', notes: 1 }))).toBe('1 note, information only');
    expect(groupStateText(group({ state: 'InformationOnly', notes: 3 }))).toBe('3 notes, information only');
    expect(groupStateText(group({ state: 'NothingFound' }))).toBe('Nothing found');
    expect(groupStateText(group({ state: 'NotChecked' }))).toBe('Not checked');
    expect(groupStateText(group({ state: 'TurnedOff' }))).toBe('Turned off');
    expect(groupStateText(group({ state: 'Paused' }))).toBe('AI checks paused');
  });

  it('says a quiet result did not run only for a group that was turned off or not checked', () => {
    expect(groupDidNotRun(group({ state: 'TurnedOff' }))).toBe(true);
    expect(groupDidNotRun(group({ state: 'NotChecked' }))).toBe(true);
    expect(groupDidNotRun(group({ state: 'NothingFound' }))).toBe(false);
    expect(groupDidNotRun(group({ state: 'Paused' }))).toBe(false); // its rule-based checks ran
    expect(groupDidNotRun(group({ state: 'InformationOnly', notes: 1 }))).toBe(false);
  });

  it('puts each finding in its group, the ones that count toward the level first, AI help after', () => {
    const bot = botVerification();
    const byGroup = findingsByGroup(bot.authenticity, bot.aiAssistance);

    expect(byGroup.Network.map((finding) => finding.signal.code)).toEqual(['NET_DATACENTER_IP', 'NET_ANON_PROXY']);
    expect(byGroup.FormUse.map((finding) => finding.signal.code)).toEqual(['AUTO_SUBMIT_VELOCITY', 'AUTO_WEBDRIVER']);
    expect(byGroup.Documents.map((finding) => [finding.signal.code, finding.kind])).toEqual([['CNT_PROMPT_INJECTION', 'aiAssistance']]);
    expect(byGroup.Answers.map((finding) => finding.signal.code)).toEqual(['CNT_NEAR_DUPLICATE_ANSWER']);
    expect(byGroup.Contact).toEqual([]);
    expect(byGroup.Duplicates).toEqual([]);
    expect(findingsByGroup(null, undefined).Network).toEqual([]);
  });

  it('gives each group an anchor of its own', () => {
    expect(groupAnchor('FormUse')).toBe('verification-group-formuse');
  });
});
