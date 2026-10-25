import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { needsAttention, statuses, type StatusName } from './status.js';

describe('status catalogue', () => {
  it('gives each status the icon and colour of its row in design system section 3.2', () => {
    const map = Object.fromEntries(Object.entries(statuses).map(([name, view]) => [name, `${view.tone} ${view.iconName}`]));

    expect(map).toEqual({
      done: 'clear circle-check',
      open: 'clear door-open',
      review: 'review eye',
      overdue: 'review clock-alert',
      verification: 'verify id-card',
      needsFixes: 'verify wrench',
      unsafeFile: 'verify shield-x',
      ai: 'ai sparkles',
      waiting: 'neutral clock-4',
      working: 'neutral loader-circle',
      closed: 'neutral door-closed',
      draft: 'neutral file-pen-line',
      notInEffect: 'neutral circle-minus',
    });
  });

  it('tells every status apart by its icon alone (T1)', () => {
    const icons = Object.values(statuses).map((view) => view.iconName);

    expect(new Set(icons).size).toBe(icons.length);
  });

  it('uses only icons section 3.2 names, and never the warning triangle', () => {
    const doc = readFileSync(resolve(process.cwd(), '../../docs/design/design-system.md'), 'utf8');
    const table = doc.slice(doc.indexOf('### 3.2 Status'), doc.indexOf('### 3.3'));

    for (const view of Object.values(statuses)) expect(table).toContain(`\`${view.iconName}\``);
    expect(Object.values(statuses).map((view) => view.iconName)).not.toContain('triangle-alert');
  });

  it('keeps waiting states grey: nobody needs to act (T3)', () => {
    const attention = (Object.keys(statuses) as StatusName[]).filter(needsAttention);

    expect(attention.toSorted()).toEqual(['needsFixes', 'overdue', 'review', 'unsafeFile', 'verification']);
  });
});
