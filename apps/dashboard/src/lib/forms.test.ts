import { CompleteOnboardingBody } from '@screenhire/api-client/zod';
import { defaults } from 'sveltekit-superforms';
import { zod4 } from 'sveltekit-superforms/adapters';
import { describe, expect, it } from 'vitest';
import { applyValidationProblem, areCountryCodes, countryCodes, paragraphs, slugify } from './forms.js';

describe('applyValidationProblem', () => {
  it("puts the API's errors on the fields they name, and unknown fields on the form", () => {
    const form = defaults(zod4(CompleteOnboardingBody));

    applyValidationProblem(form, {
      errors: { slug: ['That address is taken. Choose another.'], somethingElse: ['Try again.'] },
    });

    expect(form.valid).toBe(false);
    expect(form.errors.slug).toEqual(['That address is taken. Choose another.']);
    expect(form.errors._errors).toEqual(['Try again.']);
  });

  it('leaves the form alone when the problem has no field errors', () => {
    const form = defaults(zod4(CompleteOnboardingBody));

    applyValidationProblem(form, undefined);

    expect(form.errors).toEqual({});
  });
});

describe('slugify', () => {
  it.each([
    ['Brightwater Studio', 'brightwater-studio'],
    ['  Café Nø. 5 / Hiring!  ', 'cafe-n-5-hiring'],
    ['ACME', 'acme'],
    ['---', ''],
  ])('%s becomes %s', (name, slug) => {
    expect(slugify(name)).toBe(slug);
  });

  it('stays within 63 characters without a trailing hyphen', () => {
    const slug = slugify(`${'a'.repeat(62)} b`);

    expect(slug.length).toBeLessThanOrEqual(63);
    expect(slug.endsWith('-')).toBe(false);
  });
});

describe('countryCodes', () => {
  it('reads codes separated by commas or spaces, upper-cased and each once', () => {
    expect(countryCodes(' gb, IE;ie  fr ')).toEqual(['GB', 'IE', 'FR']);
    expect(countryCodes('')).toEqual([]);
  });

  it('tells real two-letter codes from anything else', () => {
    expect(areCountryCodes(['GB', 'IE'])).toBe(true);
    expect(areCountryCodes(['GB', 'UK1'])).toBe(false);
    expect(areCountryCodes(['ENGLAND'])).toBe(false);
  });
});

describe('paragraphs', () => {
  it('makes a paragraph per block and keeps line breaks', () => {
    expect(paragraphs('Build the API.\nOwn it in production.\n\n  Remote in the UK.  ')).toBe(
      '<p>Build the API.<br>Own it in production.</p><p>Remote in the UK.</p>',
    );
  });

  it('escapes markup instead of passing it on', () => {
    expect(paragraphs('<script>alert("x")</script> & more')).toBe('<p>&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; more</p>');
  });

  it('is empty for blank text', () => {
    expect(paragraphs(' \n\n ')).toBe('');
  });
});
