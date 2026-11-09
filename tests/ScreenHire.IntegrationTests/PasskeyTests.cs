using System.Net.Http.Json;
using System.Text.Json;

namespace ScreenHire.IntegrationTests;

/// <summary>
/// Passkeys: registering one, signing in with it, listing and removing, and what is refused. The browser and the device
/// are <see cref="PasskeyAuthenticator"/>. The stack has no dashboard, so the API is its own app origin
/// (<c>ScreenHire:Domains:AppHost</c>, AppHost.cs), which is the origin and the relying party the authenticator answers for.
/// Every request to the two anonymous routes carries its own <c>X-Forwarded-For</c> (loopback is trusted), so no test shares
/// a rate-limit bucket with another.
/// </summary>
public sealed class PasskeyTests(AppHostFixture fixture)
{
    private const string SignInOptions = "/v1/auth/passkeys/sign-in/options";
    private const string SignIn = "/v1/auth/passkeys/sign-in";
    private const string RegisterOptions = "/v1/auth/passkeys/register/options";
    private const string Register = "/v1/auth/passkeys/register";
    private const string Passkeys = "/v1/auth/passkeys";

    // The default of ScreenHire:Auth:PasskeyPermitsPerMinute, which no test stack changes (the API project is not referenced here).
    private const int DefaultPasskeyLimit = 20;

    private static int addresses;

    [Fact]
    public async Task A_signed_in_person_registers_a_passkey_and_sees_it_listed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await SignedInAsync(cancellationToken);
        using var authenticator = NewAuthenticator(session);

        var summary = await RegisterAsync(session, authenticator, "Work laptop", cancellationToken);

