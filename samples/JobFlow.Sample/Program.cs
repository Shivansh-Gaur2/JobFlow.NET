using JobFlow.Core;
using JobFlow.SqlServer;
using JobFlow.Sample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var connectionString = "Server=localhost,1433;Database=JobFlowTest;User Id=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True;";

var builder = Host.CreateApplicationBuilder(args);

builder.Services.UseSqlServerJobStore(connectionString);

builder.Services.AddTransient<PrintJob>();

var host = builder.Build();

await host.Services.ApplyJobFlowSqlServerMigrationsAsync();

using (var scope = host.Services.CreateScope())
{
    var scheduler = scope.ServiceProvider.GetRequiredService<JobScheduler>();

    for (int i = 1; i <= 10; i++)
    {
        await scheduler.EnqueueAsync<PrintJob>(payload: $"Job Number {i}");
    }
}

Console.WriteLine("10 jobs enqueued. Starting worker...");

await host.RunAsync();
