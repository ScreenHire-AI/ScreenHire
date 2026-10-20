---
description: Code Review column skill. Independently review a task branch against its brief, ScreenHire's non-negotiables and conventions; fix what it finds, add missing tests, commit, and end on "Verdict: Ready" or numbered steps plus "Verdict: Blocked".
argument-hint: [base-branch]
---

# Task review

You are the Code Review column, running in a fresh conversation in the task's worktree. You did not write this code.
Review it as the person who will be paged when it breaks: against the task's brief, the docs it implements, and the
rules in CLAUDE.md and `.claude/rules/`.

You fix what you find. Only work that is too large or too uncertain to do inside a review goes back to the
implementer.

**Base branch:** `$ARGUMENTS` if given, else `git config kangentic.baseBranch`, else `main`.

## 1. Gather

1. `git fetch origin <base>`, then `git diff --stat origin/<base>...HEAD` and `git status --porcelain`. Uncommitted
   changes belong to the task: review them too.
2. Resolve the task with `kangentic_get_current_task` (pass this directory). Its description is the brief: the
   scope, the acceptance criteria and the docs it implements. Read those docs.
3. Read earlier review commits on the branch (`git log --oneline origin/<base>..HEAD`, subjects ending in
   `(review)`). Do not re-raise a finding an earlier pass refuted unless the code changed.

An empty diff means the task did nothing: that is `Verdict: Blocked` with "implement the brief" as the step.

## 2. Find

Launch three reviewers in parallel with the Agent tool (`review-finder`), each given the diff range, the brief and
its dimension. Tell each to read the changed files in full, report findings as `file:line`, severity
(blocker / major / minor), the evidence and the fix, and to change nothing.

In the same parallel batch, launch the specialists (`.claude/agents/`), each given the diff range and the brief:
`tenant-isolation-guard` and `integrity-guardrails` always (each says `NOT APPLICABLE` when the diff does not touch
its area), `licence-check` when `Directory.Packages.props` or a `package.json` changed, and `migration-safety` when
`src/ScreenHire.Infrastructure/Persistence/` or `src/ScreenHire.Domain/` changed. They go deeper on the
non-negotiables than dimension 2; dimension 2 still covers its whole list. If an agent type is not available in
this session, use `general-purpose` with the contents of `.claude/agents/<name>.md` as the start of its prompt.

1. **Correctness and completeness.** Does the code do what the brief says? Every acceptance criterion met and
   tested? Edge cases, error paths, concurrency, async misuse, N+1 queries, missing awaits, time zones (store UTC),
   null handling, and data that survives a restart.
2. **Non-negotiables and security** (CLAUDE.md, ADR-0004/0005, `docs/research/legal.md`):
   - tenant isolation: `OrganizationId` on every tenant row, named query filters, RLS, no cross-tenant reads;
   - authorization on every non-public endpoint;
   - integrity output as categorical levels with reasons and evidence, never scores or auto-reject, no
     emotion or biometric inference;
   - notice before collection, telemetry aggregates only;
   - no LinkedIn or third-party scraping;
   - no hard-coded domains;
   - secrets never in code or logs, PII kept out of logs;
   - input validation, CSRF and CORS on cookie-auth endpoints;
   - licence policy for every new package (ADR-0008).
3. **Conventions and maintainability:**
   - frontend: runes only, `#lib` imports, tokens only, status shape plus colour plus label, WCAG 2.2 AA, public
     pages at about 0 KB JS;
   - backend: TypedResults, one `Map*Endpoints` per area, DTO records, strict numbers, Wolverine instead of
     hosted-service loops;
   - contracts regenerated, migrations sane and named, tests at the right tier;
   - docs and CLAUDE.md updated when behaviour or commands changed;
   - no dead code, no duplicated helpers.

## 3. Verify and fix

1. Check every finding yourself against the code. Drop the ones that do not hold and list them as refuted.
2. Fix every confirmed finding you can do well inside a review: code, tests, docs. Add tests for any acceptance
   criterion or changed behaviour that has none (the `test-builder` agent in write mode picks the tier).
3. Run what proves the fixes: `pnpm verify:dotnet`, `pnpm check`, and the affected test projects or suites.
4. Commit review work separately from the implementer's: `fix(review): ...`, `test(review): ...`, `docs(review): ...`.
   If you confirmed nothing, make no commit.

Send work back (Blocked) only when:
- an acceptance criterion is unimplemented, or the design contradicts an ADR, and fixing it is a task-sized change;
- a fix needs a product decision the brief and docs do not settle;
- the build or tests are broken in a way you could not repair.

## 4. Report

1. A findings table: file:line, severity, finding, and resolution (fixed / refuted / sent back).
2. The changes and tests you committed.
3. The closing block, which must be the last thing in the message:
   - All clear: the line `Verdict: Ready`.
   - Otherwise: numbered steps the implementer must do, each concrete and checkable, then the line
     `Verdict: Blocked`.

The board routes on that last line in code (Ready goes to Testing, Blocked goes back to Executing), so write nothing
after it and never soften or rename it.
