// The graph view's projection of a workflow (workflow-engine.md §4): the pipeline's stages along the top, and one row
// per automation, trigger then conditions then actions, linked to the stages it fires on and moves into. It is derived
// from the workflow on every change and never stored: the workflow stays the one format, and the layout is computed,
// so there is nothing to drag.
import type { Edge, Node } from '@xyflow/svelte';
import type { HiringWorkflow } from '@screenhire/api-client';
import { describeAction, emailTemplateNames, followUpName, operatorLabels, triggerLabels } from './workflow.js';

export type StepKind = 'stage' | 'trigger' | 'conditions' | 'action';

export interface StepData extends Record<string, unknown> {
  kind: StepKind;
  title: string;
  detail: string;
  /** The automation this node belongs to, by index, for opening its editor; null for stages. */
  automation: number | null;
  /** The node's place in the definition, for matching validator errors (`automations[0].actions[1]`). */
  path: string;
  disabled: boolean;
}

export type StepNode = Node<StepData, 'step'>;

const COLUMN = 230;
const ROW = 110;
const STAGES_Y = 0;
const FIRST_ROW_Y = 150;

export function toGraph(
  workflow: HiringWorkflow,
  templateNames: ReadonlyMap<string, string> = emailTemplateNames(undefined),
): { nodes: StepNode[]; edges: Edge[] } {
  const nodes: StepNode[] = [];
  const edges: Edge[] = [];
  const stageNode = (id: string) => `stage:${id}`;
  const node = (id: string, x: number, y: number, data: StepData): StepNode => ({ id, type: 'step', position: { x, y }, data });

  workflow.stages.forEach((stage, index) => {
    nodes.push(
      node(stageNode(stage.id), index * COLUMN, STAGES_Y, {
        kind: 'stage',
        title: stage.name,
        detail: stage.kind === stage.name ? '' : stage.kind,
        automation: null,
        path: `stages[${index}]`,
        disabled: false,
      }),
    );
    const next = workflow.stages[index + 1];
    if (next) edges.push({ id: `order:${stage.id}`, source: stageNode(stage.id), target: stageNode(next.id), type: 'smoothstep' });
  });

  const stageIds = new Set(workflow.stages.map((stage) => stage.id));
  workflow.automations.forEach((automation, index) => {
    const y = FIRST_ROW_Y + index * ROW;
    const path = `automations[${index}]`;
    const disabled = automation.enabled === false;
    const triggerId = `${automation.id}:trigger`;
    const stageId = automation.trigger.stageId;
    nodes.push(
      node(triggerId, 0, y, {
        kind: 'trigger',
        title: automation.name,
        detail: automation.trigger.questionnaireId
          ? `${triggerLabels[automation.trigger.type]}: ${followUpName(workflow, automation.trigger.questionnaireId)}`
          : (triggerLabels[automation.trigger.type] ?? automation.trigger.type),
        automation: index,
        path: `${path}.trigger`,
        disabled,
      }),
    );
    if (stageId && stageIds.has(stageId)) {
      edges.push({ id: `${automation.id}:fires-on`, source: stageNode(stageId), target: triggerId, label: 'fires', type: 'smoothstep' });
    }

    let previous = triggerId;
    const rules = automation.conditions?.rules ?? [];
    if (rules.length > 0) {
      const conditionsId = `${automation.id}:conditions`;
      const match = automation.conditions?.match === 'Any' ? 'Any of' : 'All of';
      nodes.push(
        node(conditionsId, COLUMN, y, {
          kind: 'conditions',
          title: `${match} ${rules.length} ${rules.length === 1 ? 'condition' : 'conditions'}`,
          detail: rules.map((rule) => `${rule.negate ? 'not ' : ''}${rule.fact} ${operatorLabels[rule.operator]} ${rule.value ?? rule.values?.join(', ') ?? ''}`.trim()).join('; '),
          automation: index,
          path: `${path}.conditions`,
          disabled,
        }),
      );
      edges.push({ id: `${automation.id}:to-conditions`, source: previous, target: conditionsId });
      previous = conditionsId;
    }

    automation.actions.forEach((action, actionIndex) => {
      const actionId = `${automation.id}:action:${actionIndex}`;
      nodes.push(
        node(actionId, COLUMN * (2 + actionIndex), y, {
          kind: 'action',
          title: describeAction(action, workflow, undefined, templateNames),
          detail: action.reason ?? action.note ?? action.url ?? '',
          automation: index,
          path: `${path}.actions[${actionIndex}]`,
          disabled,
        }),
      );
      edges.push({ id: `${actionId}:in`, source: previous, target: actionId });
      previous = actionId;
      if (action.type === 'MoveToStage' && action.stageId && stageIds.has(action.stageId)) {
        edges.push({ id: `${actionId}:moves`, source: actionId, target: stageNode(action.stageId), label: 'moves to', type: 'smoothstep' });
      }
    });
  });

  return { nodes, edges };
}
