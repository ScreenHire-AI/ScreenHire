import { CompleteOnboardingBody } from '@screenhire/api-client/zod';
import { defaults } from 'sveltekit-superforms';
import { zod4 } from 'sveltekit-superforms/adapters';
import { describe, expect, it } from 'vitest';
import { applyValidationProblem, paragraphs, slugify } from './forms.js';

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

describe('paragraphs', () => {
  it('makes a paragraph per block and keeps line breaks', () => {
    expect(paragraphs('Build the API.\nOwn it in production.\n\n  Remote in the UK.  ')).toBe(
      '<p>Build the API.<br>Own it in production.</p><p>Remote in the UK.</p>',
    );
  });

  it('escapes markup instead of passing it on', () => {
    expect(paragraphs('<script>alert("x")</script> & more')).toBe('<p>&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; more</p>');
  });

  it('makes one list of the "- " items in a row, with or without blank lines between them', () => {
    expect(paragraphs('What you will do\n\n- Ship features.\n\n- Review <code>.\n\nApply by Friday.\n- not a list')).toBe(
      '<p>What you will do</p><ul><li>Ship features.</li><li>Review &lt;code&gt;.</li></ul><p>Apply by Friday.<br>- not a list</p>',
    );
    expect(paragraphs('- One\n- Two')).toBe('<ul><li>One</li><li>Two</li></ul>');
  });

  it('is empty for blank text', () => {
    expect(paragraphs(' \n\n ')).toBe('');
  });
});
