import mcpCatalogue from '@screenhire/api-client/mcp-tools.json';
import openApi from '@screenhire/api-client/openapi-public.json';
import { describe, expect, it } from 'vitest';
import vector from '../developers/samples/signature-vector.json';
import { verifyScreenHireSignature } from '../developers/samples/verify-signature.mjs';
import { apiOrigin, apiTagGroups, exampleApiUrl, inlineCode, mcpToolGroups, webhookEvents } from './developers';

// A delivery signed by the API's own signer (WebhookSignature.Create), with a fictional secret. The C# unit test
// SignatureSampleTests reads the same file and checks it against the signer and the C# sample, so the samples and the API agree.

describe('developer docs data', () => {
  it('lists every operation of the public document once, under its group', () => {
    const listed = apiTagGroups().flatMap((group) => group.tags.flatMap((tag) => tag.operations.map((op) => `${op.method} ${op.path}`)));
    const inDocument = Object.entries(openApi.paths).flatMap(([path, item]) =>
      Object.keys(item)
        .filter((method) => ['get', 'post', 'put', 'patch', 'delete'].includes(method))
        .map((method) => `${method.toUpperCase()} ${path}`),
    );
    expect(listed.sort()).toEqual(inDocument.sort());
    expect(apiTagGroups().flatMap((group) => group.tags).every((tag) => tag.operations.every((op) => op.summary.length > 0))).toBe(true);
  });

  it('describes every webhook event with a sample of its own type', () => {
    const events = webhookEvents();
    expect(events.map((event) => event.type)).toEqual(Object.keys(openApi.webhooks));
    for (const event of events) {
      expect(JSON.parse(event.sample).type).toBe(event.type);
      expect(event.description).not.toContain('Check the `ScreenHire-Signature`');
    }
  });

  it('groups every MCP tool by its access and says which ask first', () => {
    const tools = mcpToolGroups().flatMap((group) => group.tools);
    expect(tools.map((tool) => tool.name).sort()).toEqual(mcpCatalogue.tools.map((tool) => tool.name).sort());
    expect(tools.find((tool) => tool.name === 'send_email')?.confirms).toBe('always');
    expect(tools.find((tool) => tool.name === 'move_to_stage')?.confirms).toBe('rejection');
    expect(tools.find((tool) => tool.name === 'find_jobs')?.confirms).toBe('never');
  });

  it('draws Markdown code spans in a description as code', () => {
    expect(inlineCode('`startsAt` is the booked time.')).toEqual([
      { text: 'startsAt', code: true },
      { text: ' is the booked time.', code: false },
    ]);
  });

  it('uses the configured API address, or the example domain', () => {
    expect(apiOrigin(undefined)).toBe(exampleApiUrl);
    expect(apiOrigin('https://api.acme.test/')).toBe('https://api.acme.test');
  });
});

describe('the Node signature sample', () => {
  it('accepts a delivery the API signed and refuses a changed body, a wrong secret or an old time', () => {
    expect(verifyScreenHireSignature(vector.body, vector.header, vector.secret, 300, vector.sentAt + 10)).toBe(true);
    expect(verifyScreenHireSignature(`${vector.body} `, vector.header, vector.secret, 300, vector.sentAt)).toBe(false);
    expect(verifyScreenHireSignature(vector.body, vector.header, `${vector.secret}0`, 300, vector.sentAt)).toBe(false);
    expect(verifyScreenHireSignature(vector.body, vector.header, vector.secret, 300, vector.sentAt + 301)).toBe(false);
    expect(verifyScreenHireSignature(vector.body, 'v1=nothing', vector.secret, 300, vector.sentAt)).toBe(false);
  });
});
