namespace ScreenHire.Api.Contracts;

/// <summary>
/// The organization's tags (<c>GET /v1/tags</c>), most used first: every tag on an application the reader can see, and
/// every tag an automation names in a job that is not closed or in a hiring template's draft or newest published version.
/// </summary>
public sealed record TagList(IReadOnlyList<TagSummary> Tags);

/// <summary>
/// One tag, as it is spelled where it is used most. <c>applications</c> counts the applications that have it (on the jobs
/// the reader sees); a tag only an automation names has none. <c>templates</c> lists the hiring templates whose newest
/// published version names it: a published version never changes, so they keep naming it after a rename until a new
/// version is published.
/// </summary>
public sealed record TagSummary(string Name, int Applications, IReadOnlyList<TagTemplateUse> Templates);

/// <summary>A hiring template whose newest published version names a tag.</summary>
public sealed record TagTemplateUse(Guid TemplateId, string Name, int Version);

/// <summary>Renames a tag everywhere. <c>newName</c> must not be another tag already: merge into that one instead.</summary>
public sealed record RenameTagRequest(string Tag, string NewName);

/// <summary>Merges a tag into another existing tag: every application with <c>tag</c> gets <c>into</c> instead.</summary>
public sealed record MergeTagRequest(string Tag, string Into);

/// <summary>
/// What a rename, a merge or a delete changed at once: the pipelines of jobs that are not closed, the hiring templates'
/// drafts and the saved views that named the tag (none for a delete, which takes the tag off applications only). The
/// applications change a moment later, in batches, each raising ApplicationTagsChanged. <c>stillNamedBy</c> lists the
/// hiring templates whose newest published version still names the old tag.
/// </summary>
public sealed record TagChangeResult(int Jobs, int Drafts, int Views, IReadOnlyList<TagTemplateUse> StillNamedBy);
