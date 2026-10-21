import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import FailureText from './failure-text.svelte';

describe('failure text', () => {
  it('says what failed with the verify mark, its colour and the reason (T1)', () => {
    render(FailureText, { props: { message: 'Could not publish the job.' } });

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not publish the job.');
    expect(alert).toHaveClass('text-status-verify');
    expect(alert.querySelector('[data-shape]')).toHaveAttribute('data-shape', 'square');
  });

  it('stays in the page, empty and without a mark, until there is a message', () => {
    render(FailureText, { props: { message: '' } });

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('');
    expect(alert.querySelector('[data-shape]')).toBeNull();
  });
});
