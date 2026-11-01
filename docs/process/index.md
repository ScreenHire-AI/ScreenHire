# Process

How work moves through this repository: the board, the tests and the Claude Code tooling.

* [Board pipeline (Kangentic)](board-pipeline.md) - How a Kangentic task moves through Executing, Code Review, Testing and Merge: what each column runs, the outcome lines and the silent stall, notifications and unblocking, and processes on the dev machine (Aspire teardown, Docker Desktop, a gate stopped with TaskStop).
* [Claude Code tooling](dev-tooling.md) - Every skill, agent and rule in .claude/ and when to use each, commands that were never run, why pnpm's global virtual store was not adopted for worktree installs, where a new task's start-up time goes and what would cut it, what a preview stack commits in memory and why working set hides it, and how to add a new skill, agent or rule.
* [Testing](testing.md) - Which test tier proves what (xUnit, Vitest, Aspire integration, Playwright), running one test and a branch's own tests, the shared AppHostFixture, isolating data by organization, golden files, the marketing JS and Lighthouse checks, CI on GitHub Actions as the board's gate and how to read a failed run, the flake policy and its history, and gate time locally and in CI.
