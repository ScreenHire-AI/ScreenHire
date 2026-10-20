// schema.org JobPosting for Google for Jobs (docs/architecture/job-distribution.md), built from the public job's
// structured fields. Pure, so the rules Google documents are unit-tested
// (developers.google.com/search/docs/appearance/structured-data/job-posting).
import type { PublicJob } from '@screenhire/api-client';
import { escapeHtml } from './format';

/** Google's employmentType values, which are not our enum names: CONTRACTOR, not CONTRACT; INTERN, not INTERNSHIP. */
const employmentTypes: Record<PublicJob['employmentType'], string> = {
  FullTime: 'FULL_TIME',
  PartTime: 'PART_TIME',
  Contract: 'CONTRACTOR',
  Temporary: 'TEMPORARY',
  Internship: 'INTERN',
};

export interface JobPostingContext {
  organizationName: string;
  /** The tenant's careers board, such as https://acme.careers.example.com. */
  boardUrl: string;
  /** This job's page. */
  url: string;
}

/**
 * A complete JobPosting. For a closed job (the API's 410), `validThrough` is when it closed, which is in the past: with
 * the page's 410 status, that is how Google learns the posting expired.
 */
export function buildJobPosting(job: PublicJob, context: JobPostingContext): Record<string, unknown> {
  const address = (location: PublicJob['locations'][number]) => ({
    '@type': 'PostalAddress',
    ...(location.city && { addressLocality: location.city }),
    ...(location.region && { addressRegion: location.region }),
    ...(location.country && { addressCountry: location.country }),
  });
  const places = job.locations.map((location) => ({ '@type': 'Place', address: address(location) }));
  const hasSalary = job.salaryCurrency != null && (job.salaryMin != null || job.salaryMax != null);
  const remote = job.workplaceType === 'Remote';

  return {
    '@context': 'https://schema.org',
    '@type': 'JobPosting',
    title: job.title,
    description: job.description || `<p>${escapeHtml(job.title)} at ${escapeHtml(context.organizationName)}.</p>`,
    identifier: { '@type': 'PropertyValue', name: context.organizationName, value: job.id },
    url: context.url,
    datePosted: job.publishedAt,
    ...(job.validThrough && { validThrough: job.validThrough }),
    employmentType: employmentTypes[job.employmentType] ?? 'OTHER',
    hiringOrganization: { '@type': 'Organization', name: context.organizationName, sameAs: context.boardUrl },
    ...(job.department && { occupationalCategory: job.department }),
    // A hybrid job is still based somewhere, so only a remote one is TELECOMMUTE.
    ...(remote
      ? {
          jobLocationType: 'TELECOMMUTE',
          applicantLocationRequirements: job.applicantLocationRequirements.map((country) => ({ '@type': 'Country', name: country })),
        }
      : { jobLocation: places.length === 1 ? places[0] : places }),
    ...(hasSalary && {
      baseSalary: {
        '@type': 'MonetaryAmount',
        currency: job.salaryCurrency,
        value: {
          '@type': 'QuantitativeValue',
          ...(job.salaryMin != null && job.salaryMax != null
            ? { minValue: job.salaryMin, maxValue: job.salaryMax }
            : { value: job.salaryMin ?? job.salaryMax }),
          unitText: (job.salaryPeriod ?? 'Year').toUpperCase(),
        },
      },
    }),
  };
}

/** A tenant's sitemap: the board page and every open job's page, with lastmod from the job's last change. */
export function buildSitemap(boardUrl: string, jobs: Pick<PublicJob, 'url' | 'updatedAt' | 'publishedAt'>[]): string {
  const entry = (loc: string, lastmod?: string | null) =>
    `  <url><loc>${escapeHtml(loc)}</loc>${lastmod ? `<lastmod>${escapeHtml(lastmod)}</lastmod>` : ''}</url>`;
  const newest = jobs.map((job) => job.updatedAt ?? job.publishedAt).filter((date): date is string => date != null).sort().at(-1);
  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    entry(boardUrl, newest),
    ...jobs.map((job) => entry(job.url, job.updatedAt ?? job.publishedAt)),
    '</urlset>',
    '',
  ].join('\n');
}
