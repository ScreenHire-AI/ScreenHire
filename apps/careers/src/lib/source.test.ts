import { describe, expect, it } from 'vitest';
import { sourceBoard, withSource } from './source';

describe('the board that sent a candidate', () => {
  it('accepts a board id and nothing else', () => {
    expect(sourceBoard('talent')).toBe('talent');
    expect(sourceBoard('job-board-api')).toBe('job-board-api');
    expect(sourceBoard('Talent')).toBeNull();
    expect(sourceBoard('a b')).toBeNull();
    expect(sourceBoard('x'.repeat(41))).toBeNull();
    expect(sourceBoard('')).toBeNull();
    expect(sourceBoard(null)).toBeNull();
    expect(sourceBoard(undefined)).toBeNull();
  });

  it('keeps it on a link', () => {
    expect(withSource('/acme/jobs/dev/apply', 'talent')).toBe('/acme/jobs/dev/apply?src=talent');
    expect(withSource('/acme/jobs/dev/apply?embed=https%3A%2F%2Fwww.example.com', 'adzuna')).toBe(
      '/acme/jobs/dev/apply?embed=https%3A%2F%2Fwww.example.com&src=adzuna',
    );
    expect(withSource('/a#top', 'jooble')).toBe('/a?src=jooble#top');
  });

  it('leaves a link alone when no board sent the candidate or the value is not a board id', () => {
    expect(withSource('/a', null)).toBe('/a');
    expect(withSource('/a', 'not a board')).toBe('/a');
  });
});
