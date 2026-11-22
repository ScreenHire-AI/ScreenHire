import mcpCatalogue from '@screenhire/api-client/mcp-tools.json';
import openApi from '@screenhire/api-client/openapi-public.json';

// The developer docs (docs/architecture/api-reference.md) are built from what the API itself publishes: the public
// OpenAPI document (its operations, tag groups, introduction and webhooks section) and the MCP tools catalogue. Both are
// written by the API's build and tests into packages/api-client, so these pages cannot list a route, an event or a tool
// the API lacks. Hosts come from configuration (PUBLIC_API_URL), never from a host written here (ADR-0003).

/** What the pages show when no API address is configured: the documentation domain reserved for examples. */
export const exampleApiUrl = 'https://api.example.com';

/** The API's origin, without a trailing slash: the configured one, or the example. */
export function apiOrigin(configured: string | undefined): string {
  return (configured ?? exampleApiUrl).replace(/\/+$/, '');
}

export interface ApiOperation {
  method: string;
  path: string;
  summary: string;
}

export interface ApiTag {
  name: string;
  description: string;
  operations: ApiOperation[];
}

export interface ApiTagGroup {
  name: string;
  tags: ApiTag[];
}

const methods = ['get', 'post', 'put', 'patch', 'delete'] as const;

interface OperationObject {
  summary?: string;
  tags?: string[];
}

/** Every operation a key can call, grouped as Scalar groups them, in the document's order. */
export function apiTagGroups(): ApiTagGroup[] {
  const operations: (ApiOperation & { tag: string })[] = [];
  for (const [path, item] of Object.entries(openApi.paths as Record<string, Record<string, OperationObject>>)) {
    for (const method of methods) {
      const operation = item[method];
      if (operation) {
        operations.push({ method: method.toUpperCase(), path, summary: operation.summary ?? '', tag: operation.tags?.[0] ?? '' });
      }
    }
  }

  const descriptions = new Map(openApi.tags.map((tag) => [tag.name, tag.description ?? '']));
  return openApi['x-tagGroups'].map((group) => ({
    name: group.name,
    tags: group.tags
      .map((name) => ({ name, description: descriptions.get(name) ?? '', operations: operations.filter((operation) => operation.tag === name) }))
      .filter((tag) => tag.operations.length > 0),
  }))
    .filter((group) => group.tags.length > 0);
}

/** How many operations the public document holds. */
export function apiOperationCount(): number {
  return apiTagGroups().reduce((sum, group) => sum + group.tags.reduce((count, tag) => count + tag.operations.length, 0), 0);
}

export interface WebhookEvent {
  type: string;
  summary: string;
  description: string;
  /** The sample delivery body, indented for reading. */
  sample: string;
}

interface WebhookOperation {
  summary: string;
  description: string;
  requestBody: { content: { 'application/json': { example: unknown } } };
}

/** Every webhook event, in the catalogue's order, with a sample delivery. */
export function webhookEvents(): WebhookEvent[] {
  return Object.entries(openApi.webhooks as unknown as Record<string, { post: WebhookOperation }>).map(([type, item]) => ({
    type,
    summary: item.post.summary,
    // The document adds the delivery note to every event's description; the page says it once.
    description: item.post.description.split('\n\n')[0] ?? '',
    sample: JSON.stringify(item.post.requestBody.content['application/json'].example, null, 2),
  }));
}

/** A description written for Scalar, which reads Markdown, split into text and `code` parts for a page to draw. */
export function inlineCode(text: string): { text: string; code: boolean }[] {
  return text.split('`').map((part, index) => ({ text: part, code: index % 2 === 1 })).filter((part) => part.text.length > 0);
}

export interface McpTool {
  name: string;
  title: string;
  description: string;
  access: string;
  /** Always: every call asks the person. Rejection: only a move to a Rejected stage does (its confirm is optional). */
  confirms: 'always' | 'rejection' | 'never';
  example: string;
}

/** The MCP tools by the access that shows them, in the catalogue's order. */
export function mcpToolGroups(): { access: string; tools: McpTool[] }[] {
  const tools = mcpCatalogue.tools.map((tool) => ({
    name: tool.name,
    title: tool.title,
    description: tool.description,
    access: tool.access,
    confirms: !tool.confirms ? ('never' as const) : tool.inputSchema.required?.includes('confirm') ? ('always' as const) : ('rejection' as const),
    example: JSON.stringify(tool.example.arguments, null, 2),
  }));
  return ['Read', 'Write', 'Verification read'].map((access) => ({ access, tools: tools.filter((tool) => tool.access === access) }));
}

/** The path the MCP server answers on, from the catalogue. */
export const mcpPath = mcpCatalogue.path;
