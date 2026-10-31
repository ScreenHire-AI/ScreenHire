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

/**
 * Plain text as the HTML a job description is: a paragraph per blank-line-separated block, line breaks kept, and every
 * character escaped. Lines that start with "- " are list items, and items in a row make one list, blank lines between
 * them or not. The API sanitizes the result again.
 */
export function paragraphs(text: string): string {
  const escape = (value: string) =>
    value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  let html = '';
  let items: string[] = [];
  const endList = () => {
    if (items.length > 0) html += `<ul>${items.map((item) => `<li>${item}</li>`).join('')}</ul>`;
    items = [];
  };
  for (const block of text.split(/\n\s*\n/).map((part) => part.trim()).filter(Boolean)) {
    const lines = block.split('\n').map((line) => line.trim());
    if (lines.every((line) => line.startsWith('- '))) {
      items.push(...lines.map((line) => escape(line.slice(2).trim())));
    } else {
      endList();
      html += `<p>${lines.map(escape).join('<br>')}</p>`;
    }
  }

  endList();
  return html;
}
