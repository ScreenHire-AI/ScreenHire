import type { ScorecardTemplateSummary, TeamMember, WorkflowStage } from '@screenhire/api-client';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import Harness from '../../test-support/ask-scorecard-harness.svelte';
import AskScorecardSheet from './ask-scorecard-sheet.svelte';

const teammate: TeamMember = {
  id: 'm-2', userId: 'u-2', email: 'jordan@example.com', displayName: 'Jordan Ellis', role: 'Recruiter', customRoleId: null, roleName: 'Recruiter',
  joinedAt: '2026-10-01T09:00:00Z', jobAccess: 'All', seesAllJobs: true, jobCount: 0,
};
const library: ScorecardTemplateSummary[] = [
  { id: 'interview', name: 'Interview', builtIn: true, questions: 4, ratingQuestions: 3, lastUpdated: { at: null, by: 'ScreenHire' }, archived: false },
  { id: 'screening', name: 'Screening', builtIn: true, questions: 3, ratingQuestions: 2, lastUpdated: { at: null, by: 'ScreenHire' }, archived: false },
];
const interview: WorkflowStage = {
  id: 'interview',
  name: 'Interview',
  kind: 'Interview',
  scorecard: { name: 'Interview', questions: [{ id: 'q', type: 'Rating5', label: 'Skills', required: false }] },
  scorecardTemplateId: 'interview',
};

const sheet = (currentStage: WorkflowStage | undefined) => ({
  component: AskScorecardSheet,
  props: { open: true, applicationId: 'a-1', teammates: [teammate], library, currentStage, ondone: () => {} },
});

const scorecardSelect = () => screen.getByRole('combobox', { name: 'Scorecard' }) as HTMLSelectElement;

describe('ask for a scorecard sheet', () => {
  it("starts on the current stage's own scorecard", () => {
    render(WithQueryClient, { props: sheet(interview) });

    expect(scorecardSelect().value).toBe('interview');
  });

  it("fills in the stage's scorecard when the job's workflow arrives after the sheet opened", async () => {
    render(WithQueryClient, { props: { component: Harness, props: { teammates: [teammate], library, arriving: interview } } });
    expect(scorecardSelect().value).toBe('');

    await fireEvent.click(screen.getByText('Workflow arrives'));

    expect(scorecardSelect().value).toBe('interview');
  });

  it("keeps the person's own choice when the workflow arrives later", async () => {
    render(WithQueryClient, { props: { component: Harness, props: { teammates: [teammate], library, arriving: interview } } });
    await fireEvent.change(scorecardSelect(), { target: { value: 'screening' } });

    await fireEvent.click(screen.getByText('Workflow arrives'));

    expect(scorecardSelect().value).toBe('screening');
  });
});
