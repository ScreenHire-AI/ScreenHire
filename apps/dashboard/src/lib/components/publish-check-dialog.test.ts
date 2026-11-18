import type { PublishCheckItem } from '@screenhire/api-client';
import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import { jobId } from '../../test-support/fixtures.js';
import PublishCheckDialog from './publish-check-dialog.svelte';

const items: PublishCheckItem[] = [
  { kind: 'TemplatePrompt', field: 'Description', text: 'The description still has the template\'s fill-in text, "Describe the work, the office and the team.".', boards: [] },
  { kind: 'NoCity', field: 'City', text: 'Talent.com needs a city for an on-site or hybrid job.', boards: ['Talent.com'] },
  { kind: 'NoSalary', field: 'Salary', text: 'Pay Board needs a salary.', boards: ['Pay Board'] },
];

function renderDialog(shown = items, extra: { busy?: boolean } = {}) {
  const onpublish = vi.fn();
  render(PublishCheckDialog, { props: { open: true, jobId, items: shown, onpublish, ...extra } });
  return { onpublish };
}

describe('publish check dialog', () => {
  it('counts the problems in its title and gives each one a sentence and a link to its field', async () => {
    renderDialog();

    const dialog = await screen.findByRole('dialog', { name: '3 things to check before publishing' });
    const rows = within(dialog).getAllByRole('listitem');
    expect(rows).toHaveLength(3);
    expect(within(rows[0]!).getByText(/fill-in text/)).toBeInTheDocument();
    expect(within(rows[0]!).getByRole('link', { name: 'Edit details, description' })).toHaveAttribute('href', `/jobs/${jobId}/edit?focus=description`);
    expect(within(rows[1]!).getByRole('link', { name: 'Edit details, city' })).toHaveAttribute('href', `/jobs/${jobId}/edit?focus=city`);
    expect(within(rows[2]!).getByRole('link', { name: 'Edit details, salary' })).toHaveAttribute('href', `/jobs/${jobId}/edit?focus=salary`);
    expect(within(rows[1]!).getByRole('link', { name: 'Edit details, city' })).toHaveTextContent('Edit details');
  });

  it('says "1 thing" for one problem', async () => {
    renderDialog([items[1]!]);

    expect(await screen.findByRole('dialog', { name: '1 thing to check before publishing' })).toBeInTheDocument();
  });

  it('has the way out to the first problem and Publish anyway as its one primary button', async () => {
    renderDialog();

    const dialog = await screen.findByRole('dialog');
    const footer = within(dialog).getByRole('link', { name: 'Edit details' });
    expect(footer).toHaveAttribute('href', `/jobs/${jobId}/edit?focus=description`);
    expect(within(dialog).getByRole('button', { name: 'Publish anyway' })).toBeEnabled();
  });

  it('closes and hands the publish to the page on Publish anyway', async () => {
    const { onpublish } = renderDialog();

    const dialog = await screen.findByRole('dialog');
    await fireEvent.click(within(dialog).getByRole('button', { name: 'Publish anyway' }));

    expect(onpublish).toHaveBeenCalledTimes(1);
    await vi.waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('keeps Publish anyway off while a publish is under way', async () => {
    renderDialog(items, { busy: true });

    expect(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Publish anyway' })).toBeDisabled();
  });

  it('has no accessibility violations', async () => {
    renderDialog();

    await screen.findByRole('dialog');
    await expectNoAxeViolations(document.body);
  });
});
