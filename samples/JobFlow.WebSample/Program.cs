using JobFlow.Core;
using JobFlow.SqlServer;
using JobFlow.WebSample;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("JobFlow")
    ?? "Server=localhost,1433;Database=JobFlowWebSample;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;";

builder.Services.UseSqlServerJobStore(connectionString);
builder.Services.AddTransient<DemoWorkJob>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
await app.Services.ApplyJobFlowSqlServerMigrationsAsync();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
