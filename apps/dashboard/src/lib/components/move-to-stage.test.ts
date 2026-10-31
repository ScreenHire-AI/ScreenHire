import { fireEvent, render, screen } from '@testing-library/svelte';
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

  describe('Keep in mind', () => {
    const open = () => fireEvent.pointerDown(screen.getByRole('button', { name: 'Move to stage, Robin Ashby' }), { button: 0, pointerType: 'mouse' });

    it('offers Keep in mind after the stages only when the page passes onkeep', async () => {
      render(MoveToStage, { props: { candidateName: 'Robin Ashby', currentStageId: stages[0]!.id, stages, onmove: vi.fn(), onkeep: vi.fn() } });
      await open();

      expect((await screen.findAllByRole('menuitem')).map((item) => item.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
        'Applied current',
        'Screening',
        'Interview',
        'Offer',
        'Keep in mind…',
      ]);
    });

    it('calls onkeep, and moves nothing, when chosen', async () => {
      const onkeep = vi.fn();
      const onmove = vi.fn();
      render(MoveToStage, { props: { candidateName: 'Robin Ashby', currentStageId: stages[0]!.id, stages, onmove, onkeep } });
      await open();

      await fireEvent.click(await screen.findByRole('menuitem', { name: 'Keep in mind…' }));

      expect(onkeep).toHaveBeenCalledTimes(1);
      expect(onmove).not.toHaveBeenCalled();
    });

    it('leaves it out when the candidate did not allow it, so the page passes no onkeep', async () => {
      render(MoveToStage, { props: { candidateName: 'Robin Ashby', currentStageId: stages[0]!.id, stages, onmove: vi.fn() } });
      await open();

      await screen.findAllByRole('menuitem');
      expect(screen.queryByRole('menuitem', { name: /Keep in mind/ })).toBeNull();
    });
  });
});
