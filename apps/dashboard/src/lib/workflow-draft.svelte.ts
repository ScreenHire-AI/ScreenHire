import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { ApiError } from '@screenhire/api-client/svelte-query';
import { describeError } from '#lib/query.js';
import { problemErrors } from '#lib/workflow.js';
import { createLiveValidation } from '#lib/workflow-validation.svelte.js';

/**
 * The workflow being edited on a builder page, held locally until it is saved: the API refuses to save an invalid
 * workflow, so work in progress can only live here. It knows whether there are unsaved changes, checks each change with
 * the API's validator, and keeps the errors a save answered with until the workflow changes again. Call during
 * component setup.
 */
export function createWorkflowDraft() {
  let workflow = $state<HiringWorkflow | undefined>();
  let source = $state('');
  let baseline = $state('');
  let saveErrors = $state<{ snapshot: string; errors: DefinitionError[] } | null>(null);
  const snapshot = $derived(workflow ? JSON.stringify(workflow) : '');
  const live = createLiveValidation(() => workflow);

  return {
    get workflow() {
      return workflow;
    },
    set workflow(value: HiringWorkflow | undefined) {
      workflow = value;
    },
    /** Starts editing `loaded` when `key` (which version, in which state) differs from what is being edited. */
    load(loaded: HiringWorkflow, key: string) {
      if (key === source) return;
      source = key;
      workflow = JSON.parse(JSON.stringify(loaded)) as HiringWorkflow;
      baseline = JSON.stringify(workflow);
      saveErrors = null;
    },
    /** The save went through: what is on screen is what is stored. */
    saved(stored: HiringWorkflow, key: string) {
      source = key;
      baseline = JSON.stringify(stored);
      saveErrors = null;
    },
    /** A save failed. Validation problems go on the fields; anything else comes back as a sentence. */
    failed(error: unknown, fallback: string): string {
      if (error instanceof ApiError && error.status === 400) {
        const errors = problemErrors(error.body);
        if (errors.length > 0) {
          saveErrors = { snapshot, errors };
          return 'The workflow has problems to fix; they are listed above.';
        }
      }
      return describeError(error, fallback);
    },
    get dirty() {
      return snapshot !== '' && snapshot !== baseline;
    },
    /** The validator's errors, plus any a save answered with for the workflow as it is now (a stage still in use). */
    get errors(): DefinitionError[] {
      const fromSave = saveErrors?.snapshot === snapshot ? saveErrors.errors : [];
      const seen = new Set(live.errors.map((error) => `${error.path}\n${error.message}`));
      return [...live.errors, ...fromSave.filter((error) => !seen.has(`${error.path}\n${error.message}`))];
    },
    get checking() {
      return live.checking;
    },
  };
}
