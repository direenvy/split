using Microsoft.EntityFrameworkCore;
using Split.Api;
using Split.Api.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// One SQLite file next to the executable. Nothing here needs a database server, and a single
// file is something you can delete to start over, which is the right weight for this app.
string connectionString = builder.Configuration.GetConnectionString("Split") ?? "Data Source=split.db";
builder.Services.AddDbContext<SplitDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddHostedService<AnonymousGroupCleanupService>();

// Google sign-in, if this deployment has credentials for it. If not, the whole feature turns
// itself off and the app runs anonymously — see src/Split.Api/Auth.cs.
builder.AddSplitAuthentication();

WebApplication app = builder.Build();

// In development the framework's own exception page is the most useful thing to see. Anywhere
// else, an unhandled exception should come back as JSON like every other response, rather than
// an HTML stack trace the client cannot parse and the world should not read.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errors => errors.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Something went wrong handling that request." });
    }));
}

// Create the database on first run and apply the small compatibility additions used by the
// optional sign-in history and anonymous-group retention features.
using (IServiceScope scope = app.Services.CreateScope())
{
    DatabaseSchemaSetup.Initialize(scope.ServiceProvider.GetRequiredService<SplitDbContext>());
}

// Note what is *missing* here: no CORS policy, no second origin, no proxy configuration. The
// built Vue app is served as static files by this same host, so the browser only ever talks to
// one origin and none of that applies. That is the main reason the project is laid out this way.
app.UseDefaultFiles();
app.UseStaticFiles();

// Reads the session cookie and populates context.User. There is deliberately no
// UseAuthorization() to go with it: no endpoint here is gated by a policy. The one endpoint
// that needs an account, /api/groups/mine, checks for one itself and says so, which is
// clearer than a policy for a single case — and adding the middleware without the matching
// services throws at startup.
app.UseAuthentication();

app.MapAuthEndpoints();
app.MapGroupEndpoints();
app.MapPreviewEndpoints();

// Anything under /api that no endpoint matched is a genuine 404 and must not fall through to
// index.html — otherwise a mistyped API call returns an HTML page and the client reports a
// baffling JSON parse error instead of the real problem.
app.Map("/api/{**rest}", () => Results.NotFound(new { error = "No such endpoint." }));

// Everything else is the single-page app: Vue Router owns those paths, so a deep link such as
// /g/bright-tapir-4821 has to return index.html and let the client work out the rest.
string indexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");

if (File.Exists(indexPath))
{
    app.MapFallbackToFile("index.html");
}
else
{
    // Running the API without having built the client is a normal thing to do — it is what
    // `npm run dev` expects — so say so plainly rather than returning a bare 404.
    app.MapFallback(() => Results.Text(
        """
        The API is running, but the Vue app has not been built into wwwroot yet.

          cd src/Split.Client
          npm install
          npm run build

        Or, for development with hot reload, run `npm run dev` in src/Split.Client and open the
        address it prints - it proxies /api back to this host.
        """,
        "text/plain"));
}

app.Run();
