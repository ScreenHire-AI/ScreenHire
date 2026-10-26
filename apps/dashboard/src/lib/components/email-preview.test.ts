import { fireEvent, render, screen } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import EmailPreview from './email-preview.svelte';

const preview = {
  subject: 'We received your application',
  html: '<!doctype html><html lang="en"><body><p>Hi <b>Jordan</b>,</p><script>parent.hacked = true;</script></body></html>',
  text: 'Hi Jordan,\n\nThanks.\n',
};

describe('email preview', () => {
  it('shows the subject and the HTML part in a sandboxed frame with no scripts', async () => {
    render(EmailPreview, { props: { preview } });

    expect(screen.getByText('We received your application')).toBeInTheDocument();
    const frame = screen.getByTitle('Email preview');
    expect(frame).toHaveAttribute('sandbox', '');
    expect(frame).toHaveAttribute('srcdoc', preview.html);
    await expectNoAxeViolations();
  });

  it('switches to the plain-text part', async () => {
    const { container } = render(EmailPreview, { props: { preview } });

    await fireEvent.click(screen.getByRole('button', { name: 'Plain text' }));

    expect(screen.getByRole('button', { name: 'Plain text' })).toHaveAttribute('aria-pressed', 'true');
    expect(container.querySelector('[data-email-text]')).toHaveTextContent('Hi Jordan, Thanks.');
    expect(screen.queryByTitle('Email preview')).not.toBeInTheDocument();
  });

  it('shows a skeleton while loading and the failure when the template cannot render', () => {
    const { rerender } = render(EmailPreview, { props: { preview: undefined } });
    expect(screen.getByRole('status')).toHaveTextContent('Loading the preview');

    rerender({ preview: undefined, failure: 'There is no variable "x".' });
    expect(screen.getByText('There is no variable "x".')).toBeInTheDocument();
  });
});
