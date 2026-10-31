import type { JobSummary } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { apiStub, jobs, user } from '../../test-support/fixtures.js';
import WithQueryClient from '../../test-support/with-query-client.svelte';
import AddCandidateSheet from './add-candidate-sheet.svelte';

vi.mock('$app/state', () => ({ page: { data: { user }, url: new URL('http://dashboard.test/candidates') }, navigating: { to: null } }));
// Superforms needs the rest of the module (beforeNavigate).
vi.mock(import('$app/navigation'), async (importOriginal) => ({ ...(await importOriginal()), goto: vi.fn(async () => {}) }));

afterEach(() => {
  vi.unstubAllGlobals();
});

const closed: JobSummary = { ...jobs[0]!, id: '01a157d4-0000-7000-8000-000000000003', title: 'Payroll Specialist', status: 'Closed' };
const preset = { id: jobs[1]!.id, title: jobs[1]!.title };
const createPath = `/v1/jobs/${preset.id}/applications`;
const ticket = {
  uploadId: '01a157d4-0000-7000-8000-0000000009aa',
  uploadUrl: 'http://storage.test/applicant-uploads/resume.pdf?sig=create',
  method: 'PUT',
  headers: { 'x-ms-blob-type': 'BlockBlob' },
  expiresAt: '2026-10-20T12:15:00Z',
  maxBytes: 10_485_760,
};
const created = (body: { candidate: { firstName: string; lastName: string | null } }) =>
  Response.json(
    { id: 'new-application', jobTitle: preset.title, candidate: { fullName: [body.candidate.firstName, body.candidate.lastName].filter(Boolean).join(' ') } },
    { status: 201 },
  );

function renderSheet(routes: Parameters<typeof apiStub>[0], props: Record<string, unknown> = {}) {
  const calls: string[] = [];
  const stub = apiStub(routes);
  vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push(`${(init?.method ?? 'GET').toUpperCase()} ${String(input)}`);
    return stub(input, init);
  });
  const onadded = vi.fn();
  render(WithQueryClient, { props: { component: AddCandidateSheet, props: { open: true, onadded, ...props } } });
  return { calls, onadded, sheet: () => screen.getByRole('dialog', { name: 'Add candidate' }) };
}

async function fill(sheet: HTMLElement, values: Record<string, string>) {
  for (const [label, value] of Object.entries(values)) {
    await fireEvent.input(within(sheet).getByLabelText(label), { target: { value } });
  }
}

