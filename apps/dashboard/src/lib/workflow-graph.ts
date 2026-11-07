// The graph view's projection of a workflow (workflow-engine.md §4). The stages run left to right as the backbone,
// after a "Whole workflow" lane, and each lane or stage's automations hang beneath it, one column each, read down as
// When, If, Then. It is derived from the workflow on every change and never stored: the workflow stays the one
// format, and the layout is computed here, so nothing is dragged and no position is saved. Every size is fixed, so
// every position and every line below is exact and a line can be checked against the nodes it must not cross.
// Edits made in the graph are the functions at the end of this file, each a change to the workflow that the next
// projection shows.
import type { Edge, Node } from '@xyflow/svelte';
import type { DefinitionError, EvaluationOutcome, HiringWorkflow } from '@screenhire/api-client';
import {
  describeAction,
  emailTemplateNames,
  errorsUnder,
  factsFor,
  followUpName,
  laneOf,
  newAction,
  newAutomation,
  newRule,
  operatorLabels,
  triggerLabels,
} from './workflow.js';

export type StepKind = 'lane' | 'stage' | 'trigger' | 'conditions' | 'action' | 'add-automation' | 'add-condition' | 'add-action';

/** The kinds that are a plus button: the place a new automation, condition or action would go. */
export const isAdd = (kind: StepKind) => kind === 'add-automation' || kind === 'add-condition' || kind === 'add-action';

interface StepFields {
  kind: StepKind;
  title: string;
  detail: string;
  /** The automation this node belongs to, by index, for opening its form; null for the lane and stages. */
  automation: number | null;
  /** That automation's id and name, for naming the node's controls; null and '' elsewhere. */
  automationId: string | null;
  automationName: string;
  /** The node's place in the definition (`automations[0].actions[1]`): the form part it opens, and its errors. */
  path: string;
  /** The stage a lane or stage node (or its plus) adds automations to, or null for the whole workflow. */
  lane: string | null;
  /** The lane or stage this node hangs under: `LANE_BAND` or a stage id. */
  band: string;
  disabled: boolean;
  /** A "move to a stage" action, the one node a link can be drawn from (`connect`). */
  movesToStage: boolean;
  /** The validator's messages about this node, as its form shows them. */
  problems: string[];
  /**
   * Its place for moving between nodes by key: the column it stands in (a lane or stage shares its first automation's)
   * and how far down it is. A plus between two nodes has a half step, so the order along a line stays the order of the
   * line.
   */
  column: number;
  depth: number;
  /** What a screen reader hears for the node (with its problem count): its heading, title and automation. */
  label: string;
}

/** Svelte Flow's node data is a record; the graph component adds the node's `actions` to it. */
export interface StepData extends StepFields, Record<string, unknown> {}

export type StepNode = Node<StepData, 'step'>;

export type EdgeKind = 'order' | 'fires' | 'chain' | 'moves' | 'stub';

export interface Point {
  x: number;
  y: number;
}

/** What a line is: the path it takes, drawn by routed-edge.svelte, and what it belongs to for lighting a path. */
export interface RouteData extends Record<string, unknown> {
  kind: EdgeKind;
  /** The corners of the line, from its source to its target; every segment is horizontal or vertical. */
  points: Point[];
  arrow: boolean;
  dashed: boolean;
  /** The words a line carries, only where they add meaning (a move). */
  label?: string;
  labelAt?: Point;
  automationId: string | null;
  band: string;
}

export type RouteEdge = Edge<RouteData, 'routed'>;

export interface GraphOptions {
  templateNames?: ReadonlyMap<string, string>;
  errors?: readonly DefinitionError[];
  /** Whether the graph can be edited here: it then draws the plus buttons. */
  editable?: boolean;
}

export interface Graph {
  nodes: StepNode[];
  edges: RouteEdge[];
  /** The size of everything drawn, for the first view. */
  bounds: { width: number; height: number };
}

/** The node box's width, which step-node.svelte sets from this. */
export const NODE_WIDTH = 160;
/** The room between two automations' columns: a move's line rises through it. */
const CHANNEL = 28;
const BAND_GAP = 28;
/** More room after the lane, which is not part of the pipeline. */
const LANE_GAP = 20;
const CHAIN_GAP = 36;
/** A plus button's size at full size; `firstViewMinZoom` keeps it a big enough target on screen. */
export const PLUS = 48;
const PLUS_PAD = 16;
/** Where a plus ends and the line below it can turn without touching it. */
const TRUNK_CLEARANCE = 32;
const BUS_PITCH = 24;
const BUS_MARGIN = 16;
/** How far apart lines that share a channel or a stage's top are drawn. */
const LINE_SPACING = 8;
const LANDING_SPACING = 12;

