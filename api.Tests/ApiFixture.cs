using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Api.Tests;

// One SQL Server container (built from database.sql, phase2.sql and phase3.sql)
// and one in-memory API shared by every test. Needs Docker running.
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AdminEmail = "admin@demo.com";
    public const string StaffEmail = "staff@demo.com";
    private const string DemoPassword = "Demo@123";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private WebApplicationFactory<Program> _factory = null!;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        foreach (var script in new[] { "database.sql", "phase2.sql", "phase3.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", script));
            var result = await _sql.ExecScriptAsync(sql);
            if (result.ExitCode != 0)
                throw new InvalidOperationException($"{script} failed:\n{result.Stdout}\n{result.Stderr}");
        }

        ConnectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "InventoryDb"
        }.ConnectionString;

        // Program.cs reads these while it builds, so they are set as environment variables
        // (they override appsettings*.json, the same way docker-compose.yml configures the API).
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
        Environment.SetEnvironmentVariable("Jwt__Key", "test-only-signing-key-" + Guid.NewGuid().ToString("N"));

        _factory = new WebApplicationFactory<Program>();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _sql.DisposeAsync();
    }

    // An HttpClient logged in as the given demo user
    public async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DemoPassword });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<LoginBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
        return client;
    }

    private sealed record LoginBody(string Token);
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
