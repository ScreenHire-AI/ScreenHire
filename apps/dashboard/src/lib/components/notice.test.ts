import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import Notice from './notice.svelte';

const words = (text: string) => createRawSnippet(() => ({ render: () => `<span>${text}</span>` }));

describe('notice', () => {
  it('says what is wrong in words, in the soft verify colour, with the problem icon and a link to the fix', async () => {
    render(Notice, { props: { href: '#postings', link: 'Fix on Postings', children: words('Adzuna refused this job&#39;s posting.') } });

    const notice = screen.getByRole('status');
    expect(notice).toHaveTextContent("Adzuna refused this job's posting.");
    expect(notice).toHaveClass('bg-status-verify-soft', 'text-status-verify', 'w-full');
    expect(notice.querySelector('[data-problem-icon]')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Fix on Postings' })).toHaveAttribute('href', '#postings');
    await expectNoAxeViolations(document.body);
  });

  it('can say what is wrong without a link', () => {
    render(Notice, { props: { children: words('The careers page could not be published.') } });

    expect(screen.getByRole('status')).toHaveTextContent('The careers page could not be published.');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});