/** The whole-workflow lane's node id; a stage's is `stage:{id}`. */
export const LANE_NODE = 'lane:workflow';
/** The band the lane's automations hang under. */
export const LANE_BAND = 'lane';
export const stageNode = (stageId: string) => `stage:${stageId}`;
export const triggerNode = (automationId: string) => `${automationId}:trigger`;
const actionNode = (automationId: string, index: number) => `${automationId}:action:${index}`;
const headerNode = (band: string) => (band === LANE_BAND ? LANE_NODE : stageNode(band));

/**
 * The height of a node, which is fixed so lines can be placed exactly: step-node.svelte clamps the title and a problem
 * to two lines and the detail to one, in 16px lines, and always keeps room for them.
 */
export function nodeHeight(data: Pick<StepData, 'problems'>): number {
  const line = 16;
  const gap = 4;
  const frame = 2 + 16;
  return frame + line + gap + 2 * line + gap + line + (data.problems.length > 0 ? gap + 2 * line : 0);
}

/**
 * The validator's messages for one node. A trigger node stands for its automation, so it also shows the automation's
 * own messages (its name, a missing action); conditions and actions show theirs.
 */
export function nodeErrors(errors: readonly DefinitionError[], kind: StepKind, path: string): string[] {
  if (kind === 'trigger') {
    const automation = path.replace(/\.trigger$/, '');
    return errorsUnder(errors, automation)
      .filter((error) => !errorsUnder([error], `${automation}.conditions`).length && !error.path.startsWith(`${automation}.actions[`))
      .map((error) => error.message);
  }
  if (kind === 'stage' || kind === 'conditions' || kind === 'action') return errorsUnder(errors, path).map((error) => error.message);
  return [];
}

type Spec = Omit<StepFields, 'problems' | 'column' | 'depth' | 'label'>;

interface Item {
  id: string;
  spec: Spec;
  ariaLabel: string;
  problems: string[];
  height: number;
  /** Set once the band is placed. */
  y: number;
}

interface Chain {
  index: number;
  id: string;
  name: string;
  items: Item[];
  /** Whether a condition would go between the trigger and the first action: it has none and has an action. */
  addCondition: boolean;
  column: number;
  x: number;
}

interface Band {
  key: string;
  header: Item;
  chains: Chain[];
  x: number;
  width: number;
  firstColumn: number;
}

function groupBy<T, K>(items: readonly T[], key: (item: T) => K): T[][] {
  const groups = new Map<K, T[]>();
  for (const entry of items) groups.set(key(entry), [...(groups.get(key(entry)) ?? []), entry]);
  return [...groups.values()];
}

const none = { automation: null, automationId: null, automationName: '', path: '', lane: null, disabled: false, movesToStage: false } as const;

