// Equality monitoring totals on Reports: beside each answer's count, how many of those candidates moved on, were hired and
// were flagged by the checks (the counts a bias audit divides), each hidden under five as the count is.
import { render, screen, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { user } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import EqualityReportPanel from './equality-report-panel.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/reports') } }));

afterEach(() => vi.unstubAllGlobals());

const formTemplateId = '01a157d4-0000-7000-8000-000000000701';

function stubApi() {
  vi.stubGlobal('fetch', async (input: RequestInfo | URL) => {
    const path = new URL(String(input), 'http://dashboard.test').pathname;
    if (path === '/v1/form-templates') {
      return Response.json({ forms: [{ id: formTemplateId, name: 'Equality monitoring', kind: 'EqualityMonitoring' }], equalityMonitoringEnabled: true });
    }
    if (path === '/v1/reports/equality') {
      return Response.json({
        minimumCount: 5,
        forms: [
          {
            formTemplateId,
            name: 'Equality monitoring',
            responses: 14,
            questions: [
              {
                questionId: 'disability',
                label: 'Do you consider yourself to have a disability?',
                answers: [
                  { value: 'yes', label: 'Yes', count: 6, advanced: 5, hired: null, flagged: null },
                  { value: 'no', label: 'No', count: 8, advanced: 6, hired: null, flagged: 5 },
                ],
              },
            ],
          },
        ],
      });
    }
    throw new Error(`No stub for ${path}`);
  });
}

describe('equality report panel', () => {
  it('shows each answer with the candidates who moved on, were hired and were flagged, hiding small counts', async () => {
    stubApi();
    render(WithQueryClient, { props: { component: EqualityReportPanel, props: { from: '2026-11-01T00:00:00Z', to: '2026-11-23T00:00:00Z', jobIds: [] } } });

    const table = await screen.findByRole('table', { name: 'Do you consider yourself to have a disability?' });
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent?.trim())).toEqual(['Answer', 'Candidates', 'Moved on', 'Hired', 'Flagged']);
    const [, yes, no] = within(table).getAllByRole('row');
    expect(within(yes!).getAllByRole('cell').map((cell) => cell.textContent?.trim())).toEqual(['Yes', '6', '5', 'Fewer than 5', 'Fewer than 5']);
    expect(within(no!).getAllByRole('cell').map((cell) => cell.textContent?.trim())).toEqual(['No', '8', '6', 'Fewer than 5', '5']);
  });
});
