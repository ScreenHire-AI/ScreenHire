using System.Text.RegularExpressions;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The pull-request skill names the PR body's file by an absolute path inside the task's worktree. A relative
/// <c>.kangentic/PR_BODY.tmp</c> is resolved against the project root by the Write tool, which put five agents' bodies in
/// the root checkout, where a stale file from another task was the only thing that stopped the write.
/// </summary>
public sealed partial class PullRequestSkillTests
{
    [Fact]
    public void Every_use_of_the_PR_body_file_names_it_inside_the_worktree()
    {
        var skill = File.ReadAllText(RepositoryFiles.PathTo(".claude", "skills", "pull-request", "SKILL.md"));

        var uses = BodyFile().Matches(skill);

        uses.Should().NotBeEmpty();
        uses.Should().AllSatisfy(use => use.Groups["root"].Value.Should().Be("<worktree>/", $"'{use.Value}' must not be relative"));
    }

    [GeneratedRegex(@"(?<root><worktree>/)?\.kangentic/PR_BODY\.tmp")]
    private static partial Regex BodyFile();
}
