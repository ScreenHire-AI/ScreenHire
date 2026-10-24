import type { HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { LANE_NODE, addAutomation, canConnect, connect, neighbour, nodeHeight, removeAutomation, toGraph } from './workflow-graph.js';

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

const links = (graph: ReturnType<typeof toGraph>) => graph.edges.map((edge) => `${edge.source}>${edge.target}`);

describe('toGraph', () => {
  it('lays the whole-workflow lane and the stages out along the top, the stages linked in pipeline order', () => {
    const { nodes, edges } = toGraph(workflow);

    const top = nodes.filter((node) => node.data.row === 0);
    expect(top.map((node) => node.data.title)).toEqual(['Whole workflow', 'Applied', 'Screening', 'Interview']);
    expect(top.map((node) => node.data.kind)).toEqual(['lane', 'stage', 'stage', 'stage']);
    expect(top.map((node) => node.position.y)).toEqual([0, 0, 0, 0]);
    expect(edges.filter((edge) => edge.id.startsWith('order:')).map((edge) => `${edge.source}>${edge.target}`)).toEqual([
      'stage:applied>stage:screening',
      'stage:screening>stage:interview',
    ]);
  });

  it('draws each automation as trigger, conditions and actions, from the stage it fires on to the stage it moves into', () => {
    const graph = toGraph(workflow);

    const advance = graph.nodes.filter((node) => node.id.startsWith('advance:'));
    expect(advance.map((node) => node.data.kind)).toEqual(['trigger', 'conditions', 'action', 'action']);
    expect(advance.map((node) => node.data.path)).toEqual([
      'automations[0].trigger',
      'automations[0].conditions',
      'automations[0].actions[0]',
      'automations[0].actions[1]',
    ]);
    expect(new Set(advance.map((node) => node.position.y)).size).toBe(1);
    expect(advance[1]!.data.title).toBe('Any of 1 condition');
    expect(links(graph)).toEqual(
      expect.arrayContaining([
        'stage:screening>advance:trigger',
        'advance:trigger>advance:conditions',
        'advance:conditions>advance:action:0',
        'advance:action:0>advance:action:1',
        'advance:action:1>stage:interview',
      ]),
    );
  });

  it('marks a switched-off automation and leaves out conditions it does not have', () => {
    const hello = toGraph(workflow).nodes.filter((node) => node.id.startsWith('hello:'));

    expect(hello.map((node) => node.data.kind)).toEqual(['trigger', 'action']);
    expect(hello.every((node) => node.data.disabled)).toBe(true);
    expect(hello[0]!.ariaLabel).toBe('When, off: Hello');
  });

  it('links an automation that fires on no one stage to the whole-workflow lane', () => {
    expect(links(toGraph(workflow))).toContain(`${LANE_NODE}>hello:trigger`);
    expect(links(toGraph(workflow))).not.toContain(`${LANE_NODE}>advance:trigger`);
  });

  it("puts each validator message on the node it is about, and an automation's own on its trigger", () => {
    const errors = [
      { path: 'automations[0].name', message: 'Give the automation a name.' },
      { path: 'automations[0].conditions.rules[0].value', message: 'Give the condition a value.' },
      { path: 'automations[0].actions[1].stageId', message: 'Pick a stage.' },
      { path: 'stages[2].name', message: 'Give the stage a name.' },
    ];
    const { nodes } = toGraph(workflow, { errors });
    const problems = Object.fromEntries(nodes.map((node) => [node.id, node.data.problems]));

    expect(problems['advance:trigger']).toEqual(['Give the automation a name.']);
    expect(problems['advance:conditions']).toEqual(['Give the condition a value.']);
    expect(problems['advance:action:0']).toEqual([]);
    expect(problems['advance:action:1']).toEqual(['Pick a stage.']);
    expect(problems['stage:interview']).toEqual(['Give the stage a name.']);
    expect(problems[LANE_NODE]).toEqual([]);
    expect(nodes.find((node) => node.id === 'advance:action:1')!.ariaLabel).toBe('Then, action 2: Move to Interview, Advance, 1 problem to fix');
  });

  it('spaces the rows by the tallest a node in them can be, so no two nodes overlap', () => {
    const errors = [{ path: 'automations[0].actions[0].tag', message: 'Give the tag a name.' }];
    for (const editable of [false, true]) {
      const { nodes } = toGraph(workflow, { errors, editable });
      for (const row of [1, 2]) {
        const above = nodes.filter((node) => node.data.row === row - 1);
        const bottom = Math.max(...above.map((node) => node.position.y + nodeHeight(node.data, editable)));
        expect(nodes.find((node) => node.data.row === row)!.position.y).toBeGreaterThan(bottom);
      }
    }
  });

  it('allows for 44px buttons on touch', () => {
    const data = { detail: 'A', problems: ['B'] };

    expect(nodeHeight(data, true, true) - nodeHeight(data, true, false)).toBe(20);
    expect(nodeHeight(data, false, true)).toBe(nodeHeight(data, false, false));
  });

  it('is a projection: the same workflow gives the same graph', () => {
    expect(toGraph(workflow)).toEqual(toGraph(structuredClone(workflow)));
  });
});

describe('the projection round-trips', () => {
  // The part of the workflow at a node's path: `automations[0].actions[1]` is that action, and a trigger node's path is
  // its automation's trigger.
  const at = (path: string): unknown =>
    path
      .split(/[.[\]]+/)
      .filter(Boolean)
      .reduce<unknown>((value, key) => (value as Record<string, unknown> | undefined)?.[key], workflow);

  it("resolves every node's path to the part of the workflow it draws, and draws every part", () => {
    const { nodes } = toGraph(workflow);

    for (const node of nodes.filter((candidate) => candidate.data.kind !== 'lane')) expect(at(node.data.path)).toBeDefined();
    expect(nodes.filter((node) => node.data.kind === 'stage').map((node) => at(node.data.path))).toEqual(workflow.stages);
    expect(nodes.filter((node) => node.data.kind === 'trigger').map((node) => at(node.data.path))).toEqual(workflow.automations.map((automation) => automation.trigger));
    expect(nodes.filter((node) => node.data.kind === 'action').map((node) => at(node.data.path))).toEqual(workflow.automations.flatMap((automation) => automation.actions));
  });

  it('connecting a "moves to" link the graph already draws gives back the same workflow', () => {
    const moves = toGraph(workflow).edges.filter((edge) => edge.id.endsWith(':moves'));

    expect(moves).toHaveLength(1);
    for (const edge of moves) expect(connect(workflow, edge.source, edge.target)).toEqual(workflow);
  });

  it('pointing a move elsewhere, then connecting the link it had, gives back the original', () => {
    const moved = connect(workflow, 'advance:action:1', 'stage:applied');

    expect(moved.automations[0]!.actions[1]!.stageId).toBe('applied');
    expect(links(toGraph(moved))).toContain('advance:action:1>stage:applied');
    expect(links(toGraph(moved))).not.toContain('advance:action:1>stage:interview');
    expect(connect(moved, 'advance:action:1', 'stage:interview')).toEqual(workflow);
  });
});

describe('graph edits', () => {
  it('adds an automation to a stage, on entering it, and the projection shows it fire on that stage', () => {
    const { workflow: added, id } = addAutomation(workflow, 'interview');

    expect(added.automations).toHaveLength(3);
    expect(added.automations[2]).toMatchObject({ id, trigger: { type: 'StageEntered', stageId: 'interview' } });
    expect(workflow.automations).toHaveLength(2);
    expect(links(toGraph(added))).toContain(`stage:interview>${id}:trigger`);
  });

  it('adds an automation to the whole workflow, on an arriving application', () => {
    const { workflow: added, id } = addAutomation(workflow, null);

    expect(added.automations.at(-1)!.trigger).toEqual({ type: 'ApplicationSubmitted' });
    expect(links(toGraph(added))).toContain(`${LANE_NODE}>${id}:trigger`);
  });

  it('removes an automation and every node it had', () => {
    const removed = removeAutomation(workflow, 'advance');

    expect(removed.automations.map((automation) => automation.id)).toEqual(['hello']);
    expect(toGraph(removed).nodes.some((node) => node.id.startsWith('advance:'))).toBe(false);
    expect(removeAutomation(workflow, 'missing')).toBe(workflow);
  });

  it('links only a "move to a stage" action to a stage, and leaves the workflow alone for anything else', () => {
    expect(canConnect(workflow, 'advance:action:1', 'stage:screening')).toBe(true);
    expect(canConnect(workflow, 'advance:action:0', 'stage:screening')).toBe(false);
    expect(canConnect(workflow, 'advance:action:1', 'advance:trigger')).toBe(false);
    expect(canConnect(workflow, 'stage:applied', 'stage:screening')).toBe(false);
    expect(connect(workflow, 'advance:action:0', 'stage:screening')).toBe(workflow);
  });
});

describe('moving between nodes by key', () => {
  const { nodes } = toGraph(workflow);
  const from = (id: string, key: string) => neighbour(nodes, id, key);

  it('steps through the nodes in reading order with Left and Right, row after row', () => {
    expect(from(LANE_NODE, 'ArrowRight')).toBe('stage:applied');
    expect(from('stage:interview', 'ArrowRight')).toBe('advance:trigger');
    expect(from('advance:trigger', 'ArrowLeft')).toBe('stage:interview');
    expect(from(LANE_NODE, 'ArrowLeft')).toBeNull();
  });

  it('goes to the nearest node in the row below or above with Down and Up', () => {
    expect(from('stage:applied', 'ArrowDown')).toBe('advance:conditions');
    expect(from('advance:action:1', 'ArrowDown')).toBe('hello:action:0');
    expect(from('hello:trigger', 'ArrowUp')).toBe('advance:trigger');
    expect(from(LANE_NODE, 'ArrowUp')).toBeNull();
  });

  it('goes to the first and last node with Home and End, and ignores other keys', () => {
    expect(from('advance:action:0', 'Home')).toBe(LANE_NODE);
    expect(from(LANE_NODE, 'End')).toBe('hello:action:0');
    expect(from(LANE_NODE, 'Enter')).toBeNull();
  });
});
