import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { createValidateWorkflow } from '@screenhire/api-client/svelte-query';

/**
 * Checks the workflow being edited with the API's own validator (`POST /v1/workflows/validate`), guardrails included,
 * a moment after each change. Error paths are list indexes, so an answer is kept only if the workflow still equals the
 * snapshot it checked; an answer that arrives after a newer change is dropped. Call during component setup.
 */
export function createLiveValidation(workflow: () => HiringWorkflow | undefined, delayMs = 300) {
  const validate = createValidateWorkflow();
  let errors = $state<DefinitionError[]>([]);
  let checked = $state('');
  let failed = $state(false);
  const current = $derived.by(() => {
    const value = workflow();
    return value ? JSON.stringify(value) : '';
  });

  $effect(() => {
    const snapshot = current;
    if (!snapshot || snapshot === checked) return;
    const timer = setTimeout(async () => {
      try {
        const response = await validate.mutateAsync({ data: { workflow: JSON.parse(snapshot) as HiringWorkflow } });
        if (snapshot !== current) return;
        errors = response.data.errors;
        checked = snapshot;
        failed = false;
      } catch {
        if (snapshot === current) failed = true;
      }
    }, delayMs);
    return () => clearTimeout(timer);
  });

  return {
    /** The validator's last errors. While a check is pending they describe the previous change, so they do not flicker. */
    get errors() {
      return errors;
    },
    /** A check is on its way for the latest change. */
    get checking() {
      return checked !== current && !failed;
    },
    /** The last check could not reach the API; the save will still validate. */
    get failed() {
      return failed;
    },
    get valid() {
      return checked === current && errors.length === 0;
    },
  };
}
