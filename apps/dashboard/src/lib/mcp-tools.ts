import catalogue from '@screenhire/api-client/mcp-tools.json';

/** One MCP tool as the MCP server page lists it: its name for the reference's anchor and its title in words. */
export interface McpToolChip {
  name: string;
  title: string;
  confirms: boolean;
}

/**
 * The MCP server's tools by the access that shows them (docs/architecture/mcp-server.md), from the catalogue the API's
 * tests write (packages/api-client/mcp-tools.json), so the page never lists a tool the server lacks. Each links to its
 * entry in the tools reference the API serves at /mcp-tools.
 */
export const mcpTools = {
  read: chips('Read'),
  write: chips('Write'),
  verification: chips('Verification read'),
};

/** The address of one tool's entry in the reference, on the API's origin. */
export function mcpToolHref(apiOrigin: string, name: string): string {
  return `${apiOrigin}/mcp-tools#${encodeURIComponent(name)}`;
}

function chips(access: string): McpToolChip[] {
  return catalogue.tools.filter((tool) => tool.access === access).map(({ name, title, confirms }) => ({ name, title, confirms }));
}
