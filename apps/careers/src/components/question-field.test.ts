// The shared question components (packages/ui) as the careers site renders them: on the server for the apply page,
// where the no-JavaScript re-render depends on values, checks and ids coming back in the HTML, and as a hydrated island.
import { getContainerRenderer as svelteContainerRenderer } from '@astrojs/svelte/container-renderer';
import type { PublicQuestion } from '@screenhire/api-client';
import { experimental_AstroContainer as AstroContainer } from 'astro/container';
import { loadRenderers } from 'astro:container';
import { beforeAll, describe, expect, it } from 'vitest';
import QuestionIsland from '../test-support/QuestionIsland.astro';
import { emptyState, type ApplyState } from '../lib/apply';
import QuestionField from './QuestionField.astro';
import StepIndicator from './StepIndicator.astro';

let container: AstroContainer;

beforeAll(async () => {
  container = await AstroContainer.create({ renderers: await loadRenderers([svelteContainerRenderer()]) });
});

const question = (overrides: Partial<PublicQuestion>): PublicQuestion => ({
  id: 'q1',
  type: 'ShortText',
  label: 'Question',
  required: false,
  helpText: null,
  options: null,
  min: null,
  max: null,
  maxLength: null,
  acceptedFileTypes: null,
  ...overrides,
});

const render = (props: { question: PublicQuestion; state?: ApplyState; errors?: Record<string, string>; autofocus?: boolean }) =>
  container.renderToString(QuestionField, {
    props: { state: emptyState(), errors: {}, organization: 'brightwater', jobId: '0199e5b1-0000-7000-8000-000000000000', maxBytes: 10 * 1024 * 1024, ...props },
    request: new Request('http://localhost/brightwater/jobs/x/apply'),
  });

describe('QuestionField on the apply page (server-rendered)', () => {
  it('renders a yes/no question as radios named for the form, with the answer checked', async () => {
    const state = { ...emptyState(), answers: { work_authorization: 'no' } };
    const html = await render({
      question: question({ id: 'work_authorization', type: 'YesNo', label: 'Can you work in the UK?', required: true }),
      state,
    });

    expect(html).toMatch(/<fieldset[^>]*id="field-q-work_authorization"[^>]*tabindex="-1"/);
    expect(html).toContain('Can you work in the UK?');
    expect(html).not.toContain('(optional)');
    expect(html).toMatch(/<input[^>]*type="radio"[^>]*name="q:work_authorization"[^>]*value="yes"/);
    expect(html).toMatch(/<input[^>]*value="no"[^>]*checked/);
    expect(html).not.toMatch(/<input[^>]*value="yes"[^>]*checked/);
    expect(html).not.toContain('astro-island');
  });

  it('renders multiple choice as checkboxes with every chosen option checked', async () => {
    const html = await render({
      question: question({
        id: 'shifts',
        type: 'MultiChoice',
        label: 'Which shifts?',
        options: [
          { id: 'mornings', label: 'Mornings' },
          { id: 'evenings', label: 'Evenings' },
          { id: 'nights', label: 'Nights' },
        ],
      }),
      state: { ...emptyState(), answers: { shifts: ['mornings', 'nights'] } },
    });

    expect(html.match(/type="checkbox"/g)).toHaveLength(3);
    expect(html).toMatch(/value="mornings"[^>]*checked/);
    expect(html).toMatch(/value="nights"[^>]*checked/);
    expect(html).not.toMatch(/value="evenings"[^>]*checked/);
    expect(html).toContain('(optional)');
  });

  it('renders a long answer back into its textarea and links the hint and error', async () => {
    const html = await render({
      question: question({ id: 'why', type: 'LongText', label: 'Why this role?', helpText: 'A few sentences.', maxLength: 2000 }),
      state: { ...emptyState(), answers: { why: 'Because <I> like it' } },
      errors: { 'q:why': 'Keep it under 2000 characters.' },
    });

    expect(html).toMatch(/<textarea[^>]*id="field-q-why"[^>]*name="q:why"/);
    expect(html).toContain('maxlength="2000"');
    expect(html).toContain('Because &lt;I> like it');
    expect(html).toContain('aria-describedby="field-q-why-hint field-q-why-error"');
    expect(html).toContain('aria-invalid="true"');
    expect(html).toContain('Keep it under 2000 characters.');
  });

  it('gives a number question its range as the hint and a decimal keyboard', async () => {
    const html = await render({ question: question({ id: 'years', type: 'Number', label: 'Years of experience', min: 0, max: 50 }) });

    expect(html).toContain('A number from 0 up to 50.');
    expect(html).toContain('inputmode="decimal"');
  });

  it('renders a file question as the upload island, keeping its no-JavaScript field name', async () => {
    const html = await render({ question: question({ id: 'resume', type: 'File', label: 'Resume or CV', acceptedFileTypes: ['.pdf', '.docx'] }) });

    expect(html).toContain('<astro-island');
    expect(html).toContain('name="file:resume"');
    // Before the island runs it is the drop area's plain input in its dashed frame, which posts with the form.
    expect(html).toContain('data-drop-area');
    expect(html).toMatch(/<input[^>]*type="file"[^>]*class="file-input"/);
    expect(html).toContain('PDF or DOCX, up to 10 MB.');
  });
});

