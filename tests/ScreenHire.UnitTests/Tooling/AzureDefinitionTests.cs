namespace ScreenHire.UnitTests.Tooling;

/// <summary>
/// What the generated Bicep under infra/ must keep. The infra CI job proves it matches the publish model, not that the
/// model is right, and an Aspire upgrade can bring a default back.
/// </summary>
public sealed class AzureDefinitionTests
{
    private static string Infra(params string[] path) => File.ReadAllText(RepositoryFiles.PathTo(["infra", .. path]));

    [Fact]
    public void No_app_has_a_role_on_the_vault_that_holds_the_owners_connection_strings()
    {
        Infra("postgres", "postgres.bicep").Should().Contain("administratorLoginPassword}", "the owner's password is in this vault");

        Infra("main.bicep").Should().NotContain("roles_postgres_kv",
            "the API and worker connect as screenhire_app, bound by row-level security, and must not be able to read the owner's login");
    }

    [Theory]
    [InlineData("api-roles-storage")]
    [InlineData("worker-roles-storage")]
    public void The_apps_have_the_blob_role_on_storage_and_no_other(string module)
    {
        const string storageBlobDataContributor = "ba92f5b4-2d11-453d-a403-e96b0029c9fe";

        var bicep = Infra(module, $"{module}.bicep");

        bicep.Should().Contain(storageBlobDataContributor);
        bicep.Split("'Microsoft.Authorization/roleAssignments").Should().HaveCount(2, "one role assignment");
    }
}
