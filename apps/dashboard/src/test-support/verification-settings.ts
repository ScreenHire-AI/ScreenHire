import type { OrganizationVerificationSettings } from '@screenhire/api-client';
import { vi } from 'vitest';
import { apiStub } from './fixtures.js';

/** Fictional verification settings for the Verification area's tab tests, shaped like `GET /v1/organization/verification-settings`. */

// Nobody has changed the defaults yet (T6).
const lastUpdated = { at: null, by: 'ScreenHire' as const };

export const presets: OrganizationVerificationSettings['presets'] = [
  { provider: 'Anthropic', baseUrl: null, baseUrlEditable: false, listsModels: true },
  { provider: 'OpenAi', baseUrl: 'https://api.openai.com/v1', baseUrlEditable: false, listsModels: true },
  { provider: 'OpenRouter', baseUrl: 'https://openrouter.ai/api/v1', baseUrlEditable: false, listsModels: true },
  { provider: 'AzureOpenAi', baseUrl: null, baseUrlEditable: true, listsModels: false },
  { provider: 'Gemini', baseUrl: 'https://generativelanguage.googleapis.com/v1beta/openai', baseUrlEditable: false, listsModels: true },
  { provider: 'OpenAiCompatible', baseUrl: null, baseUrlEditable: true, listsModels: true },
];

export const limits: NonNullable<OrganizationVerificationSettings['limits']> = {
  unit: 'Tokens',
  budget: null,
  cap: null,
  effectiveBudget: { amount: 2_000_000, unit: 'Tokens', isDefault: true, used: 420, percent: 0 },
  effectiveCap: { amount: 2_000_000, unit: 'Tokens', isDefault: true, used: 420, percent: 0 },
};

export const defaults: OrganizationVerificationSettings = {
  llmConsistencyCheck: false,
  lastUpdated,
  modelSource: 'Included',
  provider: null,
  baseUrl: null,
  model: null,
  apiKeySaved: false,
  ownKeyInputPrice: null,
  ownKeyOutputPrice: null,
  limits: null,
  presets,
  checks: { network: true, contact: true, documents: true, answers: true, duplicates: true, formUse: true },
  defaultAiUse: 'AllowedWithDisclosure',
  deviceCheck: null,
  trustedDomains: [],
  trustedNetworks: [],
  alerts: { suggestedEmail: false, suggestedSlack: false, budgetEmail: false, budgetSlack: false },
  emailAvailable: true,
  slackConnected: true,
};

export const settingsWith = (overrides: Partial<OrganizationVerificationSettings> = {}): OrganizationVerificationSettings => ({ ...defaults, ...overrides });

export const ownKey = (overrides: Partial<OrganizationVerificationSettings> = {}) =>
  settingsWith({ modelSource: 'OwnKey', provider: 'Anthropic', model: 'claude-test', apiKeySaved: true, ...overrides });

export type Body = Record<string, unknown>;

/**
 * Stubs `fetch` for a settings tab: the settings read, a PUT that records its body, and any other routes. Returns the recorded
 * bodies. The caller renders the page and calls `vi.unstubAllGlobals()` afterwards.
 */
export function stubSettings(settings: OrganizationVerificationSettings, routes: Parameters<typeof apiStub>[0] = {}) {
  const bodies: Body[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(
      apiStub({
        'GET /v1/organization/verification-settings': () => Response.json(settings),
        'PUT /v1/organization/verification-settings': (init) => {
          bodies.push(JSON.parse(String(init?.body)) as Body);
          return Response.json(settings);
        },
        ...routes,
      }),
    ),
  );
  return bodies;
}
