import { isRedirect } from '@sveltejs/kit';
import { describe, expect, it } from 'vitest';
import { load as loadList } from './+page.js';
import { load as loadTemplate } from './[templateId]/+page.js';
import { load as loadNew } from './new/+page.js';

// Email templates moved to the Templates page (design system section 22): every old address lands on its new one.
function locationOf(run: () => unknown): string {
  try {
    run();
  } catch (thrown) {
    if (isRedirect(thrown)) return thrown.location;
    throw thrown;
  }
  throw new Error('The load did not redirect.');
}

describe('email template redirects', () => {
  it('sends the list, a template and a new template to the Templates page', () => {
    expect(locationOf(() => (loadList as () => unknown)())).toBe('/templates?tab=emails');
    expect(locationOf(() => (loadNew as () => unknown)())).toBe('/templates/emails/new');
    expect(locationOf(() => (loadTemplate as (event: { params: { templateId: string } }) => unknown)({ params: { templateId: 'stage-change' } }))).toBe(
      '/templates/emails/stage-change',
    );
  });
});
