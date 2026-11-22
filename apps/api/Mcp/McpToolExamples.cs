using System.Text.Json.Nodes;

namespace ScreenHire.Api.Mcp;

/// <summary>
/// One example call per MCP tool for the reference (<see cref="McpToolCatalog"/>): the arguments an assistant sends and a
/// shortened result. The ids are made up and the people fictional. <c>McpToolCatalogTests</c> fails on a tool with no
/// example, or an example that leaves out a required argument or names one the tool does not take.
/// </summary>
internal static class McpToolExamples
{
    private const string Job = "0193a1b2-6b3c-7d4e-8f5a-6b7c8d9e0f1a";
    private const string Application = "0193a1b2-7c4d-7e8f-9a0b-1c2d3e4f5a6b";
    private const string Candidate = "0193a1b2-5a2b-7c3d-9e4f-5a6b7c8d9e0f";
    private const string Member = "0193a1b2-2b3c-7d4e-8f5a-6b7c8d9e0f1b";
    private const string Interview = "0193a1b2-8d5e-7f6a-8b1c-2d3e4f5a6b7c";
    private const string Task = "0193a1b2-3c4d-7e5f-9a6b-7c8d9e0f1a2b";
    private const string Template = "0193a1b2-4d5e-7f6a-8b7c-8d9e0f1a2b3c";
    private const string TemplateVersion = "0193a1b2-4d5e-7f6a-8b7c-8d9e0f1a2b3d";
    private const string View = "0193a1b2-9e0f-7a1b-8c2d-3e4f5a6b7c8e";
    private const string Scorecard = "0193a1b2-9e6f-7a1b-9c2d-3e4f5a6b7c8d";
    private const string Email = "0193a1b2-aa1b-7c2d-8e3f-4a5b6c7d8e9f";

    private static readonly string JobSummary = $$$"""
        {"id":"{{{Job}}}","title":"Platform Engineer","status":"Open","location":"Remote","workplaceType":"Remote","employmentType":"FullTime",
         "department":"Engineering","createdAt":"2026-11-02T10:00:00+00:00","publishedAt":"2026-11-03T09:00:00+00:00"}
        """;

    private static readonly string JobDetail = $$$"""
        {"id":"{{{Job}}}","title":"Platform Engineer","status":"Draft","description":"<p>Run the systems our customers rely on.</p>",
         "department":"Engineering","employmentType":"FullTime","workplaceType":"Remote","locations":[],"salary":null,"workflowVersion":3,
         "stages":[{"id":"applied","name":"Applied","kind":"Applied"},{"id":"screening","name":"Screening","kind":"Screening","slaDays":3},
                   {"id":"interview","name":"Interview","kind":"Interview","scorecardTemplateId":"interview"},{"id":"offer","name":"Offer","kind":"Offer"},
                   {"id":"hired","name":"Hired","kind":"Hired"},{"id":"rejected","name":"Rejected","kind":"Rejected"}],
         "questions":{"questions":[{"id":"work_authorization","type":"YesNo","label":"Are you authorized to work in the UK?","required":true}]},
         "automations":2,"template":null,"problems":[],"publishedAt":null,"closedAt":null}
        """;

