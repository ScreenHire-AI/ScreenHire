# Integrating `main` into a task branch

Several tasks run side by side, and all of them touch the same hot files. Integrate with a merge, not a rebase:
`git fetch origin main` then `git merge origin/main --no-edit`. One merge resolves each conflict once, where a rebase
replays it for every commit, and the Merge column squashes the branch anyway.

In a merge from the task branch, `--ours` is the task branch and `--theirs` is `main`.

## Generated and derived files: take `main`, then regenerate

| File | Resolve with |
|---|---|
| `pnpm-lock.yaml` | `git checkout --theirs pnpm-lock.yaml`, finish the other files, then `pnpm install` (not frozen) so the branch's dependencies are re-resolved on top. Stage the result. |
| `packages/api-client/openapi.json`, `packages/api-client/src/generated/**` | `git checkout --theirs` on each, finish the code conflicts, then `pnpm generate:api`. Stage the result. |
| EF Core migrations (`src/ScreenHire.Infrastructure/Persistence/Migrations/**`) | Never edit a migration that is already on `main`. If both sides added migrations: take `main`'s `ScreenHireDbContextModelSnapshot.cs` (`--theirs`), delete the branch's own migration files (`<timestamp>_<Name>.cs` and `.Designer.cs`), conclude the merge, then re-create the branch's migration with the same name (the command is in CLAUDE.md). The new migration is generated against the merged model, so it contains only this branch's changes. |

## Shared source files: keep both sides

`Directory.Packages.props`, `package.json` files, `pnpm-workspace.yaml`, `ScreenHire.slnx`, `apps/api/Program.cs`,
`apps/apphost/AppHost.cs`, `ScreenHireDbContext.cs`, `CLAUDE.md`, `docs/**`: almost every conflict here is two
additions. Keep both, in a sensible order. When both sides pin the same package to different versions, keep the
higher one. When both sides changed the same line for different reasons, combine the intent and say so in the commit.

## Finish

1. `git diff --check` and a search for `<<<<<<<` must come back clean.
2. `git commit --no-edit` to conclude the merge.
3. Run `/verify` (`pnpm verify:fast` and the branch's added or changed tests). A merge that builds is not a merge
   that works, so the push that follows runs CI's whole suite on the merged code before anything lands.

Never resolve wholesale with `-X ours` or `-X theirs`, and never abandon a merge without trying. If a conflict
needs a product decision you cannot make with confidence, `git merge --abort`, explain the conflict, and end with the
skill's blocked outcome line.
