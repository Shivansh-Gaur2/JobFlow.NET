using JobFlow.Core;
using JobFlow.SqlServer;
using JobFlow.WebSample;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("JobFlow")
    ?? throw new InvalidOperationException(
        "Connection string 'JobFlow' is required. Set ConnectionStrings__JobFlow before starting the sample.");

builder.Services.UseSqlServerJobStore(connectionString);
builder.Services.AddTransient<DemoWorkJob>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
await app.Services.ApplyJobFlowSqlServerMigrationsAsync();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
