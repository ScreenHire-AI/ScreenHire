import { describe, expect, it } from 'vitest';
import { describeQueued, emailCount, insertAt, sendableTemplates, variableTag } from './email.js';

describe('email', () => {
  it('never offers the rejection email or the follow-up questions email for sending by hand', () => {
    const templates = [
      { id: 'application-received', rejectOnly: false },
      { id: 'application-rejected', rejectOnly: true },
      { id: 'questionnaire-request', rejectOnly: false, questionnaireOnly: true },
      { id: '0199f0c4-0000-7000-8000-000000000001', rejectOnly: false },
    ];
    expect(sendableTemplates(templates).map((template) => template.id)).toEqual(['application-received', '0199f0c4-0000-7000-8000-000000000001']);
  });

  it('inserts a variable over the selection and puts the caret after it', () => {
    expect(insertAt('Hi there', 3, 8, variableTag('candidateName'))).toEqual({ text: 'Hi {{ candidateName }}', caret: 22 });
    expect(insertAt('Hi', 99, 99, '!')).toEqual({ text: 'Hi!', caret: 3 });
  });

  it('says how many emails are on their way and how many were skipped', () => {
    expect(emailCount(1)).toBe('1 email');
    expect(describeQueued(3, 0)).toBe('Sending 3 emails.');
    expect(describeQueued(2, 1)).toBe('Sending 2 emails. 1 candidate was skipped: their application has ended.');
    expect(describeQueued(0, 2)).toBe('No emails to send. 2 candidates were skipped: their application has ended.');
  });
});
