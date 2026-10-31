import { configureApiClient } from '@screenhire/api-client';

/** The API's address, server-side only. Aspire injects API_URL in development; production sets the internal address. */
export const apiBaseUrl = process.env.API_URL ?? 'http://localhost:5080';
configureApiClient({ baseUrl: apiBaseUrl });

export * from '@screenhire/api-client';
export { escapeHtml, formatSalary, jsonLdScript, label } from './format';
