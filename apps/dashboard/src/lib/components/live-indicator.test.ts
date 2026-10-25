import type { ConnectionStatus } from '@screenhire/realtime';
import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import LiveIndicator from './live-indicator.svelte';

describe('live indicator', () => {
  it.each<[ConnectionStatus, string]>([
    ['connected', 'Live'],
    ['connecting', 'Connecting…'],
    ['reconnecting', 'Reconnecting…'],
    ['disconnected', 'Offline'],
  ])('names the %s status in words, not only in colour', (status, label) => {
    render(LiveIndicator, { props: { status } });

    expect(screen.getByRole('status')).toHaveTextContent(`Live updates: ${label}`);
  });

  it('gives each kind of status its own icon (T1)', () => {
    const icons = (['connected', 'reconnecting', 'disconnected'] as const).map((status) => {
      const { container, unmount } = render(LiveIndicator, { props: { status } });
      const icon = container.querySelector('[data-status-icon]')!.getAttribute('data-status-icon');
      unmount();
      return icon;
    });

    expect(icons).toEqual(['circle-check', 'loader-circle', 'circle-minus']);
  });
});
