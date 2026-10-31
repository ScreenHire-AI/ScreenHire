import { StageKind } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { applications, now } from '../test-support/fixtures.js';
import { summary } from '../test-support/verification-fixtures.js';
import { bulkMoveStages, verificationOverview, matchesVerificationFilter, needsReview, noVerificationFilter, verificationRecommended } from './verification.js';
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

  it('combines the authenticity and AI assistance levels', () => {
    const filter = { authenticity: 'ReviewSuggested', aiAssistance: 'SomeIndicators' } as const;
    expect([clear, review, verify, pending].filter((a) => matchesVerificationFilter(a, filter))).toEqual([review]);
    expect([clear, review, verify, pending].filter((a) => matchesVerificationFilter(a, { ...filter, aiAssistance: 'StrongIndicators' }))).toEqual([]);
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

  it('never offers a stage that ends the candidacy while either verification filter is set (ADR-0004)', () => {
    for (const filter of [
      { ...noVerificationFilter, authenticity: 'VerificationRecommended' },
      { ...noVerificationFilter, authenticity: 'Pending' },
      { ...noVerificationFilter, aiAssistance: 'StrongIndicators' },
    ] as const) {
      const kinds = bulkMoveStages(stages, filter).map((stage) => stage.kind);
      expect(kinds).not.toContain('Rejected');
      expect(kinds).not.toContain('Withdrawn');
      expect(kinds).toContain('Interview');
    }
  });
});
