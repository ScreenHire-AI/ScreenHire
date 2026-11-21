// A step of an automation in plain words, as the Test drive says what happens to a sample candidate. Pure, so the words
// are tested without a page. Generated text never names a person: a reviewer is "the team", whoever is saved in the step.
import type { AutomationAction, HiringWorkflow } from '@screenhire/api-client';
import { followUpName, followUpsOf } from '#lib/workflow.js';

/** One step in a sentence: a verb, third person. */
export function actionWords(action: AutomationAction, workflow: HiringWorkflow, templateNames: ReadonlyMap<string, string>): string {
  switch (action.type) {
    case 'SendEmail': {
      const name = action.emailTemplate ? templateNames.get(action.emailTemplate) : undefined;
      return name ? `Sends the "${name}" email` : 'Sends an email';
    }
    case 'SendQuestionnaire': {
      const form = followUpsOf(workflow).find((followUp) => followUp.id === action.questionnaireId);
      const count = form?.questions.length ?? 0;
      return `Sends the "${followUpName(workflow, action.questionnaireId)}" form${count > 0 ? `, ${count} ${count === 1 ? 'question' : 'questions'}` : ''}`;
    }
    case 'MoveToStage':
      return `Moves them to ${workflow.stages.find((stage) => stage.id === action.stageId)?.name ?? 'another stage'}`;
    case 'AddTag':
      return `Adds the tag "${action.tag ?? ''}"`;
    case 'AssignReviewer':
      return 'Asks the team to review';
    case 'NotifyTeam':
      return 'Tells the team';
    case 'CreateTask':
      return 'Adds a task for the team';
    case 'CallWebhook':
      return 'Tells another system';
    case 'ScheduleInterview':
      return 'Offers an interview';
    case 'RequestVerification':
      return 'Asks for a check';
    case 'QueueForRejectionReview':
      return 'Queues them for the team to look at first';
    case 'Reject':
      return 'Declines them';
    default:
      return 'Does something';
  }
}
