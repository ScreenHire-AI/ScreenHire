import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../../test-support/axe.js';
import WithTooltips from '../../../test-support/with-tooltips.svelte';
import Page from './+page.svelte';

// The real viewer (pdf.js) is built by file-viewer-plugin.ts and is not needed to draw the page.
vi.mock('virtual:file-viewer', () => ({ viewerDocument: '<!doctype html><title>viewer</title>' }));
// No file behind the sample PDF: its download fails, which is the state the page shows.
beforeEach(() => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 404 })));
});
afterEach(() => {
  vi.unstubAllGlobals();
});

const navigation = vi.hoisted(() => ({ goto: vi.fn(async () => {}) }));
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: navigation.goto }));
vi.mock(import('$app/state'), async (importOriginal) => ({
  ...(await importOriginal()),
  page: { url: new URL('http://localhost:4323/style-guide') } as never,
}));

/** Section 11's rows, by the name in the Component column. */
function catalogue(): string[] {
  const doc = readFileSync(resolve(process.cwd(), '../../docs/design/design-system.md'), 'utf8');
  const section = doc.slice(doc.indexOf('## 11. Components'), doc.indexOf('## 12.'));
  return [...section.matchAll(/^\| ([^|]+?) \|/gm)].map((match) => match[1]!).filter((name) => name !== 'Component' && !name.startsWith('-'));
}

/** The panel, or panels, on the style guide page for each row of section 11. */
const panels: Record<string, string[]> = {
  'Header band, Panel': ['Header band', 'Panel'],
  'Status badge': ['Status badge'],
  'Fact pill': ['Fact pill'],
  Toggle: ['Toggle'],
  'More menu': ['More menu'],
  Count: ['Count'],
  'Step marker': ['Step marker'],
  'Icon button': ['Icon button'],
  'Summary list': ['Summary list'],
  'File tile': ['File tile'],
  'Resume preview': ['Resume preview'],
  'Key cap': ['Key cap and tooltip'],
  Tooltip: ['Key cap and tooltip'],
  'Empty state': ['Empty state'],
  'Shortcuts dialog': ['Shortcuts dialog'],
  Sidebar: ['Sidebar'],
  'Brand bar': ['Brand bar'],
  'Table, list item': ['Table', 'List item, the table on a phone'],
  'Search field': ['Search field'],
  Activity: ['Activity'],
  'Action bar': ['Action bar'],
  'Relative time, truncated text': ['Time and long text'],
  'Table date': ['Time and long text'],
  'Failure text': ['Failure text'],
  'Form field': ['Form field'],
  'Integrity chip and panel': ['Integrity chip and evidence'],
};

describe('style guide', () => {
  it('shows a panel for every component in the catalogue', () => {
    render(WithTooltips, { props: { component: Page } });

    expect(catalogue().toSorted()).toEqual(Object.keys(panels).toSorted());
    for (const title of Object.values(panels).flat()) {
      expect(screen.getByRole('region', { name: title })).toBeInTheDocument();
    }
  });

  it('passes axe with every component on the page', async () => {
    render(WithTooltips, { props: { component: Page } });

    await expectNoAxeViolations(document.body);
  });

  it('has one main landmark; the sidebar demos frame their inset in a div (X1)', () => {
    render(WithTooltips, { props: { component: Page } });

    expect(screen.getAllByRole('main')).toHaveLength(1);
  });

  it('shows the sidebar expanded at 248px and as a 64px rail', () => {
    render(WithTooltips, { props: { component: Page } });

    const sidebar = screen.getByRole('region', { name: 'Sidebar' });
    const wrappers = sidebar.querySelectorAll<HTMLElement>('[data-slot="sidebar-wrapper"]');
    expect(wrappers[0]!.style.getPropertyValue('--sidebar-width')).toBe('15.5rem');
    expect(wrappers[0]!.style.getPropertyValue('--sidebar-width-icon')).toBe('4rem');
    expect([...sidebar.querySelectorAll('[data-slot="sidebar"]')].map((element) => element.getAttribute('data-state'))).toEqual([
      'expanded',
      'collapsed',
    ]);
    expect(within(sidebar).getByRole('button', { name: 'Collapse sidebar' })).toBeInTheDocument();
    expect(within(sidebar).getByRole('button', { name: 'Expand sidebar' })).toBeInTheDocument();
  });

  it('keeps the chosen width in the address, so a reviewer can link to it', async () => {
    render(WithTooltips, { props: { component: Page } });

    await fireEvent.change(screen.getByRole('combobox', { name: 'Width' }), { target: { value: '360' } });

    expect(navigation.goto).toHaveBeenCalledWith(new URL('http://localhost:4323/style-guide?width=360'), { replace: true, reset: false });
  });
});