export function toGraph(workflow: HiringWorkflow, options: GraphOptions = {}): Graph {
  const { templateNames = emailTemplateNames(undefined), errors = [], editable = false } = options;
  const addable = editable && workflow.automations.length > 0;
  const stageIds = new Set(workflow.stages.map((stage) => stage.id));

  const item = (id: string, spec: Spec, ariaLabel: string): Item => {
    const problems = nodeErrors(errors, spec.kind, spec.path);
    const label = problems.length > 0 ? `${ariaLabel}, ${problems.length} ${problems.length === 1 ? 'problem' : 'problems'} to fix` : ariaLabel;
    return { id, spec, ariaLabel: label, problems, height: nodeHeight({ problems }), y: 0 };
  };

  // Each automation hangs under the lane or stage it fires on. One whose stage is gone stays in the lane's band, where
  // the validator's message about it shows.
  const bandOf = (automationIndex: number) => {
    const lane = laneOf(workflow.automations[automationIndex]!);
    return lane && stageIds.has(lane) ? lane : LANE_BAND;
  };

  const chainOf = (index: number): Chain => {
    const automation = workflow.automations[index]!;
    const path = `automations[${index}]`;
    const band = bandOf(index);
    const disabled = automation.enabled === false;
    const off = disabled ? ', off' : '';
    const base = { automation: index, automationId: automation.id, automationName: automation.name, lane: null, band, disabled };
    const items: Item[] = [];
    const trigger = triggerLabels[automation.trigger.type] ?? automation.trigger.type;
    items.push(
      item(
        triggerNode(automation.id),
        {
          ...base,
          kind: 'trigger',
          title: automation.name,
          detail: automation.trigger.questionnaireId ? `${trigger}: ${followUpName(workflow, automation.trigger.questionnaireId)}` : trigger,
          path: `${path}.trigger`,
          movesToStage: false,
        },
        `When${off}: ${automation.name}`,
      ),
    );
    const rules = automation.conditions?.rules ?? [];
    if (rules.length > 0) {
      const match = automation.conditions?.match === 'Any' ? 'Any of' : 'All of';
      const title = `${match} ${rules.length} ${rules.length === 1 ? 'condition' : 'conditions'}`;
      items.push(
        item(
          `${automation.id}:conditions`,
          {
            ...base,
            kind: 'conditions',
            title,
            detail: rules.map((rule) => `${rule.negate ? 'not ' : ''}${rule.fact} ${operatorLabels[rule.operator]} ${rule.value ?? rule.values?.join(', ') ?? ''}`.trim()).join('; '),
            path: `${path}.conditions`,
            movesToStage: false,
          },
          `If${off}: ${title}, ${automation.name}`,
        ),
      );
    }
    automation.actions.forEach((action, actionIndex) => {
      const title = describeAction(action, workflow, undefined, templateNames);
      items.push(
        item(
          actionNode(automation.id, actionIndex),
          {
            ...base,
            kind: 'action',
            title,
            detail: action.reason ?? action.note ?? action.url ?? '',
            path: `${path}.actions[${actionIndex}]`,
            movesToStage: action.type === 'MoveToStage',
          },
          `Then${off}, action ${actionIndex + 1}: ${title}, ${automation.name}`,
        ),
      );
    });
    return { index, id: automation.id, name: automation.name, items, addCondition: rules.length === 0 && automation.actions.length > 0, column: 0, x: 0 };
  };

  const chains = workflow.automations.map((_, index) => chainOf(index));
  const bands: Band[] = [
    {
      key: LANE_BAND,
      header: item(LANE_NODE, { ...none, kind: 'lane', title: 'Whole workflow', detail: '', band: LANE_BAND }, 'Whole workflow'),
      chains: chains.filter((chain) => bandOf(chain.index) === LANE_BAND),
      x: 0,
      width: 0,
      firstColumn: 0,
    },
    ...workflow.stages.map((stage, index): Band => ({
      key: stage.id,
      header: item(
        stageNode(stage.id),
        { ...none, kind: 'stage', title: stage.name, detail: stage.kind === stage.name ? '' : stage.kind, path: `stages[${index}]`, lane: stage.id, band: stage.id },
        `Stage ${index + 1}: ${stage.name}`,
      ),
      chains: chains.filter((chain) => bandOf(chain.index) === stage.id),
      x: 0,
      width: 0,
      firstColumn: 0,
    })),
  ];

  // Across: a band is as wide as its automations' columns, with a channel between them.
  let x = 0;
  let column = 0;
  for (const band of bands) {
    const columns = Math.max(1, band.chains.length);
    band.x = x;
    band.firstColumn = column;
    band.width = columns * NODE_WIDTH + (columns - 1) * CHANNEL;
    band.chains.forEach((chain, i) => {
      chain.x = band.x + i * (NODE_WIDTH + CHANNEL);
      chain.column = column + i;
    });
    x += band.width + BAND_GAP + (band.key === LANE_BAND ? LANE_GAP : 0);
    column += columns;
  }
  const totalWidth = x - BAND_GAP - (bands.length === 1 ? LANE_GAP : 0);

  // The headers share a row and a height, so the backbone's lines are straight.
  const rowHeight = Math.max(...bands.map((band) => band.header.height));
  for (const band of bands) band.header.height = rowHeight;
  const headerGap = PLUS_PAD + PLUS + PLUS_PAD + TRUNK_CLEARANCE;
  const centre = (left: number) => left + NODE_WIDTH / 2;
  const bandByKey = new Map(bands.map((band) => [band.key, band]));
  const headerCentre = (band: Band) => centre(band.x);

  // Down: each chain stacks from under its header. A trigger with no conditions and an action has room for a plus
  // between them.
  const conditionGap = PLUS + 2 * PLUS_PAD;
  const actionTailGap = 20;
  let bottom = rowHeight;
  const chainsTop = rowHeight + headerGap;
  for (const band of bands) {
    for (const chain of band.chains) {
      let y = chainsTop;
      chain.items.forEach((entry, i) => {
        entry.y = y;
        const gap = i === 0 && chain.addCondition && addable ? conditionGap : CHAIN_GAP;
        y += entry.height + gap;
      });
      const last = chain.items.at(-1)!;
      bottom = Math.max(bottom, last.y + last.height + (addable ? actionTailGap + PLUS : 0));
    }
  }
  if (addable || bands.some((band) => band.chains.length === 0)) bottom = Math.max(bottom, rowHeight + PLUS_PAD + PLUS);

  // Moves: a move leaves its action's right side, rises through the channel beside its column to a lane above the
  // stages, runs along it and drops into the target stage's top. Lanes are shared by moves that do not overlap. The
  // shortest runs choose first, so a run inside another sits nearer the stages and the longer line's rise and drop pass
  // outside it. Two runs that only partly overlap cross once whichever is lower.
  interface Move {
    id: string;
    automationId: string;
    band: string;
    chain: Chain;
    action: Item;
    target: Band;
    channelX: number;
    landX: number;
    lane: number;
  }
  const moves: Move[] = [];
  for (const chain of chains) {
    const automation = workflow.automations[chain.index]!;
    automation.actions.forEach((action, actionIndex) => {
      const target = action.type === 'MoveToStage' && action.stageId ? bandByKey.get(action.stageId) : undefined;
      if (!target || target.key === LANE_BAND) return;
      moves.push({
        id: actionNode(chain.id, actionIndex),
        automationId: chain.id,
        band: bandOf(chain.index),
        chain,
        action: chain.items.find((entry) => entry.id === actionNode(chain.id, actionIndex))!,
        target,
        channelX: 0,
        landX: 0,
        lane: 0,
      });
    });
  }
  const spread = (count: number, index: number, spacing: number, limit: number) =>
    Math.max(-limit, Math.min(limit, (index - (count - 1) / 2) * spacing));
  for (const group of groupBy(moves, (move) => move.chain.column)) {
    group.sort((a, b) => a.action.y - b.action.y);
    group.forEach((move, i) => (move.channelX = move.chain.x + NODE_WIDTH + CHANNEL / 2 + spread(group.length, i, LINE_SPACING, CHANNEL / 2 - 6)));
  }
  for (const group of groupBy(moves, (move) => move.target.key)) {
    group.sort((a, b) => a.channelX - b.channelX);
    group.forEach((move, i) => (move.landX = headerCentre(move.target) + spread(group.length, i, LANDING_SPACING, NODE_WIDTH / 2 - 16)));
  }
  const lanes: { left: number; right: number }[][] = [];
  for (const move of [...moves].sort((a, b) => Math.abs(a.channelX - a.landX) - Math.abs(b.channelX - b.landX))) {
    const run = { left: Math.min(move.channelX, move.landX) - LANDING_SPACING, right: Math.max(move.channelX, move.landX) + LANDING_SPACING };
    let lane = lanes.findIndex((runs) => runs.every((other) => other.right < run.left || run.right < other.left));
    if (lane < 0) lane = lanes.push([]) - 1;
    lanes[lane]!.push(run);
    move.lane = lane;
  }
  const busHeight = lanes.length > 0 ? BUS_MARGIN + lanes.length * BUS_PITCH : 0;
  const top = busHeight;
  const busY = (lane: number) => top - BUS_MARGIN - lane * BUS_PITCH;

  // Nodes, in reading order: a band's header and its plus, then each automation from the top, which is also the order
  // Tab visits them.
  const nodes: StepNode[] = [];
  const edges: RouteEdge[] = [];
  // The node's button or box inside it is the control and carries the label, so the wrapper Svelte Flow draws says nothing.
  const wrapper = { 'aria-roledescription': undefined };
  const place = (entry: Item, left: number, upper: number, columnOf: number, depth: number, width = NODE_WIDTH) =>
    nodes.push({
      id: entry.id,
      type: 'step',
      position: { x: left, y: upper },
      width,
      height: entry.height,
      domAttributes: wrapper,
      data: { ...entry.spec, problems: entry.problems, column: columnOf, depth, label: entry.ariaLabel },
    });
  const plusNode = (id: string, left: number, upper: number, spec: Spec, ariaLabel: string, columnOf: number, depth: number) =>
    nodes.push({
      id,
      type: 'step',
      position: { x: left, y: upper },
      width: PLUS,
      height: PLUS,
      domAttributes: wrapper,
      data: { ...spec, problems: [], column: columnOf, depth, label: ariaLabel },
    });
  const route = (
    id: string,
    kind: EdgeKind,
    source: string,
    target: string,
    handles: [string, string],
    points: Point[],
    owner: { automationId: string | null; band: string },
    extra: Partial<RouteData> = {},
  ) =>
    edges.push({
      id,
      type: 'routed',
      source,
      target,
      sourceHandle: handles[0],
      targetHandle: handles[1],
      focusable: false,
      selectable: false,
      data: { kind, points, arrow: kind !== 'stub', dashed: kind === 'stub', automationId: owner.automationId, band: owner.band, ...extra },
    });

  bands.forEach((band, bandIndex) => {
    const hx = headerCentre(band);
    place(band.header, band.x, top, band.firstColumn, 0);
    const empty = band.chains.length === 0;
    const plusTop = top + rowHeight + PLUS_PAD;
    if (addable) {
      const title = band.key === LANE_BAND ? 'the whole workflow' : band.header.spec.title;
      plusNode(
        `add:${band.key}`,
        hx - PLUS / 2,
        plusTop,
        { ...none, kind: 'add-automation', title: 'Add an automation', detail: '', lane: band.header.spec.lane, band: band.key },
        `Add an automation to ${title}`,
        band.firstColumn,
        0.5,
      );
      if (empty) route(`stub:${band.key}`, 'stub', band.header.id, `add:${band.key}`, ['bottom', 'top'], [{ x: hx, y: top + rowHeight }, { x: hx, y: plusTop }], { automationId: null, band: band.key });
    }

    const next = bands[bandIndex + 1];
    if (next && band.key !== LANE_BAND) {
      const y = top + rowHeight / 2;
      route(`order:${band.key}`, 'order', band.header.id, next.header.id, ['right', 'left'], [{ x: band.x + NODE_WIDTH, y }, { x: next.x, y }], { automationId: null, band: band.key });
    }

    const trunk = top + rowHeight + headerGap - TRUNK_CLEARANCE / 2;
    for (const chain of band.chains) {
      const cx = centre(chain.x);
      const owner = { automationId: chain.id, band: band.key };
      chain.items.forEach((entry, i) => {
        const upper = top + entry.y;
        place(entry, chain.x, upper, chain.column, i + 1);
        if (i === 0) {
          const start = { x: hx, y: top + rowHeight };
          const points = cx === hx ? [start, { x: cx, y: upper }] : [start, { x: hx, y: trunk }, { x: cx, y: trunk }, { x: cx, y: upper }];
          route(`${chain.id}:fires-on`, 'fires', band.header.id, entry.id, ['bottom', 'top'], points, owner);
          return;
        }
        const before = chain.items[i - 1]!;
        const id = entry.spec.kind === 'conditions' ? `${chain.id}:to-conditions` : `${entry.id}:in`;
        route(id, 'chain', before.id, entry.id, ['bottom', 'top'], [{ x: cx, y: top + before.y + before.height }, { x: cx, y: upper }], owner);
      });
      if (!addable) continue;
      const first = chain.items[0]!;
      if (chain.addCondition) {
        const above = top + first.y + first.height;
        plusNode(
          `${chain.id}:add-condition`,
          cx - PLUS / 2,
          above + PLUS_PAD,
          { ...none, kind: 'add-condition', title: 'Add a condition', detail: '', automation: chain.index, automationId: chain.id, automationName: chain.name, band: band.key },
          `Add a condition to ${chain.name}`,
          chain.column,
          1.5,
        );
      }
      const last = chain.items.at(-1)!;
      const lastBottom = top + last.y + last.height;
      const tail = `${chain.id}:add-action`;
      plusNode(
        tail,
        cx - PLUS / 2,
        lastBottom + actionTailGap,
        { ...none, kind: 'add-action', title: 'Add an action', detail: '', automation: chain.index, automationId: chain.id, automationName: chain.name, band: band.key },
        `Add an action to ${chain.name}`,
        chain.column,
        chain.items.length + 0.5,
      );
      route(`${chain.id}:stub-action`, 'stub', last.id, tail, ['bottom', 'top'], [{ x: cx, y: lastBottom }, { x: cx, y: lastBottom + actionTailGap }], owner);
    }
  });

  for (const move of moves) {
    const start = { x: move.chain.x + NODE_WIDTH, y: top + move.action.y + move.action.height / 2 };
    const lane = busY(move.lane);
    const points = [start, { x: move.channelX, y: start.y }, { x: move.channelX, y: lane }, { x: move.landX, y: lane }, { x: move.landX, y: top }];
    route(`${move.id}:moves`, 'moves', move.id, headerNode(move.target.key), ['right', 'top'], points, { automationId: move.automationId, band: move.band }, {
      label: 'Moves to',
      labelAt: { x: (move.channelX + move.landX) / 2, y: lane },
    });
  }

  return { nodes, edges, bounds: { width: Math.max(totalWidth, 0), height: top + bottom } };
}

