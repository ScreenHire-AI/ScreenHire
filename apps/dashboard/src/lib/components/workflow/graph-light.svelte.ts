import { getContext, setContext } from 'svelte';
import type { Path } from '#lib/workflow-graph.js';

export type LightState = 'lit' | 'quiet' | 'rest';

/**
 * Which nodes and lines the graph lights: the path under the pointer or the focus, or the path a test took. With none,
 * every node and line is at rest; with one, those on it are lit and the rest quiet. Nodes and lines read this through
 * context, so lighting a path changes their classes without rebuilding the graph.
 */
export class PathLight {
  #path = $state.raw<Path | null>(null);

  set(path: Path | null) {
    this.#path = path;
  }

  node(id: string): LightState {
    return this.#path === null ? 'rest' : this.#path.nodes.has(id) ? 'lit' : 'quiet';
  }

  edge(id: string): LightState {
    return this.#path === null ? 'rest' : this.#path.edges.has(id) ? 'lit' : 'quiet';
  }
}

const key = Symbol('graph path light');

export const providePathLight = (): PathLight => setContext(key, new PathLight());
export const usePathLight = (): PathLight => getContext<PathLight>(key) ?? new PathLight();
