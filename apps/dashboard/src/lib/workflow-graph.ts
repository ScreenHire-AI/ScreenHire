// The graph view's projection of a workflow (workflow-engine.md §4): the whole-workflow lane and the pipeline's stages
// along the top, and one row per automation, trigger then conditions then actions, linked to the lane or stage it fires
// on and the stage it moves into. It is derived from the workflow on every change and never stored: the workflow stays
// the one format, and the layout is computed, so nothing is dragged and no position is saved. Edits made in the graph
// are the functions at the end of this file, each a change to the workflow that the next projection shows.
import type { Edge, Node } from '@xyflow/svelte';
import type { DefinitionError, HiringWorkflow } from '@screenhire/api-client';
import { describeAction, emailTemplateNames, errorsUnder, followUpName, laneOf, newAutomation, operatorLabels, removeAt, triggerLabels } from './workflow.js';

export type StepKind = 'lane' | 'stage' | 'trigger' | 'conditions' | 'action';

interface StepFields {
  kind: StepKind;
  title: string;
  detail: string;
  /** The automation this node belongs to, by index, for opening its form; null for the lane and stages. */
  automation: number | null;
  /** That automation's id and name, for removing it and for naming the node's buttons; null and '' elsewhere. */
  automationId: string | null;
  automationName: string;
  /** The node's place in the definition (`automations[0].actions[1]`): the form part its Edit opens, and its errors. */
  path: string;
  /** The stage a lane or stage node adds automations to, or null for the whole workflow. Unused on other nodes. */
  lane: string | null;
  disabled: boolean;
  /** A "move to a stage" action, the one node a link can be drawn from (`connect`). */
  movesToStage: boolean;
  /** The validator's messages about this node, as its form shows them. */
  problems: string[];
  /** Its row (0 is the lane and stages, then one per automation) and column, for moving between nodes by key. */
  row: number;
  column: number;
}

/** Svelte Flow's node data is a record; the graph component adds the buttons' `actions` to it. */
export interface StepData extends StepFields, Record<string, unknown> {}

export type StepNode = Node<StepData, 'step'>;

export interface GraphOptions {
  templateNames?: ReadonlyMap<string, string>;
  errors?: readonly DefinitionError[];
  /** Whether nodes carry their Edit, Add and Remove buttons, which make them taller. */
  editable?: boolean;
  /** A coarse pointer, whose buttons are 44px high (design system F4). */
  touch?: boolean;
}

/** The node box's width, which step-node.svelte sets from this, and the room between columns. */
export const NODE_WIDTH = 220;
const COLUMN = NODE_WIDTH + 50;
const ROW_GAP = 56;

/**
 * The tallest a node can be, from what it shows: step-node.svelte clamps the title and a problem to two lines and the
 * detail to one, in 16px lines, so this is an upper bound and rows laid out with it never overlap.
 */
export function nodeHeight(data: Pick<StepData, 'detail' | 'problems'>, buttons: boolean, touch = false): number {
  const line = 16;
  const gap = 4;
  const frame = 2 + 16;
  return (
    frame +
    line +
    gap +
    2 * line +
    (data.detail ? gap + line : 0) +
    (data.problems.length > 0 ? gap + 2 * line : 0) +
    (buttons ? gap + 4 + (touch ? 44 : 24) : 0)
  );
}

/** The whole-workflow lane's node id; a stage's is `stage:{id}`. */
export const LANE_NODE = 'lane:workflow';
export const stageNode = (stageId: string) => `stage:${stageId}`;
export const triggerNode = (automationId: string) => `${automationId}:trigger`;
const actionNode = (automationId: string, index: number) => `${automationId}:action:${index}`;

/**
 * The validator's messages for one node. A trigger node stands for its automation, so it also shows the automation's
 * own messages (its name, a missing action); conditions and actions show theirs.
 */
export function nodeErrors(errors: readonly DefinitionError[], kind: StepKind, path: string): string[] {
  if (kind === 'lane') return [];
  if (kind === 'trigger') {
    const automation = path.replace(/\.trigger$/, '');
    return errorsUnder(errors, automation)
      .filter((error) => !errorsUnder([error], `${automation}.conditions`).length && !error.path.startsWith(`${automation}.actions[`))
      .map((error) => error.message);
  }
  return errorsUnder(errors, path).map((error) => error.message);
}