/** How long and wide a line's arrowhead is. */
export const ARROW = { length: 9, half: 5 } as const;

const toward = (from: Point, to: Point, distance: number): Point => {
  const length = Math.hypot(to.x - from.x, to.y - from.y) || 1;
  return { x: from.x + ((to.x - from.x) / length) * distance, y: from.y + ((to.y - from.y) / length) * distance };
};

/**
 * The SVG path of a line through `points`, its corners rounded; with an arrowhead the line stops where the head starts.
 */
export function routePath(points: readonly Point[], arrow: boolean, radius = 8): string {
  const line = points.map((point) => ({ ...point }));
  if (arrow && line.length >= 2) line[line.length - 1] = toward(line.at(-1)!, line.at(-2)!, ARROW.length);
  let path = `M${line[0]!.x} ${line[0]!.y}`;
  for (let i = 1; i < line.length; i++) {
    const corner = line[i]!;
    const before = line[i - 1]!;
    const after = line[i + 1];
    if (!after) {
      path += `L${corner.x} ${corner.y}`;
      continue;
    }
    const size = Math.min(radius, Math.hypot(corner.x - before.x, corner.y - before.y) / 2, Math.hypot(after.x - corner.x, after.y - corner.y) / 2);
    const start = toward(corner, before, size);
    const end = toward(corner, after, size);
    path += `L${start.x} ${start.y}Q${corner.x} ${corner.y} ${end.x} ${end.y}`;
  }
  return path;
}

