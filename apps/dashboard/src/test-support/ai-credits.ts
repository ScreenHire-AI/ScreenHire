import type { AiCreditDeduction, AiCreditPurchase, AiCreditsView } from '@screenhire/api-client';
import type { apiStub } from './fixtures.js';

/** Fictional prepaid AI credits (ADR-0032), shaped like `GET /v1/organization/billing/credits` and the two lists behind the balance. */
export const aiCredits: AiCreditsView = {
  available: true,
  balance: 18.4,
  paused: false,
  ownKey: false,
  amounts: [10, 25, 50],
  expiryMonths: 12,
  lowBalanceAlert: 5,
  autoReload: { enabled: false, below: null, amount: null, monthlyMax: null, reloadedThisMonth: 0 },
  hasSavedCard: false,
};

export const creditsWith = (overrides: Partial<AiCreditsView> = {}): AiCreditsView => ({ ...aiCredits, ...overrides });

export const creditPurchases: AiCreditPurchase[] = [
  { id: 'p-3', date: '2026-11-08T09:00:00Z', amount: 10, source: 'AutoReload', status: 'Failed', expiresAt: null },
  { id: 'p-2', date: '2026-11-05T09:00:00Z', amount: 25, source: 'Checkout', status: 'Paid', expiresAt: '2027-11-05T09:00:00Z' },
  { id: 'p-1', date: '2026-10-01T09:00:00Z', amount: 5, source: 'StartingCredit', status: 'Included', expiresAt: null },
];

export const applicationId = '01a157d4-0000-7000-8000-0000000000a1';

export const creditDeductions: AiCreditDeduction[] = [
  { id: 'd-2', date: '2026-11-08T10:00:00Z', kind: 'Check', amount: 0.31, calls: 2, applicationId, candidateName: 'Casey Rowan' },
  { id: 'd-1', date: '2026-10-31T23:00:00Z', kind: 'Expiry', amount: 1.25, calls: 0, applicationId: null, candidateName: null },
];

/** The three credit routes, for a page test's stub. Pass overrides to change one. */
export function creditRoutes(credits: AiCreditsView = aiCredits): Parameters<typeof apiStub>[0] {
  return {
    'GET /v1/organization/billing/credits': () => Response.json(credits),
    'GET /v1/organization/billing/credits/purchases': () => Response.json({ purchases: creditPurchases }),
    'GET /v1/organization/billing/credits/deductions': () => Response.json({ deductions: creditDeductions, next: null }),
  };
}
