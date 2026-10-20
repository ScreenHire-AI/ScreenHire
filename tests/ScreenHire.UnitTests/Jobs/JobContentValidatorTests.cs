using ScreenHire.Api.Contracts;
using ScreenHire.Domain.Jobs;

namespace ScreenHire.UnitTests.Jobs;

public sealed class JobContentValidatorTests
{
    private readonly JobContentValidator validator = new();

    // Salaries are stored as numeric(12, 2). A value that does not fit is refused here, not left to fail on save.
    [Theory]
    [InlineData("9999999999.99", true)]
    [InlineData("10000000000", false)]
    [InlineData("1000.5", true)]
    [InlineData("1000.50", true)]
    [InlineData("1000.005", false)]
    public void A_salary_fits_its_column_or_is_refused(string amount, bool valid)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        validator.Validate(Content(new JobSalary(value, null, "GBP", SalaryPeriod.Year))).IsValid.Should().Be(valid);
        validator.Validate(Content(new JobSalary(null, value, "GBP", SalaryPeriod.Year))).IsValid.Should().Be(valid);
    }

    private static JobSnapshot Content(JobSalary salary) =>
        new("Data Engineer", "data-engineer", "<p>Build pipelines.</p>", null, EmploymentType.FullTime, WorkplaceType.Remote, [], ["GB"], salary, null);
}
