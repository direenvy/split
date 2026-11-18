namespace Split.Api.Tests;

/// <summary>
/// <see cref="Auth.IsSafeReturnUrl"/> is the only thing standing between the sign-in endpoint
/// and an open redirect.
///
/// The attack it stops: a link to <c>/api/auth/sign-in?returnUrl=https://evil.example</c>
/// starts on a domain the user trusts, runs a real Google sign-in, and then drops them on
/// somebody else's page — which is an excellent place to ask them to "sign in again". The
/// whole defence is one predicate, so it is worth testing properly.
/// </summary>
public class AuthTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/groups")]
    [InlineData("/g/bright-tapir-4821")]
    [InlineData("/g/bright-tapir-4821/settle")]
    [InlineData("/new?from=nav")]
    [InlineData("/groups#recent")]
    public void Paths_inside_this_site_are_allowed(string returnUrl)
    {
        Assert.True(Auth.IsSafeReturnUrl(returnUrl));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example/steal")]
    [InlineData("//evil.example")]            // protocol-relative: the browser reads it as another origin
    [InlineData("//evil.example/groups")]
    [InlineData("/\\evil.example")]           // some browsers normalise the backslash into //
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("groups")]                    // relative, so it depends on the current path
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Anything_that_could_leave_this_site_is_rejected(string? returnUrl)
    {
        Assert.False(Auth.IsSafeReturnUrl(returnUrl));
    }

    [Fact]
    public void Sign_in_availability_is_per_host_not_global()
    {
        // This used to be a static property, which meant every application instance in a
        // process shared one answer — including the several an integration test spins up.
        Assert.True(new GoogleSignIn(true).IsConfigured);
        Assert.False(new GoogleSignIn(false).IsConfigured);
    }
}
