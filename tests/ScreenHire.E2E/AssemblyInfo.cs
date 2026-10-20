// One AppHost and one browser for the whole assembly; every test gets its own browser context (see BrowserTest).
[assembly: AssemblyFixture(typeof(ScreenHire.E2E.E2EFixture))]
