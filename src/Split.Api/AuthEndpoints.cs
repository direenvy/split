using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Split.Api.Data;

namespace Split.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder auth = app.MapGroup("/api/auth");

        auth.MapGet("/me", WhoAmI);
        auth.MapGet("/sign-in", SignIn);
        // The cast matters. SignOutUser takes an HttpContext and returns a Task, which is
        // exactly the shape of a RequestDelegate — so without it, ASP.NET Core would treat
        // the method as raw middleware and throw the IResult away, returning an empty 200.
        auth.MapPost("/sign-out", (Delegate)SignOutUser);
    }

    /// <summary>
    /// Who is signed in, and whether signing in is even possible on this deployment.
    ///
    /// The client calls this once on startup. <c>signInAvailable</c> is what lets it hide the
    /// sign-in button entirely when nobody has configured Google credentials, rather than
    /// offering a button that would fail.
    /// </summary>
    private static async Task<IResult> WhoAmI(HttpContext context, SplitDbContext db)
    {
        User? user = await context.GetCurrentUser(db);

        return Results.Ok(new
        {
            signInAvailable = Auth.IsConfigured,
            user = user is null
                ? null
                : new { user.Id, user.DisplayName, user.Email, user.AvatarUrl },
        });
    }

    /// <summary>
    /// Starts the Google sign-in dance. This is a normal browser navigation, not an XHR —
    /// the user has to actually land on Google's page — so the client sets window.location
    /// here rather than calling fetch.
    /// </summary>
    private static IResult SignIn(string? returnUrl)
    {
        if (!Auth.IsConfigured)
        {
            return Results.BadRequest(new
            {
                error = "Signing in is not set up on this deployment. See the README.",
            });
        }

        // Anything that is not a path inside this site is discarded, not rejected: a bad
        // returnUrl is far more likely to be a tampered link than a user error, and sending
        // them to the home page is the safe outcome either way.
        string target = Auth.IsSafeReturnUrl(returnUrl) ? returnUrl! : "/";

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = target },
            [GoogleDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// Ends the session. POST rather than GET, so that a stray &lt;img&gt; tag or a prefetch
    /// on some other site cannot sign the user out behind their back.
    /// </summary>
    private static async Task<IResult> SignOutUser(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { signedOut = true });
    }
}
