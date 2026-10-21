import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import LayoutHarness from '../../test-support/layout-harness.svelte';

describe('header band', () => {
  it('holds the page h1 with its status beside it, the actions, and a labelled facts row', async () => {
    const { container } = render(LayoutHarness, { props: { example: 'band' } });

    const title = screen.getByRole('heading', { level: 1, name: 'Jordan Ellis' });
    // Status comes first in its group: right after the title (T2).
    expect(title.nextElementSibling).toHaveTextContent('Review suggested');
    expect(screen.getByRole('button', { name: 'Move to stage' })).toBeInTheDocument();

    const facts = container.querySelector('dl')!;
    expect([...facts.querySelectorAll('dt')].map((term) => term.textContent)).toEqual(['Job', 'Stage']);
    expect(within(facts).getByText('Screening').tagName).toBe('DD');
    await expectNoAxeViolations(container);
  });

  it('puts tabs on its bottom edge and has no facts row without facts', async () => {
    const { container } = render(LayoutHarness, { props: { example: 'band-with-tabs' } });

    expect(screen.getByRole('tablist')).toBeInTheDocument();
    expect(container.querySelector('dl')).toBeNull();
    // A list page's band widens with the page at 2xl (S6).
    expect(container.querySelector('[data-slot="header-band"] > div')).toHaveClass('max-w-page', '2xl:max-w-page-wide');
    await expectNoAxeViolations(container);
  });
});

describe('panel', () => {
  it('is a region named by its h2 title, with a count, actions, a body and a footer', async () => {
    const { container } = render(LayoutHarness, { props: { example: 'panel' } });

    const panel = screen.getByRole('region', { name: 'Answers' });
    expect(within(panel).getByRole('heading', { level: 2 })).toHaveTextContent('Answers');
    const count = within(panel).getByText('3');
    expect(count).toHaveAttribute('data-slot', 'badge');
    expect(count).toHaveClass('min-w-7', 'rounded-full');
    expect(within(panel).getByRole('button', { name: 'Edit' })).toBeInTheDocument();
    expect(within(panel).getByText('The body').parentElement).toHaveClass('px-4.5', 'py-4');
    expect(within(panel).getByText('The footer').parentElement).toHaveClass('bg-muted');
    expect(panel).toHaveClass('rounded-panel', 'bg-card');
    await expectNoAxeViolations(container);
  });

  it('runs its body edge to edge when flush', async () => {
    const { container } = render(LayoutHarness, { props: { example: 'flush-panel' } });

    expect(screen.getByText('Edge to edge').parentElement).not.toHaveClass('px-4.5');
    await expectNoAxeViolations(container);
  });
});

describe('summary list', () => {
  it('lists read-only data as labels and values', async () => {
    const { container } = render(LayoutHarness, { props: { example: 'summary' } });

    const list = container.querySelector('dl')!;
    expect([...list.querySelectorAll('dt')].map((term) => term.textContent)).toEqual(['Title', 'Location']);
    expect([...list.querySelectorAll('dd')].map((value) => value.textContent)).toEqual(['Senior product designer', 'Remote, United States']);
    // Never a disabled input standing in for a value.
    expect(container.querySelector('input')).toBeNull();
    await expectNoAxeViolations(container);
  });
});

describe('list item', () => {
  it('links the whole item by the name, with the status first and facts as pills', async () => {
    const { container } = render(LayoutHarness, { props: { example: 'list' } });

    const [first] = screen.getAllByRole('listitem');
    const link = within(first!).getByRole('link', { name: 'Jordan Ellis' });
    expect(link).toHaveAttribute('href', '/applications/1');
    // The link's box covers the item, so the whole row is the link (section 9).
    expect(link).toHaveClass('after:absolute', 'after:inset-0');
    expect(first!.textContent?.indexOf('Review suggested')).toBeLessThan(first!.textContent!.indexOf('Jordan Ellis'));
    expect(within(first!).getByText('Screening')).toHaveAttribute('data-slot', 'badge');
    await expectNoAxeViolations(container);
  });

  it('drops the name below a wide status badge instead of breaking it mid-word (F2, F5)', () => {
    render(LayoutHarness, { props: { example: 'list' } });

    const link = within(screen.getAllByRole('listitem')[0]!).getByRole('link', { name: 'Jordan Ellis' });
    expect(link.parentElement).toHaveClass('flex-wrap');
    expect(link).toHaveClass('flex-[1_1_10rem]');
  });
});