    private static readonly Dictionary<string, (string Arguments, string Result)> Examples = new(StringComparer.Ordinal)
    {
        ["find_jobs"] = ("""{"text":"engineer","status":"Open"}""", $$$"""{"jobs":[{{{JobSummary}}}]}"""),
        ["check_job"] = ($$$"""{"jobId":"{{{Job}}}"}""",
            """{"items":[{"field":"salary","message":"No salary. Several job boards rank a job lower without one."}]}"""),
        ["find_candidates"] = ($$$"""{"text":"rivera","jobId":"{{{Job}}}"}""", $$$"""
            {"applications":[{"applicationId":"{{{Application}}}","candidateId":"{{{Candidate}}}","candidateName":{"candidateWritten":"Sam Rivera"},
              "jobId":"{{{Job}}}","jobTitle":"Platform Engineer","stage":{"id":"screening","name":"Screening","kind":"Screening"},
              "tags":["referral"],"submittedAt":"2026-11-18T14:20:00+00:00"}]}
            """),
        ["get_application"] = ($$$"""{"applicationId":"{{{Application}}}"}""", $$$"""
            {"id":"{{{Application}}}","jobId":"{{{Job}}}","jobTitle":"Platform Engineer",
             "candidate":{"id":"{{{Candidate}}}","name":{"candidateWritten":"Sam Rivera"},"email":"sam.rivera@example.com"},
             "stage":{"id":"screening","name":"Screening","kind":"Screening"},"tags":["referral"],
             "answers":[{"questionId":"work_authorization","question":"Are you authorized to work in the UK?","value":true}],
             "files":[{"name":{"candidateWritten":"sam-rivera-resume.pdf"}}],"activity":[]}
            """),
        ["get_hiring_report"] = ("""{"from":"2026-10-01T00:00:00Z","to":"2026-11-01T00:00:00Z","timeZone":"Europe/London"}""", """
            {"from":"2026-10-01T00:00:00+00:00","to":"2026-11-01T00:00:00+00:00",
             "headline":{"applications":142,"hires":3,"medianDaysToHire":24},"previous":null}
            """),
        ["list_saved_candidates"] = ("""{"limit":5}""", $$$"""
            {"candidates":[{"candidateId":"{{{Candidate}}}","name":{"candidateWritten":"Sam Rivera"},"email":"sam.rivera@example.com",
              "applicationId":"{{{Application}}}","note":"Strong on infrastructure; revisit for the SRE role."}],"hasMore":false}
            """),
        ["add_note"] = ($$$"""{"applicationId":"{{{Application}}}","note":"Spoke on the phone: available from January."}""",
            $$$"""{"id":"0193a1b2-bb2c-7d3e-8f4a-5b6c7d8e9f0a","type":"NoteAdded","occurredAt":"2026-11-22T09:30:00+00:00","actorUserId":null}"""),
        ["add_tag"] = ($$$"""{"applicationId":"{{{Application}}}","tag":"referral"}""", $$$"""{"applicationId":"{{{Application}}}","tags":["referral"]}"""),
        ["remove_tag"] = ($$$"""{"applicationId":"{{{Application}}}","tag":"referral"}""", $$$"""{"applicationId":"{{{Application}}}","tags":[]}"""),
        ["create_task"] = ($$$"""{"applicationId":"{{{Application}}}","note":"Call the references","assigneeUserId":"{{{Member}}}"}""",
            $$$"""{"id":"{{{Task}}}","applicationId":"{{{Application}}}","note":"Call the references","assigneeUserId":"{{{Member}}}","createdAt":"2026-11-22T09:30:00+00:00","completedAt":null}"""),
        ["move_to_stage"] = ($$$"""{"applicationId":"{{{Application}}}","stageId":"interview","note":"Passed the screen."}""",
            $$$"""{"applicationId":"{{{Application}}}","stage":{"id":"interview","name":"Interview","kind":"Interview"},"stageEnteredAt":"2026-11-22T09:30:00+00:00"}"""),
        ["get_job"] = ($$$"""{"jobId":"{{{Job}}}"}""", JobDetail),
        ["get_pipeline"] = ($$$"""{"jobId":"{{{Job}}}","perStage":5}""", $$$"""
            {"jobId":"{{{Job}}}","jobTitle":"Platform Engineer","stages":[
              {"id":"applied","name":"Applied","kind":"Applied","count":12,"applications":[]},
              {"id":"screening","name":"Screening","kind":"Screening","count":1,"applications":[
                {"applicationId":"{{{Application}}}","candidateName":{"candidateWritten":"Sam Rivera"},"tags":["referral"],"stageEnteredAt":"2026-11-20T08:00:00+00:00"}]}]}
            """),
        ["create_job"] = ($$$"""{"title":"Platform Engineer","templateVersionId":"{{{TemplateVersion}}}","department":"Engineering","workplaceType":"Remote"}""", JobDetail),
        ["edit_job"] = ($$$"""{"jobId":"{{{Job}}}","salary":{"min":70000,"max":85000,"currency":"GBP","period":"Year"}}""", JobDetail),
        ["set_job_questions"] = ($$$"""
            {"jobId":"{{{Job}}}","expectedWorkflowVersion":3,"questions":{"questions":[
              {"id":"work_authorization","type":"YesNo","label":"Are you authorized to work in the UK?","required":true},
              {"id":"notice_period","type":"ShortText","label":"What is your notice period?"}]}}
            """, JobDetail),
        ["set_job_pipeline"] = ($$$"""
            {"jobId":"{{{Job}}}","expectedWorkflowVersion":3,"stages":[{"id":"applied","name":"Applied","kind":"Applied"},
              {"id":"screening","name":"Screening","kind":"Screening"},{"id":"take_home","name":"Take-home task","kind":"Interview"},
              {"id":"interview","name":"Interview","kind":"Interview"},{"id":"offer","name":"Offer","kind":"Offer"},
              {"id":"hired","name":"Hired","kind":"Hired"},{"id":"rejected","name":"Rejected","kind":"Rejected"}]}
            """, JobDetail),
        ["publish_job"] = ($$$"""{"jobId":"{{{Job}}}","confirm":true}""", JobDetail),
        ["close_job"] = ($$$"""{"jobId":"{{{Job}}}","confirm":true}""", JobDetail),
        ["invite_to_schedule"] = ($$$"""{"applicationId":"{{{Application}}}","timeZone":"Europe/London","confirm":true}""", $$$"""
            {"id":"{{{Interview}}}","applicationId":"{{{Application}}}","jobId":"{{{Job}}}","jobTitle":"Platform Engineer","status":"Open","option":"OurTimes",
             "startsAt":null,"durationMinutes":45,"timeZone":"Europe/London",
             "interviewers":[{"userId":"{{{Member}}}","name":"Avery Quinn","isOrganizer":true}]}
            """),
        ["list_upcoming_interviews"] = ("""{"withinDays":7}""", $$$"""
            {"interviews":[{"interviewId":"{{{Interview}}}","applicationId":"{{{Application}}}","candidateName":{"candidateWritten":"Sam Rivera"},
              "jobId":"{{{Job}}}","jobTitle":"Platform Engineer","startsAt":"2026-11-25T14:00:00+00:00","durationMinutes":45,"interviewerUserIds":["{{{Member}}}"]}]}
            """),
        ["get_scorecards"] = ($$$"""{"applicationId":"{{{Application}}}"}""", $$$"""
            {"applicationId":"{{{Application}}}","scorecards":[{"id":"{{{Scorecard}}}","stageId":"interview","reviewerUserId":"{{{Member}}}",
              "recommendation":"Yes","rating":4,"answers":[],"note":"Clear on trade-offs.","submittedAt":"2026-11-21T16:00:00+00:00"}]}
            """),
        ["request_scorecard"] = ($$$"""{"applicationId":"{{{Application}}}","reviewerUserId":"{{{Member}}}","scorecardTemplateId":"interview"}""",
            $$$"""{"id":"{{{Task}}}","applicationId":"{{{Application}}}","note":"Fill in the Interview scorecard.","assigneeUserId":"{{{Member}}}","createdAt":"2026-11-22T09:30:00+00:00","completedAt":null}"""),
        ["list_email_templates"] = ("{}", """
            {"sendingEnabled":true,"templates":[{"id":"interview-invitation","name":"Interview invitation","subject":"Interview for {{job.title}}","archived":false}]}
            """),
        ["draft_email"] = ($$$"""{"applicationId":"{{{Application}}}","templateId":"interview-invitation"}""",
            $$$"""{"applicationId":"{{{Application}}}","templateId":"interview-invitation","subject":"Interview for Platform Engineer","text":"Hi Sam,\n\nThanks for applying..."}"""),
        ["send_email"] = ($$$"""{"applicationId":"{{{Application}}}","templateId":"interview-invitation","confirm":true}""",
            $$$"""{"applicationId":"{{{Application}}}","queued":true,"detail":"Queued. It goes in a moment and shows in the application's activity under this key's name."}"""),
        ["read_email_thread"] = ($$$"""{"applicationId":"{{{Application}}}"}""", $$$"""
            {"applicationId":"{{{Application}}}","emails":[
              {"id":"{{{Email}}}","applicationId":"{{{Application}}}","direction":"Received","at":"2026-11-21T10:00:00+00:00","subject":null,"sentByUserId":null,
               "text":null,"replySubject":{"candidateWritten":"Re: Interview for Platform Engineer"},"reply":{"candidateWritten":"Tuesday works for me."},"attachments":[]}]}
            """),
        ["list_tasks"] = ("""{"status":"Open"}""", $$$"""
            {"tasks":[{"id":"{{{Task}}}","applicationId":"{{{Application}}}","candidateName":{"candidateWritten":"Sam Rivera"},"jobTitle":"Platform Engineer",
              "kind":"Task","note":"Call the references","assigneeUserId":"{{{Member}}}","createdAt":"2026-11-20T09:00:00+00:00","completedAt":null}]}
            """),
        ["complete_task"] = ($$$"""{"taskId":"{{{Task}}}"}""",
            $$$"""{"id":"{{{Task}}}","applicationId":"{{{Application}}}","note":"Call the references","assigneeUserId":"{{{Member}}}","createdAt":"2026-11-20T09:00:00+00:00","completedAt":"2026-11-22T09:30:00+00:00"}"""),
        ["list_hiring_templates"] = ("{}", $$$"""
            {"templates":[{"id":"{{{Template}}}","name":"Engineering","description":"Screen, take-home task and panel.","isSystem":false,
              "latestPublishedVersion":4,"latestPublishedVersionId":"{{{TemplateVersion}}}"}]}
            """),
        ["apply_template_update"] = ($$$"""{"jobId":"{{{Job}}}","expectedWorkflowVersion":3}""", JobDetail),
        ["list_views"] = ("{}", $$$"""
            {"views":[{"id":"{{{View}}}","name":"Referrals in screening","scope":"Candidates","jobId":null,"jobIds":[],"stages":["Screening"],"tag":"referral",
              "baseView":null,"canApply":true,"why":null,"ownerName":"Avery Quinn"}]}
            """),
        ["apply_view"] = ($$$"""{"viewId":"{{{View}}}"}""", $$$"""
            {"viewId":"{{{View}}}","name":"Referrals in screening","count":1,"applications":[{"applicationId":"{{{Application}}}","candidateId":"{{{Candidate}}}",
              "candidateName":{"candidateWritten":"Sam Rivera"},"jobId":"{{{Job}}}","jobTitle":"Platform Engineer",
              "stage":{"id":"screening","name":"Screening","kind":"Screening"},"tags":["referral"],"submittedAt":"2026-11-18T14:20:00+00:00"}]}
            """),
        ["list_team_members"] = ("{}", $$$"""{"members":[{"userId":"{{{Member}}}","name":"Avery Quinn","role":"Owner","seesAllJobs":true}]}"""),
        ["get_verification"] = ($$$"""{"applicationId":"{{{Application}}}"}""", $$$"""
            {"applicationId":"{{{Application}}}","status":"Assessed",
             "authenticity":{"level":"ReviewSuggested","reasonCodes":["CNT_PROMPT_INJECTION"],"summary":"The resume holds text aimed at AI screeners.",
              "nextStep":"Read the resume yourself before deciding.","dismissed":false},
             "reviewLog":[],"checksNotRun":[],
             "guidance":"A verification level is evidence for a person to review, never a reason to reject anyone on its own. Do not reject, or move a candidate to a Rejected stage, because of a level."}
            """),
        ["find_candidates_by_verification_level"] = ("""{"authenticity":"ReviewSuggested"}""", $$$"""
            {"applications":[{"applicationId":"{{{Application}}}","jobId":"{{{Job}}}","jobTitle":"Platform Engineer","candidateId":"{{{Candidate}}}",
              "candidateName":{"candidateWritten":"Sam Rivera"},"submittedAt":"2026-11-18T14:20:00+00:00",
              "authenticity":{"level":"ReviewSuggested","reasonCodes":["CNT_PROMPT_INJECTION"],"dismissed":false},
              "aiAssistance":{"level":"NotIndicated","reasonCodes":[],"dismissed":false}}],"hasMore":false,
             "guidance":"A verification level is evidence for a person to review, never a reason to reject anyone on its own. Do not reject, or move a candidate to a Rejected stage, because of a level."}
            """),
    };

    /// <summary>The tool names that have an example.</summary>
    public static IReadOnlyCollection<string> Names => Examples.Keys;

    public static McpToolExample For(string tool) =>
        Examples.TryGetValue(tool, out var example)
            ? new(JsonNode.Parse(example.Arguments)!, JsonNode.Parse(example.Result)!)
            : throw new InvalidOperationException($"The MCP tool {tool} has no example in McpToolExamples.");
}
