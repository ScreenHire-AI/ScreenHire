namespace ScreenHire.Api.Contracts;

/// <summary>
/// What is serving this API: the product name, the build version, the hosting environment, and the API's public origin
/// (<c>ScreenHire:Domains:ApiHost</c>), which the API reference (<c>/scalar</c>), the OpenAPI file and <c>/v1</c> sit under.
/// <c>careersUrlPattern</c> is a tenant's careers address with <c>{tenant}</c> where its slug goes, such as
/// <c>https://{tenant}.careers.example.com</c> (<c>ScreenHire:Domains:CareersHostPattern</c>), so the dashboard can show the
/// address a name will get.
/// </summary>
public sealed record ApiMeta(string Name, string Version, string Environment, string ApiOrigin, string CareersUrlPattern);
