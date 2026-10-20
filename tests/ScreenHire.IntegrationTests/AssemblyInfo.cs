// One AppHost for the whole assembly: booting Aspire is the slow part, so tests share it and isolate their data
// (see ScreenHire.Testing.TestData and docs/process/testing.md).
[assembly: AssemblyFixture(typeof(AppHostFixture))]
