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
  // A newer stored version that arrived while there were unsaved changes: kept aside, never loaded over them.
  let newer = $state<{ workflow: HiringWorkflow; key: string } | null>(null);
  const snapshot = $derived(workflow ? JSON.stringify(workflow) : '');
  const live = createLiveValidation(() => workflow);

  return {
    get workflow() {
      return workflow;
    },
    set workflow(value: HiringWorkflow | undefined) {
      workflow = value;
    },
    /**
     * The key of the stored version the edit started from. It stays put while a newer version waits in `outdated`, so a
     * save can name the version it was based on and be refused if someone else saved since.
     */
    get source() {
      return source;
    },
    /**
     * Starts editing `loaded` when `key` (which version, in which state) differs from what is being edited. With unsaved
     * changes it keeps them and sets `outdated` instead: someone else's save never wipes work in progress.
     */
    load(loaded: HiringWorkflow, key: string) {
      if (key === source) return;
      if (snapshot !== '' && snapshot !== baseline) {
        newer = { workflow: loaded, key };
        return;
      }
      newer = null;
      source = key;
      workflow = JSON.parse(JSON.stringify(loaded)) as HiringWorkflow;
      baseline = JSON.stringify(workflow);
      saveErrors = null;
    },
    /** Someone else saved a newer version while there were unsaved changes here. */
    get outdated() {
      return newer !== null && newer.key !== source;
    },
    /** Drops the unsaved changes and loads the newer version. */
    discardForNewer() {
      if (!newer) return;
      const { workflow: loaded, key } = newer;
      newer = null;
      source = '';
      baseline = '';
      workflow = undefined;
      this.load(loaded, key);
    },
    /** Loads `loaded` whatever is on screen: the result of an action that replaced the workflow on purpose. */
    replace(loaded: HiringWorkflow, key: string) {
      newer = null;
      source = '';
      workflow = undefined;
      this.load(loaded, key);
    },
    /** The save went through: what is on screen is what is stored. */
    saved(stored: HiringWorkflow, key: string) {
      newer = null;
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
