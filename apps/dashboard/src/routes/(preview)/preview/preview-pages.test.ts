import { render, screen } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { previewWaitMs } from '#lib/preview-window.svelte.js';
import BrandingPreviewPage from './branding/+page.svelte';
import EmailPreviewPage from './email/+page.svelte';
import FormPreviewPage from './form/+page.svelte';
import ScorecardPreviewPage from './scorecard/+page.svelte';

const { pageState } = vi.hoisted(() => ({ pageState: { url: new URL('http://dashboard.test/preview/scorecard') } }));
vi.mock('$app/state', () => ({ page: pageState, navigating: { to: null } }));

const pages = [
  ['scorecard', ScorecardPreviewPage, 'Scorecard preview'],
  ['form', FormPreviewPage, 'Form preview'],
  ['email', EmailPreviewPage, 'Email preview'],
  ['branding', BrandingPreviewPage, 'Branding preview'],
] as const;

// A pop-out preview is fed by its editor over a BroadcastChannel (design system A8). A window opened with no editor behind
// it (reloaded, or the editor closed) must say so instead of loading for ever (E1, E3).
describe('the pop-out preview pages', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it.each(pages)('%s: says there is no editor when its address names none', (name, Page) => {
    pageState.url = new URL(`http://dashboard.test/preview/${name}`);
    render(Page);

    expect(screen.getByRole('status')).toHaveTextContent('No editor is open for this preview.');
  });

  it.each(pages)('%s: loads quietly at first, then says there is no editor once none answers', async (name, Page) => {
    pageState.url = new URL(`http://dashboard.test/preview/${name}?channel=gone-${name}`);
    render(Page);
    expect(screen.queryByText(/No editor is open/)).toBeNull();

    vi.advanceTimersByTime(previewWaitMs + 1);

    await vi.waitFor(() => expect(screen.getByText(/No editor is open for this preview/)).toBeInTheDocument());
  });

  it.each(pages)('%s: names its window apart from the other previews', (name, Page, title) => {
    pageState.url = new URL(`http://dashboard.test/preview/${name}`);
    render(Page);

    expect(document.title).toBe(`${title} · ScreenHire`);
  });
});

describe('a pop-out preview with its editor', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  const scorecard = { questions: [{ id: 'q1', type: 'Rating5', label: 'Problem solving', required: true }] };
  const values = [
    ['scorecard', ScorecardPreviewPage, scorecard, 'Problem solving'],
    ['form', FormPreviewPage, { form: 'apply', questions: [{ id: 'q1', type: 'ShortText', label: 'Your notice period' }], sections: null, questionsPerPage: 5 }, 'Your notice period'],
    ['email', EmailPreviewPage, { preview: { subject: 'About your application', html: '<p>Hello</p>', text: 'Hello' } }, 'About your application'],
    [
      'branding',
      BrandingPreviewPage,
      { organizationName: 'Harbor Lane', brand: null, header: null, headingFont: null, logo: null, favicon: null, sharingImage: null },
      'Jobs at Harbor Lane',
    ],
  ] as const;

  it.each(values)('%s: shows the editor value as it arrives and never the no-editor line', async (name, Page, value, text) => {
    pageState.url = new URL(`http://dashboard.test/preview/${name}?channel=live-${name}`);
    render(Page);
    const editor = new BroadcastChannel(`screenhire:preview:live-${name}`);
    editor.postMessage({ type: 'value', value });

    await vi.waitFor(() => expect(screen.getAllByText(text)[0]).toBeInTheDocument());
    vi.advanceTimersByTime(previewWaitMs + 1);
    expect(screen.queryByText(/No editor is open/)).toBeNull();
    editor.close();
  });

  it('replaces the no-editor line when a slow editor answers after all', async () => {
    pageState.url = new URL('http://dashboard.test/preview/scorecard?channel=slow');
    render(ScorecardPreviewPage);
    vi.advanceTimersByTime(previewWaitMs + 1);
    await vi.waitFor(() => expect(screen.getByText(/No editor is open/)).toBeInTheDocument());

    const editor = new BroadcastChannel('screenhire:preview:slow');
    editor.postMessage({ type: 'value', value: scorecard });

    await vi.waitFor(() => expect(screen.getByText('Problem solving')).toBeInTheDocument());
    expect(screen.queryByText(/No editor is open/)).toBeNull();
    editor.close();
  });

  it('stops loading when the editor closes before it sent anything', async () => {
    pageState.url = new URL('http://dashboard.test/preview/scorecard?channel=early');
    render(ScorecardPreviewPage);
    const editor = new BroadcastChannel('screenhire:preview:early');
    editor.postMessage({ type: 'closed' });
    await vi.waitFor(() => expect(screen.getByText('Editor closed.')).toBeInTheDocument());

    vi.advanceTimersByTime(previewWaitMs + 1);

    await vi.waitFor(() => expect(screen.getByText(/No editor is open/)).toBeInTheDocument());
    editor.close();
  });
});
