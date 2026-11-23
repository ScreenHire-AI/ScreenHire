// Move with a note: choosing a rejected stage offers the reason the move keeps for a candidate who asks why, any other
// stage offers none, and the knockout answer (an automation's reason) is never offered to a person.
import type { WorkflowStage } from '@screenhire/api-client';
import { fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import { describe, expect, it, vi } from 'vitest';
import MoveNoteSheet from './move-note-sheet.svelte';

const stages: WorkflowStage[] = [
  { id: 'applied', name: 'Applied', kind: 'Applied' },
  { id: 'screening', name: 'Screening', kind: 'Screening' },
  { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
];

describe('move note sheet', () => {
  it('asks for a reason only when the stage rejects, and moves with the one chosen', async () => {
    const onmove = vi.fn(async () => true);
    render(MoveNoteSheet, { props: { open: true, candidateName: 'Robin Ellis', currentStageId: 'applied', stages, failure: '', onmove } });

    const stage = await screen.findByLabelText('Stage');
    expect(screen.queryByLabelText('Reason')).toBeNull();
    await fireEvent.change(stage, { target: { value: 'rejected' } });
    const reason = await screen.findByLabelText('Reason');
    const offered = [...(reason as HTMLSelectElement).options].map((option) => option.value);
    expect(offered).toEqual(['', 'RequirementNotMet', 'AnotherCandidate', 'PositionClosed', 'NoResponse', 'Other']);
    await fireEvent.change(reason, { target: { value: 'PositionClosed' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Move' }));

    await waitFor(() => expect(onmove).toHaveBeenCalledWith(stages[2], '', 'PositionClosed'));
  });
});
