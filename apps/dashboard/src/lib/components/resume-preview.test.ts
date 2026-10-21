import { createRawSnippet } from 'svelte';
import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import ResumePreview from './resume-preview.svelte';

describe('resume preview', () => {
  it('shows the text read from the file as text, never the file itself', async () => {
    const text = 'Jordan Ellis\nSenior product designer\n<script>alert(1)</script>';
    const { container } = render(ResumePreview, {
      props: { name: 'resume.pdf', preview: { status: 'Ready', text, pageCount: 2, note: null } },
    });

    const page = screen.getByRole('article', { name: 'resume.pdf, 2 pages' });
    expect(page.textContent?.trim()).toBe(text);
    // Isolation: markup in the file stays text, and nothing frames, embeds or runs the file.
    expect(container.querySelector('script, iframe, object, embed, img')).toBeNull();
    // An inset with a page on it (section 6).
    expect(container.querySelector('[data-slot="resume-preview"]')).toHaveClass('bg-secondary');
    expect(page).toHaveClass('bg-card', 'text-reading');
    await expectNoAxeViolations(container);
  });

  it.each([
    ['loading', undefined],
    ['waiting for the scan and the read', { status: 'Pending', text: '', pageCount: 0, note: null }],
  ] as const)('shows a page-shaped skeleton while %s', async (_, preview) => {
    const { container } = render(ResumePreview, { props: { name: 'resume.pdf', preview } });

    expect(screen.getByRole('status')).toHaveTextContent('Loading the preview of resume.pdf');
    // A card-coloured page on the inset, as the text will be; the skeleton's own fill would not show on it.
    expect(container.querySelector('[data-slot="skeleton"]')).toHaveClass('aspect-[17/22]', 'bg-card');
    expect(container.querySelector('[data-slot="skeleton"]')).not.toHaveClass('bg-muted');
    await expectNoAxeViolations(container);
  });

  it('says why there is no preview and shows the fallback, so the file can still be downloaded', async () => {
    const fallback = createRawSnippet(() => ({ render: () => '<a href="/v1/files/1">Download portfolio.png</a>' }));
    const { container } = render(ResumePreview, {
      props: {
        name: 'portfolio.png',
        preview: { status: 'Unavailable', text: '', pageCount: 0, note: 'No text is read from PNG files.' },
        fallback,
      },
    });

    expect(screen.getByText('No text is read from PNG files.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download portfolio.png' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });
});
