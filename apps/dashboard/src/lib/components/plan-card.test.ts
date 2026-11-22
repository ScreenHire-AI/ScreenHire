import type { BillingOverview, BillingPlan } from '@screenhire/api-client';
import { included } from '@screenhire/plans';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import PlanCard from './plan-card.svelte';
import PlanComparison from './plan-comparison.svelte';

// The plan card and the comparison cards on Billing (docs/architecture/billing.md, "The Billing page"; design system section 11).
// The ticks are the plan catalogue's own list (`included` from @screenhire/plans), which the marketing pricing page renders too
// (apps/marketing's pricing test), so these tests read it rather than repeat it.

const plan = (key: string, name: string, extra: Partial<BillingPlan> = {}): BillingPlan => ({
  key,
  name,
  monthlyPrice: null,
  currency: 'USD',
  activeJobs: null,
  unlimited: false,
  canSubscribe: true,
  ...extra,
});
const starter = plan('starter', 'Starter', { monthlyPrice: 79, activeJobs: 3 });
const growth = plan('growth', 'Growth', { monthlyPrice: 149, activeJobs: 10 });
const unlimited = plan('unlimited', 'Unlimited', { monthlyPrice: 249, unlimited: true });

const active: BillingOverview = {
  available: true,
  sandbox: false,
  state: 'Active',
  currentPlan: growth,
  trialEndsAt: null,
  nextPaymentAt: '2026-12-22T12:00:00Z',
  nextPaymentAmount: 149,
  cancelsAtPeriodEnd: false,
  openJobs: 3,
  activeJobLimit: 10,
  canManage: true,
  taxAtCheckout: false,
  plans: [starter, growth, unlimited],
  aiUsage: null,
  thisMonth: null,
};
const now = Date.parse('2026-11-22T10:00:00Z');
const trial: BillingOverview = { ...active, state: 'Trialing', trialEndsAt: '2026-12-01T12:00:00Z', nextPaymentAt: null, nextPaymentAmount: null, canManage: false };

const renderCard = (props: Record<string, unknown>) => render(WithTooltips, { props: { component: PlanCard, props } });
const card = () => screen.getByRole('region', { name: 'Plan' });
const ticks = (name: string) => within(screen.getByRole('list', { name })).getAllByRole('listitem').map((item) => item.textContent?.trim());
const meterWidth = () => (card().querySelector('[data-headroom-meter] > div') as HTMLElement | null)?.style.width;