        summary.GetProperty("id").GetString().Should().Be(authenticator.Id);
        summary.GetProperty("name").GetString().Should().Be("Work laptop");
        var list = await ListAsync(session, cancellationToken);
        list.GetProperty("limit").GetInt32().Should().Be(10);
        var passkey = list.GetProperty("passkeys").EnumerateArray().Should().ContainSingle().Subject;
        passkey.GetProperty("id").GetString().Should().Be(authenticator.Id);
        passkey.GetProperty("name").GetString().Should().Be("Work laptop");
        passkey.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.Null);
        (await session.CurrentUserAsync(cancellationToken)).Should().NotBeNull("adding a passkey keeps the session");
    }

    [Fact]
    public async Task A_passkey_without_a_name_is_called_Passkey_and_control_characters_are_dropped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await SignedInAsync(cancellationToken);
        using var unnamed = NewAuthenticator(session);
        using var odd = NewAuthenticator(session);

        (await RegisterAsync(session, unnamed, null, cancellationToken)).GetProperty("name").GetString().Should().Be("Passkey");
        (await RegisterAsync(session, odd, "  Phone\u0007\n  ", cancellationToken)).GetProperty("name").GetString().Should().Be("Phone");
    }

    [Fact]
    public async Task A_registered_passkey_signs_in_from_a_fresh_browser_and_is_stamped_as_used()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = ApiSession.NewEmail("passkey");
        using var first = new ApiSession(fixture);
        await first.SignInWithMagicLinkAsync(email, cancellationToken);
        using var authenticator = NewAuthenticator(first);
        await RegisterAsync(first, authenticator, "Laptop", cancellationToken);

        using var browser = new ApiSession(fixture);
        (await browser.CurrentUserAsync(cancellationToken)).Should().BeNull();
        await SignInAsync(browser, authenticator, cancellationToken);

        (await browser.CurrentUserAsync(cancellationToken))!.Value.GetProperty("email").GetString().Should().Be(email);
        var passkey = (await ListAsync(browser, cancellationToken)).GetProperty("passkeys")[0];
        passkey.GetProperty("lastUsedAt").ValueKind.Should().Be(JsonValueKind.String);

        // A second sign-in from another browser works too: the counter only has to go up.
        using var another = new ApiSession(fixture);
        await SignInAsync(another, authenticator, cancellationToken);
        (await another.CurrentUserAsync(cancellationToken)).Should().NotBeNull();
    }

    [Fact]
    public async Task Sign_in_is_refused_for_a_wrong_origin_a_bad_signature_and_an_unknown_passkey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await SignedInAsync(cancellationToken);
        using var authenticator = NewAuthenticator(owner);
        await RegisterAsync(owner, authenticator, "Laptop", cancellationToken);
        using var stranger = NewAuthenticator(owner);

        // The same refusal whatever is wrong, so the answer never says which passkeys exist.
        foreach (var attempt in new Func<string, string>[]
        {
            options => authenticator.Assert(options, originOverride: "https://careers.example.org"),
            options => authenticator.Assert(options, corruptSignature: true),
            options => stranger.Assert(options),
        })
        {
            using var browser = new ApiSession(fixture);
            var address = NewAddress();
            var options = await OptionsAsync(browser, SignInOptions, address, cancellationToken);

            using var response = await browser.PostAsync(SignIn, new { credentialJson = attempt(options) }, Forwarded(address), cancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await browser.CurrentUserAsync(cancellationToken)).Should().BeNull();
        }
    }

    [Fact]
    public async Task An_answer_cannot_be_replayed_or_used_without_its_own_challenge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await SignedInAsync(cancellationToken);
        using var authenticator = NewAuthenticator(owner);
        await RegisterAsync(owner, authenticator, "Laptop", cancellationToken);

        using var browser = new ApiSession(fixture);
        var address = NewAddress();
        var options = await OptionsAsync(browser, SignInOptions, address, cancellationToken);
        var answer = authenticator.Assert(options);
        using var accepted = await browser.PostAsync(SignIn, new { credentialJson = answer }, Forwarded(address), cancellationToken);
        accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The challenge lives in the asking browser's cookie, so the same answer sent from a browser that never asked finds
        // no ceremony and gets nothing.
        using var thief = new ApiSession(fixture);
        using var replayed = await thief.PostAsync(SignIn, new { credentialJson = answer }, Forwarded(NewAddress()), cancellationToken);
        replayed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await thief.CurrentUserAsync(cancellationToken)).Should().BeNull();

        // And an answer to options another browser was given does not work without that browser's cookie.
        using var asker = new ApiSession(fixture);
        var askedAddress = NewAddress();
        var askedOptions = await OptionsAsync(asker, SignInOptions, askedAddress, cancellationToken);
        using var elsewhere = await thief.PostAsync(
            SignIn, new { credentialJson = authenticator.Assert(askedOptions) }, Forwarded(NewAddress()), cancellationToken);
        elsewhere.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_authenticator_that_did_not_verify_the_person_neither_registers_nor_signs_in()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await SignedInAsync(cancellationToken);

        // Registration: a security key that was only touched, with no PIN or biometric check, is refused.
        using var touchedOnly = NewAuthenticator(session);
        var registerOptions = await OptionsAsync(session, RegisterOptions, null, cancellationToken);
        using var registered = await session.PostAsync(
            Register, new { credentialJson = touchedOnly.Register(registerOptions, userVerified: false), name = "Key" }, cancellationToken);
        registered.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListAsync(session, cancellationToken)).GetProperty("passkeys").GetArrayLength().Should().Be(0);

        // Sign-in: a passkey registered with verification, answering once without it, is refused.
        using var authenticator = NewAuthenticator(session);
        await RegisterAsync(session, authenticator, "Laptop", cancellationToken);
        using var browser = new ApiSession(fixture);
        var address = NewAddress();
        var signInOptions = await OptionsAsync(browser, SignInOptions, address, cancellationToken);
        using var signedIn = await browser.PostAsync(
            SignIn, new { credentialJson = authenticator.Assert(signInOptions, userVerified: false) }, Forwarded(address), cancellationToken);
        signedIn.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await browser.CurrentUserAsync(cancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_registration_from_the_wrong_origin_is_refused_and_adds_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await SignedInAsync(cancellationToken);
        using var authenticator = NewAuthenticator(session);
        var options = await OptionsAsync(session, RegisterOptions, null, cancellationToken);

        using var response = await session.PostAsync(
            Register, new { credentialJson = authenticator.Register(options, originOverride: "https://careers.example.org"), name = "X" },
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListAsync(session, cancellationToken)).GetProperty("passkeys").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_credential_id_that_belongs_to_another_person_cannot_be_registered_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var victim = await SignedInAsync(cancellationToken);
        using var attacker = await SignedInAsync(cancellationToken);
        using var original = NewAuthenticator(victim);
        await RegisterAsync(victim, original, "Victim's", cancellationToken);
        // The attacker's own key pair, announced under the victim's credential id.
        using var forged = new PasskeyAuthenticator(
            attacker.BaseAddress.Host, attacker.BaseAddress.GetLeftPart(UriPartial.Authority), original.CredentialId);

        var options = await OptionsAsync(attacker, RegisterOptions, null, cancellationToken);
        using var response = await attacker.PostAsync(Register, new { credentialJson = forged.Register(options), name = "Mine" }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a credential id is registered once, to one person");
        (await ListAsync(attacker, cancellationToken)).GetProperty("passkeys").GetArrayLength().Should().Be(0);
        var kept = (await ListAsync(victim, cancellationToken)).GetProperty("passkeys").EnumerateArray().Should().ContainSingle().Subject;
        kept.GetProperty("name").GetString().Should().Be("Victim's");
        // And the victim's own passkey still signs them in: the forged key pair did not replace theirs.
        using var browser = new ApiSession(fixture);
        await SignInAsync(browser, original, cancellationToken);
    }

    [Fact]
    public async Task A_body_that_is_not_a_credential_is_refused_not_an_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var body in new[] { "{", "[]", "{\"id\":1}", "\"text\"", "{\"response\":{\"clientDataJSON\":\"!!\"}}" })
        {
            using var browser = new ApiSession(fixture);
            var address = NewAddress();
            await OptionsAsync(browser, SignInOptions, address, cancellationToken);

            using var response = await browser.PostAsync(SignIn, new { credentialJson = body }, Forwarded(address), cancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"sign-in with {body}");
        }

        using var session = await SignedInAsync(cancellationToken);
        await OptionsAsync(session, RegisterOptions, null, cancellationToken);
        using var registration = await session.PostAsync(Register, new { credentialJson = "{" }, cancellationToken);
        registration.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_oversized_body_is_refused_before_it_is_read()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new ApiSession(fixture);
        var address = NewAddress();
        await OptionsAsync(browser, SignInOptions, address, cancellationToken);

        using var response = await browser.PostAsync(
            SignIn, new { credentialJson = new string('a', 100_000) }, Forwarded(address), cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_registration_with_no_ceremony_underway_is_a_400_not_a_500()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await SignedInAsync(cancellationToken);
        using var other = await SignedInAsync(cancellationToken);
        using var authenticator = NewAuthenticator(session);
        var options = await OptionsAsync(session, RegisterOptions, null, cancellationToken);

        // The challenge cookie belongs to the first browser; the second never asked for options.
        using var response = await other.PostAsync(Register, new { credentialJson = authenticator.Register(options) }, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ListAsync(other, cancellationToken)).GetProperty("passkeys").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_person_removes_their_own_passkey_but_not_another_persons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var mine = await SignedInAsync(cancellationToken);
        using var theirs = await SignedInAsync(cancellationToken);
        using var keep = NewAuthenticator(mine);
        using var drop = NewAuthenticator(mine);
        using var other = NewAuthenticator(theirs);
        await RegisterAsync(mine, keep, "Keep", cancellationToken);
        await RegisterAsync(mine, drop, "Drop", cancellationToken);
        await RegisterAsync(theirs, other, "Theirs", cancellationToken);

        using var crossed = await mine.DeleteAsync($"{Passkeys}/{other.Id}", cancellationToken);
        using var malformed = await mine.DeleteAsync($"{Passkeys}/not*base64", cancellationToken);
        using var unknown = await mine.DeleteAsync($"{Passkeys}/{Convert.ToBase64String(new byte[32]).Replace('+', '-').Replace('/', '_').TrimEnd('=')}", cancellationToken);
        using var removed = await mine.DeleteAsync($"{Passkeys}/{drop.Id}", cancellationToken);

        crossed.StatusCode.Should().Be(HttpStatusCode.NotFound, "another person's passkey looks like none at all");
        malformed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListAsync(mine, cancellationToken)).GetProperty("passkeys").EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()).Should().Equal(keep.Id);
        (await ListAsync(theirs, cancellationToken)).GetProperty("passkeys").GetArrayLength().Should().Be(1);
        (await mine.CurrentUserAsync(cancellationToken)).Should().NotBeNull("removing a passkey keeps the session");
    }

    [Fact]
    public async Task A_removed_passkey_no_longer_signs_in_and_the_last_one_may_go_while_email_links_work()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await SignedInAsync(cancellationToken);
        using var authenticator = NewAuthenticator(session);
        await RegisterAsync(session, authenticator, "Only", cancellationToken);

        using var removed = await session.DeleteAsync($"{Passkeys}/{authenticator.Id}", cancellationToken);

        removed.StatusCode.Should().Be(HttpStatusCode.NoContent, "the email link is still a way in");
        using var browser = new ApiSession(fixture);
        var address = NewAddress();
        var options = await OptionsAsync(browser, SignInOptions, address, cancellationToken);
        using var response = await browser.PostAsync(SignIn, new { credentialJson = authenticator.Assert(options) }, Forwarded(address), cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_person_keeps_at_most_ten_passkeys()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var session = await SignedInAsync(cancellationToken);
        for (var i = 0; i < 10; i++)
        {
            using var authenticator = NewAuthenticator(session);
            await RegisterAsync(session, authenticator, $"Device {i}", cancellationToken);
        }

        using var eleventh = await session.PostAsync(RegisterOptions, new { }, cancellationToken);

        eleventh.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Registering_and_listing_need_a_signed_in_person_and_posts_need_the_antiforgery_header()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = new ApiSession(fixture);

        (await anonymous.Client.GetAsync(Passkeys, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(RegisterOptions, new { }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync(Register, new { credentialJson = "{}" }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Passkeys}/AAAA", cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var withoutToken = new ApiSession(fixture);
        using var content = JsonContent.Create(new { });
        using var request = new HttpRequestMessage(HttpMethod.Post, SignInOptions) { Content = content };
        request.Headers.Add("X-Forwarded-For", NewAddress());
        (await withoutToken.Client.SendAsync(request, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Too_many_sign_in_requests_from_one_address_get_429_without_slowing_another_address()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var browser = new ApiSession(fixture);
        var busy = NewAddress();
        const int limit = DefaultPasskeyLimit;

        for (var i = 0; i < limit; i++)
        {
            using var allowed = await browser.PostAsync(SignInOptions, new { }, Forwarded(busy), cancellationToken);
            allowed.StatusCode.Should().Be(HttpStatusCode.OK, $"request {i + 1} of {limit} is within the limit");
        }

        using var refused = await browser.PostAsync(SignInOptions, new { }, Forwarded(busy), cancellationToken);
        using var other = await browser.PostAsync(SignInOptions, new { }, Forwarded(NewAddress()), cancellationToken);
        using var sameAddressElsewhere = await browser.PostAsync(SignIn, new { credentialJson = "{}" }, Forwarded(busy), cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        other.StatusCode.Should().Be(HttpStatusCode.OK);
        sameAddressElsewhere.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "both anonymous routes share the address's allowance");
    }

    private async Task<ApiSession> SignedInAsync(CancellationToken cancellationToken)
    {
        var session = new ApiSession(fixture);
        await session.SignInWithMagicLinkAsync(ApiSession.NewEmail("passkey"), cancellationToken);
        return session;
    }

    private static PasskeyAuthenticator NewAuthenticator(ApiSession session) =>
        new(session.BaseAddress.Host, session.BaseAddress.GetLeftPart(UriPartial.Authority));

    private static async Task<JsonElement> RegisterAsync(
        ApiSession session, PasskeyAuthenticator authenticator, string? name, CancellationToken cancellationToken)
    {
        var options = await OptionsAsync(session, RegisterOptions, null, cancellationToken);
        using var response = await session.PostAsync(
            Register, new { credentialJson = authenticator.Register(options), name }, cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
    }

    private static async Task SignInAsync(ApiSession browser, PasskeyAuthenticator authenticator, CancellationToken cancellationToken)
    {
        var address = NewAddress();
        var options = await OptionsAsync(browser, SignInOptions, address, cancellationToken);
        using var response = await browser.PostAsync(SignIn, new { credentialJson = authenticator.Assert(options) }, Forwarded(address), cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    // The options JSON the API returns inside its { optionsJson } wrapper.
    private static async Task<string> OptionsAsync(ApiSession session, string path, string? address, CancellationToken cancellationToken)
    {
        using var response = await session.PostAsync(path, new { }, address is null ? new Dictionary<string, string>() : Forwarded(address), cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("optionsJson").GetString()!;
    }

    private static async Task<JsonElement> ListAsync(ApiSession session, CancellationToken cancellationToken) =>
        await session.Client.GetFromJsonAsync<JsonElement>(Passkeys, cancellationToken);

    private static Dictionary<string, string> Forwarded(string address) => new() { ["X-Forwarded-For"] = address };

    // 198.18.0.0/15 is reserved for benchmarking, so never a real client's address. One new address per call.
    private static string NewAddress()
    {
        var n = Interlocked.Increment(ref addresses);
        return $"198.18.{n / 250 % 250}.{n % 250 + 1}";
    }
}
