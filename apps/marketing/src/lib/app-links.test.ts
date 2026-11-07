import { describe, expect, it } from 'vitest';
import { appLinks } from './app-links';

describe('appLinks', () => {
  it('sends Start free to the dashboard sign-in page, which is where a new account starts', () => {
    expect(appLinks('https://app.example.org', '/pricing/')).toEqual({
      signIn: 'https://app.example.org/',
      signUp: 'https://app.example.org/sign-in',
    });
  });

  it('ignores a path on the dashboard address', () => {
    expect(appLinks('https://app.example.org/somewhere/', '/pricing/').signUp).toBe('https://app.example.org/sign-in');
  });

  it('falls back to the given page, with no Sign in, when the dashboard is not configured', () => {
    expect(appLinks(undefined, '/pricing/')).toEqual({ signIn: undefined, signUp: '/pricing/' });
    expect(appLinks('', '/pricing/')).toEqual({ signIn: undefined, signUp: '/pricing/' });
  });
});
