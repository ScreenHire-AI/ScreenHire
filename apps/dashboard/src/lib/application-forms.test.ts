// A candidate's forms on their Application tab: each sent form's state in words, and what Send a form offers.
import type { ApplicationFollowUp, FollowUpQuestionnaire } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { sendableForms, sentFormState } from './application-forms.js';

const sent = (questionnaireId: string, extra: Partial<ApplicationFollowUp> = {}): ApplicationFollowUp => ({
  id: `request-${questionnaireId}`,
  questionnaireId,
  name: questionnaireId,
  firstSentAt: '2026-11-19T09:00:00Z',
  lastSentAt: '2026-11-20T09:00:00Z',
  expiresAt: '2026-12-04T09:00:00Z',
  timesSent: 1,
  completedAt: null,
  questions: [],
  answers: [],
  ...extra,
});

const form = (id: string, extra: Partial<FollowUpQuestionnaire> = {}): FollowUpQuestionnaire => ({
  id,
  name: id,
  questions: [{ id: `q_${id}`, type: 'ShortText', label: 'A?', required: false }],
  ...extra,
});

describe('sentFormState', () => {
  const now = new Date('2026-11-22T09:00:00Z');

  it('is answered at its answer, else waiting since its last send while the link works, and expired after', () => {
    expect(sentFormState(sent('a', { completedAt: '2026-11-21T09:00:00Z' }), now)).toEqual({ kind: 'answered', at: '2026-11-21T09:00:00Z' });
    expect(sentFormState(sent('b'), now)).toEqual({ kind: 'waiting', at: '2026-11-20T09:00:00Z' });
    expect(sentFormState(sent('c', { expiresAt: '2026-11-22T09:00:00Z' }), now)).toEqual({ kind: 'expired', at: '2026-11-20T09:00:00Z' });
  });
});

describe('sendableForms', () => {
  it("offers each of the job's forms with Send, Send again while it waits, and nothing once answered", () => {
    expect(
      sendableForms(
        [form('screening'), form('references'), form('equality', { kind: 'EqualityMonitoring' }), form('fresh')],
        [sent('screening', { completedAt: '2026-11-21T09:00:00Z' }), sent('references')],
      ),
    ).toEqual([
      { id: 'screening', name: 'screening', sent: false, answered: true, equality: false },
      { id: 'references', name: 'references', sent: true, answered: false, equality: false },
      { id: 'equality', name: 'equality', sent: false, answered: false, equality: true },
      { id: 'fresh', name: 'fresh', sent: false, answered: false, equality: false },
    ]);
  });
});
