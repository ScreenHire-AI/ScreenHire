import { fireEvent, render, screen, within } from '@testing-library/svelte';
import { describe, expect, it } from 'vitest';
import { expectNoAxeViolations } from '../../test-support/axe.js';
import WithTooltips from '../../test-support/with-tooltips.svelte';
import CountButton, { type CountItem } from './count-button.svelte';

const jobs = (count: number): CountItem[] =>
  Array.from({ length: count }, (_, index) => ({ id: `j${index}`, label: index === 3 ? 'Support engineer' : `Job ${index + 1}`, href: `/jobs/j${index}` }));

const renderButton = (items: CountItem[]) =>
  render(WithTooltips, { props: { component: CountButton, props: { items, one: 'job', other: 'jobs', title: 'Used by', description: 'Technical interview' } } });

describe('count button', () => {
  it('says how many, and opens a sheet listing each item as a link', async () => {
    renderButton(jobs(2));

    await fireEvent.click(screen.getByRole('button', { name: '2 jobs' }));

    const sheet = await screen.findByRole('dialog', { name: 'Used by' });
    expect(within(sheet).getByText('Technical interview')).toBeInTheDocument();
    expect(within(sheet).getByRole('link', { name: 'Job 1' })).toHaveAttribute('href', '/jobs/j0');
    expect(within(sheet).getAllByRole('link')).toHaveLength(2);
    await expectNoAxeViolations(document.body);
  });

  it('uses the singular for one item, and plain text for none', () => {
    const first = renderButton(jobs(1));
    expect(screen.getByRole('button', { name: '1 job' })).toBeInTheDocument();
    first.unmount();

    renderButton([]);
    expect(screen.getByText('0 jobs')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('says what it is given for none, to match a list column', () => {
    render(WithTooltips, {
      props: { component: CountButton, props: { items: [], one: 'job', other: 'jobs', title: 'Used by', description: 'Technical interview', empty: 'None' } },
    });

    expect(screen.getByText('None')).toBeInTheDocument();
    expect(screen.queryByText('0 jobs')).not.toBeInTheDocument();
  });

  it('has no search for 10 items', async () => {
    renderButton(jobs(10));

    await fireEvent.click(screen.getByRole('button', { name: '10 jobs' }));

    const sheet = await screen.findByRole('dialog', { name: 'Used by' });
    expect(within(sheet).queryByRole('searchbox')).not.toBeInTheDocument();
    expect(within(sheet).getAllByRole('link')).toHaveLength(10);
  });

  it('searches past 10 items, by every word, and says when nothing matches', async () => {
    renderButton(jobs(11));

    await fireEvent.click(screen.getByRole('button', { name: '11 jobs' }));
    const sheet = await screen.findByRole('dialog', { name: 'Used by' });
    const search = within(sheet).getByRole('searchbox', { name: 'Search jobs' });

    await fireEvent.input(search, { target: { value: 'SUPPORT eng' } });
    expect(within(sheet).getAllByRole('link').map((link) => link.textContent)).toEqual(['Support engineer']);

    await fireEvent.input(search, { target: { value: 'designer' } });
    expect(within(sheet).queryByRole('link')).not.toBeInTheDocument();
    expect(within(sheet).getByRole('status')).toHaveTextContent('No matches');
  });

  it('closes the sheet when a link is followed', async () => {
    renderButton(jobs(2));

    await fireEvent.click(screen.getByRole('button', { name: '2 jobs' }));
    const sheet = await screen.findByRole('dialog', { name: 'Used by' });
    const link = within(sheet).getByRole('link', { name: 'Job 2' });
    link.addEventListener('click', (event) => event.preventDefault());
    await fireEvent.click(link);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
