/**
 * Lets `milliseconds` pass so a test can then show that something did not happen in them: a control that must not
 * appear, a second request, a navigation. There is nothing to wait on when the point is that nothing comes, so this is
 * the one fixed wait a test may make, and `because` names what must not happen. Everything else waits on a condition
 * (`waitFor`, `findBy...`), never on time (docs/process/testing.md, "Flake policy"); `TestSuiteRulesTests` fails on a
 * timer sleep written out in a test.
 */
export function nothingHappensFor(milliseconds: number, because: string): Promise<void> {
  if (because.trim() === '') throw new Error('Say what must not happen in the window.');
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
