import type { CheckGroup, ModelProblem, ModelProvider } from '@screenhire/api-client';

// The words of the Verification area's settings tabs. The group and device check labels mirror `CheckGroups.Label` and
// `ChangeLabels.DeviceCheck` in src/ScreenHire.Domain, which History uses; `ChangeLabelsTests` fails when they drift, so
// change both together.

/** The groups of checks an organization can turn off. */
export const checkGroupLabels: Record<CheckGroup, string> = {
  Network: 'Network',
  Contact: 'Contact details',
  Documents: 'Documents',
  Answers: 'Answers',
  Duplicates: 'Duplicates',
  FormUse: 'Form use',
};

/** What each group looks at, one line under its switch. */
export const checkGroupHints: Record<CheckGroup, string> = {
  Network: 'Hosting providers, proxies and a country that differs from the phone number.',
  Contact: 'Whether the email address and phone number can be real.',
  Documents: 'Hidden text and signs of a resume generator in uploaded files.',
  Answers: 'Repeated or templated answers, and the consistency check when it is on.',
  Duplicates: 'The same email, phone number, device or resume file on another candidate.',
  FormUse: 'Counts of automation and pasting, never a level alone. Off also stops device matches.',
};

/** The device check basis. `ByRegion` is no organization choice: where the job is decides. */
export type DeviceCheckChoice = 'ByRegion' | 'Notice' | 'Consent' | 'Off';

export const deviceCheckLabels: Record<DeviceCheckChoice, string> = {
  ByRegion: 'By region',
  Notice: 'On, with notice',
  Consent: 'Ask for consent',
  Off: 'Off',
};

/** The model providers an organization can bring a key for. */
export const modelProviderLabels: Record<NonNullable<ModelProvider>, string> = {
  Anthropic: 'Anthropic',
  OpenAi: 'OpenAI',
  OpenRouter: 'OpenRouter',
  AzureOpenAi: 'Azure OpenAI',
  Gemini: 'Google Gemini',
  OpenAiCompatible: 'Other (OpenAI-compatible)',
};

/** A failed connection test, in a sentence. Never the provider's own words. The same as a refused save: `ChangeLabelsTests` compares them. */
export const modelProblemMessages: Record<NonNullable<ModelProblem>, string> = {
  InvalidKey: 'The provider did not accept this key.',
  ModelNotFound: 'The provider has no model with this id.',
  Unreachable: 'The provider did not answer. Check the address and try again.',
  AddressNotAllowed: "ScreenHire cannot reach this address. Use the provider's public https address.",
  Other: 'The connection test failed. Check the provider, model and key.',
};

const ipv4 = /^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?:\/(\d{1,2}))?$/;

/** The most entries of each trusted list; the same as the server's. */
export const maxTrustedEntries = 50;

/** A domain as the server stores it (lower case, a dot, letters, digits and hyphens), or null when it is not one. */
export function normalizeDomain(value: string): string | null {
  const domain = value.trim().replace(/^@/, '').replace(/^\.+|\.+$/g, '').toLowerCase();
  if (!domain || domain.length > 253 || !domain.includes('.')) return null;
  return domain.split('.').every((label) => label.length > 0 && label.length <= 63 && /^[a-z0-9]([a-z0-9-]*[a-z0-9])?$/.test(label)) ? domain : null;
}

/** What is wrong with a trusted domain, or null. */
export function domainProblem(value: string): string | null {
  return normalizeDomain(value) ? null : 'Enter a domain such as partner.example.com.';
}

/**
 * What is wrong with a trusted network, or null: a CIDR range (an address alone is one host) no wider than /16 for IPv4
 * or /48 for IPv6, which the server checks again.
 */
export function networkProblem(value: string): string | null {
  const text = value.trim();
  const invalid = 'Enter a network such as 203.0.113.0/24.';
  const four = ipv4.exec(text);
  if (four) {
    if (four.slice(1, 5).some((part) => Number(part) > 255)) return invalid;
    const prefix = four[5] === undefined ? 32 : Number(four[5]);
    if (prefix > 32) return invalid;
    return prefix < 16 ? 'That network is too wide. Use /16 or narrower.' : null;
  }
  const [address, rest, ...extra] = text.split('/');
  if (extra.length > 0 || !address.includes(':') || !/^[0-9a-fA-F:.]+$/.test(address)) return invalid;
  const prefix = rest === undefined ? 128 : Number(rest);
  if (!Number.isInteger(prefix) || prefix < 0 || prefix > 128 || (rest !== undefined && !/^\d+$/.test(rest))) return invalid;
  return prefix < 48 ? 'That network is too wide. Use /48 or narrower.' : null;
}
