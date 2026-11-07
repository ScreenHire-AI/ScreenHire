import { render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { describe, expect, it } from 'vitest';
import HeaderBand from './header-band.svelte';

const actions = createRawSnippet(() => ({ render: () => '<span>Last updated 2 hours ago by Avery Quinn</span>' }));

// jsdom does no layout, so these hold the classes that keep the title's row one row; the E2E test measures the menu.
const titleRow = () => screen.getByRole('heading', { level: 1 }).closest('.min-h-9')!;

describe('header band', () => {
  it('lets the title row wrap its actions by default, as a list page with several actions needs', () => {
    render(HeaderBand, { props: { title: 'Jobs', actions } });

    expect(titleRow()).toHaveClass('flex-wrap');
  });

  it('keeps the title row one row with oneRow, so the pages of a set share a frame (S8), with the actions giving way', () => {
    render(HeaderBand, { props: { title: 'General', oneRow: true, actions } });

    expect(titleRow()).not.toHaveClass('flex-wrap');
    expect(screen.getByText(/Last updated/).parentElement).toHaveClass('min-w-0');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('General');
  });
});