describe('plan card', () => {
  it('shows a paying plan with presence: its name, its price large, when it renews in words and what it includes from the catalogue', async () => {
    const onchoose = vi.fn();
    renderCard({ overview: active, now, onchoose });

    expect(card().querySelector('[data-plan-headline]')).toHaveTextContent(/^Growth$/);
    expect(card().querySelector('[data-plan-price] .text-metric')).toHaveTextContent(/^\$149\.00$/);
    expect(card().querySelector('[data-plan-price]')).toHaveTextContent('a month');
    expect(within(card()).getByText(/^Renews/)).toHaveTextContent(/^Renews December 22$/);
    expect(ticks('What your plan includes')).toEqual(included);
    await fireEvent.click(within(card()).getByRole('button', { name: 'Change plan' }));
    expect(onchoose).toHaveBeenCalledOnce();
    await expectNoAxeViolations();
  });

  it('shows the headroom on a limited plan as a small meter and its words, the meter a picture of the words', () => {
    renderCard({ overview: active, now });

    expect(card().querySelector('[data-headroom]')).toHaveTextContent(/^3 of 10 open jobs$/);
    expect(card().querySelector('[data-headroom-meter]')).toHaveAttribute('aria-hidden', 'true');
    expect(meterWidth()).toBe('30%');
  });

  it('stops the meter full when a downgrade left more open than the limit, and keeps the true count in words', () => {
    renderCard({ overview: { ...active, currentPlan: starter, openJobs: 5, activeJobLimit: 3 }, now });

    expect(card().querySelector('[data-headroom]')).toHaveTextContent(/^5 of 3 open jobs$/);
    expect(meterWidth()).toBe('100%');
  });

  it('shows only the count on Unlimited, with no meter', () => {
    renderCard({ overview: { ...active, currentPlan: unlimited, activeJobLimit: null, openJobs: 1 }, now });

    expect(card().querySelector('[data-headroom]')).toHaveTextContent(/^1 open job$/);
    expect(card().querySelector('[data-headroom-meter]')).toBeNull();
  });

  it('is the trial card during the trial: the days left, the trial plan until its end, what it includes and Choose a plan, with no price', async () => {
    renderCard({ overview: trial, now, onchoose: () => {} });

    expect(card().querySelector('[data-plan-headline]')).toHaveTextContent(/^Trial: 9 days left$/);
    expect(within(card()).getByText(/until/)).toHaveTextContent(/^Growth until December 1$/);
    expect(card().querySelector('[data-plan-price]')).toBeNull();
    expect(ticks('What your plan includes')).toEqual(included);
    expect(within(card()).getByRole('button', { name: 'Choose a plan' })).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it('counts the trial in calendar days, as the bell does: one day left, then today', () => {
    const { unmount } = renderCard({ overview: trial, now: Date.parse('2026-11-30T09:00:00Z') });
    expect(card().querySelector('[data-plan-headline]')).toHaveTextContent(/^Trial: 1 day left$/);
    unmount();

    renderCard({ overview: trial, now: Date.parse('2026-12-01T08:00:00Z') });
    expect(card().querySelector('[data-plan-headline]')).toHaveTextContent(/^Trial: ends today$/);
  });

  it('says when a plan set to end ends, instead of when it renews', () => {
    renderCard({ overview: { ...active, cancelsAtPeriodEnd: true }, now });

    expect(within(card()).getByText(/^Ends/)).toHaveTextContent(/^Ends December 22$/);
    expect(within(card()).queryByText(/^Renews/)).toBeNull();
  });

  it('says the price is before tax when Checkout adds tax', () => {
    renderCard({ overview: { ...active, taxAtCheckout: true }, now });

    expect(card().querySelector('[data-plan-price]')).toHaveTextContent('plus tax where it applies');
  });

  it('offers no button when the page has nothing to offer, and says No plan once the plan ended', () => {
    renderCard({ overview: { ...active, state: 'Ended', currentPlan: null, activeJobLimit: null, nextPaymentAt: null }, now });

    expect(card().querySelector('[data-plan-headline]')).toHaveTextContent(/^No plan$/);
    expect(screen.queryByRole('list', { name: 'What your plan includes' })).toBeNull();
    expect(within(card()).queryByRole('button')).toBeNull();
  });
});

describe('plan comparison cards', () => {
  const cardOf = (key: string) => document.querySelector(`[data-plan-card="${key}"]`) as HTMLElement;

  it('shows each plan in the given order with its name, price, its limit first and then what every plan includes', async () => {
    render(PlanComparison, { props: { plans: [starter, growth, unlimited], current: 'growth', actionFor: () => null } });

    expect(screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)).toEqual(['Starter', 'Growth', 'Unlimited']);
    expect(within(cardOf('starter')).getByText('$79.00')).toBeInTheDocument();
    expect(ticks('What Starter includes')).toEqual(['Up to 3 open jobs', ...included]);
    expect(ticks('What Growth includes')).toEqual(['Up to 10 open jobs', ...included]);
    expect(ticks('What Unlimited includes')).toEqual(['Unlimited open jobs', ...included]);
    await expectNoAxeViolations();
  });

  it('marks the current plan with its words and the accent edge, and only that one', () => {
    render(PlanComparison, { props: { plans: [starter, growth, unlimited], current: 'growth', actionFor: () => null } });

    expect(screen.getAllByText('Your plan')).toHaveLength(1);
    expect(within(cardOf('growth')).getByText('Your plan')).toBeInTheDocument();
    expect(cardOf('growth')).toHaveClass('border-primary');
    expect(cardOf('starter')).not.toHaveClass('border-primary');
  });

  it('names the marked plan as the trial while the trial runs on it', () => {
    render(PlanComparison, { props: { plans: [starter, growth], current: 'growth', currentLabel: 'Your trial', actionFor: () => null } });

    expect(within(cardOf('growth')).getByText('Your trial')).toBeInTheDocument();
    expect(screen.queryByText('Your plan')).toBeNull();
  });

  it("puts the page's action for each plan on its card, and runs it", async () => {
    const run = vi.fn();
    render(PlanComparison, {
      props: { plans: [starter, growth, unlimited], current: 'growth', actionFor: (p: BillingPlan) => (p.key === 'growth' ? null : { label: `Change to ${p.name}`, run }) },
    });

    expect(screen.getAllByRole('button').map((button) => button.textContent?.trim())).toEqual(['Change to Starter', 'Change to Unlimited']);
    expect(within(cardOf('growth')).queryByRole('button')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: 'Change to Unlimited' }));
    expect(run).toHaveBeenCalledOnce();
  });

  it('shows a plan with no price set as such, and tax only beside a price', () => {
    render(PlanComparison, { props: { plans: [starter, plan('later', 'Later')], taxAtCheckout: true, actionFor: () => null } });

    expect(within(cardOf('later')).getByText('Price not set')).toBeInTheDocument();
    expect(within(cardOf('later')).queryByText('plus tax where it applies')).toBeNull();
    expect(within(cardOf('starter')).getByText('plus tax where it applies')).toBeInTheDocument();
  });

  it('stacks the cards in one column until the panel is wide enough for all three, never two and one', () => {
    render(PlanComparison, { props: { plans: [starter, growth, unlimited], actionFor: () => null } });

    const grid = cardOf('starter').parentElement as HTMLElement;
    expect(grid).toHaveClass('grid-cols-1', '@lg:grid-cols-3');
    expect(grid.className).not.toMatch(/grid-cols-2/);
  });
});
