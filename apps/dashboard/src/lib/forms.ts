import type { HttpValidationProblemDetails } from '@screenhire/api-client';
import { setError, type SuperValidated } from 'sveltekit-superforms';

/**
 * Puts an API validation problem's errors on the matching form fields. The API names fields in camelCase, the same
 * names as the request body, so they line up with the form's keys. Errors for fields the form does not have become
 * form-level errors.
 */
export function applyValidationProblem<T extends Record<string, unknown>>(
  form: SuperValidated<T>,
  problem: HttpValidationProblemDetails | undefined,
): void {
  for (const [field, messages] of Object.entries(problem?.errors ?? {})) {
    for (const message of messages) {
      if (field in form.data) {
        setError(form, field as never, message);
      } else {
        setError(form, message);
      }
    }
  }
}

/** A URL-safe slug from an organization name: lower-case letters, digits and single hyphens, at most 63 long. */
export function slugify(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 63)
    .replace(/-+$/g, '');
}