export function toGraph(workflow: HiringWorkflow, options: GraphOptions = {}): { nodes: StepNode[]; edges: Edge[] } {
  const { templateNames = emailTemplateNames(undefined), errors = [], editable = false, touch = false } = options;
  const nodes: StepNode[] = [];
  const edges: Edge[] = [];
  // Built row by row, then placed: a row is as tall as its tallest node can be.
  const rows: { id: string; data: Omit<StepFields, 'row' | 'column' | 'problems'>; column: number; ariaLabel: string }[][] = [];

  const top: (typeof rows)[number] = [
    {
      id: LANE_NODE,
      column: 0,
      data: { kind: 'lane', title: 'Whole workflow', detail: '', automation: null, automationId: null, automationName: '', path: '', lane: null, disabled: false, movesToStage: false },
      ariaLabel: 'Whole workflow',
    },
  ];
  workflow.stages.forEach((stage, index) => {
    top.push({
      id: stageNode(stage.id),
      column: index + 1,
      data: { kind: 'stage', title: stage.name, detail: stage.kind === stage.name ? '' : stage.kind, automation: null, automationId: null, automationName: '', path: `stages[${index}]`, lane: stage.id, disabled: false, movesToStage: false },
      ariaLabel: `Stage ${index + 1}: ${stage.name}`,
    });
    const next = workflow.stages[index + 1];
    if (next) edges.push({ id: `order:${stage.id}`, source: stageNode(stage.id), target: stageNode(next.id), type: 'smoothstep' });
  });
  rows.push(top);

  const stageIds = new Set(workflow.stages.map((stage) => stage.id));
  workflow.automations.forEach((automation, index) => {
    const row: (typeof rows)[number] = [];
    const path = `automations[${index}]`;
    const disabled = automation.enabled === false;
    const off = disabled ? ', off' : '';
    const triggerId = triggerNode(automation.id);
    const trigger = triggerLabels[automation.trigger.type] ?? automation.trigger.type;
    row.push({
      id: triggerId,
      column: 0,
      data: {
        kind: 'trigger',
        title: automation.name,
        detail: automation.trigger.questionnaireId ? `${trigger}: ${followUpName(workflow, automation.trigger.questionnaireId)}` : trigger,
        automation: index,
        automationId: automation.id,
        automationName: automation.name,
        path: `${path}.trigger`,
        lane: null,
        disabled,
        movesToStage: false,
      },
      ariaLabel: `When${off}: ${automation.name}`,
    });
    const lane = laneOf(automation);
    const firesOn = lane && stageIds.has(lane) ? stageNode(lane) : lane ? null : LANE_NODE;
    if (firesOn) edges.push({ id: `${automation.id}:fires-on`, source: firesOn, target: triggerId, label: 'fires', type: 'smoothstep' });

    let previous = triggerId;
    const rules = automation.conditions?.rules ?? [];
    if (rules.length > 0) {
      const conditionsId = `${automation.id}:conditions`;
      const match = automation.conditions?.match === 'Any' ? 'Any of' : 'All of';
      const title = `${match} ${rules.length} ${rules.length === 1 ? 'condition' : 'conditions'}`;
      row.push({
        id: conditionsId,
        column: 1,
        data: {
          kind: 'conditions',
          title,
          detail: rules.map((rule) => `${rule.negate ? 'not ' : ''}${rule.fact} ${operatorLabels[rule.operator]} ${rule.value ?? rule.values?.join(', ') ?? ''}`.trim()).join('; '),
          automation: index,
          automationId: automation.id,
          automationName: automation.name,
          path: `${path}.conditions`,
          lane: null,
          disabled,
          movesToStage: false,
        },
        ariaLabel: `If${off}: ${title}, ${automation.name}`,
      });
      edges.push({ id: `${automation.id}:to-conditions`, source: previous, target: conditionsId });
      previous = conditionsId;
    }

    automation.actions.forEach((action, actionIndex) => {
      const actionId = actionNode(automation.id, actionIndex);
      const title = describeAction(action, workflow, undefined, templateNames);
      row.push({
        id: actionId,
        column: 2 + actionIndex,
        data: {
          kind: 'action',
          title,
          detail: action.reason ?? action.note ?? action.url ?? '',
          automation: index,
          automationId: automation.id,
          automationName: automation.name,
          path: `${path}.actions[${actionIndex}]`,
          lane: null,
          disabled,
          movesToStage: action.type === 'MoveToStage',
        },
        ariaLabel: `Then${off}, action ${actionIndex + 1}: ${title}, ${automation.name}`,
      });
      edges.push({ id: `${actionId}:in`, source: previous, target: actionId });
      previous = actionId;
      if (action.type === 'MoveToStage' && action.stageId && stageIds.has(action.stageId)) {
        edges.push({ id: `${actionId}:moves`, source: actionId, target: stageNode(action.stageId), label: 'moves to', type: 'smoothstep' });
      }
    });
    rows.push(row);
  });

  let y = 0;
  rows.forEach((row, rowIndex) => {
    let height = 0;
    for (const { id, data, column, ariaLabel } of row) {
      const problems = nodeErrors(errors, data.kind, data.path);
      // Lanes and stages carry Add automation; every automation node carries Edit.
      const bound = nodeHeight({ detail: data.detail, problems }, editable, touch);
      height = Math.max(height, bound);
      const label = problems.length > 0 ? `${ariaLabel}, ${problems.length} ${problems.length === 1 ? 'problem' : 'problems'} to fix` : ariaLabel;
      nodes.push({
        id,
        type: 'step',
        position: { x: column * COLUMN, y },
        // Until Svelte Flow measures a node it is hidden, unless it has a size to start from.
        initialWidth: NODE_WIDTH,
        initialHeight: bound,
        ariaLabel: label,
        data: { ...data, problems, row: rowIndex, column },
      });
    }
    y += height + ROW_GAP;
  });

  return { nodes, edges };
}

