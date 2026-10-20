import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import contract from '../contract/events.json' with { type: 'json' };
import { parseRealtimeEvent, realtimeEventSchemas } from './events.js';

// contract/events.json is a sample of every C# tenant event, serialized exactly as the hub sends it. The .NET test
// RealtimeContractTests writes and checks it; this side parses it, so the two cannot drift apart unnoticed.
describe('realtime event contract', () => {
  it('has a schema for every event the API sends, and no schema the API does not send', () => {
    expect(contract.map((event) => event.type).sort()).toEqual(Object.keys(realtimeEventSchemas).sort());
  });

  it.each(contract)('parses $type as the API serializes it', (sample) => {
    expect(parseRealtimeEvent(sample)).toEqual(sample);
  });

  it.each(contract)('knows every property of $type', (sample) => {
    const schema = realtimeEventSchemas[sample.type as keyof typeof realtimeEventSchemas];
    // Strict only here: a property the C# record gained must reach the schema too. At runtime extra properties are
    // dropped, so a newer API never breaks an open tab.
    expect(() => z.strictObject(schema.shape).parse(sample.data)).not.toThrow();
  });
});

describe('parseRealtimeEvent', () => {
  const jobPublished = {
    type: 'JobPublished',
    data: {
      organizationId: '0199f0c4-0000-7000-8000-000000000001',
      jobId: '0199f0c4-0000-7000-8000-000000000002',
      publishedAt: '2026-10-20T09:00:00+00:00',
    },
  };

  it('types the data by the event type', () => {
    const event = parseRealtimeEvent(jobPublished);

    expect(event?.type).toBe('JobPublished');
    if (event?.type === 'JobPublished') {
      expect(event.data.jobId).toBe('0199f0c4-0000-7000-8000-000000000002');
    }
  });

  it('ignores an event type it does not know', () => {
    expect(parseRealtimeEvent({ type: 'SomethingNew', data: {} })).toBeNull();
  });

  it('throws when a known event does not match its schema', () => {
    expect(() => parseRealtimeEvent({ ...jobPublished, data: { ...jobPublished.data, jobId: 'not-a-uuid' } })).toThrow();
  });

  it('throws on something that is not an event at all', () => {
    expect(() => parseRealtimeEvent('JobPublished')).toThrow();
  });
});
