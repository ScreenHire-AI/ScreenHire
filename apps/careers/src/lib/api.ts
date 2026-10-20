import { configureApiClient } from '@screenhire/api-client';

// Server-side only: Aspire injects API_URL in development; production sets it to the internal API address.
configureApiClient({ baseUrl: process.env.API_URL ?? 'http://localhost:5080' });

export * from '@screenhire/api-client';
export { formatSalary, label } from './format';
