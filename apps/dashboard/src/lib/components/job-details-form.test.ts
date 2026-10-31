import type { JobDetail } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiStub, jobId } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import JobDetailsForm from './job-details-form.svelte';
import { editRichText } from '../../test-support/rich-text.js';

const description = '<p>Join our payments team.</p><ul><li>Ship features.</li></ul>';

const job = {
  id: jobId,
  title: 'Backend Engineer (Payments)',
  slug: 'backend-engineer-payments',
  status: 'Open',
  description,
  department: 'Engineering',
  employmentType: 'FullTime',
  workplaceType: 'Remote',
  locations: [],
  applicantLocationRequirements: ['GB', 'IE'],
  salary: { min: 70000, max: 90000, currency: 'GBP', period: 'Year' },
  validThrough: null,
} as unknown as JobDetail;

function renderForm(onsaved = vi.fn()) {
  const bodies: unknown[] = [];
  const fetchMock = vi.fn(
    apiStub({
      [`PUT /v1/jobs/${jobId}`]: (init) => {
        bodies.push(JSON.parse(String(init?.body)));
        return Response.json(job);
      },
    }),
  );
  vi.stubGlobal('fetch', fetchMock);
  render(WithQueryClient, { props: { component: JobDetailsForm, props: { job, cancelHref: `/jobs/${jobId}`, onsaved } } });
  return { bodies, fetchMock, onsaved };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('job details form', () => {
  it("starts from the job and saves the whole posting, keeping an untouched description's HTML", async () => {
    const { bodies, onsaved } = renderForm();
    const form = await screen.findByRole('form', { name: 'Edit job details' });
    expect(within(form).getByLabelText('Countries candidates can work from')).toHaveValue('GB, IE');
    expect(within(form).getByLabelText('From')).toHaveValue('70000');

    await fireEvent.input(within(form).getByLabelText('To'), { target: { value: '95000' } });
    await fireEvent.input(within(form).getByLabelText(/^Closing date/), { target: { value: '2026-11-30' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toEqual({
      title: 'Backend Engineer (Payments)',
      department: 'Engineering',
      employmentType: 'FullTime',
      workplaceType: 'Remote',
      applicantLocationRequirements: ['GB', 'IE'],
      locations: null,
      salary: { min: 70000, max: 95000, currency: 'GBP', period: 'Year' },
      validThrough: '2026-11-30T23:59:59Z',
      description,
    });
    await waitFor(() => expect(onsaved).toHaveBeenCalledWith('Saved. The careers page shows it now; publish again to update the boards.'));
  });

  it('moves a job on site and rewrites its description in the editor', async () => {
    const { bodies } = renderForm();
    const form = await screen.findByRole('form', { name: 'Edit job details' });

    await fireEvent.change(within(form).getByLabelText('Workplace'), { target: { value: 'Onsite' } });
    await fireEvent.input(await within(form).findByLabelText('City'), { target: { value: 'Leeds' } });
    await fireEvent.input(within(form).getByLabelText('Country code'), { target: { value: 'gb' } });
    editRichText(within(form).getByRole('textbox', { name: 'Description' }), '<h2>Payouts</h2><p>Run <strong>payouts</strong>.</p><ul><li>Office in Leeds.</li></ul>');
    await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      workplaceType: 'Onsite',
      applicantLocationRequirements: null,
      locations: [{ city: 'Leeds', region: null, country: 'GB' }],
      description: '<h2>Payouts</h2><p>Run <strong>payouts</strong>.</p><ul><li><p>Office in Leeds.</p></li></ul>',
    });
  });

  it('keeps what the person typed when the job refetches while the form is open', async () => {
    const onsaved = vi.fn();
    vi.stubGlobal('fetch', vi.fn(apiStub({})));
    const { rerender } = render(WithQueryClient, { props: { component: JobDetailsForm, props: { job, cancelHref: `/jobs/${jobId}`, onsaved } } });
    const form = await screen.findByRole('form', { name: 'Edit job details' });
    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Payments Engineer' } });

    // A live event or a board confirming the job hands the page a new object for the same job.
    await rerender({ component: JobDetailsForm, props: { job: { ...job, updatedAt: '2026-10-20T12:00:00Z' }, cancelHref: `/jobs/${jobId}`, onsaved } });

    expect(within(form).getByLabelText('Title')).toHaveValue('Payments Engineer');
  });

  it('keeps the location region and the closing instant it did not change', async () => {
    const onsite = {
      ...job,
      workplaceType: 'Hybrid',
      applicantLocationRequirements: [],
      locations: [{ city: 'Leeds', region: 'West Yorkshire', country: 'GB' }],
      validThrough: '2026-11-30T12:00:00+00:00',
    } as unknown as JobDetail;
    const bodies: unknown[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn(apiStub({ [`PUT /v1/jobs/${jobId}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(onsite)) })),
    );
    render(WithQueryClient, { props: { component: JobDetailsForm, props: { job: onsite, cancelHref: `/jobs/${jobId}` } } });
    const form = await screen.findByRole('form', { name: 'Edit job details' });

    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: 'Payments Engineer' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

    await waitFor(() => expect(bodies).toHaveLength(1));
    expect(bodies[0]).toMatchObject({
      locations: [{ city: 'Leeds', region: 'West Yorkshire', country: 'GB' }],
      validThrough: '2026-11-30T12:00:00+00:00',
    });
  });

  it('says what is missing before sending anything', async () => {
    const { fetchMock } = renderForm();
    const form = await screen.findByRole('form', { name: 'Edit job details' });

    await fireEvent.input(within(form).getByLabelText('Title'), { target: { value: ' ' } });
    await fireEvent.input(within(form).getByLabelText('Countries candidates can work from'), { target: { value: '' } });
    await fireEvent.input(within(form).getByLabelText('From'), { target: { value: '99000' } });
    await fireEvent.click(within(form).getByRole('button', { name: 'Save details' }));

    expect(await within(form).findByText('Give the job a title.')).toBeInTheDocument();
    expect(within(form).getByText('List the countries candidates can work from, such as GB, IE.')).toBeInTheDocument();
    expect(within(form).getByText('The top of the range is below the bottom.')).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