/** The arrowhead at the end of a line: a triangle whose tip is the last point, pointing the way the line runs. */
export function arrowhead(points: readonly Point[]): string {
  const tip = points.at(-1)!;
  const from = points.at(-2)!;
  const base = toward(tip, from, ARROW.length);
  const length = Math.hypot(tip.x - from.x, tip.y - from.y) || 1;
  const side = { x: (-(tip.y - from.y) / length) * ARROW.half, y: ((tip.x - from.x) / length) * ARROW.half };
  return `M${tip.x} ${tip.y}L${base.x + side.x} ${base.y + side.y}L${base.x - side.x} ${base.y - side.y}Z`;
}

/** What a test ran on: its result stops applying when this changes. */
export const definitionStamp = (workflow: Pick<HiringWorkflow, 'stages' | 'automations'>) => JSON.stringify([workflow.stages, workflow.automations]);

/** The ids of the nodes and lines a hover, a focus or a test lights, and so what everything else quietens. */
export interface Path {
  nodes: Set<string>;
  edges: Set<string>;
}

const emptyPath = (): Path => ({ nodes: new Set(), edges: new Set() });

/**
 * The path through the graph from one node: for an automation's node, its whole chain, the stage or lane it fires on and
 * the stage it moves into; for a lane or stage, itself, its automations and the moves into it.
 */
