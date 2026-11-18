using Microsoft.EntityFrameworkCore;
using Split.Api;
using Split.Api.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// One SQLite file next to the executable. Nothing here needs a database server, and a single
// file is something you can delete to start over, which is the right weight for this app.
string connectionString = builder.Configuration.GetConnectionString("Split") ?? "Data Source=split.db";
builder.Services.AddDbContext<SplitDbContext>(options => options.UseSqlite(connectionString));

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

// Create the database on first run. A real deployment would use EF migrations; this app has
// one schema and no upgrade path to worry about yet, so the extra tooling would not earn its
// keep. The README says so out loud rather than leaving it to be discovered.
using (IServiceScope scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<SplitDbContext>().Database.EnsureCreated();
}

// Note what is *missing* here: no CORS policy, no second origin, no proxy configuration. The
// built Vue app is served as static files by this same host, so the browser only ever talks to
// one origin and none of that applies. That is the main reason the project is laid out this way.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGroupEndpoints();

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
