namespace ScreenHire.Api.Contracts;

/// <summary>
/// What is serving this API: the product name, the build version, the hosting environment, and the API's public origin
/// (<c>ScreenHire:Domains:ApiHost</c>), which the API reference (<c>/scalar</c>), the OpenAPI file and <c>/v1</c> sit under.
/// </summary>
public sealed record ApiMeta(string Name, string Version, string Environment, string ApiOrigin);