export function pathOf(graph: Pick<Graph, 'nodes' | 'edges'>, id: string): Path {
  const path = emptyPath();
  const node = graph.nodes.find((candidate) => candidate.id === id);
  if (!node) return path;
  const { kind, automationId, band } = node.data;
  const add = (nodeId: string) => path.nodes.add(nodeId);
  if (kind === 'lane' || kind === 'stage' || kind === 'add-automation') {
    add(headerNode(band));
    if (kind === 'add-automation') {
      add(id);
      for (const edge of graph.edges) if (edge.id === `stub:${band}`) path.edges.add(edge.id);
      return path;
    }
    for (const candidate of graph.nodes) if (candidate.data.band === band) add(candidate.id);
    for (const edge of graph.edges) {
      const into = edge.data?.kind === 'moves' && edge.target === id;
      const order = edge.data?.kind === 'order' && (edge.source === id || edge.target === id);
      if (edge.data?.band === band) {
        path.edges.add(edge.id);
        if (edge.data.kind === 'moves') add(edge.target);
      }
      if (into) {
        path.edges.add(edge.id);
        add(edge.source);
      }
      if (order) {
        path.edges.add(edge.id);
        add(edge.source === id ? edge.target : edge.source);
      }
    }
    return path;
  }
  if (!automationId) return path;
  add(headerNode(band));
  for (const candidate of graph.nodes) if (candidate.data.automationId === automationId) add(candidate.id);
  for (const edge of graph.edges) {
    if (edge.data?.automationId !== automationId) continue;
    path.edges.add(edge.id);
    if (edge.data.kind === 'moves') add(edge.target);
  }
  return path;
}

