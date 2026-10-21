using System.Diagnostics;
using System.Text.Json.Nodes;

namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// The process guard (<c>scripts/guard-processes.mjs</c>), run the way Claude Code runs a PreToolUse hook: the tool call
/// as JSON on stdin, a deny decision as JSON on stdout, nothing when the command may run. A pattern that is too broad
/// blocks every agent's ordinary commands, so the allowed rows matter as much as the denied ones.
/// </summary>
public sealed class GuardProcessesTests
{
    private const string DockerExe = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";

    public static TheoryData<string, string> DockerLaunches => new()
    {
        { "PowerShell", $"Start-Process \"{DockerExe}\"" },
        { "PowerShell", "Start-Process -FilePath 'Docker Desktop.exe' -WorkingDirectory $dir" },
        { "PowerShell", $"saps \"{DockerExe}\"" },
        { "PowerShell", $"& \"{DockerExe}\"" },
        { "PowerShell", $"if (-not (docker info)) {{ & '{DockerExe}' }}" },
        { "PowerShell", $". \"{DockerExe}\"" },
        { "PowerShell", $"Invoke-Item \"{DockerExe}\"" },
        { "PowerShell", $"ii \"{DockerExe}\"" },
        { "PowerShell", $"explorer.exe \"{DockerExe}\"" },
        { "Bash", $"\"{DockerExe}\" &" },
        { "Bash", "/c/Program\\ Files/Docker/Docker/Docker\\ Desktop.exe &" },
        { "Bash", $"cmd //c start \"\" \"{DockerExe}\"" },
        { "Bash", $"docker info || \"{DockerExe}\"" },
        { "Bash", $"cmd.exe //c \"{DockerExe}\"" },
        { "PowerShell", $"cmd /c \"{DockerExe}\"" },
        { "Bash", "docker desktop start" },
        { "PowerShell", "docker desktop restart --timeout 120" },
    };

    public static TheoryData<string, string> StopsByName => new()
    {
        { "PowerShell", "taskkill /IM node.exe /F" },
        { "PowerShell", "Get-Process chrome | Stop-Process" },
        { "PowerShell", "Stop-Process -Name dotnet" },
        { "Bash", "pkill -f vite" },
        // PowerShell has no backslash escape: this bar is a pipe, unlike a grep alternation in Bash.
        { "PowerShell", @"Get-Process node \| Stop-Process" },
    };

    public static TheoryData<string, string> Allowed => new()
    {
        { "PowerShell", $"Test-Path \"{DockerExe}\"" },
        { "PowerShell", "Get-Process \"Docker Desktop\" -ErrorAction SilentlyContinue" },
        { "PowerShell", $"(Get-Item \"{DockerExe}\").VersionInfo" },
        { "PowerShell", $"Start-Sleep 1; Test-Path \"{DockerExe}\"" },
        { "PowerShell", "Start-Process notepad; Get-Process 'Docker Desktop'" },
        { "PowerShell", "foreach ($n in 'WindowsTerminal.exe','explorer.exe','Docker Desktop.exe') { Get-Process -Name $n }" },
        { "Bash", "docker desktop status" },
        { "Bash", "docker info --format '{{.ServerVersion}}'" },
        { "Bash", "pnpm docker:start" },
        { "Bash", "grep -rn \"Docker Desktop\" docs" },
        { "Bash", "grep -E \"executable.pid\\|stop-process-tree\" dcp.log" },
        { "Bash", "grep -ih \"tree\\|kill\" *.log" },
        { "PowerShell", "Select-String -Path dcp.log -Pattern 'a|stop-process-tree'" },
    };

    [Theory]
    [MemberData(nameof(DockerLaunches))]
    public async Task Launching_Docker_Desktop_directly_is_denied_with_the_script_as_the_route(string tool, string command)
    {
        var reason = await DenyReasonAsync(tool, command);

        reason.Should().NotBeNull($"`{command}` launches Docker Desktop");
        reason.Should().Contain("pnpm docker:start");
    }

    [Theory]
    [MemberData(nameof(StopsByName))]
    public async Task Stopping_processes_by_name_is_still_denied(string tool, string command)
    {
        var reason = await DenyReasonAsync(tool, command);

        reason.Should().NotBeNull($"`{command}` stops processes by name");
        reason.Should().Contain("except the gate");
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public async Task Read_only_commands_run(string tool, string command)
    {
        (await DenyReasonAsync(tool, command)).Should().BeNull($"`{command}` only reads");
    }

    private static async Task<string?> DenyReasonAsync(string tool, string command)
    {
        var start = new ProcessStartInfo("node", [RepositoryFiles.PathTo("scripts", "guard-processes.mjs")])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        var input = new JsonObject
        {
            ["tool_name"] = tool,
            ["tool_input"] = new JsonObject { ["command"] = command },
            ["cwd"] = RepositoryFiles.PathTo(),
        };
        await process.StandardInput.WriteAsync(input.ToJsonString());
        process.StandardInput.Close();

        var cancellationToken = TestContext.Current.CancellationToken;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        process.ExitCode.Should().Be(0, error);

        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var decision = JsonNode.Parse(output)!["hookSpecificOutput"]!;
        decision["permissionDecision"]!.GetValue<string>().Should().Be("deny");
        return decision["permissionDecisionReason"]!.GetValue<string>();
    }
}