describe('Add candidate sheet', () => {
  it('offers the drafts and open jobs, never a closed one, and asks for a job, a first name and an email first', async () => {
    const { calls, sheet } = renderSheet({ 'GET /v1/jobs': () => Response.json([...jobs, closed]) });

    const picker = within(sheet()).getByLabelText('Job');
    await waitFor(() => expect(within(picker).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Choose a job',
      'Customer Success Lead',
      'Senior .NET Engineer',
    ]));
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Add candidate' }));

    expect(await within(sheet()).findByText('Choose a job.')).toBeInTheDocument();
    expect(within(sheet()).getByText('Enter a first name.')).toBeInTheDocument();
    expect(within(sheet()).getByText('Enter an email address.')).toBeInTheDocument();
    // One message for an empty address, not a second one about its format.
    expect(within(sheet()).queryByText('Enter a valid email address.')).toBeNull();
    expect(calls.filter((call) => call.startsWith('POST'))).toEqual([]);
    await expectNoAxeViolations();
  });

  it("on a job's page has no picker, and posts only the candidate: no source, telemetry or declarations", async () => {
    const bodies: unknown[] = [];
    const { calls, onadded, sheet } = renderSheet(
      { [`POST ${createPath}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), created(JSON.parse(String(init?.body)))) },
      { job: preset },
    );

    expect(within(sheet()).queryByLabelText('Job')).toBeNull();
    expect(within(sheet()).getByText(`They join ${preset.title} in its first stage.`)).toBeInTheDocument();
    await fill(sheet(), { 'First name': ' Jordan ', 'Last name (optional)': 'Lee', Email: 'jordan.lee@example.com', 'Phone (optional)': '' });
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Add candidate' }));

    await waitFor(() => expect(bodies).toEqual([
      { candidate: { firstName: 'Jordan', lastName: 'Lee', email: 'jordan.lee@example.com', phone: null }, resumeUploadId: null },
    ]));
    expect(calls).toEqual([`POST ${createPath}`]);
    await waitFor(() => expect(onadded).toHaveBeenCalledWith({ applicationId: 'new-application', name: 'Jordan Lee', jobTitle: preset.title }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('uploads a resume straight to storage before adding the candidate with it', async () => {
    const bodies: unknown[] = [];
    const { calls, sheet } = renderSheet(
      {
        [`POST ${createPath}/resume-uploads`]: (init) => (bodies.push(JSON.parse(String(init?.body))), Response.json(ticket)),
        [`PUT ${ticket.uploadUrl}`]: () => new Response(null, { status: 201 }),
        [`POST ${createPath}`]: (init) => (bodies.push(JSON.parse(String(init?.body))), created(JSON.parse(String(init?.body)))),
      },
      { job: preset },
    );

    await fill(sheet(), { 'First name': 'Jordan', Email: 'jordan.lee@example.com' });
    const file = new File(['%PDF-1.7'], 'jordan-lee.pdf', { type: 'application/pdf' });
    await fireEvent.change(within(sheet()).getByLabelText('Resume (optional)'), { target: { files: [file] } });
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Add candidate' }));

    await waitFor(() => expect(calls).toEqual([`POST ${createPath}/resume-uploads`, `PUT ${ticket.uploadUrl}`, `POST ${createPath}`]));
    expect(bodies).toEqual([
      { fileName: 'jordan-lee.pdf', sizeBytes: file.size },
      { candidate: { firstName: 'Jordan', lastName: null, email: 'jordan.lee@example.com', phone: null }, resumeUploadId: ticket.uploadId },
    ]);
  });

  it("puts the API's field errors under the fields they are about, the resume's under Resume", async () => {
    const { sheet } = renderSheet(
      {
        [`POST ${createPath}`]: () =>
          Response.json(
            { status: 400, errors: { 'candidate.email': ['Enter a real address.'], resumeUploadId: ['This file is not a PDF or DOCX file.'] } },
            { status: 400 },
          ),
      },
      { job: preset },
    );

    await fill(sheet(), { 'First name': 'Jordan', Email: 'jordan.lee@example.com' });
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Add candidate' }));

    const email = within(sheet()).getByLabelText('Email');
    await waitFor(() => expect(email).toHaveAttribute('aria-invalid', 'true'));
    expect(document.getElementById(email.getAttribute('aria-describedby')!)).toHaveTextContent('Enter a real address.');
    const resume = within(sheet()).getByLabelText('Resume (optional)');
    expect(document.getElementById(resume.getAttribute('aria-describedby')!)).toHaveTextContent('This file is not a PDF or DOCX file.');
  });

  it('says why the API refused, in the sheet, and keeps it open', async () => {
    const { onadded, sheet } = renderSheet(
      {
        [`POST ${createPath}`]: () =>
          Response.json({ title: 'Conflict', detail: 'This candidate already applied to this job.', status: 409 }, { status: 409 }),
      },
      { job: preset },
    );

    await fill(sheet(), { 'First name': 'Jordan', Email: 'jordan.lee@example.com' });
    await fireEvent.click(within(sheet()).getByRole('button', { name: 'Add candidate' }));

    // FailureText's line, which is always in the sheet and empty until something fails.
    await waitFor(() => expect(sheet().querySelector('[data-slot="failure-text"]')).toHaveTextContent('This candidate already applied to this job.'));
    expect(onadded).not.toHaveBeenCalled();
    expect(sheet()).toBeInTheDocument();
  });
});
