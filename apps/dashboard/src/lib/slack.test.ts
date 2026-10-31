import { describe, expect, it } from 'vitest';
import { isSlackOutcome, slackEvents, slackFailure, workspaceStatus } from './slack.js';

describe('Slack helpers', () => {
  it('lists the four events a channel can get, in words', () => {
    expect(slackEvents.map((option) => option.label)).toEqual(['New application', 'Candidate moved', 'Automation failed', 'Job published']);
  });

  it('marks a connected workspace calm and one with a problem as needing a fix', () => {
    expect(workspaceStatus(null)).toEqual({ status: 'done', label: 'Connected' });
    expect(workspaceStatus('Disconnected')).toEqual({ status: 'needsFixes', label: 'Disconnected' });
    expect(workspaceStatus('ChannelArchived')).toEqual({ status: 'needsFixes', label: 'Not posting' });
  });

  it('words each failed round trip, and nothing for a success or an unknown value', () => {
    expect(slackFailure('denied')).toMatch(/cancelled/);
    expect(slackFailure('wrong-workspace')).toMatch(/another workspace/);
    expect(slackFailure('connected')).toBeUndefined();
    expect(slackFailure('linked')).toBeUndefined();
    expect(slackFailure('toString')).toBeUndefined();
    expect(slackFailure(null)).toBeUndefined();
    expect(isSlackOutcome('linked')).toBe(true);
    expect(isSlackOutcome('expired')).toBe(true);
    expect(isSlackOutcome('nope')).toBe(false);
  });
});
