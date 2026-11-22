using Polly.CircuitBreaker;
using Polly.Timeout;
using ScreenHire.Api.Fonts;

namespace ScreenHire.UnitTests.Organizations;

/// <summary>
/// What hosting a heading font and drawing its preview accept from the font source (docs/architecture/branding.md
/// section 3). A source failure must refuse the save with its message, not fail it with a 500.
/// </summary>
public sealed class FontDownloadsTests
{
    [Fact]
    public void The_resilience_handlers_timeout_and_open_circuit_count_as_an_unreachable_source()
    {
        // The standard resilience handler throws these, and neither is an HttpRequestException.
        FontDownloads.IsUnreachable(new TimeoutRejectedException()).Should().BeTrue();
        FontDownloads.IsUnreachable(new BrokenCircuitException()).Should().BeTrue();
        FontDownloads.IsUnreachable(new HttpRequestException("503")).Should().BeTrue();
        FontDownloads.IsUnreachable(new InvalidOperationException("a bug")).Should().BeFalse("a bug in our own code stays a 500");
    }

    [Fact]
    public void A_file_is_kept_only_when_it_is_woff2_and_not_empty_or_over_two_megabytes()
    {
        byte[] Woff2(int length)
        {
            var bytes = new byte[length];
            "wOF2"u8.CopyTo(bytes);
            return bytes;
        }

        FontDownloads.IsWoff2(Woff2(1024)).Should().BeTrue();
        FontDownloads.IsWoff2(Woff2(FontDownloads.MaxFileBytes + 1)).Should().BeFalse();
        FontDownloads.IsWoff2([]).Should().BeFalse();
        FontDownloads.IsWoff2("wOFF and more"u8.ToArray()).Should().BeFalse("WOFF 1 is not what the careers pages load");
    }
}
