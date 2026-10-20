import { render, screen } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import { stages } from '../../test-support/fixtures.js';
import MoveToStage from './move-to-stage.svelte';

describe('MoveToStage', () => {
  it('is named by its visible words and who it moves', () => {
    render(MoveToStage, { props: { candidateName: 'Robin Ashby', currentStageId: stages[0]!.id, stages, onmove: vi.fn() } });

    expect(screen.getByRole('button', { name: 'Move to stage, Robin Ashby' })).toBeInTheDocument();
  });

  it('has a short name in tables and cards', () => {
    render(MoveToStage, { props: { candidateName: 'Robin Ashby', currentStageId: stages[0]!.id, stages, compact: true, onmove: vi.fn() } });

    expect(screen.getByRole('button', { name: 'Move Robin Ashby to another stage' })).toBeInTheDocument();
  });
});
