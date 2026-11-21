// The starting points of the new hiring template screen: four system templates ScreenHire ships (migrations
// AddStartingPointTemplates and AddHospitalityTemplate), listed in the order the cards show. The ids are SystemTemplates.cs's;
// StartingPointsTests (unit) fails when the two drift apart. A copy starts from the version named here, the newest one;
// the card's stage list reads version 1, which has the same stage names (a new version of a starting point never renames them).

export interface StartingPoint {
  templateId: string;
  versionId: string;
}

export const startingPoints: readonly StartingPoint[] = [
  { templateId: '0199e8a0-0000-7000-8000-000000000003', versionId: '0199e8a0-0000-7000-8000-000000000301' }, // Office role
  { templateId: '0199e8a0-0000-7000-8000-000000000004', versionId: '0199e8a0-0000-7000-8000-000000000403' }, // Trade role with a trial shift, version 3
  { templateId: '0199e8a0-0000-7000-8000-000000000006', versionId: '0199e8a0-0000-7000-8000-000000000602' }, // Hospitality, version 2
  { templateId: '0199e8a0-0000-7000-8000-000000000005', versionId: '0199e8a0-0000-7000-8000-000000000501' }, // High volume
];
