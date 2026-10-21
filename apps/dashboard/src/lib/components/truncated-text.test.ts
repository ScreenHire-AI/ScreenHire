import MailIcon from '@lucide/svelte/icons/mail';
import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import TruncatedText from './truncated-text.svelte';

const tooltip = () => document.querySelector('[data-slot="tooltip-content"]');
const text = 'Senior product designer, design systems and accessibility';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('truncated text', () => {
  it('has no tooltip and no tab stop while the whole text fits', async () => {
    const { container } = render(WithTooltips, { props: { component: TruncatedText, props: { text } } });

    const span = screen.getByText(text);
    expect(span).toHaveClass('truncate');
    expect(span).not.toHaveAttribute('tabindex');
    await fireEvent.focus(span);
    expect(tooltip()).toBeNull();
    await expectNoAxeViolations(container);
  });

  it('shows the full value in a tooltip on focus once the text is cut (F5)', async () => {
    // jsdom does no layout: make the text wider than its box.
    vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(2000);
    render(WithTooltips, { props: { component: TruncatedText, props: { text } } });

    const span = screen.getByText(text);
    expect(span).toHaveAttribute('tabindex', '0');
    await fireEvent.focus(span);
    expect(tooltip()).toHaveTextContent(text);
    expect(span).toHaveAttribute('aria-describedby', tooltip()!.id);
    await expectNoAxeViolations(document.body);
  });

  it('as a link, is one tab stop that carries the tooltip, with its icon hidden', async () => {
    vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(2000);
    const email = 'samuel.okonkwo-hartley@careers.example.com';
    render(WithTooltips, { props: { component: TruncatedText, props: { text: email, href: `mailto:${email}`, icon: MailIcon } } });

    const link = screen.getByRole('link', { name: email });
    expect(link).toHaveAttribute('href', `mailto:${email}`);
    expect(link).not.toHaveAttribute('tabindex');
    expect(link.querySelectorAll('[tabindex]')).toHaveLength(0);
    expect(link.querySelector('svg')).toHaveAttribute('aria-hidden', 'true');
    await fireEvent.focus(link);
    expect(tooltip()).toHaveTextContent(email);
    await expectNoAxeViolations(document.body);
  });
});
