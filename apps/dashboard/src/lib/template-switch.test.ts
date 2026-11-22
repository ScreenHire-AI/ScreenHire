import type { JobTemplateSwitchPreview } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { candidateCount, moveLines, stageMappings, suggestedChoices } from './template-switch.js';

const preview: JobTemplateSwitchPreview = {
  templateId: 't1',
  templateName: 'Kitchen',
  templateVersionId: 'v1',
  version: 2,
  expectedWorkflowVersion: 4,
  stages: [
    { id: 'new', name: 'New', kind: 'Applied' },
    { id: 'call', name: 'Phone call', kind: 'Screening' },
    { id: 'hired', name: 'Hired', kind: 'Hired' },
  ],
  occupied: [
    { stageId: 'screening', name: 'Screening', kind: 'Screening', candidates: 3, suggestedStageId: 'call' },
    { stageId: 'hired', name: 'Hired', kind: 'Hired', candidates: 1, suggestedStageId: 'hired' },
  ],
};

describe('switching a job to another template', () => {
  it('starts from the suggested stages and sends what the person chose', () => {
    const choices = suggestedChoices(preview);
    expect(choices).toEqual({ screening: 'call', hired: 'hired' });

    expect(stageMappings(preview, { ...choices, screening: 'new' })).toEqual([
      { fromStageId: 'screening', toStageId: 'new' },
      { fromStageId: 'hired', toStageId: 'hired' },
    ]);
  });

  it('says where each stage of candidates goes, and when they stay in a stage of the same name', () => {
    expect(moveLines(preview, suggestedChoices(preview))).toEqual(['3 candidates in Screening move to Phone call', '1 candidate in Hired stays in Hired']);
    expect(candidateCount(preview)).toBe(4);
  });
});