/**
 * The node an arrow key moves to from `id`, in reading order: Left and Right step through the nodes one by one, row
 * after row; Up and Down go to the nearest node in the row above or below; Home and End to the first and last.
 */
export function neighbour(nodes: readonly StepNode[], id: string, key: string): string | null {
  const ordered = [...nodes].sort((a, b) => a.data.row - b.data.row || a.data.column - b.data.column);
  const index = ordered.findIndex((node) => node.id === id);
  if (index < 0) return null;
  const current = ordered[index]!;
  if (key === 'ArrowRight') return ordered[index + 1]?.id ?? null;
  if (key === 'ArrowLeft') return ordered[index - 1]?.id ?? null;
  if (key === 'Home') return ordered[0]!.id;
  if (key === 'End') return ordered.at(-1)!.id;
  if (key !== 'ArrowDown' && key !== 'ArrowUp') return null;
  const rows = [...new Set(ordered.map((node) => node.data.row))];
  const row = rows[rows.indexOf(current.data.row) + (key === 'ArrowDown' ? 1 : -1)];
  if (row === undefined) return null;
  const candidates = ordered.filter((node) => node.data.row === row);
  return candidates.reduce((best, node) =>
    Math.abs(node.data.column - current.data.column) < Math.abs(best.data.column - current.data.column) ? node : best,
  ).id;
}

/** Adds a new automation to a stage's lane, or the whole workflow's (null), as the Pipeline view's drawer does. */
export function addAutomation(workflow: HiringWorkflow, stageId: string | null): { workflow: HiringWorkflow; id: string } {
  const automation = newAutomation(workflow, stageId);
  return { workflow: { ...workflow, automations: [...workflow.automations, automation] }, id: automation.id };
}

/** The workflow without the automation `automationId`. */
export function removeAutomation(workflow: HiringWorkflow, automationId: string): HiringWorkflow {
  const index = workflow.automations.findIndex((automation) => automation.id === automationId);
  return index < 0 ? workflow : { ...workflow, automations: removeAt(workflow.automations, index) };
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
 * the inverse of the "moves to" edge the projection draws. Any other pair leaves the workflow as it is.
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
