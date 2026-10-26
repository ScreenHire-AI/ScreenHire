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
its area), `licence-check` when `Directory.Packages.props` or a `package.json` changed, `migration-safety` when
`src/ScreenHire.Infrastructure/Persistence/` or `src/ScreenHire.Domain/` changed, and `design-review` when
`apps/dashboard/`, `apps/careers/`, `apps/marketing/` or `packages/ui/` changed (it holds screens to
`docs/design/design-system.md`, the one source of truth for UI). When `apps/dashboard/` changed, hold
`design-review` back from the batch and start it at the end of "Look at dashboard changes" below, with what you
saw in its prompt. They go deeper on the
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
   - frontend: runes only, `#lib` imports, tokens only, status icon plus colour plus label, WCAG 2.2 AA, public
     pages at about 0 KB JS; the design system's checklist is `design-review`'s, not this dimension's;
   - backend: TypedResults, one `Map*Endpoints` per area, DTO records, strict numbers, Wolverine instead of
     hosted-service loops;
   - contracts regenerated, migrations sane and named, tests at the right tier;
   - docs and CLAUDE.md updated when behaviour or commands changed;
   - no dead code, no duplicated helpers.

### Look at dashboard changes

Only when the diff touches `apps/dashboard/`. Do this while the batch above runs. The design-review agent cannot
drive a browser, so you take the screenshots and measurements and it judges them.

1. Run `/preview` (the Skill tool). It starts the stack and signs in as Avery Quinn in the task's browser pane. If
   Docker is not running, the stack does not come up or the `kangentic_browser_*` tools are missing, skip to step 5
   without notes and add a row to the findings table: "visual pass not run", with the reason. That alone is never a
   reason for `Verdict: Blocked`.
2. Pick the pages, at most six, each once, in this order (past six, drop from the end and say so in the findings
   table):
   - `/style-guide`, always;
   - each changed route under `apps/dashboard/src/routes/`, as its URL without the `(group)` folders. Fill a
     `[param]` from a link on the page above it with `kangentic_browser_query_all` and `includeHtml`: for `[jobId]`
     the `href` of "Senior .NET Engineer" among `a[href^="/jobs/"]` on `/` (the seeded job with candidates and
     integrity chips, as `/preview` step 5 finds it); for `[applicationId]` the first `a[href^="/applications/"]` on
     that job's page; for `[templateId]` the first `a[href^="/templates/"]` on `/templates`. If no link matches,
     skip the page and say so in the findings table. Leave `(auth)` routes to the code review: the signed-in pane
     redirects away from them;
   - when no route changed (only `src/lib/`, `app.css` or a layout), `/` and the "Senior .NET Engineer" job page,
     found as above.
3. For each page, `kangentic_browser_navigate` to it and `kangentic_browser_wait` for `h1` (repeat the wait once if
   it fails with a driver error mid-navigation). Then at 1440x900, 768x1024 and 390x844:
   1. `kangentic_browser_set_viewport` with that width and height;
   2. `kangentic_browser_screenshot`. Its result names the saved image (`source: ...`); keep that path. If the image
      is blank, take it again: the first paint after a navigation can lag. If a result names no file, the notes
      carry what you saw in it instead;
   3. `kangentic_browser_eval` with the full text of [`visual-probe.js`](visual-probe.js) on the first width of each
      page load, and `visualProbe()` on the others. It returns raw measurements as JSON. If eval is refused (a
      Kangentic setting, Agent Browser > Allow eval), go on with screenshots alone and say so in the notes.

   Where the change sits below the first screen, `kangentic_browser_scroll` to it and take one more screenshot.
4. Write notes for each page and width: the URL, the width, the screenshot paths, the probe's JSON, and what you see.
   Describe the navigation at that size, the layers, whether panels in a row share a top edge, text or controls that
   wrap or are cut off, and anything that scrolls sideways. Describe; leave the judging to the agent. Write the
   notes to a file in your scratchpad, never in the repository: screenshots and probe text show demo candidates.
5. Start `design-review` with the diff range, the brief and the notes. It reads the screenshots itself and ends by
   listing the paths it opened. If that list misses screenshots, send it back with SendMessage to open them before
   you use its findings.
6. Put the pane back: `kangentic_browser_set_viewport` with `reset: true`, then `kangentic_browser_close_pane`. Run
   `/preview stop` and its confirmation steps now, before any build in section 3: a running stack holds the API's
   and worker's build output.

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

1. A findings table: file:line, severity, finding, and resolution (fixed / refuted / sent back). A visual finding
   also names its page and width.
2. The changes and tests you committed.
3. If you started the stack and it still runs (you started it again to check a fix), run `/preview stop` and its
   confirmation steps now. No outcome line while it runs.
4. The closing block, which must be the last thing in the message:
   - All clear: the line `Verdict: Ready`.
   - Otherwise: numbered steps the implementer must do, each concrete and checkable, then the line
     `Verdict: Blocked`.

The board routes on that last line in code (Ready goes to Testing, Blocked goes back to Executing), so write nothing
after it and never soften or rename it.
