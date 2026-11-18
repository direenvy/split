using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Split.Api.Data;

namespace Split.Api;

/// <summary>
/// Sign-in with Google, and nothing else.
///
/// <para>
/// This application never sees a password. Google authenticates the person, hands back a set
/// of claims, and we keep a session cookie. There is no password hash to get wrong, no reset
/// flow to secure, and no credential of any kind in the database.
/// </para>
///
/// <para>
/// <b>It is optional.</b> Google OAuth needs a client id and secret that only the repository's
/// owner can create, so a fresh clone has neither. Rather than crash at startup or show a
/// button that cannot work, the whole feature switches itself off: the API reports that
/// sign-in is unavailable, the client hides it, and the app works anonymously exactly as it
/// did before accounts existed. Everything except the history list is reachable without
/// signing in, by design.
/// </para>
/// </summary>
public static class Auth
{
    /// <summary>Claim holding our own user id, so requests need no lookup by Google subject.</summary>
    public const string UserIdClaim = "split:uid";

    /// <summary>Set when a client id and secret are present; false otherwise.</summary>
    public static bool IsConfigured { get; private set; }

    public static void AddSplitAuthentication(this WebApplicationBuilder builder)
    {
        string? clientId = builder.Configuration["Authentication:Google:ClientId"];
        string? clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];

        IsConfigured = !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret);

        AuthenticationBuilder authentication = builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;

            // Only point "challenge" at Google when Google can actually answer. Without this
            // guard an unconfigured app would throw on the first protected request instead of
            // simply reporting that nobody is signed in.
            if (IsConfigured)
            {
                options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
            }
        });

        authentication.AddCookie(options =>
        {
            options.Cookie.Name = "split.session";
            options.Cookie.HttpOnly = true;

            // Lax rather than Strict: the browser arrives back here as a top-level navigation
            // from Google's domain, and Strict would withhold the cookie on that first hop,
            // so the user would land signed out immediately after signing in.
            options.Cookie.SameSite = SameSiteMode.Lax;

            // Secure over HTTPS, not over plain HTTP. Development runs on http://localhost,
            // where an Always policy would mean the cookie is silently never stored. A
            // production deployment is HTTPS, so this resolves to Always there.
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;

            // This is an API, not a server-rendered site: an unauthenticated XHR should get a
            // 401 it can act on, not a 302 to a login page that it will try to parse as JSON.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };

            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        if (!IsConfigured)
        {
            return;
        }

        authentication.AddGoogle(options =>
        {
            options.ClientId = clientId!;
            options.ClientSecret = clientSecret!;

            // Google redirects back here. This exact path, on this exact host, is what has to
            // be registered as an authorised redirect URI in the Google Cloud console.
            options.CallbackPath = "/signin-google";

            options.SaveTokens = false; // we never call a Google API on the user's behalf

            options.Events.OnCreatingTicket = async context =>
            {
                await UpsertUser(context);
            };
        });
    }

    /// <summary>
    /// Records the person in our own database the moment Google vouches for them, and adds
    /// our user id to the identity so later requests do not have to look them up by subject.
    /// </summary>
    private static async Task UpsertUser(Microsoft.AspNetCore.Authentication.OAuth.OAuthCreatingTicketContext context)
    {
        JsonElement payload = context.User;

        string? subject = payload.TryGetProperty("sub", out JsonElement sub) ? sub.GetString() : null;
        if (string.IsNullOrWhiteSpace(subject))
        {
            // Google always sends `sub`. If it somehow did not, we have no stable identity and
            // must not guess one from the email address.
            context.Fail("Google did not return an account identifier.");
            return;
        }

        string email = payload.TryGetProperty("email", out JsonElement e) ? e.GetString() ?? "" : "";
        string name = payload.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
        string? picture = payload.TryGetProperty("picture", out JsonElement p) ? p.GetString() : null;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.IsNullOrWhiteSpace(email) ? "Someone" : email.Split('@')[0];
        }

        SplitDbContext db = context.HttpContext.RequestServices.GetRequiredService<SplitDbContext>();

        User? user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubject == subject);

        if (user is null)
        {
            user = new User
            {
                GoogleSubject = subject,
                Email = email,
                DisplayName = name,
                AvatarUrl = picture,
            };

            db.Users.Add(user);
        }
        else
        {
            // Refresh the display data; people change their name and picture.
            user.Email = email;
            user.DisplayName = name;
            user.AvatarUrl = picture;
            user.LastSeenAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();

        context.Identity?.AddClaim(new Claim(UserIdClaim, user.Id.ToString()));
    }

    /// <summary>
    /// The signed-in user, or null. Returns null rather than throwing, because almost every
    /// endpoint here works perfectly well for an anonymous caller.
    /// </summary>
    public static async Task<User?> GetCurrentUser(this HttpContext context, SplitDbContext db)
    {
        string? id = context.User.FindFirstValue(UserIdClaim);

        if (!Guid.TryParse(id, out Guid userId))
        {
            return null;
        }

        return await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
    }

    /// <summary>
    /// Whether a redirect target is somewhere inside this application.
    ///
    /// Without this check, <c>/api/auth/sign-in?returnUrl=https://evil.example</c> would hand
    /// an attacker a link that starts on a domain the user trusts, runs a genuine Google
    /// sign-in, and lands them somewhere else entirely — an open redirect, and a good basis
    /// for a phishing page. Only site-relative paths are allowed, and a protocol-relative
    /// "//host" is rejected because the browser would read it as another origin.
    /// </summary>
    public static bool IsSafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal);
}