/**
 * The path a test took (design system section 11, workflow graph): an automation that would run lights its whole chain;
 * one whose conditions do not hold stops at its conditions; a skipped or blocked one lights only its trigger. A plus and
 * its dashed line are places to add a step, not steps, so a test never lights them.
 */
export function testedPath(graph: Pick<Graph, 'nodes' | 'edges'>, tested: ReadonlyMap<string, EvaluationOutcome>): Path {
  const path = emptyPath();
  for (const [automationId, outcome] of tested) {
    const trigger = graph.nodes.find((node) => node.id === triggerNode(automationId));
    if (!trigger) continue;
    if (outcome === 'Matched') {
      const whole = pathOf(graph, trigger.id);
      for (const node of graph.nodes) if (whole.nodes.has(node.id) && !isAdd(node.data.kind)) path.nodes.add(node.id);
      for (const edge of graph.edges) if (whole.edges.has(edge.id) && edge.data?.kind !== 'stub') path.edges.add(edge.id);
      continue;
    }
    path.nodes.add(headerNode(trigger.data.band));
    path.nodes.add(trigger.id);
    path.edges.add(`${automationId}:fires-on`);
    if (outcome === 'NotMatched' && graph.nodes.some((node) => node.id === `${automationId}:conditions`)) {
      path.nodes.add(`${automationId}:conditions`);
      path.edges.add(`${automationId}:to-conditions`);
    }
  }
  return path;
}

/**
 * The smallest zoom the first view fits a workflow to: a plus is then still a 24px target, or 44px where a finger edits
 * (design system F4). A fitted view is an overview of the flow: node text is 12px at full size and scales with the canvas.
 */
export const firstViewMinZoom = (touchEditing: boolean) => (touchEditing ? 44 : 24) / PLUS;
/** The room the first view leaves around the graph: at the left, for the zoom controls floating over the canvas. */
export const VIEW_INSET = { left: 72, top: 24, right: 24, bottom: 24 } as const;

/**
 * Where the graph starts: the whole workflow when it fits at `firstViewMinZoom` or larger, centred across the pane.
 * One that does not would be neither whole nor readable at that zoom, so it starts at full size from the top left, where
 * the lane and the first stage are and reading starts, and the rest is a pan away.
 */
export function firstView(
  bounds: { width: number; height: number },
  pane: { width: number; height: number },
  touchEditing = false,
): { x: number; y: number; zoom: number } {
  const room = { width: pane.width - VIEW_INSET.left - VIEW_INSET.right, height: pane.height - VIEW_INSET.top - VIEW_INSET.bottom };
  const fit = Math.min(1, room.width / Math.max(bounds.width, 1), room.height / Math.max(bounds.height, 1));
  const zoom = fit >= firstViewMinZoom(touchEditing) ? fit : 1;
  const spare = room.width - bounds.width * zoom;
  return { x: VIEW_INSET.left + Math.max(0, spare / 2), y: VIEW_INSET.top, zoom };
}

/**
 * The node an arrow key moves to from `id`: Up and Down go along its column, one node at a time; Left and Right to the
 * nearest node in the next column, level with it if there is one; Home and End to the first and last, in the order
 * `toGraph` lists them (a band's header, then each automation from the top).
 */
export function neighbour(nodes: readonly StepNode[], id: string, key: string): string | null {
  const current = nodes.find((node) => node.id === id);
  if (!current) return null;
  if (key === 'Home') return nodes[0]!.id;
  if (key === 'End') return nodes.at(-1)!.id;
  const { column, depth } = current.data;
  if (key === 'ArrowDown' || key === 'ArrowUp') {
    const down = key === 'ArrowDown';
    const candidates = nodes.filter((node) => node.data.column === column && (down ? node.data.depth > depth : node.data.depth < depth));
    if (candidates.length === 0) return null;
    return candidates.reduce((best, node) => ((down ? node.data.depth < best.data.depth : node.data.depth > best.data.depth) ? node : best)).id;
  }
  if (key !== 'ArrowRight' && key !== 'ArrowLeft') return null;
  const right = key === 'ArrowRight';
  const columns = nodes.map((node) => node.data.column).filter((value) => (right ? value > column : value < column));
  if (columns.length === 0) return null;
  const nearest = right ? Math.min(...columns) : Math.max(...columns);
  const candidates = nodes.filter((node) => node.data.column === nearest);
  return candidates.reduce((best, node) => (Math.abs(node.data.depth - depth) < Math.abs(best.data.depth - depth) ? node : best)).id;
}

