---
paths:
  - "src/**"
  - "apps/api/**"
  - "apps/worker/**"
---
# Rule: timestamps are UTC instants

Hiring has deadlines, audit trails and legal retention periods (`docs/research/legal.md`), so every stored time must
be an unambiguous instant. A local or zone-less time shifts by the server's offset and cannot be audited.

## The rule

- Model times as `DateTimeOffset`, always with offset zero. Postgres stores them as `timestamp with time zone`
  (Npgsql's default mapping for `DateTimeOffset`); check the migration SQL says so.
- Get "now" from the injected `TimeProvider` (`timeProvider.GetUtcNow()`), never `DateTime.Now`, `DateTime.UtcNow`
  or `DateTimeOffset.Now`. `TimeProvider` is registered in `apps/api/Program.cs`. Domain types have no framework
  dependencies, so their methods take the time as a parameter (`Job.Publish(DateTimeOffset now)`), and unit tests
  pass a fixed value. `Job.CreatedAt` defaults to `DateTimeOffset.UtcNow` as a creation stamp; new code sets such
  stamps from `TimeProvider` instead, so tests can control them.
- Postgres keeps times to the microsecond, .NET to 100 ns. The API registers `DatabasePrecisionTimeProvider`, so
  "now" already has the database's precision. A time from anywhere else (a request body) that is compared after a
  database round trip, such as a field inside a frozen snapshot, is cut first with `ToDatabasePrecision()`
  (`src/ScreenHire.Domain/Jobs/Instants.cs`), as `Job.ValidThrough` does. Otherwise the copy never equals the row.
- Never use `DateTime` with `DateTimeKind.Local` or `Unspecified` for stored data, and never `timestamp without
  time zone` columns.
- Dates without a time (a start date, a date of birth if one is ever needed) are `DateOnly`, stored as `date`.
- Over the wire, times are ISO 8601 with an offset (`System.Text.Json` does this for `DateTimeOffset`). The
  frontends format them for display with `Intl.DateTimeFormat`; the API never sends pre-formatted dates.

## Enforcement

- **Review.** The `migration-safety` agent checks column types in new migrations, and `/task-review`'s correctness
  dimension checks time zones.
- No mechanical test yet. A scan for `DateTime.Now` / `DateTime.UtcNow` under `src/` and `apps/` is a candidate.

## Scope

Persisted and transmitted times in the .NET code. Display formatting in the frontends is not covered.
