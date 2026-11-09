// The starting points of the new hiring template screen: three system templates ScreenHire ships (migration
// AddStartingPointTemplates), listed in the order the cards show. The ids are SystemTemplates.cs's;
// StartingPointsTests (unit) fails when the two drift apart. A copy starts from version 1, the only version.

export interface StartingPoint {
  templateId: string;
  versionId: string;
}

export const startingPoints: readonly StartingPoint[] = [
  { templateId: '0199e8a0-0000-7000-8000-000000000003', versionId: '0199e8a0-0000-7000-8000-000000000301' }, // Office role
  { templateId: '0199e8a0-0000-7000-8000-000000000004', versionId: '0199e8a0-0000-7000-8000-000000000401' }, // Trade role with a trial shift
  { templateId: '0199e8a0-0000-7000-8000-000000000005', versionId: '0199e8a0-0000-7000-8000-000000000501' }, // High volume
];
