using Microsoft.Extensions.Options;
using ScreenHire.Api.Configuration;
using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Organizations;

namespace ScreenHire.UnitTests.Auth;

public sealed class CreateApiKeyRequestValidatorTests
{
    private readonly CreateApiKeyRequestValidator validator = new(Options.Create(new ApiKeyOptions { PermitsPerMinute = 600 }));

    [Theory]
    [InlineData(new[] { ApiKeyScope.Read }, null)]
    [InlineData(new[] { ApiKeyScope.Read, ApiKeyScope.Write }, 600)]
    [InlineData(new[] { ApiKeyScope.Write, ApiKeyScope.Read }, 1)]
    public void A_named_key_with_read_and_a_limit_up_to_the_deployments_is_accepted(ApiKeyScope[] scopes, int? permits) =>
        validator.Validate(new CreateApiKeyRequest("ATS sync", scopes, permits)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_key_needs_a_name(string name) =>
        Errors(new CreateApiKeyRequest(name, [ApiKeyScope.Read])).Should().Contain(nameof(CreateApiKeyRequest.Name));

    [Fact]
    public void A_name_longer_than_the_column_is_refused() =>
        Errors(new CreateApiKeyRequest(new string('a', ApiKey.MaxNameLength + 1), [ApiKeyScope.Read]))
            .Should().Contain(nameof(CreateApiKeyRequest.Name));

    [Fact]
    public void A_write_only_key_is_refused_because_every_key_reads() =>
        Errors(new CreateApiKeyRequest("Writer", [ApiKeyScope.Write])).Should().Contain(nameof(CreateApiKeyRequest.Scopes));

    [Fact]
    public void A_scope_listed_twice_is_refused() =>
        Errors(new CreateApiKeyRequest("Twice", [ApiKeyScope.Read, ApiKeyScope.Read])).Should().Contain(nameof(CreateApiKeyRequest.Scopes));

    // JSON numbers bind to enums too: [0, 7] arrives as Read and an undefined scope.
    [Fact]
    public void A_scope_that_does_not_exist_is_refused() =>
        Errors(new CreateApiKeyRequest("Numbers", [ApiKeyScope.Read, (ApiKeyScope)7])).Should().Contain(nameof(CreateApiKeyRequest.Scopes));

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void A_limit_outside_one_to_the_deployments_is_refused(int permits) =>
        Errors(new CreateApiKeyRequest("Fast", [ApiKeyScope.Read], permits)).Should().Contain(nameof(CreateApiKeyRequest.PermitsPerMinute));

    private List<string> Errors(CreateApiKeyRequest request) => [.. validator.Validate(request).Errors.Select(e => e.PropertyName)];
}
