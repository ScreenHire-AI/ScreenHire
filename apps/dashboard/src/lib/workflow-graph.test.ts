import type { HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { toGraph } from './workflow-graph.js';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screening', name: 'Screening', kind: 'Screening' },
    { id: 'interview', name: 'Interview', kind: 'Interview' },
  ],
  questionnaire: { questions: [] },
  integrityPolicy: { aiUse: 'Allowed' },
  automations: [
    {
      id: 'advance',
      name: 'Advance',
      trigger: { type: 'StageEntered', stageId: 'screening' },
      conditions: { match: 'Any', rules: [{ fact: 'application.tags', operator: 'Contains', value: 'referral' }] },
      actions: [
        { type: 'AddTag', tag: 'fast-track' },
        { type: 'MoveToStage', stageId: 'interview' },
      ],
    },
    { id: 'hello', name: 'Hello', trigger: { type: 'ApplicationSubmitted' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'New' }], enabled: false },
  ],
};

describe('toGraph', () => {
  it('lays the stages out in pipeline order, linked one to the next', () => {
    const { nodes, edges } = toGraph(workflow);

    const stages = nodes.filter((node) => node.data.kind === 'stage');
    expect(stages.map((node) => node.data.title)).toEqual(['Applied', 'Screening', 'Interview']);
    expect(stages.map((node) => node.position.y)).toEqual([0, 0, 0]);
    expect(edges.filter((edge) => edge.id.startsWith('order:')).map((edge) => `${edge.source}>${edge.target}`)).toEqual([
      'stage:applied>stage:screening',
      'stage:screening>stage:interview',
    ]);
  });

  it('draws each automation as trigger, conditions and actions, from the stage it fires on to the stage it moves into', () => {
    const { nodes, edges } = toGraph(workflow);

    const advance = nodes.filter((node) => node.id.startsWith('advance:'));
    expect(advance.map((node) => node.data.kind)).toEqual(['trigger', 'conditions', 'action', 'action']);
    expect(advance.map((node) => node.data.path)).toEqual([
      'automations[0].trigger',
      'automations[0].conditions',
      'automations[0].actions[0]',
      'automations[0].actions[1]',
    ]);
    expect(new Set(advance.map((node) => node.position.y)).size).toBe(1);
    expect(advance[1]!.data.title).toBe('Any of 1 condition');
    const links = edges.map((edge) => `${edge.source}>${edge.target}`);
    expect(links).toContain('stage:screening>advance:trigger');
    expect(links).toContain('advance:trigger>advance:conditions');
    expect(links).toContain('advance:conditions>advance:action:0');
    expect(links).toContain('advance:action:0>advance:action:1');
    expect(links).toContain('advance:action:1>stage:interview');
  });

  it('marks a switched-off automation and leaves out conditions it does not have', () => {
    const hello = toGraph(workflow).nodes.filter((node) => node.id.startsWith('hello:'));

    expect(hello.map((node) => node.data.kind)).toEqual(['trigger', 'action']);
    expect(hello.every((node) => node.data.disabled)).toBe(true);
  });

  it('is a projection: the same workflow gives the same graph', () => {
    expect(toGraph(workflow)).toEqual(toGraph(structuredClone(workflow)));
  });
});
