using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScreenHire.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A fourth starting point for the new hiring template screen, "Hospitality" (cafes, restaurants and bars), and a second
    /// version of "Trade role with a trial shift" whose Trial shift stage names the built-in Trial shift scorecard
    /// (<c>SystemTemplates</c> holds the ids). Both are system templates with no organization, so every organization sees them,
    /// the ones that exist already included. A published version never changes: a job already on Trade role version 1 keeps it
    /// and sees "update available". FORCE row-level security is off around the inserts as in AddStartingPointTemplates and
    /// AddFollowUpQuestionnaires, because the policies let nobody write a row without an organization.
    /// </summary>
    /// <remarks>
    /// Hospitality version 1 is Applied, Screening, Trial shift, Offer and the three end stages. It carries the right-to-work
    /// knockout every starting point has (an objective eligibility check, which only queues for review), a questionnaire
    /// emailed on entering Trial shift, a reminder for the team on every stage with a time limit and nothing that rejects anyone
    /// on a check. It cannot carry an "Offer an interview" automation, because that names interviewers, who are an
    /// organization's members: the recruiter offers times from the candidate's page. Trade role version 2 is version 1 with
    /// <c>scorecardTemplateId</c> set on its <c>trial_shift</c> stage (stage 2), and the INSERT does nothing if version 1 is not
    /// as expected.
    /// </remarks>
    public partial class AddHospitalityTemplate : Migration
    {
        private const string InsertHospitalityAndTradeRoleVersion2 = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            INSERT INTO hiring_templates (id, organization_id, name, description, created_at, latest_published_version) VALUES
                ('0199e8a0-0000-7000-8000-000000000006', NULL, 'Hospitality', 'Barista, line cook, server', '2026-11-21T00:00:00Z', 1);

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at) VALUES
                ('0199e8a0-0000-7000-8000-000000000601', NULL, '0199e8a0-0000-7000-8000-000000000006', 1, 'Published',
                 '{"workflow":{"stages":[{"id":"applied","name":"Applied","kind":"Applied"},{"id":"screening","name":"Screening","kind":"Screening","slaDays":2,"scorecardTemplateId":"screening"},{"id":"trial_shift","name":"Trial shift","kind":"Assessment","slaDays":5,"scorecardTemplateId":"trial-shift"},{"id":"offer","name":"Offer","kind":"Offer","slaDays":2},{"id":"hired","name":"Hired","kind":"Hired"},{"id":"rejected","name":"Rejected","kind":"Rejected"},{"id":"withdrawn","name":"Withdrawn","kind":"Withdrawn"}],"questionnaire":{"questions":[{"id":"resume","type":"File","label":"Your CV or resume","required":false,"acceptedFileTypes":[".pdf",".docx"],"helpText":"Optional."},{"id":"work_authorization","type":"YesNo","label":"Do you have the right to work in the country this job is in?","required":true,"knockout":{"disqualifyingAnswer":false,"reason":"This role needs the right to work in the country it is based in."}},{"id":"shifts","type":"MultiChoice","label":"Which shifts can you work?","required":true,"options":[{"id":"weekday_mornings","label":"Weekday mornings"},{"id":"weekday_evenings","label":"Weekday evenings"},{"id":"weekends","label":"Weekends"}]},{"id":"hospitality_experience","type":"SingleChoice","label":"How long have you worked in hospitality?","required":true,"options":[{"id":"none","label":"I have not yet"},{"id":"under_2","label":"Under 2 years"},{"id":"two_to_five","label":"2 to 5 years"},{"id":"over_5","label":"More than 5 years"}]},{"id":"food_hygiene","type":"YesNo","label":"Do you hold a current food hygiene certificate?","required":false},{"id":"start_date","type":"ShortText","label":"When could you start?","required":true,"maxLength":100}]},"verificationPolicy":{"aiUse":"AllowedWithDisclosure"},"automations":[{"id":"thanks_for_applying","name":"Thank them for applying","trigger":{"type":"ApplicationSubmitted"},"actions":[{"type":"SendEmail","emailTemplate":"application-received"}],"enabled":true,"allowAutoReject":false},{"id":"right_to_work_review","name":"Queue for review if no right to work","trigger":{"type":"ApplicationSubmitted"},"conditions":{"match":"All","rules":[{"fact":"answers.work_authorization","operator":"EqualTo","value":"no","negate":false}]},"actions":[{"type":"QueueForRejectionReview","reason":"Answered no to the right-to-work question."}],"enabled":true,"allowAutoReject":false},{"id":"before_trial_shift","name":"Ask for details before the trial shift","trigger":{"type":"StageEntered","stageId":"trial_shift"},"actions":[{"type":"SendQuestionnaire","questionnaireId":"before_trial_shift"}],"enabled":true,"allowAutoReject":false},{"id":"reminder-screening","name":"Remind the team when Screening waits too long","trigger":{"type":"SlaBreached","stageId":"screening"},"actions":[{"type":"NotifyTeam","note":"Someone has waited in Screening longer than the time limit."}],"enabled":true,"allowAutoReject":false},{"id":"reminder-trial_shift","name":"Remind the team when Trial shift waits too long","trigger":{"type":"SlaBreached","stageId":"trial_shift"},"actions":[{"type":"NotifyTeam","note":"Someone has waited in Trial shift longer than the time limit."}],"enabled":true,"allowAutoReject":false},{"id":"reminder-offer","name":"Remind the team when Offer waits too long","trigger":{"type":"SlaBreached","stageId":"offer"},"actions":[{"type":"NotifyTeam","note":"Someone has waited in Offer longer than the time limit."}],"enabled":true,"allowAutoReject":false}],"followUpQuestionnaires":[{"id":"before_trial_shift","name":"Before your trial shift","questions":[{"id":"trial_day","type":"SingleChoice","label":"Which day suits you best for the trial shift?","required":true,"options":[{"id":"weekday","label":"A weekday"},{"id":"weekend","label":"A weekend day"}]},{"id":"trial_travel","type":"ShortText","label":"How will you get to the shift?","required":true,"maxLength":200},{"id":"trial_notes","type":"LongText","label":"Is there anything we should know before the day?","required":false,"maxLength":1000}]}]},"postingDefaults":{"employmentType":"PartTime","workplaceType":"Onsite","salaryPeriod":"Hour","validForDays":21,"description":"<h2>About the role</h2><p>Describe the work, the place and the team.</p><h2>Shifts and pay</h2><p>Say which shifts are available and how pay works.</p><h2>The trial shift</h2><p>Say what a paid trial shift involves.</p>"}}',
                 '2026-11-21T00:00:00Z', '2026-11-21T00:00:00Z');

            INSERT INTO hiring_template_versions (id, organization_id, template_id, version, status, definition, created_at, published_at)
            SELECT '0199e8a0-0000-7000-8000-000000000402', NULL, v.template_id, 2, 'Published',
                   jsonb_set(v.definition, '{workflow,stages,2,scorecardTemplateId}', '"trial-shift"'::jsonb),
                   '2026-11-21T00:00:00Z', '2026-11-21T00:00:00Z'
            FROM hiring_template_versions v
            WHERE v.id = '0199e8a0-0000-7000-8000-000000000401'
              AND v.definition #>> '{workflow,stages,2,id}' = 'trial_shift'
              AND v.definition #> '{workflow,stages,2,scorecardTemplateId}' IS NULL;

            UPDATE hiring_templates SET latest_published_version = 2
            WHERE id = '0199e8a0-0000-7000-8000-000000000004' AND latest_published_version < 2
              AND EXISTS (SELECT FROM hiring_template_versions WHERE id = '0199e8a0-0000-7000-8000-000000000402');

            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        private const string DeleteHospitalityAndTradeRoleVersion2 = """
            ALTER TABLE hiring_template_versions NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_templates NO FORCE ROW LEVEL SECURITY;

            UPDATE hiring_templates SET latest_published_version = 1 WHERE id = '0199e8a0-0000-7000-8000-000000000004';
            DELETE FROM hiring_template_versions WHERE id IN ('0199e8a0-0000-7000-8000-000000000601', '0199e8a0-0000-7000-8000-000000000402');
            DELETE FROM hiring_templates WHERE id = '0199e8a0-0000-7000-8000-000000000006';

            ALTER TABLE hiring_templates FORCE ROW LEVEL SECURITY;
            ALTER TABLE hiring_template_versions FORCE ROW LEVEL SECURITY;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(InsertHospitalityAndTradeRoleVersion2);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DeleteHospitalityAndTradeRoleVersion2);
    }
}
