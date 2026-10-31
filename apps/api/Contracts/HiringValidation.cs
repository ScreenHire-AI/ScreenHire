using FluentValidation;

namespace ScreenHire.Api.Contracts;

internal static class HiringValidation
{
    /// <summary>The validators of the jobs, templates and applications areas. Stateless, so singletons.</summary>
    public static IServiceCollection AddHiringValidation(this IServiceCollection services)
    {
        // Two validators of JobSnapshot, so these two are registered by their own types.
        services.AddSingleton<JobContentValidator>();
        services.AddSingleton<JobPublishValidator>();
        services.AddSingleton<IValidator<CreateHiringTemplateRequest>, CreateHiringTemplateRequestValidator>();
        services.AddSingleton<IValidator<UpdateHiringTemplateRequest>, TemplateNameValidator>();
        services.AddSingleton<IValidator<CreateApplicationRequest>, CreateApplicationRequestValidator>();
        services.AddSingleton<IValidator<MoveApplicationRequest>, MoveApplicationRequestValidator>();
        services.AddSingleton<IValidator<AddApplicationNoteRequest>, AddApplicationNoteRequestValidator>();
        services.AddSingleton<IValidator<EditApplicationNoteRequest>, EditApplicationNoteRequestValidator>();
        services.AddSingleton<IValidator<AddApplicationTagRequest>, AddApplicationTagRequestValidator>();
        services.AddSingleton<IValidator<CreateApplicationTaskRequest>, CreateApplicationTaskRequestValidator>();
        services.AddSingleton<IValidator<SubmitScorecardRequest>, SubmitScorecardRequestValidator>();
        services.AddSingleton<IValidator<RequestScorecardRequest>, RequestScorecardRequestValidator>();
        services.AddSingleton<IValidator<CreateScorecardTemplateRequest>, CreateScorecardTemplateRequestValidator>();
        services.AddSingleton<IValidator<UpdateScorecardTemplateRequest>, UpdateScorecardTemplateRequestValidator>();
        services.AddSingleton<IValidator<DismissIntegrityAssessmentRequest>, DismissIntegrityAssessmentRequestValidator>();
        services.AddSingleton<IValidator<ExplainIntegrityReasonRequest>, ExplainIntegrityReasonRequestValidator>();
        services.AddSingleton<IValidator<AddIntegrityNoteRequest>, AddIntegrityNoteRequestValidator>();
        return services;
    }
}
