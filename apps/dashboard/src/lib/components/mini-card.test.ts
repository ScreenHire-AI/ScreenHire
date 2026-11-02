import { createRawSnippet } from 'svelte';
import { render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import MiniCard from './mini-card.svelte';

const lines = createRawSnippet(() => ({ render: () => '<div><p>jo@example.com</p><p>Applied 6 days ago</p></div>' }));

describe('mini card', () => {
  it('is a group named by its caption title, holding its lines', async () => {
    const { container } = render(MiniCard, { props: { title: 'Contact', children: lines } });

    const card = screen.getByRole('group', { name: 'Contact' });
    expect(within(card).getByText('jo@example.com')).toBeInTheDocument();
    expect(within(card).getByText('Applied 6 days ago')).toBeInTheDocument();
    await expectNoAxeViolations(container);
  });

  it('is a small bordered card, not a heading or a panel', () => {
    render(MiniCard, { props: { title: 'Application', children: lines } });

    expect(screen.getByRole('group')).toHaveClass('border', 'border-border', 'rounded-panel');
    expect(screen.queryByRole('heading')).not.toBeInTheDocument();
  });

  it('gives two cards on one page their own names', () => {
    render(MiniCard, { props: { title: 'Contact', children: lines } });
    render(MiniCard, { props: { title: 'Application', children: lines } });

    expect(screen.getByRole('group', { name: 'Contact' })).not.toBe(screen.getByRole('group', { name: 'Application' }));
  });
});
