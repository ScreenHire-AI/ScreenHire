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
  FormUse: 'Form activity',
};

/**
 * What each group of checks is, in the Checks tab's words (the owner's walk-through, 2026-11-21): a one-line summary for its row,
 * what it looks at, one example of what it catches, and what turning it off costs. Built from what the analyzers do (`CheckGroups.Checked` and the signal catalogue
 * in src/ScreenHire.Domain/Verification), never from a threshold, a weight or a rule.
 */
export interface CheckGroupCopy {
  /** The row's one line, short enough to sit beside the group's name at desktop width (the owner's walk-through, 2026-11-22). */
  summary: string;
  looksAt: string;
  example: string;
  offCost: string;
}

export const checkGroupCopy: Record<CheckGroup, CheckGroupCopy> = {
  Network: {
    summary: 'Where an application was sent from',
    looksAt: "The address an application was sent from: hosting providers, VPN, proxy and Tor, and a country that differs from the candidate's details.",
    example: 'An application sent from a cloud server rather than a home or office connection.',
    offCost: 'Where an application came from is not looked at, so one sent through a proxy reads like any other.',
  },
  Contact: {
    summary: 'Whether the email address and phone number can be real',
    looksAt: 'Whether the email address and phone number can be real: a throwaway email service, a domain that takes no mail, and an invalid or internet phone number.',
    example: 'An email address from a throwaway mail service.',
    offCost: 'A made-up email address or phone number goes unnoticed until someone tries to reach the candidate.',
  },
  Documents: {
    summary: 'Hidden instructions and generator marks in uploaded files',
    looksAt: 'Uploaded files: hidden instructions written for an AI screener, and signs that a resume generator made the file.',
    example: 'White text in a resume telling an AI screener to rank it first.',
    offCost: 'Hidden text in uploaded files is not looked for.',
  },
  Answers: {
    summary: 'Copied text and details that contradict each other',
    looksAt: 'Written answers: copied and template text, and with the consistency check on, details that contradict each other across the resume and the answers.',
    example: 'The resume and an answer give different dates for the same job.',
    offCost: 'Answers are not compared and the consistency check does not run, so a contradiction waits for a person to spot it.',
  },
  Duplicates: {
    summary: 'One person behind several applications',
    looksAt: 'The same email address, phone number, device or resume file on another of your applicants.',
    example: 'Three applications under different names sent from the same device.',
    offCost: 'One person applying under several names is not noticed, and the device check does not run.',
  },
  FormUse: {
    summary: 'How the form was filled in, as counts only',
    looksAt: 'How the form was filled in, as counts only: automation, speed, a hidden field, the bot challenge, pasting and switching away. Never a level on its own.',
    example: 'A form filled in and sent by browser automation.',
    offCost: 'A bot filling in the form goes unnoticed, and the device is not read, so Duplicates cannot compare it.',
  },
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
  const invalid = 'Enter an address such as 203.0.113.7, or a range such as 203.0.113.0/24.';
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
