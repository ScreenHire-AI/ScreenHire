import { describe, expect, it } from 'vitest';
import { embedSnippet } from './embed-snippet';

describe('embedSnippet', () => {
  it('points at the board script, whatever its trailing slash', () => {
    expect(embedSnippet('https://jobs.brightwater.example.com')).toBe('<script src="https://jobs.brightwater.example.com/embed.js" async></script>');
    expect(embedSnippet('https://careers.example/brightwater/')).toBe('<script src="https://careers.example/brightwater/embed.js" async></script>');
  });

  it('names the job when it has an address', () => {
    expect(embedSnippet('https://jobs.brightwater.example.com/', 'senior-barista')).toBe(
      '<script src="https://jobs.brightwater.example.com/embed.js" data-job="senior-barista" async></script>',
    );
  });
});
