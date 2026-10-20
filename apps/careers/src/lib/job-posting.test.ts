import type { PublicJob } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { buildJobPosting, buildSitemap } from './job-posting';

const context = {
  organizationName: 'Harbour & Co',
  boardUrl: 'https://careers.example.com/harbour',
  url: 'https://careers.example.com/harbour/jobs/engineer',
};

const job = (overrides: Partial<PublicJob> = {}): PublicJob => ({
  id: '0199f0c4-0000-7000-8000-000000000001',
  title: 'Engineer',
  slug: 'engineer',
  location: 'London, GB',
  workplaceType: 'Onsite',
  employmentType: 'FullTime',
  salaryMin: 50000,
  salaryMax: 60000,
  salaryCurrency: 'GBP',
  publishedAt: '2026-10-01T09:00:00+00:00',
  description: '<p>Build things.</p>',
  department: 'Engineering',
  locations: [{ city: 'London', region: null, country: 'GB' }],
  applicantLocationRequirements: [],
  salaryPeriod: 'Year',
  validThrough: '2026-11-01T00:00:00+00:00',
  updatedAt: '2026-10-02T09:00:00+00:00',
  url: 'https://careers.example.com/harbour/jobs/engineer',
  ...overrides,
});

describe('buildJobPosting', () => {
  it('has every property Google requires and recommends', () => {
    const posting = buildJobPosting(job(), context);

    expect(posting).toMatchObject({
      '@context': 'https://schema.org',
      '@type': 'JobPosting',
      title: 'Engineer',
      description: '<p>Build things.</p>',
      datePosted: '2026-10-01T09:00:00+00:00',
      validThrough: '2026-11-01T00:00:00+00:00',
      employmentType: 'FULL_TIME',
      url: context.url,
      identifier: { '@type': 'PropertyValue', name: 'Harbour & Co', value: job().id },
      hiringOrganization: { '@type': 'Organization', name: 'Harbour & Co', sameAs: context.boardUrl },
      jobLocation: { '@type': 'Place', address: { '@type': 'PostalAddress', addressLocality: 'London', addressCountry: 'GB' } },
      baseSalary: {
        '@type': 'MonetaryAmount',
        currency: 'GBP',
        value: { '@type': 'QuantitativeValue', minValue: 50000, maxValue: 60000, unitText: 'YEAR' },
      },
    });
  });

  it.each([
    ['FullTime', 'FULL_TIME'],
    ['PartTime', 'PART_TIME'],
    ['Contract', 'CONTRACTOR'],
    ['Temporary', 'TEMPORARY'],
    ['Internship', 'INTERN'],
  ] as const)("maps %s to Google's %s", (employmentType, expected) => {
    expect(buildJobPosting(job({ employmentType }), context).employmentType).toBe(expected);
  });

  it('marks a remote job TELECOMMUTE with the countries candidates may work from, and no jobLocation', () => {
    const posting = buildJobPosting(job({ workplaceType: 'Remote', locations: [], applicantLocationRequirements: ['GB', 'IE'] }), context);

    expect(posting.jobLocationType).toBe('TELECOMMUTE');
    expect(posting.applicantLocationRequirements).toEqual([
      { '@type': 'Country', name: 'GB' },
      { '@type': 'Country', name: 'IE' },
    ]);
    expect(posting).not.toHaveProperty('jobLocation');
  });

  it('keeps a salary with only a maximum, as a single value', () => {
    const posting = buildJobPosting(job({ salaryMin: null, salaryMax: 60000 }), context);

    expect(posting.baseSalary).toMatchObject({ value: { value: 60000, unitText: 'YEAR' } });
  });

  it('leaves the salary out without a currency', () => {
    expect(buildJobPosting(job({ salaryCurrency: null }), context)).not.toHaveProperty('baseSalary');
  });

  it("carries a closed job's past validThrough as the API sends it", () => {
    expect(buildJobPosting(job({ validThrough: '2026-10-15T12:00:00+00:00' }), context).validThrough).toBe('2026-10-15T12:00:00+00:00');
  });

  it('falls back to an escaped one-line description', () => {
    expect(buildJobPosting(job({ description: '', title: 'A <b> role' }), context).description).toBe(
      '<p>A &lt;b&gt; role at Harbour &amp; Co.</p>',
    );
  });
});

describe('buildSitemap', () => {
  it('lists the board and each job with its last change', () => {
    const xml = buildSitemap(context.boardUrl, [
      { url: `${context.boardUrl}/jobs/engineer`, updatedAt: '2026-10-02T09:00:00+00:00', publishedAt: '2026-10-01T09:00:00+00:00' },
      { url: `${context.boardUrl}/jobs/designer`, updatedAt: null, publishedAt: '2026-10-03T09:00:00+00:00' },
    ]);

    expect(xml).toContain('<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">');
    expect(xml).toContain(`<url><loc>${context.boardUrl}</loc><lastmod>2026-10-03T09:00:00+00:00</lastmod></url>`);
    expect(xml).toContain(`<url><loc>${context.boardUrl}/jobs/engineer</loc><lastmod>2026-10-02T09:00:00+00:00</lastmod></url>`);
    expect(xml).toContain(`<url><loc>${context.boardUrl}/jobs/designer</loc><lastmod>2026-10-03T09:00:00+00:00</lastmod></url>`);
  });

  it('escapes URLs', () => {
    expect(buildSitemap('https://careers.example.com/a&b', [])).toContain('<loc>https://careers.example.com/a&amp;b</loc>');
  });
});
