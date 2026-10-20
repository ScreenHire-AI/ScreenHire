namespace ScreenHire.Api.Contracts;

/// <summary>What is serving this API: the product name, the build version and the hosting environment.</summary>
public sealed record ApiMeta(string Name, string Version, string Environment);
