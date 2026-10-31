namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// No committed file holds a home-directory path, a real email address, a secret or the committer's own name
/// (<c>.claude/rules/no-personal-info.md</c>, <see cref="PersonalInfoScan"/>). The red cases build their values at run
/// time, so this file never matches its own scan.
/// </summary>
public sealed class PersonalInfoTests
{
    private static readonly string[] NoIdentity = [];

    [Fact]
    public void No_file_holds_personal_info_or_a_secret()
    {
        var (files, findings) = PersonalInfoScan.ScanRepository();

        files.Should().BeGreaterThan(1000, "the scan reads every file git would commit");
        findings.Should().BeEmpty(
            "personal data and secrets never go in the repository (.claude/rules/no-personal-info.md); use a placeholder. Found:\n{0}",
            string.Join('\n', findings));
    }

    public static TheoryData<string, string> Findings => new()
    {
        { "home path", "C:" + @"\Users\" + "alex" + @"\src" },
        { "home path", "C:" + "/Users/" + "alex/src" },
        { "home path", "cd /c" + "/Users/" + "alex/src" },
        { "home path", "/mnt/c" + "/Users/" + "alex" },
        { "home path", "/home" + "/alex/.config" },
        { "home path", "open /Users" + "/alex/Desktop" },
        { "email", "write to avery" + "@" + "example.co.uk" },
        { "email", "noreply" + "@" + "gmail.com" },
        { "anthropic key", "sk-ant-" + "api03-" + new string('a', 24) },
        { "openai-style key", "sk-" + "proj-" + new string('b', 40) },
        { "aws access key", "AKIA" + "ABCDEFGHIJKLMNOP" },
        { "github token", "ghp_" + new string('c', 36) },
        { "github token", "github_pat_" + new string('d', 40) },
        { "slack token", "xoxb-" + "1234567890-1234567890123-" + new string('s', 24) },
        { "private key", "-----BEGIN " + "RSA PRIVATE KEY-----" },
        { "google api key", "AIza" + new string('e', 35) },
        { "jwt", "eyJ" + new string('f', 20) + ".eyJ" + new string('g', 20) + "." + new string('h', 12) },
        { "azure storage key", "AccountKey" + "=" + new string('k', 86) + "==" },
        { "password in a connection string", "Host=db;Password" + "=hunter22;" },
        { "credentials in a url", "https://user:" + "hunter22" + "@db.internal.net/" },
    };

    [Theory]
    [MemberData(nameof(Findings))]
    public void A_planted_value_is_found(string kind, string text)
    {
        var finding = PersonalInfoScan.Scan("docs/notes.md", $"first line\n{text}\n", NoIdentity).Should().ContainSingle().Subject;

        finding.Kind.Should().Be(kind);
        finding.Line.Should().Be(2);
        finding.Excerpt.Should().HaveLength(4, "a finding shows only the start of its match, because CI logs are public");
        text.Should().Contain(finding.Excerpt);
    }

    [Theory]
    [InlineData(@"C:\Users\dev\src\ScreenHire")]
    [InlineData("/home/dev/src and /Users/dev/src and /c/Users/dev")]
    [InlineData(@"C:\Users\<name> and %USERPROFILE% and ~/src")]
    [InlineData("avery@brightwater.example.com, x@careers.example.org, a@b.example.net, c@d.example, e@f.test, g@h.invalid, i@j.localhost")]
    [InlineData("12345+bot@users.noreply.github.com, noreply@github.com, noreply@anthropic.com")]
    [InlineData("icon@2x.png")]
    [InlineData("Bearer xoxb-fictional-token")]
    [InlineData("https://user:secret@hooks.example.com/in and http://admin:admin@localhost:8080/")]
    [InlineData("Password={appRolePassword};Password=$PASSWORD;Password=<secret>")]
    [InlineData("AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==")]
    public void A_placeholder_or_reserved_value_is_allowed(string text)
    {
        PersonalInfoScan.Scan("docs/notes.md", text, NoIdentity).Should().BeEmpty();
    }

    [Fact]
    public void An_exempt_path_is_not_scanned()
    {
        var token = "ghp_" + new string('c', 36);

        PersonalInfoScan.Scan("pnpm-lock.yaml", token, NoIdentity).Should().BeEmpty();
        PersonalInfoScan.Scan("apps/api/packages.lock.json", token, NoIdentity).Should().BeEmpty();
        PersonalInfoScan.Scan("src/ScreenHire.Infrastructure/Verification/Data/disposable-email-domains.txt", token, NoIdentity).Should().BeEmpty();
        PersonalInfoScan.Scan("docs/pnpm-lock.yaml.md", token, NoIdentity).Should().ContainSingle();
    }

    [Fact]
    public void The_committers_name_and_address_are_found_whole_never_as_single_words()
    {
        var name = "Robin" + " Ashgrove";
        var email = "robin.ashgrove" + "@" + "mail.example.com";
        var identity = PersonalInfoScan.Identity(name, email, "git" + "@" + "github.com:Brightwater-Studio/ScreenHire.git");

        PersonalInfoScan.Scan("README.md", $"Written by {name.ToUpperInvariant()}.", identity)
            .Should().ContainSingle().Which.Kind.Should().Be("committer identity (git config)");
        PersonalInfoScan.Scan("README.md", $"Contact {email}", identity).Should().ContainSingle();
        PersonalInfoScan.Scan("README.md", "Robin wrote it; the Ashgrove office reviewed it.", identity).Should().BeEmpty();
    }

    [Theory]
    [InlineData("git" + "@" + "github.com:Octo-Cat/ScreenHire.git")]
    [InlineData("https://github.com/Octo-Cat/ScreenHire")]
    [InlineData("https://github.com/Octo-Cat/ScreenHire.git")]
    public void The_repositorys_own_owner_and_slug_are_public(string origin)
    {
        PersonalInfoScan.Identity("Octo-Cat", "octo-cat/screenhire", origin).Should().BeEmpty();
        PersonalInfoScan.Identity("Octo Cat", null, origin).Should().Equal("Octo Cat");
    }

    [Fact]
    public void No_git_identity_checks_nothing()
    {
        PersonalInfoScan.Identity(null, null, null).Should().BeEmpty();
    }
}
