import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import FileTile from './file-tile.svelte';

const renderTile = (props: Record<string, unknown>) => render(WithTooltips, { props: { component: FileTile, props } });

describe('file tile', () => {
  it('shows the name, the size and Download, which loads the file outside the client router', async () => {
    const { container } = renderTile({ name: 'resume.pdf', size: 122_880, href: '/v1/files/1', scanStatus: 'Clean' });

    expect(screen.getByText('resume.pdf')).toBeInTheDocument();
    expect(screen.getByText('120 kB')).toBeInTheDocument();
    const download = screen.getByRole('link', { name: 'Download resume.pdf' });
    expect(download).toHaveAttribute('href', '/v1/files/1');
    expect(download).toHaveAttribute('data-sveltekit-reload');
    // An inset inside a panel: secondary fill, no edge (section 6).
    expect(container.querySelector('[data-slot="file-tile"]')).toHaveClass('bg-secondary');
    expect(container.querySelector('[data-slot="file-tile"]')).not.toHaveClass('border');
    await expectNoAxeViolations(container);
  });

  it.each([
    ['Pending', 'Scanning', 'diamond', 'Being scanned for malware'],
    ['Infected', 'Withheld', 'square', 'The malware scan found a threat'],
  ])('shows a %s file as a status that needs attention, and offers no Download', async (scanStatus, label, shape, detail) => {
    const { container } = renderTile({ name: 'resume.pdf', size: 1000, href: '/v1/files/1', scanStatus });

    const badge = screen.getByText(label);
    expect(badge).toHaveAttribute('data-slot', 'badge');
    expect(badge.querySelector('[data-shape]')).toHaveAttribute('data-shape', shape);
    expect(screen.getByText(detail)).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.queryByText('1 kB')).toBeNull();
    await expectNoAxeViolations(container);
  });

  it('says nothing about a scan that needs no attention', () => {
    renderTile({ name: 'resume.pdf', href: '/v1/files/1', scanStatus: 'NotScanned' });

    expect(screen.queryByText(/scan/i)).toBeNull();
    expect(screen.getByRole('link', { name: 'Download resume.pdf' })).toBeInTheDocument();
  });
});
