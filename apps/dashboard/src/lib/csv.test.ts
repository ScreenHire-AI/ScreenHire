import { describe, expect, it } from 'vitest';
import { toCsv } from '#lib/csv.js';

describe('report CSV', () => {
  it('writes a section per panel: title, header, rows, then a blank line', () => {
    expect(
      toCsv([
        { title: 'Funnel', header: ['Stage', 'Applications'], rows: [['Applied', 7], ['Hired', 3]] },
        { title: 'Sources', header: ['Source', 'Hires'], rows: [['Careers page', 2]] },
      ]),
    ).toBe('Funnel\r\nStage,Applications\r\nApplied,7\r\nHired,3\r\n\r\nSources\r\nSource,Hires\r\nCareers page,2\r\n');
  });

  it('quotes text with commas, quotes and line breaks, and leaves an empty cell empty', () => {
    expect(toCsv([{ title: 'T', header: ['A', 'B', 'C'], rows: [['Smith, "Sam"', 'two\nlines', null]] }])).toBe(
      'T\r\nA,B,C\r\n"Smith, ""Sam""","two\nlines",\r\n',
    );
  });

  it('keeps a spreadsheet from running a typed title as a formula, in text only', () => {
    const csv = toCsv([{ title: 'Jobs', header: ['Job', 'Change'], rows: [['=HYPERLINK("x")', -3], ['+1 role', 0], ['@home', 1.5], ['-dash', 2]] }]);
    expect(csv).toContain(`"'=HYPERLINK(""x"")",-3`);
    expect(csv).toContain(`'+1 role,0`);
    expect(csv).toContain(`'@home,1.5`);
    expect(csv).toContain(`'-dash,2`);
  });

  it('writes a number that is not finite as empty', () => {
    expect(toCsv([{ title: 'T', header: ['A'], rows: [[Number.NaN]] }])).toBe('T\r\nA\r\n\r\n');
  });
});
