using Microsoft.Extensions.Logging.Abstractions;
using ScreenHire.Application.Applications;
using ScreenHire.Application.Verification.Content;

namespace ScreenHire.UnitTests.Applications;

/// <summary>Erasing a candidate deletes their uploaded files from storage; a redelivery or a repeated name does no harm.</summary>
public sealed class EraseApplicantFilesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_file_is_deleted_once_even_when_a_name_is_listed_twice()
    {
        var store = new RecordingStore();
        var message = new EraseApplicantFiles(Guid.CreateVersion7(), ["org/job/resume/1/cv.pdf", "org/job/resume/2/cv.pdf", "org/job/resume/1/cv.pdf"]);

        await EraseApplicantFilesHandler.Handle(message, store, NullLogger<EraseApplicantFiles>.Instance, Ct);

        store.Deleted.Should().Equal("org/job/resume/1/cv.pdf", "org/job/resume/2/cv.pdf");
    }

    [Fact]
    public async Task Names_that_differ_only_in_case_are_different_blobs()
    {
        var store = new RecordingStore();

        await EraseApplicantFilesHandler.Handle(
            new EraseApplicantFiles(Guid.CreateVersion7(), ["org/CV.pdf", "org/cv.pdf"]), store, NullLogger<EraseApplicantFiles>.Instance, Ct);

        store.Deleted.Should().HaveCount(2);
    }

    [Fact]
    public async Task No_files_means_no_deletes()
    {
        var store = new RecordingStore();

        await EraseApplicantFilesHandler.Handle(new EraseApplicantFiles(Guid.CreateVersion7(), []), store, NullLogger<EraseApplicantFiles>.Instance, Ct);

        store.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task The_handlers_cancellation_token_reaches_the_store()
    {
        var store = new RecordingStore();
        using var source = new CancellationTokenSource();

        await EraseApplicantFilesHandler.Handle(
            new EraseApplicantFiles(Guid.CreateVersion7(), ["org/cv.pdf"]), store, NullLogger<EraseApplicantFiles>.Instance, source.Token);

        store.Tokens.Should().ContainSingle().Which.Should().Be(source.Token);
    }

    [Fact]
    public void Where_no_storage_is_configured_a_delete_is_already_complete()
    {
        var task = new NoApplicantFileStore().DeleteAsync("org/cv.pdf", Ct);

        task.IsCompletedSuccessfully.Should().BeTrue("there is nothing to delete and nothing to wait for");
    }

    private sealed class RecordingStore : IApplicantFileStore
    {
        public List<string> Deleted { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<StoredFile> ReadAsync(string blobName, long maxBytes, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Erasing never reads a file.");

        public Task DeleteAsync(string blobName, CancellationToken cancellationToken)
        {
            Deleted.Add(blobName);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
