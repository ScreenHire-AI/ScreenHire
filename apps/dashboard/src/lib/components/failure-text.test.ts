import { render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import FailureText from './failure-text.svelte';

describe('failure text', () => {
  it('says what failed with the problem icon, the verify colour and the reason (T1)', () => {
    render(FailureText, { props: { message: 'Could not publish the job.' } });

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('Could not publish the job.');
    expect(alert).toHaveClass('text-status-verify');
    expect(alert.querySelector('[data-problem-icon]')).not.toBeNull();
    // Not a status icon: those appear only in status badges (I2).
    expect(alert.querySelector('[data-status-icon]')).toBeNull();
  });

  it('stays in the page, empty and without an icon, until there is a message', () => {
    render(FailureText, { props: { message: '' } });

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('');
    expect(alert.querySelector('svg')).toBeNull();
  });
});
