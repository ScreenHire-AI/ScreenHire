import { screen, within } from '@testing-library/svelte';
import { expect } from 'vitest';

/**
 * The List template's count rule (design system section 7, T4): no count beside a page's title, and a list that has no
 * tabs or state pills keeps its count in the grey count badge of its panel's header.
 */
export function expectCountInPanelNotBand(panelName: string, count: number) {
  const band = document.querySelector('[data-slot="header-band"]');
  expect(band).not.toBeNull();
  expect(band!.querySelector('[data-slot="badge"]')).toBeNull();
  const panel = screen.getByRole('region', { name: panelName });
  expect(within(panel).getByText(String(count), { selector: '[data-slot="badge"]' })).toBeInTheDocument();
}
