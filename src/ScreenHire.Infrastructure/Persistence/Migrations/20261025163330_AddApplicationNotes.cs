using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Notes that can be edited and deleted (design system section 22): <c>application_notes</c> holds each note's latest
    /// text and <c>application_note_mentions</c> the people it mentions. The timeline stays append-only and names a note
    /// by id, so deleting a note erases its text. Notes written before this migration move out of their timeline entries.
    /// </summary>
    public partial class AddApplicationNotes : Migration
    {
        // Row-level security like every tenant table (AddSignInAndTenantIsolation); the application role edits and
        // deletes notes, so it keeps UPDATE and DELETE here. The backfill copies each older note's text out of its
        // NoteAdded entry (the note takes the entry's id) and leaves the entry naming the note instead: the one change
        // ever made to existing timeline rows, so that deleting those notes erases them too. Row-level security is
        // forced on application_events and its policy matches no row without a tenant, so FORCE is off around the
        // backfill (AddSoftwareEngineerTemplateVersion2).
        private const string BackfillAndEnableRowLevelSecurity = """
            ALTER TABLE application_events NO FORCE ROW LEVEL SECURITY;

            INSERT INTO application_notes (id, organization_id, application_id, author_user_id, author_api_key_id, text, created_at)
            SELECT e.id, e.organization_id, e.application_id, e.actor_user_id, (e.data ->> 'apiKeyId')::uuid, left(e.data ->> 'note', 2000), e.occurred_at
            FROM application_events e
            WHERE e.type = 'NoteAdded' AND e.data ? 'note';

            UPDATE application_events
            SET data = (data - 'note') || jsonb_build_object('noteId', id)
            WHERE type = 'NoteAdded' AND data ? 'note';

            ALTER TABLE application_events FORCE ROW LEVEL SECURITY;

            ALTER TABLE application_notes ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_notes FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_notes
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

            ALTER TABLE application_note_mentions ENABLE ROW LEVEL SECURITY;
            ALTER TABLE application_note_mentions FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON application_note_mentions
                USING (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK (organization_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            """;

        // Back to text in the timeline: each surviving note's text returns to its NoteAdded entry. A deleted note's text is
        // gone for good, so its entry keeps no text and loses its note id, which the earlier model would read as a note
        // that never existed. The edit and delete entries go too, on purpose: the earlier model cannot read their types,
        // so the record of who deleted a note is lost on a rollback.
        private const string RestoreNotes = """
            ALTER TABLE application_events NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE application_notes NO FORCE ROW LEVEL SECURITY;

            UPDATE application_events e
            SET data = (e.data - 'noteId') || jsonb_build_object('note', n.text)
            FROM application_notes n
            WHERE e.type = 'NoteAdded' AND n.id = (e.data ->> 'noteId')::uuid;

            UPDATE application_events SET data = data - 'noteId' WHERE type = 'NoteAdded' AND NOT data ? 'note';

            DELETE FROM application_events WHERE type IN ('NoteEdited', 'NoteDeleted');

            ALTER TABLE application_events FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_notes", x => x.id);
                    table.UniqueConstraint("ak_application_notes_organization_id_id", x => new { x.organization_id, x.id });
                    table.ForeignKey(
                        name: "fk_application_notes_applications_organization_id_application_",
                        columns: x => new { x.organization_id, x.application_id },
                        principalTable: "applications",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "application_note_mentions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emailed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_note_mentions", x => x.id);
                    table.ForeignKey(
                        name: "fk_application_note_mentions_application_notes_organization_id",
                        columns: x => new { x.organization_id, x.note_id },
                        principalTable: "application_notes",
                        principalColumns: new[] { "organization_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_note_mentions_organization_id_note_id_user_id",
                table: "application_note_mentions",
                columns: new[] { "organization_id", "note_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_notes_organization_id_application_id",
                table: "application_notes",
                columns: new[] { "organization_id", "application_id" });

            migrationBuilder.Sql(BackfillAndEnableRowLevelSecurity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RestoreNotes);

            migrationBuilder.DropTable(
                name: "application_note_mentions");

            migrationBuilder.DropTable(
                name: "application_notes");
        }
    }
}
