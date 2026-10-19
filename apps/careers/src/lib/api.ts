import { configureApiClient } from '@screenhire/api-client';

// Server-side only: Aspire injects API_URL in development; production sets it to the internal API address.
configureApiClient({ baseUrl: process.env.API_URL ?? 'http://localhost:5080' });

export * from '@screenhire/api-client';

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