/** Puts the focus on a node's one control, its button or its box. Says whether the node was there to focus. */
export function focusGraphNode(root: ParentNode, id: string): boolean {
  const control = root.querySelector(`.svelte-flow__node[data-id="${CSS.escape(id)}"]`)?.querySelector<HTMLElement>('button, [tabindex="0"]');
  control?.focus();
  return Boolean(control);
}

/** Adds a new automation to a stage's lane, or the whole workflow's (null), as the Pipeline view's drawer does. */
export function addAutomation(workflow: HiringWorkflow, stageId: string | null): { workflow: HiringWorkflow; id: string } {
  const automation = newAutomation(workflow, stageId);
  return { workflow: { ...workflow, automations: [...workflow.automations, automation] }, id: automation.id };
}

/**
 * Adds a condition to an automation, the one its form's Add condition adds (the first fact its trigger offers), and
 * says where the new rule is so its form can start there. An automation that already has conditions is left alone: its
 * conditions node opens the form.
 */
export function addCondition(workflow: HiringWorkflow, automationId: string): { workflow: HiringWorkflow; path: string } | null {
  const index = workflow.automations.findIndex((automation) => automation.id === automationId);
  const automation = workflow.automations[index];
  const fact = automation && factsFor(workflow, automation.trigger.type)[0];
  if (!automation || !fact || (automation.conditions?.rules.length ?? 0) > 0) return null;
  const next = { ...automation, conditions: { match: 'All' as const, rules: [newRule(fact)] } };
  return { workflow: { ...workflow, automations: workflow.automations.map((candidate, i) => (i === index ? next : candidate)) }, path: `automations[${index}].conditions` };
}

/** Adds an action to the end of an automation, the one its form's Add action adds, and says where it is. */
export function addAction(workflow: HiringWorkflow, automationId: string): { workflow: HiringWorkflow; path: string } | null {
  const index = workflow.automations.findIndex((automation) => automation.id === automationId);
  const automation = workflow.automations[index];
  if (!automation) return null;
  const next = { ...automation, actions: [...automation.actions, newAction('AddTag')] };
  return {
    workflow: { ...workflow, automations: workflow.automations.map((candidate, i) => (i === index ? next : candidate)) },
    path: `automations[${index}].actions[${automation.actions.length}]`,
  };
}

/** The automation and action index a "move to a stage" action node stands for, or null for any other node. */
function moveAction(workflow: HiringWorkflow, nodeId: string): { automation: number; action: number } | null {
  const match = /^(.*):action:(\d+)$/.exec(nodeId);
  if (!match) return null;
  const automation = workflow.automations.findIndex((candidate) => candidate.id === match[1]);
  const action = Number(match[2]);
  return workflow.automations[automation]?.actions[action]?.type === 'MoveToStage' ? { automation, action } : null;
}

const stageOf = (workflow: HiringWorkflow, nodeId: string) =>
  workflow.stages.find((stage) => stageNode(stage.id) === nodeId)?.id ?? null;

/** Whether a link drawn from `source` to `target` means something: a "move to a stage" action to a stage. */
export const canConnect = (workflow: HiringWorkflow, source: string, target: string): boolean =>
  moveAction(workflow, source) !== null && stageOf(workflow, target) !== null;

/**
 * Points the "move to a stage" action `source` at the stage `target`: the change a link drawn in the graph makes, and
 * the inverse of the "moves to" line the projection draws. Any other pair leaves the workflow as it is.
 */
export function connect(workflow: HiringWorkflow, source: string, target: string): HiringWorkflow {
  const move = moveAction(workflow, source);
  const stageId = stageOf(workflow, target);
  if (!move || !stageId) return workflow;
  return {
    ...workflow,
    automations: workflow.automations.map((automation, index) =>
      index !== move.automation
        ? automation
        : { ...automation, actions: automation.actions.map((action, actionIndex) => (actionIndex === move.action ? { ...action, stageId } : action)) },
    ),
  };
}
