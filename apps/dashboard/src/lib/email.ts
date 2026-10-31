import type { EmailTemplateEntry, EmailTemplateVariableEntry } from '@screenhire/api-client';

/** The API path every email template query sits under, for `invalidatePath`. */
export const emailTemplatesPath = '/v1/email-templates';

/**
 * The templates a person can send by hand and a SendEmail automation can name: every one but the rejection email, which
 * only a Reject action sends (workflow-engine.md section 2), the follow-up questions email, which only a
 * SendQuestionnaire action sends with its link, and an archived template, which is out of the pickers.
 */
export function sendableTemplates<
  Template extends Pick<EmailTemplateEntry, 'rejectOnly'> & { questionnaireOnly?: boolean; invitationOnly?: boolean; archived?: boolean },
>(templates: readonly Template[]): Template[] {
  // The talent pool's invitation goes only with Invite to apply, which makes its links (docs/architecture/talent-pool.md).
  return templates.filter((template) => !template.rejectOnly && !template.questionnaireOnly && !template.invitationOnly && !template.archived);
}

/**
 * The variables an email a person writes may use: every one but the rejection email's reason and the follow-up questions
 * email's link, which only their actions fill in.
 */
export function sendableVariables(variables: readonly EmailTemplateVariableEntry[]): EmailTemplateVariableEntry[] {
  return variables.filter((variable) => !variable.rejectOnly && !variable.questionnaireOnly && !variable.invitationOnly);
}

/** A variable as a template writes it. */
export const variableTag = (name: string): string => `{{ ${name} }}`;

/**
 * `text` with `insert` in place of the characters from `start` to `end` (a text field's selection), and where the caret
 * goes after it.
 */
export function insertAt(text: string, start: number, end: number, insert: string): { text: string; caret: number } {
  const from = Math.max(0, Math.min(start, text.length));
  const to = Math.max(from, Math.min(end, text.length));
  return { text: text.slice(0, from) + insert + text.slice(to), caret: from + insert.length };
}

/** "1 email", "3 emails". */
export const emailCount = (count: number): string => (count === 1 ? '1 email' : `${count} emails`);

/** What a send to a selection did, in one line: "Sending 3 emails." with any the stage-change email skipped. */
export function describeQueued(queued: number, skipped: number): string {
  const sending = queued === 0 ? 'No emails to send.' : `Sending ${emailCount(queued)}.`;
  if (skipped === 0) return sending;
  return `${sending} ${skipped === 1 ? '1 candidate was' : `${skipped} candidates were`} skipped: their application has ended.`;
}
