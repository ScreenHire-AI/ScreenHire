namespace ScreenHire.Api.Contracts;

/// <summary>
/// One entry of a candidate's history across all of their applications: an entry of one application's timeline, with the
/// application and job it belongs to, newest first. It leaves out verification output (the Verification tab holds that,
/// ADR-0004) and what a reviewer scored or recommended (a scorecard entry says one was submitted, nothing more).
/// </summary>
/// <remarks>
/// Stages belong to each job's workflow, so the names come with the entry: <c>fromStageName</c> and <c>toStageName</c> name
/// the entry's <c>fromStageId</c> and <c>toStageId</c>, and <c>stageName</c> its <c>stageId</c>, as that job's workflow names
/// them now. Null when the entry has no such stage, or the workflow no longer has it.
/// </remarks>
public sealed record CandidateHistoryEntry(
    Guid ApplicationId,
    Guid JobId,
    string JobTitle,
    ApplicationTimelineEntry Entry,
    string? FromStageName = null,
    string? ToStageName = null,
    string? StageName = null);

/// <summary>
/// Erases a candidate. <c>confirmation</c> is the candidate's full name as the person typed it, compared ignoring case and
/// surrounding spaces, so an erasure is never one stray click.
/// </summary>
public sealed record EraseCandidateRequest(string Confirmation);