describe('QuestionField as an Astro island', () => {
  it('server-renders the native control inside a hydrating island', async () => {
    const html = await container.renderToString(QuestionIsland, {
      props: {
        question: { id: 'licence', type: 'YesNo', label: 'Do you hold a forklift licence?', required: true },
        answer: 'yes',
      },
    });

    expect(html).toMatch(/<astro-island[^>]*client="load"/);
    expect(html).toContain('component-export="QuestionField"');
    expect(html).toMatch(/<input[^>]*name="q:licence"[^>]*value="yes"[^>]*checked/);
  });
});

describe('the first question of a page (apply-flow.md §7)', () => {
  it('takes the focus as the page loads, in the server HTML, with no script', async () => {
    const text = await render({ question: question({ id: 'notice', label: 'Notice period' }), autofocus: true });
    expect(text).toMatch(/<input[^>]*name="q:notice"[^>]*autofocus/);
    expect(text).not.toContain('<script');

    const long = await render({ question: question({ id: 'why', type: 'LongText', label: 'Why us?' }), autofocus: true });
    expect(long).toMatch(/<textarea[^>]*name="q:why"[^>]*autofocus/);
  });

  it('puts the focus on the first choice of a choice question, and only there', async () => {
    const html = await render({
      question: question({ id: 'licence', type: 'YesNo', label: 'Do you hold a forklift licence?' }),
      autofocus: true,
    });

    expect(html.match(/autofocus/g)).toHaveLength(1);
    expect(html).toMatch(/<input[^>]*value="yes"[^>]*autofocus|<input[^>]*autofocus[^>]*value="yes"/);
  });

  it('leaves the focus alone unless asked', async () => {
    const html = await render({ question: question({ id: 'notice', label: 'Notice period' }) });
    expect(html).not.toContain('autofocus');
  });
});

describe('the check line beside a field', () => {
  it('is in the page for a required field, hidden until the browser finds the field wrong after it was left', async () => {
    const html = await render({ question: question({ id: 'notice', label: 'Notice period', required: true }) });

    expect(html).toMatch(/<div class="field [^"]*">/);
    expect(html).toMatch(/<p class="field-check">Add an answer.<\/p>/);
    // Kept out of the field's description: hidden text in aria-describedby would be read while the field is fine.
    expect(html).not.toMatch(/aria-describedby="[^"]*check/);
  });

  it('says what a web address needs, and an optional line has none', async () => {
    expect(await render({ question: question({ id: 'site', type: 'Url', label: 'Portfolio' }) })).toContain(
      'Enter a web address like https://example.com.',
    );
    expect(await render({ question: question({ id: 'notes', label: 'Anything else?' }) })).not.toContain('field-check');
  });
});

describe('StepIndicator', () => {
  it('draws one bar and "40% done", with no step count and no answer count', async () => {
    const html = await container.renderToString(StepIndicator, { props: { step: 2, steps: 4 } });

    const bar = html.match(/<progress[^>]*>/)?.[0] ?? '';
    expect(bar).toContain('class="page-progress"');
    expect(bar).toContain('max="100"');
    expect(bar).toContain('value="40"');
    // Hidden from screen readers, which read the same "40% done" as everyone else.
    expect(bar).toContain('aria-hidden="true"');
    expect(html).toMatch(/data-page-progress[^>]*>40% done</);
    expect(html).not.toMatch(/Step \d of/);
    expect(html).not.toContain('answered');
    expect(html).not.toContain('<nav');
    expect(html).not.toMatch(/style=/);
  });

  it('stays short of 100% on the review, the last step before sending', async () => {
    const html = await container.renderToString(StepIndicator, { props: { step: 4, steps: 4 } });
    expect(html).toContain('80% done');
  });
});
