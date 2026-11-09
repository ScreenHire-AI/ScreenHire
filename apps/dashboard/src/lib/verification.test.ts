import { StageKind } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { applications, now } from '../test-support/fixtures.js';
import { summary } from '../test-support/verification-fixtures.js';
import type { VerificationReviewEntry } from '@screenhire/api-client';
import {
  aiMarkOf,
  bulkMoveStages,
  groupKeyReads,
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

describe('review log rows', () => {
  const entry = (id: string, overrides: Partial<VerificationReviewEntry>): VerificationReviewEntry => ({
    id,
    type: 'NoteAdded',
    assessmentKind: null,
    assessmentId: null,
    dismissReason: null,
    note: null,
    actorUserId: 'user-1',
    actorName: 'Avery Quinn',
    occurredAt: '2026-10-20T10:00:00Z',
    signalId: null,
    signalTitle: null,
    explainReason: null,
    apiKeyId: null,
    apiKeyName: null,
    keyCreatedByName: null,
    ...overrides,
  });
  const read = (id: string, apiKeyId: string, occurredAt: string) =>
    entry(id, { type: 'ReadByKey', actorUserId: null, actorName: null, apiKeyId, apiKeyName: apiKeyId, occurredAt });

  it('folds consecutive reads by one key into the latest, with how many there were', () => {
    const rows = groupKeyReads([read('a', 'k1', '2026-10-20T10:00:00Z'), read('b', 'k1', '2026-10-20T10:05:00Z'), read('c', 'k1', '2026-10-20T10:09:00Z')]);

    expect(rows.map((row) => [row.entry.id, row.reads])).toEqual([['c', 3]]);
  });

  it('keeps a different key, and a person\'s action between two reads, as rows of their own', () => {
    const rows = groupKeyReads([read('a', 'k1', '2026-10-20T10:00:00Z'), read('b', 'k2', '2026-10-20T10:01:00Z'), entry('n', {}), read('c', 'k2', '2026-10-20T10:02:00Z')]);

    expect(rows.map((row) => [row.entry.id, row.reads])).toEqual([['a', 1], ['b', 1], ['n', 1], ['c', 1]]);
  });

  it('changes nothing for a log with no key reads', () => {
    const entries = [entry('x', {}), entry('y', { type: 'Dismissed' })];

    expect(groupKeyReads(entries).map((row) => row.entry)).toEqual(entries);
    expect(groupKeyReads([])).toEqual([]);
  });
});
