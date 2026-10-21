import type { ApplicationFileSummary } from '@screenhire/api-client';
import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import FileTile from './file-tile.svelte';

const applicationId = '0198a3c4-0000-7000-8000-000000000001';

function file(overrides: Partial<ApplicationFileSummary> = {}): ApplicationFileSummary {
  return {
    id: '0198a3c4-0000-7000-8000-0000000000f1',
    questionId: 'resume',
    uploadId: '0198a3c4-0000-7000-8000-0000000000a1',
    fileName: 'resume.pdf',
    contentType: 'application/pdf',
    sizeBytes: 122_000,
    scanStatus: 'Clean',
    uploadedAt: '2026-10-20T09:00:00Z',
    ...overrides,
  };
}

describe('file tile', () => {
  it('shows the name, the size as a fact pill and Download, which loads the file outside the client router', async () => {
    const { container } = render(WithTooltips, { props: { component: FileTile, props: { applicationId, file: file() } } });

    expect(screen.getByText('resume.pdf')).toBeInTheDocument();
    expect(screen.getByText('122 kB')).toHaveAttribute('data-slot', 'badge');
    const download = screen.getByRole('link', { name: 'Download resume.pdf' });
    expect(download).toHaveAttribute('href', `/v1/applications/${applicationId}/files/${file().id}`);
    expect(download).toHaveAttribute('data-sveltekit-reload');
    // 44px on touch (F4).
    expect(download).toHaveClass('pointer-coarse:h-11');
    await expectNoAxeViolations(container);
  });

  it('has nothing to say about a file nobody scanned', () => {
    render(WithTooltips, { props: { component: FileTile, props: { applicationId, file: file({ scanStatus: 'NotScanned' }) } } });

    expect(screen.queryByText(/scan|withheld/i)).toBeNull();
    expect(screen.getByRole('link', { name: 'Download resume.pdf' })).toBeInTheDocument();
  });

  it.each([
    ['Pending', 'Being scanned for malware', 'diamond', 'bg-status-review-soft'],
    ['Infected', 'Withheld: malware found', 'square', 'bg-status-verify-soft'],
  ] as const)('shows a %s file as a status that needs attention, and offers no Download', async (scanStatus, label, shape, fill) => {
    const { container } = render(WithTooltips, { props: { component: FileTile, props: { applicationId, file: file({ scanStatus }) } } });

    const badge = screen.getByText(label).closest('[data-slot="badge"]')!;
    // Mark, colour and label (T1).
    expect(badge.querySelector('[data-shape]')).toHaveAttribute('data-shape', shape);
    expect(badge).toHaveClass(fill);
    expect(screen.queryByRole('link')).toBeNull();
    await expectNoAxeViolations(container);
  });
});
