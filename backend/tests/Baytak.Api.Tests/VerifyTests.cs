using Baytak.Api.Auth;
using Baytak.Api.Data;
using Baytak.Api.Functions;
using Microsoft.EntityFrameworkCore;

namespace Baytak.Api.Tests;

/// <summary>Sign-in with Twilio Verify (Sms:Provider=TwilioVerify): Twilio makes and checks the code, the API's limits stay.</summary>
public class VerifyTests
{
    private const string Phone = "70 123 456";
    private static readonly string Normalized = TestApp.PhoneNumberFor(Phone);

    private static Task<Response> Login(TestApp app, string phone = Phone) =>
        app.Call<AuthFunctions>((f, r, ct) => f.Login(r, ct), new { phoneNumber = phone, status = "" });

    private static Task<Response> Validate(TestApp app, string code, string phone = Phone) =>
        app.Call<AuthFunctions>((f, r, ct) => f.Validate(r, ct), new { phoneNumber = phone, pincode = code });

    [Fact]
    public async Task Codes_come_from_verify_and_the_api_sends_no_sms_itself()
    {
        var verifier = new FakeVerifier();
        using var app = new TestApp(verifier: verifier);

        Assert.True((await Login(app)).Ok);
        Assert.Equal([Normalized], verifier.Started);
        Assert.Empty(app.Sms.Sent);

        var ok = await Validate(app, FakeVerifier.Code);
        Assert.True(ok.Ok, ok.Body);
        Assert.False(string.IsNullOrEmpty(ok.Json.GetProperty("auth").GetString()));
        Assert.Equal((Normalized, FakeVerifier.Code), verifier.Checks.Single());

        // The code was consumed: using it again fails without asking Twilio.
        Assert.Equal("That code is not correct.", (await Validate(app, FakeVerifier.Code)).Body);
        Assert.Single(verifier.Checks);
    }

    [Fact]
    public async Task Wrong_codes_are_refused_and_junk_never_reaches_twilio()
    {
        var verifier = new FakeVerifier();
        using var app = new TestApp(verifier: verifier);
        await Login(app);

        Assert.Equal("That code is not correct.", (await Validate(app, "111111")).Body);
        Assert.Equal("That code is not correct.", (await Validate(app, "12ab56")).Body);
        Assert.Single(verifier.Checks); // only the numeric one was checked with Twilio
        Assert.True((await Validate(app, FakeVerifier.Code)).Ok);
    }

    [Fact]
    public async Task The_attempt_limit_still_applies_in_front_of_twilio()
    {
        var verifier = new FakeVerifier();
        using var app = new TestApp(verifier: verifier);
        await Login(app);

        for (var i = 0; i < 5; i++) Assert.Equal(400, (await Validate(app, "000000")).Status);
        var blocked = await Validate(app, FakeVerifier.Code);
        Assert.Equal(429, blocked.Status);
        Assert.Equal(5, verifier.Checks.Count);
    }

    [Fact]
    public async Task Test_numbers_keep_their_fixed_code_without_twilio()
    {
        var verifier = new FakeVerifier();
        using var app = new TestApp(o => { o.FixedCode = "135790"; o.FixedCodePhones = Normalized; }, verifier: verifier);

        Assert.True((await Login(app)).Ok);
        Assert.Empty(verifier.Started);
        Assert.True((await Validate(app, "135790")).Ok);
        Assert.Empty(verifier.Checks);
    }

    [Fact]
    public async Task When_twilio_fails_nothing_is_left_behind_and_a_retry_is_allowed()
    {
        var verifier = new FakeVerifier { Fail = true };
        using var app = new TestApp(verifier: verifier);

        var failed = await Login(app);
        Assert.Equal(500, failed.Status);
        Assert.Equal("We could not send the verification code. Please try again.", failed.Body);
        Assert.Equal(0, await app.Db(db => db.OtpCodes.CountAsync()));

        verifier.Fail = false;
        Assert.True((await Login(app)).Ok); // no cool-down was spent on the failed attempt
    }

    [Fact]
    public async Task The_resend_cool_down_and_country_rules_still_apply()
    {
        var verifier = new FakeVerifier();
        using var app = new TestApp(verifier: verifier);

        Assert.True((await Login(app)).Ok);
        Assert.Equal(429, (await Login(app)).Status);
        Assert.Equal(400, (await Login(app, "+33 6 12 34 56 78")).Status); // France: not an allowed country
        Assert.Single(verifier.Started);
    }
}
