import { createRawSnippet } from 'svelte';
import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import ResumePreview from './resume-preview.svelte';

const pages = [
  { image: '/v1/previews/1/1.png', text: 'Jordan Ellis. Senior product designer.' },
  { image: '/v1/previews/1/2.png', text: 'Experience. Brightwater Studio, 2019 to 2026.' },
];

describe('resume preview', () => {
  it('shows each page as an image with its text for screen readers, never the file itself', async () => {
    const { container } = render(ResumePreview, { props: { name: 'resume.pdf', pages } });

    const list = screen.getByRole('list', { name: 'resume.pdf, 2 pages' });
    const images = within(list).getAllByRole('img');
    expect(images.map((image) => image.getAttribute('alt'))).toEqual(['Page 1 of 2', 'Page 2 of 2']);
    expect(images[0]).toHaveAttribute('src', '/v1/previews/1/1.png');
    expect(within(list).getByText('Experience. Brightwater Studio, 2019 to 2026.')).toHaveClass('sr-only');
    // Isolation: no frame, object or embed that could run the file.
    expect(container.querySelector('iframe, object, embed')).toBeNull();
    // An inset: secondary fill, no edge (section 6).
    expect(container.querySelector('[data-slot="resume-preview"]')).toHaveClass('bg-secondary');
    await expectNoAxeViolations(container);
  });

  it('shows a page-shaped skeleton while the preview loads', async () => {
    const { container } = render(ResumePreview, { props: { name: 'resume.pdf', pages: undefined } });

    expect(screen.getByRole('status')).toHaveTextContent('Loading the preview of resume.pdf');
    // A card-coloured page on the inset, as the loaded page will be; the skeleton's own fill would not show on it.
    expect(container.querySelector('[data-slot="skeleton"]')).toHaveClass('aspect-[17/22]', 'bg-card');
    expect(container.querySelector('[data-slot="skeleton"]')).not.toHaveClass('bg-muted');
    await expectNoAxeViolations(container);
  });

  it('says there is no preview and shows the fallback, so the file can still be downloaded', async () => {
    const fallback = createRawSnippet(() => ({ render: () => '<a href="/v1/files/1">Download resume.docx</a>' }));
    const { container } = render(ResumePreview, { props: { name: 'resume.docx', pages: null, fallback } });

    expect(screen.getByText('No preview for this file.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Download resume.docx' })).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });
});
