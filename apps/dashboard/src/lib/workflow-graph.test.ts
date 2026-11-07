import type { Automation, HiringWorkflow } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import {
  LANE_NODE,
  NODE_WIDTH,
  PLUS,
  VIEW_INSET,
  addAction,
  addAutomation,
  addCondition,
  arrowhead,
  canConnect,
  connect,
  firstView,
  neighbour,
  nodeHeight,
  pathOf,
  routePath,
  testedPath,
  toGraph,
  type Graph,
  type StepNode,
} from './workflow-graph.js';

const workflow: HiringWorkflow = {
  stages: [
    { id: 'applied', name: 'Applied', kind: 'Applied' },
    { id: 'screening', name: 'Screening', kind: 'Screening' },
    { id: 'interview', name: 'Interview', kind: 'Interview' },
  ],
  questionnaire: { questions: [] },
  verificationPolicy: { aiUse: 'Allowed' },
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

/** A workflow of any size: every third automation has a condition, every other one moves the application on. */
function large(stageCount: number, automationCount: number): HiringWorkflow {
  const stages = Array.from({ length: stageCount }, (_, i) => ({ id: `s${i}`, name: `Stage ${i}`, kind: 'Custom' as const }));
  const automations = Array.from({ length: automationCount }, (_, i): Automation => {
    const stage = i % (stageCount + 1) === stageCount ? null : `s${i % stageCount}`;
    return {
      id: `a${i}`,
      name: `Automation ${i}`,
      trigger: stage ? { type: 'StageEntered', stageId: stage } : { type: 'ApplicationSubmitted' },
      conditions: i % 3 === 0 ? { match: 'All', rules: [{ fact: 'application.tags', operator: 'Contains', value: 'x' }] } : null,
      actions: [{ type: 'AddTag', tag: `t${i}` }, ...(i % 2 === 0 ? [{ type: 'MoveToStage' as const, stageId: `s${(i * 7 + 3) % stageCount}` }] : [])],
    };
  });
  return { ...workflow, stages, automations };
}

const links = (graph: Graph) => graph.edges.map((edge) => `${edge.source}>${edge.target}`);
const node = (graph: Graph, id: string) => graph.nodes.find((candidate) => candidate.id === id)!;
const pane = (width: number) => ({ width });
const rect = (candidate: StepNode) => ({
  left: candidate.position.x,
  top: candidate.position.y,
  right: candidate.position.x + candidate.width!,
  bottom: candidate.position.y + candidate.height!,
});
const lastSegment = (points: { x: number; y: number }[]) => {
  const to = points.at(-1)!;
  const from = points.at(-2)!;
  return { dx: Math.sign(to.x - from.x), dy: Math.sign(to.y - from.y) };
};

/** Every combination the layout draws, so the geometry rules hold for each. */
const variants = (source: HiringWorkflow) =>
  [
    { editable: false },
    { editable: true },
  ].map((options) => ({ ...options, graph: toGraph(source, options) }));

function expectGeometry(graph: Graph) {
  // No two nodes overlap.
  for (const [i, a] of graph.nodes.entries()) {
    for (const b of graph.nodes.slice(i + 1)) {
      const [r, s] = [rect(a), rect(b)];
      const overlap = r.left < s.right && s.left < r.right && r.top < s.bottom && s.top < r.bottom;
      expect(overlap, `${a.id} overlaps ${b.id}`).toBe(false);
    }
  }
  // A line runs only horizontally and vertically, and never through a node. A plus sits on its line on purpose.
  const solid = graph.nodes.filter((candidate) => !candidate.data.kind.startsWith('add-')).map((candidate) => ({ id: candidate.id, ...rect(candidate) }));
  for (const edge of graph.edges) {
    const points = edge.data!.points;
    for (let i = 1; i < points.length; i++) {
      const [from, to] = [points[i - 1]!, points[i]!];
      expect(from.x === to.x || from.y === to.y, `${edge.id} is not orthogonal`).toBe(true);
      const [left, right] = [Math.min(from.x, to.x), Math.max(from.x, to.x)];
      const [top, bottom] = [Math.min(from.y, to.y), Math.max(from.y, to.y)];
      for (const box of solid) {
        const through = left < box.right && right > box.left && top < box.bottom && bottom > box.top;
        expect(through, `${edge.id} runs through ${box.id}`).toBe(false);
      }
    }
  }
}

describe('toGraph layout', () => {
  it('lays the whole-workflow lane and then the stages out left to right on one row', () => {
    const { nodes } = toGraph(workflow);

    const headers = nodes.filter((candidate) => candidate.data.kind === 'lane' || candidate.data.kind === 'stage');
    expect(headers.map((candidate) => candidate.data.title)).toEqual(['Whole workflow', 'Applied', 'Screening', 'Interview']);
    expect(new Set(headers.map((candidate) => candidate.position.y)).size).toBe(1);
    expect(new Set(headers.map((candidate) => candidate.height)).size).toBe(1);
    const xs = headers.map((candidate) => candidate.position.x);
    expect(xs).toEqual([...xs].sort((a, b) => a - b));
    expect(new Set(xs).size).toBe(xs.length);
  });

  it('links the stages in pipeline order with a line to the right, and the lane to no stage', () => {
    const graph = toGraph(workflow);

    const order = graph.edges.filter((edge) => edge.data!.kind === 'order');
    expect(order.map((edge) => `${edge.source}>${edge.target}`)).toEqual(['stage:applied>stage:screening', 'stage:screening>stage:interview']);
    for (const edge of order) expect(lastSegment(edge.data!.points)).toEqual({ dx: 1, dy: 0 });
    expect(graph.edges.some((edge) => edge.source === LANE_NODE && edge.data!.kind === 'order')).toBe(false);
  });

  describe('end stages', () => {
    const ended: HiringWorkflow = {
      ...workflow,
      stages: [
        { id: 'applied', name: 'Applied', kind: 'Applied' },
        { id: 'rejected', name: 'Rejected', kind: 'Rejected' },
        { id: 'offer', name: 'Offer', kind: 'Offer' },
        { id: 'hired', name: 'Hired', kind: 'Hired' },
        { id: 'withdrawn', name: 'Withdrawn', kind: 'Withdrawn' },
      ],
      automations: [{ id: 'note', name: 'Note', trigger: { type: 'StageEntered', stageId: 'rejected' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'Ended' }] }],
    };

    it('get no line of the backbone: an application can finish at any of them from any stage', () => {
      const order = toGraph(ended).edges.filter((edge) => edge.data!.kind === 'order');

      expect(order.map((edge) => `${edge.source}>${edge.target}`)).toEqual(['stage:applied>stage:offer']);
    });

    it('stand after the open stages, set apart, and say what they are', () => {
      const graph = toGraph(ended);
      const x = (id: string) => node(graph, `stage:${id}`).position.x;

      expect(['applied', 'offer', 'rejected', 'hired', 'withdrawn'].map(x)).toEqual(['applied', 'offer', 'rejected', 'hired', 'withdrawn'].map(x).toSorted((a, b) => a - b));
      expect(x('rejected') - x('offer')).toBeGreaterThan(x('offer') - x('applied'));
      expect(node(graph, 'stage:hired').data).toMatchObject({ endStage: true, label: 'End stage: Hired' });
      expect(node(graph, 'stage:offer').data).toMatchObject({ endStage: false, label: 'Stage 3: Offer' });
    });

    it('keep their automations, and their place in the definition', () => {
      const graph = toGraph(ended);

      expect(node(graph, 'stage:rejected').data.path).toBe('stages[1]');
      expect(node(graph, 'note:trigger').position.x).toBe(node(graph, 'stage:rejected').position.x);
    });
  });

  it("hangs each automation under the stage it fires on, as a column read down When, If, Then", () => {
    const graph = toGraph(workflow);

    const advance = graph.nodes.filter((candidate) => candidate.id.startsWith('advance:') && candidate.data.kind !== 'add-action');
    expect(advance.map((candidate) => candidate.data.kind)).toEqual(['trigger', 'conditions', 'action', 'action']);
    expect(advance.map((candidate) => candidate.data.path)).toEqual([
      'automations[0].trigger',
      'automations[0].conditions',
      'automations[0].actions[0]',
      'automations[0].actions[1]',
    ]);
    expect(new Set(advance.map((candidate) => candidate.position.x)).size).toBe(1);
    const ys = advance.map((candidate) => candidate.position.y);
    expect(ys).toEqual([...ys].sort((a, b) => a - b));
    expect(advance[0]!.position.x).toBe(node(graph, 'stage:screening').position.x);
    expect(node(graph, 'hello:trigger').position.x).toBe(node(graph, LANE_NODE).position.x);
    expect(advance[0]!.position.y).toBeGreaterThan(node(graph, 'stage:screening').position.y + node(graph, 'stage:screening').height!);
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

  it('gives every line a direction: an arrowhead, down for a flow along an automation and into a stage from above', () => {
    const graph = toGraph(workflow, { editable: true });

    for (const edge of graph.edges) {
      const { kind, arrow } = edge.data!;
      expect(arrow, edge.id).toBe(kind !== 'stub');
      if (kind === 'fires' || kind === 'chain' || kind === 'moves') expect(lastSegment(edge.data!.points), edge.id).toEqual({ dx: 0, dy: 1 });
    }
  });

  it('routes a move of its own: out of the action, up beside its column, across above the stages and down into the stage', () => {
    const graph = toGraph(workflow);

    const move = graph.edges.find((edge) => edge.id === 'advance:action:1:moves')!;
    expect(move.data!.kind).toBe('moves');
    expect(move.data!.label).toBe('Moves to');
    const action = rect(node(graph, 'advance:action:1'));
    const stage = rect(node(graph, 'stage:interview'));
    const points = move.data!.points;
    expect(points[0]).toEqual({ x: action.right, y: (action.top + action.bottom) / 2 });
    expect(points.at(-1)).toEqual({ x: expect.any(Number), y: stage.top });
    expect(points.at(-1)!.x).toBeGreaterThan(stage.left);
    expect(points.at(-1)!.x).toBeLessThan(stage.right);
    expect(Math.min(...points.map((point) => point.y))).toBeLessThan(stage.top);
    expect(links(graph)).toContain('advance:action:1>stage:interview');
  });

  it('puts words on a line only where they add meaning: a move says "Moves to", nothing says "fires"', () => {
    const { edges } = toGraph(workflow, { editable: true });

    expect(edges.filter((edge) => edge.data!.label).map((edge) => edge.data!.label)).toEqual(['Moves to']);
    expect(edges.some((edge) => 'label' in edge)).toBe(false);
  });

  it('shares a lane above the stages between moves that do not overlap, and offsets those that share a channel', () => {
    const graph = toGraph(large(8, 30));

    const moves = graph.edges.filter((edge) => edge.data!.kind === 'moves');
    const lanes = new Set(moves.map((edge) => edge.data!.points[2]!.y));
    expect(moves.length).toBeGreaterThan(8);
    expect(lanes.size).toBeLessThan(moves.length);
    expectGeometry(graph);
  });

  it('runs a move nested inside another nearer the stages, so the two lines do not cross', () => {
    const stages = Array.from({ length: 6 }, (_, i) => ({ id: `n${i}`, name: `Stage ${i}`, kind: 'Screening' as const }));
    const move = (id: string, from: number, to: number): Automation => ({
      id,
      name: id,
      trigger: { type: 'StageEntered', stageId: `n${from}` },
      conditions: null,
      actions: [{ type: 'MoveToStage', stageId: `n${to}` }],
    });
    // The first runs forward over the whole pipeline; the second runs backward inside that span.
    const graph = toGraph({ ...workflow, stages, automations: [move('wide', 0, 5), move('inner', 3, 1)] });

    const line = (id: string) => graph.edges.find((edge) => edge.id === id)!.data!.points;
    const wide = line('wide:action:0:moves');
    const inner = line('inner:action:0:moves');
    // Each move runs along its third and fourth points, and rises and drops through the rest.
    const lane = (points: typeof wide) => ({ y: points[2]!.y, left: Math.min(points[2]!.x, points[3]!.x), right: Math.max(points[2]!.x, points[3]!.x) });
    const crosses = (points: typeof wide, other: ReturnType<typeof lane>) => {
      const between = (x: number) => x > other.left && x < other.right;
      return (between(points[2]!.x) && points[2]!.y < other.y) || (between(points[3]!.x) && points[3]!.y < other.y);
    };
    expect(crosses(wide, lane(inner))).toBe(false);
    expect(crosses(inner, lane(wide))).toBe(false);
    expectGeometry(graph);
  });

  it('keeps every node apart and every line off every node, however it is drawn', () => {
    for (const { graph } of variants(workflow)) expectGeometry(graph);
    for (const { graph } of variants({ ...workflow, automations: [...workflow.automations, ...large(3, 4).automations.map((a) => ({ ...a, id: `x-${a.id}` }))] })) expectGeometry(graph);
  });

  it('stays readable at 20 stages and 50 automations: nothing overlaps and the columns keep their order', () => {
    const big = large(20, 50);
    for (const { graph } of variants(big)) {
      expectGeometry(graph);
      const stages = graph.nodes.filter((candidate) => candidate.data.kind === 'stage');
      expect(stages).toHaveLength(20);
      const xs = stages.map((candidate) => candidate.position.x);
      expect(xs).toEqual([...xs].sort((a, b) => a - b));
      expect(graph.bounds.width).toBeGreaterThan(xs.at(-1)!);
    }
  });

  it('marks a switched-off automation and leaves out conditions it does not have', () => {
    const hello = toGraph(workflow).nodes.filter((candidate) => candidate.id.startsWith('hello:'));

    expect(hello.map((candidate) => candidate.data.kind)).toEqual(['trigger', 'action']);
    expect(hello.every((candidate) => candidate.data.disabled)).toBe(true);
    expect(hello[0]!.data.label).toBe('When, off: Hello');
  });

  it("puts each validator message on the node it is about, and an automation's own on its trigger", () => {
    const errors = [
      { path: 'automations[0].name', message: 'Give the automation a name.' },
      { path: 'automations[0].conditions.rules[0].value', message: 'Give the condition a value.' },
      { path: 'automations[0].actions[1].stageId', message: 'Pick a stage.' },
      { path: 'stages[2].name', message: 'Give the stage a name.' },
    ];
    const { nodes } = toGraph(workflow, { errors });
    const problems = Object.fromEntries(nodes.map((candidate) => [candidate.id, candidate.data.problems]));

    expect(problems['advance:trigger']).toEqual(['Give the automation a name.']);
    expect(problems['advance:conditions']).toEqual(['Give the condition a value.']);
    expect(problems['advance:action:0']).toEqual([]);
    expect(problems['advance:action:1']).toEqual(['Pick a stage.']);
    expect(problems['stage:interview']).toEqual(['Give the stage a name.']);
    expect(problems[LANE_NODE]).toEqual([]);
    expect(nodes.find((candidate) => candidate.id === 'advance:action:1')!.data.label).toBe('Then, action 2: Move to Interview, Advance, 1 problem to fix');
  });

  it('gives a node with a problem the room for it, so the nodes below it still clear it', () => {
    const errors = [
      { path: 'automations[0].actions[0].tag', message: 'Give the tag a name.' },
      { path: 'stages[1].name', message: 'Give the stage a name.' },
    ];
    const graph = toGraph(workflow, { errors, editable: true });

    expect(node(graph, 'advance:action:0').height).toBe(nodeHeight({ problems: ['x'] }));
    expect(nodeHeight({ problems: ['x'] })).toBeGreaterThan(nodeHeight({ problems: [] }));
    expect(node(graph, 'stage:applied').height).toBe(node(graph, 'stage:screening').height);
    expectGeometry(graph);
  });

  it("draws the time-limit and questionnaire automations: from a stage's limit or the lane, and naming the questionnaire", () => {
    const followUps: HiringWorkflow = {
      ...workflow,
      followUpQuestionnaires: [{ id: 'availability', name: 'Availability', questions: [{ id: 'start', type: 'ShortText', label: 'When could you start?' }] }],
      automations: [
        { id: 'late', name: 'Late', trigger: { type: 'SlaBreached', stageId: 'screening' }, conditions: null, actions: [{ type: 'NotifyTeam', note: 'Overdue' }] },
        { id: 'any-late', name: 'Any late', trigger: { type: 'SlaBreached' }, conditions: null, actions: [{ type: 'CreateTask', note: 'Chase' }] },
        { id: 'ask', name: 'Ask', trigger: { type: 'StageEntered', stageId: 'screening' }, conditions: null, actions: [{ type: 'SendQuestionnaire', questionnaireId: 'availability' }] },
        { id: 'answered', name: 'Answered', trigger: { type: 'QuestionnaireCompleted', questionnaireId: 'availability' }, conditions: null, actions: [{ type: 'AddTag', tag: 'answered' }] },
      ],
    };
    const graph = toGraph(followUps);

    expect(links(graph)).toEqual(expect.arrayContaining(['stage:screening>late:trigger', `${LANE_NODE}>any-late:trigger`, `${LANE_NODE}>answered:trigger`]));
    expect(node(graph, 'late:trigger').data.detail).toBe("A stage's time limit passes");
    expect(node(graph, 'ask:action:0').data.title).toBe('Send the "Availability" questionnaire');
    expect(node(graph, 'answered:trigger').data.detail).toBe('A follow-up questionnaire is answered: Availability');
    expectGeometry(graph);
  });

  it('is a projection: the same workflow gives the same graph', () => {
    expect(toGraph(workflow)).toEqual(toGraph(structuredClone(workflow)));
  });
});

describe('the plus buttons', () => {
  const plusIds = (graph: Graph) => graph.nodes.filter((candidate) => candidate.data.kind.startsWith('add-')).map((candidate) => candidate.id);

  it('are drawn only where the graph can be edited, and only once there is an automation', () => {
    expect(plusIds(toGraph(workflow))).toEqual([]);
    expect(plusIds(toGraph({ ...workflow, automations: [] }, { editable: true }))).toEqual([]);
    expect(plusIds(toGraph(workflow, { editable: true })).length).toBeGreaterThan(0);
  });

  it('put a new automation under each lane and stage, a condition where an automation has none, an action at its end', () => {
    const graph = toGraph(workflow, { editable: true });

    expect(plusIds(graph).sort()).toEqual(
      ['add:lane', 'add:applied', 'add:screening', 'add:interview', 'advance:add-action', 'hello:add-action', 'hello:add-condition'].sort(),
    );
    const plus = PLUS;
    const under = node(graph, 'add:screening');
    expect(under.width).toBe(plus);
    expect(under.position.x + plus / 2).toBe(node(graph, 'stage:screening').position.x + NODE_WIDTH / 2);
    expect(under.position.y).toBeGreaterThan(node(graph, 'stage:screening').position.y + node(graph, 'stage:screening').height!);
    expect(node(graph, 'add:lane').data.lane).toBeNull();
    expect(node(graph, 'add:screening').data.lane).toBe('screening');
    expect(node(graph, 'hello:add-condition').data.label).toBe('Add a condition to Hello');
    expect(node(graph, 'advance:add-action').data.label).toBe('Add an action to Advance');
    expect(node(graph, 'add:lane').data.label).toBe('Add an automation to the whole workflow');
  });

  it('sit on the line they add to, between the trigger and the first action', () => {
    const graph = toGraph(workflow, { editable: true });

    const trigger = rect(node(graph, 'hello:trigger'));
    const action = rect(node(graph, 'hello:action:0'));
    const plus = rect(node(graph, 'hello:add-condition'));
    expect(plus.top).toBeGreaterThan(trigger.bottom);
    expect(plus.bottom).toBeLessThan(action.top);
  });

  it('are a target for a finger at the size the first view shows them', () => {
    expect(firstView({ width: 400 }, pane(1000)).zoom).toBe(1);
    expect(node(toGraph(workflow, { editable: true }), 'add:screening').width).toBe(PLUS);
    expect(PLUS).toBeGreaterThanOrEqual(44);
  });
});

describe('reading a path', () => {
  const graph = toGraph(workflow, { editable: true });

  it("lights an automation's chain, the stage it fires on and the stage it moves into", () => {
    const path = pathOf(graph, 'advance:conditions');

    expect([...path.nodes].sort()).toEqual(
      ['stage:screening', 'advance:trigger', 'advance:conditions', 'advance:action:0', 'advance:action:1', 'advance:add-action', 'stage:interview'].sort(),
    );
    expect(path.edges.has('advance:action:1:moves')).toBe(true);
    expect(path.edges.has('advance:fires-on')).toBe(true);
    expect(path.nodes.has('hello:trigger')).toBe(false);
  });

  it("lights a stage, its automations and the moves into it; a lane, its automations", () => {
    const interview = pathOf(graph, 'stage:interview');
    expect(interview.nodes.has('advance:action:1')).toBe(true);
    expect(interview.edges.has('advance:action:1:moves')).toBe(true);
    expect(interview.nodes.has('hello:trigger')).toBe(false);

    const lane = pathOf(graph, LANE_NODE);
    expect(lane.nodes.has('hello:action:0')).toBe(true);
    expect(lane.nodes.has('advance:trigger')).toBe(false);
  });

  it('lights nothing for a node it does not know', () => {
    expect(pathOf(graph, 'missing').nodes.size).toBe(0);
  });

  it('lights the path a test took: all of a match, up to the conditions of a miss, only the trigger of a skip', () => {
    const matched = testedPath(graph, new Map([['advance', 'Matched' as const]]));
    expect(matched.nodes.has('advance:action:1')).toBe(true);
    expect(matched.nodes.has('stage:interview')).toBe(true);

    const missed = testedPath(graph, new Map([['advance', 'NotMatched' as const]]));
    expect([...missed.nodes].sort()).toEqual(['advance:conditions', 'advance:trigger', 'stage:screening']);
    expect(missed.edges.has('advance:to-conditions')).toBe(true);

    const skipped = testedPath(graph, new Map([['hello', 'Skipped' as const], ['gone', 'Matched' as const]]));
    expect([...skipped.nodes].sort()).toEqual([LANE_NODE, 'hello:trigger'].sort());
  });

  it('never lights a plus or its dashed line for a test: they are places to add a step, not steps it took', () => {
    const matched = testedPath(graph, new Map([['hello', 'Matched' as const]]));

    expect([...matched.nodes].filter((id) => id.includes(':add-'))).toEqual([]);
    expect([...matched.edges].filter((id) => id.includes(':stub-'))).toEqual([]);
    expect(matched.nodes.has('hello:action:0')).toBe(true);
    expect(pathOf(graph, 'hello:trigger').nodes.has('hello:add-action')).toBe(true);
  });
});

describe('lines', () => {
  it('round their corners and stop short of the arrowhead, which points the way the line runs', () => {
    const points = [{ x: 0, y: 0 }, { x: 0, y: 40 }, { x: 40, y: 40 }];

    expect(routePath(points, false, 8)).toBe('M0 0L0 32Q0 40 8 40L40 40');
    expect(routePath(points, true, 8)).toMatch(/^M0 0L0 32Q0 40 8 40L31 40$/);
    expect(arrowhead(points)).toBe('M40 40L31 45L31 35Z');
    expect(arrowhead([{ x: 5, y: 0 }, { x: 5, y: 20 }])).toBe('M5 20L0 11L10 11Z');
  });
});

describe('the first view', () => {
  it('centres a workflow narrower than the pane, at full size', () => {
    const view = firstView({ width: 400 }, pane(1000));

    expect(view.zoom).toBe(1);
    expect(view.x).toBeGreaterThan(VIEW_INSET.left);
    expect(view.y).toBe(VIEW_INSET.top);
  });

  it('starts a workflow wider than the pane at the top left, at full size, because node text would fall under 12px', () => {
    // Seven stages, as the default workflow has, and six automations, at a laptop's width.
    const { bounds } = toGraph(large(7, 6), { editable: true });

    expect(bounds.width).toBeGreaterThan(1100);
    expect(firstView(bounds, pane(1100))).toEqual({ x: VIEW_INSET.left, y: VIEW_INSET.top, zoom: 1 });
  });

  it('starts a phone at full size: text never shrinks there, and the workflow does not fit', () => {
    expect(firstView(toGraph(large(4, 4)).bounds, pane(343)).zoom).toBe(1);
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

  it("adds a condition the way the form's Add condition does, and the projection then draws an If", () => {
    const added = addCondition(workflow, 'hello')!;

    expect(added.path).toBe('automations[1].conditions');
    expect(added.workflow.automations[1]!.conditions).toMatchObject({ match: 'All', rules: [{ fact: expect.any(String) }] });
    expect(workflow.automations[1]!.conditions).toBeNull();
    expect(toGraph(added.workflow).nodes.some((candidate) => candidate.id === 'hello:conditions')).toBe(true);
    expect(addCondition(workflow, 'advance')).toBeNull();
    expect(addCondition(workflow, 'missing')).toBeNull();
  });

  it('adds an action at the end of an automation, and says where it is', () => {
    const added = addAction(workflow, 'advance')!;

    expect(added.path).toBe('automations[0].actions[2]');
    expect(added.workflow.automations[0]!.actions).toHaveLength(3);
    expect(workflow.automations[0]!.actions).toHaveLength(2);
    expect(toGraph(added.workflow).nodes.some((candidate) => candidate.id === 'advance:action:2')).toBe(true);
    expect(addAction(workflow, 'missing')).toBeNull();
  });

  it('links only a "move to a stage" action to a stage, and leaves the workflow alone for anything else', () => {
    expect(canConnect(workflow, 'advance:action:1', 'stage:screening')).toBe(true);
    expect(canConnect(workflow, 'advance:action:0', 'stage:screening')).toBe(false);
    expect(canConnect(workflow, 'advance:action:1', 'advance:trigger')).toBe(false);
    expect(canConnect(workflow, 'stage:applied', 'stage:screening')).toBe(false);
    expect(connect(workflow, 'advance:action:0', 'stage:screening')).toBe(workflow);
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
    const drawn = (kind: string) => nodes.filter((candidate) => candidate.data.kind === kind);

    for (const candidate of nodes.filter((entry) => entry.data.kind !== 'lane')) expect(at(candidate.data.path)).toBeDefined();
    expect(drawn('stage').map((candidate) => at(candidate.data.path))).toEqual(workflow.stages);
    const text = (value: unknown) => JSON.stringify(value);
    const bySorted = (values: unknown[]) => values.map(text).sort();
    expect(bySorted(drawn('trigger').map((candidate) => at(candidate.data.path)))).toEqual(bySorted(workflow.automations.map((automation) => automation.trigger)));
    expect(bySorted(drawn('action').map((candidate) => at(candidate.data.path)))).toEqual(bySorted(workflow.automations.flatMap((automation) => automation.actions)));
  });

  it('connecting a "moves to" line the graph already draws gives back the same workflow', () => {
    const moves = toGraph(workflow).edges.filter((edge) => edge.data!.kind === 'moves');

    expect(moves).toHaveLength(1);
    for (const edge of moves) expect(connect(workflow, edge.source, edge.target)).toEqual(workflow);
  });

  it('pointing a move elsewhere, then connecting the line it had, gives back the original', () => {
    const moved = connect(workflow, 'advance:action:1', 'stage:applied');

    expect(moved.automations[0]!.actions[1]!.stageId).toBe('applied');
    expect(links(toGraph(moved))).toContain('advance:action:1>stage:applied');
    expect(links(toGraph(moved))).not.toContain('advance:action:1>stage:interview');
    expect(connect(moved, 'advance:action:1', 'stage:interview')).toEqual(workflow);
  });
});

describe('moving between nodes by key', () => {
  const { nodes } = toGraph(workflow, { editable: true });
  const from = (id: string, key: string) => neighbour(nodes, id, key);

  it('goes down and up an automation one node at a time, through the plus on its line', () => {
    expect(from('stage:screening', 'ArrowDown')).toBe('add:screening');
    expect(from('add:screening', 'ArrowDown')).toBe('advance:trigger');
    expect(from('advance:trigger', 'ArrowDown')).toBe('advance:conditions');
    expect(from('advance:action:1', 'ArrowDown')).toBe('advance:add-action');
    expect(from('advance:add-action', 'ArrowDown')).toBeNull();
    expect(from('hello:trigger', 'ArrowDown')).toBe('hello:add-condition');
    expect(from('advance:trigger', 'ArrowUp')).toBe('add:screening');
    expect(from(LANE_NODE, 'ArrowUp')).toBeNull();
  });

  it('goes across to the nearest column, level with where it was, with Right and Left', () => {
    expect(from(LANE_NODE, 'ArrowRight')).toBe('stage:applied');
    expect(from('stage:applied', 'ArrowRight')).toBe('stage:screening');
    expect(from('hello:action:0', 'ArrowRight')).toBe('add:applied');
    expect(from('stage:screening', 'ArrowLeft')).toBe('stage:applied');
    expect(from(LANE_NODE, 'ArrowLeft')).toBeNull();
    expect(from('stage:interview', 'ArrowRight')).toBeNull();
  });

  it('goes to the first and last node with Home and End, and ignores other keys', () => {
    expect(from('advance:action:0', 'Home')).toBe(LANE_NODE);
    expect(from(LANE_NODE, 'End')).toBe(nodes.at(-1)!.id);
    expect(from(LANE_NODE, 'Enter')).toBeNull();
    expect(from('missing', 'ArrowRight')).toBeNull();
  });

  it('lists the nodes in reading order, a band at a time, which is also the order Tab visits them', () => {
    const ids = nodes.map((candidate) => candidate.id);

    expect(ids.indexOf(LANE_NODE)).toBe(0);
    expect(ids.indexOf('hello:trigger')).toBeLessThan(ids.indexOf('stage:applied'));
    expect(ids.indexOf('stage:screening')).toBeLessThan(ids.indexOf('advance:trigger'));
    expect(ids.indexOf('advance:action:1')).toBeLessThan(ids.indexOf('stage:interview'));
  });
});
