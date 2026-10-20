// Display helpers for careers pages. Pure functions: no API client and no environment, so they unit-test cleanly.

const labels: Record<string, string> = {
  Remote: 'Remote',
  Hybrid: 'Hybrid',
  Onsite: 'On-site',
  FullTime: 'Full-time',
  PartTime: 'Part-time',
  Contract: 'Contract',
  Temporary: 'Temporary',
  Internship: 'Internship',
};

export const label = (value: string): string => labels[value] ?? value;

export function formatSalary(min?: number | null, max?: number | null, currency?: string | null): string | null {
  if (min == null && max == null) return null;
  const format = (amount: number) =>
    new Intl.NumberFormat('en-GB', {
      style: 'currency',
      currency: currency ?? 'GBP',
      notation: 'compact',
      maximumFractionDigits: 0,
    }).format(amount);
  if (min != null && max != null) return `${format(min)}–${format(max)}`;
  return format((min ?? max)!);
}

/**
 * Serializes JSON-LD for an inline `<script>`. JSON.stringify leaves `<` alone, so a `</script>` in a job title or
 * location would end the element early and run as markup; `\u003c` is the same character to a JSON parser.
 */
export const jsonLdScript = (data: unknown): string => JSON.stringify(data).replace(/</g, '\\u003c');

/** Escapes text for use inside an HTML string, such as the fallback JobPosting description. */
export const escapeHtml = (text: string): string =>
  text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
