import axe from 'axe-core';
import { expect } from 'vitest';

/**
 * Fails with every axe violation of WCAG 2.2 A and AA in `root`, the tags the end-to-end tests check. Overlays render
 * in a portal, so check `document.body` for a dialog or a tooltip. jsdom has no layout, so colour contrast is left to
 * Playwright, and it cannot run axe inside a frame (an email preview), so frames' contents are too.
 */
export async function expectNoAxeViolations(root: Element = document.body): Promise<void> {
  const results = await axe.run(root, {
    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'] },
    rules: { 'color-contrast': { enabled: false } },
    iframes: false,
  });
  const violations = results.violations.flatMap((violation) =>
    violation.nodes.map((node) => `${violation.id}: ${node.target.join(' ')} (${violation.help})`),
  );
  expect(violations).toEqual([]);
}
