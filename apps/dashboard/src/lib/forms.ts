import type { HttpValidationProblemDetails } from '@screenhire/api-client';
import { setError, type SuperValidated } from 'sveltekit-superforms';

/**
 * Puts an API validation problem's errors on the matching form fields. The API names fields in camelCase, the same
 * names as the request body, so they line up with the form's keys. `field` maps a name that differs, such as a nested
 * `candidate.email` for a flat form's `email`. Errors for fields the form does not have become form-level errors.
 */
export function applyValidationProblem<T extends Record<string, unknown>>(
  form: SuperValidated<T>,
  problem: HttpValidationProblemDetails | undefined,
  rename: (field: string) => string = (field) => field,
): void {
  for (const [name, messages] of Object.entries(problem?.errors ?? {})) {
    const field = rename(name);
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

/** Country codes from what a person typed ("gb, ie" or "GB IE"): upper-cased, in order, each once. */
export function countryCodes(text: string): string[] {
  return [...new Set(text.split(/[\s,;]+/).filter(Boolean).map((code) => code.toUpperCase()))];
}

/** Whether every code is an ISO 3166-1 alpha-2 code, as the API requires. */
export const areCountryCodes = (codes: readonly string[]): boolean => codes.every((code) => /^[A-Z]{2}$/.test(code));
