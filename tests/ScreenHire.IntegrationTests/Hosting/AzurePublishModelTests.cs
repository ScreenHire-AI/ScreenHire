using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace ScreenHire.IntegrationTests.Hosting;

/// <summary>
/// The publish model (apps/apphost/AzurePublish.cs), built and never started. A deployment gives parameters their values
/// through configuration, and the generated Bicep cannot show whether it did: the values arrive at deploy time.
/// </summary>
public sealed class AzurePublishModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("auth-google-client-id")]
    [InlineData("careers-allowed-domains")]
    [InlineData("verification-llm-extraction-model")]
    [InlineData("auth-forward-limit")]
    public async Task An_optional_parameter_takes_the_deployments_value(string name)
    {
        var parameters = await ParametersAsync($"--Parameters:{name}=from-the-deployment");

        (await parameters.Single(parameter => parameter.Name == name).GetValueAsync(Ct)).Should().Be("from-the-deployment");
    }

    [Theory]
    [InlineData("auth-google-client-id", "")]
    [InlineData("auth-forward-limit", "1")]
    [InlineData("google-indexing-mode", "Off")]
    [InlineData("verification-llm-provider", "None")]
    public async Task An_optional_parameter_keeps_its_fallback_when_the_deployment_gives_none(string name, string fallback)
    {
        var parameters = await ParametersAsync();

        (await parameters.Single(parameter => parameter.Name == name).GetValueAsync(Ct)).Should().Be(fallback);
    }

    private static async Task<IReadOnlyList<ParameterResource>> ParametersAsync(params string[] configuration)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ScreenHire_AppHost>(
            ["--operation", "publish", "--publisher", "default", "--output-path", Path.GetTempPath(), .. configuration], Ct);
        return [.. builder.Resources.OfType<ParameterResource>()];
    }
}
