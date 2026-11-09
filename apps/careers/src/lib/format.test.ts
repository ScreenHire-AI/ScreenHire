import { describe, expect, it } from 'vitest';
import { escapeHtml, formatSalary, jobFacts, jsonLdScript, label } from './format';

describe('label', () => {
  it.each([
    ['Onsite', 'On-site'],
    ['Hybrid', 'Hybrid'],
    ['Remote', 'Remote'],
    ['FullTime', 'Full-time'],
    ['PartTime', 'Part-time'],
    ['Contract', 'Contract'],
    ['Temporary', 'Temporary'],
    ['Internship', 'Internship'],
  ])('labels %s as %s', (value, expected) => {
    expect(label(value)).toBe(expected);
  });

  it('passes unknown values through unchanged', () => {
    expect(label('Apprenticeship')).toBe('Apprenticeship');
  });
});

describe('formatSalary', () => {
  // en-GB compact notation (CLDR) writes thousands with a lowercase k.
  it('formats a range compactly with an en dash', () => {
    expect(formatSalary(70_000, 85_000, 'GBP')).toBe('£70k–£85k');
  });

  it('formats an open-ended range from whichever bound is known', () => {
    expect(formatSalary(70_000, null, 'GBP')).toBe('£70k');
    expect(formatSalary(undefined, 85_000, 'GBP')).toBe('£85k');
  });

  it('defaults to pounds when the currency is missing', () => {
    expect(formatSalary(45_000, 55_000, null)).toBe('£45k–£55k');
  });

  it("uses the job's currency", () => {
    expect(formatSalary(120_000, 150_000, 'USD')).toBe('US$120k–US$150k');
    expect(formatSalary(60_000, null, 'EUR')).toBe('€60k');
  });

  it('returns null when there is no salary', () => {
    expect(formatSalary(null, null, 'GBP')).toBeNull();
    expect(formatSalary()).toBeNull();
  });
});

describe('jobFacts', () => {
  const job = { location: 'London, England, GB', workplaceType: 'Hybrid', employmentType: 'FullTime', salaryMin: 60_000, salaryMax: 75_000, salaryCurrency: 'GBP' };

  it('lists place, contract and pay, in that order, and never more than three', () => {
    expect(jobFacts(job)).toEqual(['London, England, GB (Hybrid)', 'Full-time', '£60k–£75k']);
    expect(jobFacts(job).length).toBeLessThanOrEqual(3);
  });

  it('leaves the pay out when there is none', () => {
    expect(jobFacts({ ...job, salaryMin: null, salaryMax: null })).toEqual(['London, England, GB (Hybrid)', 'Full-time']);
  });

  it('names an on-site job by its address alone', () => {
    expect(jobFacts({ ...job, workplaceType: 'Onsite' })).toEqual(['London, England, GB', 'Full-time', '£60k–£75k']);
  });

  it('does not say remote twice when the location already does', () => {
    expect(jobFacts({ ...job, location: 'Remote (GB, IE)', workplaceType: 'Remote' })).toEqual(['Remote (GB, IE)', 'Full-time', '£60k–£75k']);
  });

  it('keeps the workplace type in the place when the location does not name it', () => {
    expect(jobFacts({ ...job, location: 'Lisbon, PT', workplaceType: 'Remote' })[0]).toBe('Lisbon, PT (Remote)');
  });

  it('shows the workplace type alone when a hybrid job has no address yet', () => {
    expect(jobFacts({ ...job, location: '' })).toEqual(['Hybrid', 'Full-time', '£60k–£75k']);
  });

  it('shows no empty chip for an on-site job with no address', () => {
    expect(jobFacts({ ...job, location: '', workplaceType: 'Onsite', salaryMin: null, salaryMax: null })).toEqual(['Full-time']);
  });
});

describe('jsonLdScript', () => {
  it('cannot be closed early by a value, and parses back to the same data', () => {
    const data = { title: 'x</script><img src=x onerror=alert(1)>' };
    const script = jsonLdScript(data);
    expect(script).not.toContain('<');
    expect(JSON.parse(script)).toEqual(data);
  });
});

describe('escapeHtml', () => {
  it('escapes the characters that would start markup or end an attribute', () => {
    expect(escapeHtml('Tom & Jerry <b>"Ltd"</b>')).toBe('Tom &amp; Jerry &lt;b&gt;&quot;Ltd&quot;&lt;/b&gt;');
  });
});
