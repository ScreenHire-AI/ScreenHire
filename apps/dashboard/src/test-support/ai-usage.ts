import type { AiUsageSummary } from '@screenhire/api-client';
import { limits } from './verification-settings.js';

/**
 * A fictional month of AI use, shaped like `GET /v1/organization/ai-usage/summary`: two days, ScreenHire's key and an own key,
 * for Billing's AI use section and the Activity log's line about this month.
 */
export const aiUsageSummary: AiUsageSummary = {
  from: '2026-11-01T00:00:00Z',
  to: '2026-11-09T12:00:00Z',
  totals: { calls: 12, inputTokens: 9000, outputTokens: 1500, cost: 0.42, callsWithUnknownCost: 0, failedCalls: 1 },
  byDay: [
    { date: '2026-11-07', calls: 5, inputTokens: 4000, outputTokens: 500, cost: 0.2, builtInTokens: 4500, ownKeyTokens: 0, builtInCost: 0.2, ownKeyCost: 0 },
    { date: '2026-11-08', calls: 7, inputTokens: 5000, outputTokens: 1000, cost: 0.22, builtInTokens: 2000, ownKeyTokens: 4000, builtInCost: 0.1, ownKeyCost: 0.12 },
  ],
  byModel: [
    { provider: 'Anthropic', model: 'claude-test', keySource: 'BuiltIn', calls: 8, inputTokens: 6000, outputTokens: 500, cost: 0.3, callsWithUnknownCost: 0 },
    { provider: 'OpenAi', model: 'gpt-test', keySource: 'OwnKey', calls: 4, inputTokens: 3000, outputTokens: 1000, cost: 0.12, callsWithUnknownCost: 0 },
  ],
  byKeySource: [
    { provider: '', model: '', keySource: 'BuiltIn', calls: 8, inputTokens: 6000, outputTokens: 500, cost: 0.3, callsWithUnknownCost: 0 },
    { provider: '', model: '', keySource: 'OwnKey', calls: 4, inputTokens: 3000, outputTokens: 1000, cost: 0.12, callsWithUnknownCost: 0 },
  ],
  limits,
  // Five minutes before the test runs, so it always reads as time ago.
  lastCallAt: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
};
