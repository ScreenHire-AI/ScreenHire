import type { CurrentUser } from '@screenhire/api-client';
import { describe, expect, it } from 'vitest';
import { guardRedirect, safeReturnUrl } from './auth.js';

const signedIn = (organization: CurrentUser['organization']): CurrentUser => ({
  id: '01a157d4-0000-7000-8000-000000000001',
  email: 'avery@brightwater.example.com',
  displayName: 'Avery Quinn',
  organization,
});
const member = signedIn({ id: '01a157d4-0000-7000-8000-000000000002', name: 'Brightwater Studio', slug: 'brightwater', role: 'Owner' });
const newcomer = signedIn(null);
const url = (path: string) => new URL(path, 'http://localhost:4323');

describe('auth guard', () => {
  it('sends signed-out visitors to sign-in, remembering where they were going', () => {
    expect(guardRedirect('/(app)', null, url('/'))).toBe('/sign-in');
    expect(guardRedirect('/(app)/candidates', null, url('/candidates?stage=new'))).toBe(
      '/sign-in?returnUrl=%2Fcandidates%3Fstage%3Dnew',
    );
    expect(guardRedirect(null, null, url('/no-such-page'))).toBe('/sign-in?returnUrl=%2Fno-such-page');
  });

  it('lets signed-out visitors use the sign-in pages', () => {
    expect(guardRedirect('/(auth)/sign-in', null, url('/sign-in'))).toBeNull();
    expect(guardRedirect('/(auth)/sign-in/verify', null, url('/sign-in/verify#token=abc'))).toBeNull();
  });

  it('keeps onboarding for signed-in users', () => {
    expect(guardRedirect('/(auth)/onboarding', null, url('/onboarding'))).toBe('/sign-in?returnUrl=%2Fonboarding');
  });

  it('sends a user without an organization to onboarding, wherever they were going', () => {
    expect(guardRedirect('/(app)', newcomer, url('/'))).toBe('/onboarding');
    expect(guardRedirect('/(auth)/sign-in', newcomer, url('/sign-in'))).toBe('/onboarding');
    expect(guardRedirect('/(auth)/onboarding', newcomer, url('/onboarding'))).toBeNull();
  });

  it('sends a member away from sign-in and onboarding, to their return URL when it is local', () => {
    expect(guardRedirect('/(app)', member, url('/'))).toBeNull();
    expect(guardRedirect('/(auth)/sign-in', member, url('/sign-in?returnUrl=%2Fcandidates'))).toBe('/candidates');
    expect(guardRedirect('/(auth)/sign-in', member, url('/sign-in?returnUrl=https%3A%2F%2Fevil.example.org'))).toBe('/');
    expect(guardRedirect('/(auth)/onboarding', member, url('/onboarding'))).toBe('/');
  });
});

describe('safe return URL', () => {
  it.each([
    ['/jobs', '/jobs'],
    ['/jobs?view=open', '/jobs?view=open'],
    [null, '/'],
    ['', '/'],
    ['jobs', '/'],
    ['https://evil.example.org', '/'],
    ['//evil.example.org', '/'],
    ['/\\evil.example.org', '/'],
  ])('%s becomes %s', (value, expected) => {
    expect(safeReturnUrl(value)).toBe(expected);
  });
});
