using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Sentinel.Worker;
using Sentinel.Worker.Ingestion;

var builder = Host.CreateApplicationBuilder(args);
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

var serviceName = builder.Environment.ApplicationName ?? "Sentinel.Worker";
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"];
var useConsoleExporter = builder.Configuration.GetValue("Otel:ConsoleExporter", false);

builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<IngestionOptions>(
    builder.Configuration.GetSection(IngestionOptions.SectionName));
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
builder.Services.AddHttpClient<IngestionApiClient>()
    .AddPolicyHandler(retryPolicy);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource =>
        resource.AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing =>
    {
        tracing.AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }

        if (useConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }
    })
    .WithMetrics(metrics =>
    {
        metrics.AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }

        if (useConsoleExporter)
        {
            metrics.AddConsoleExporter();
        }
    });
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<FileTailIngestionService>();
builder.Services.AddHostedService<JournaldIngestionService>();
builder.Services.AddHostedService<MacUnifiedLogIngestionService>();
builder.Services.AddHostedService<SyslogUdpIngestionService>();
builder.Services.AddHostedService<KafkaIngestionService>();
builder.Services.AddHostedService<DockerLogIngestionService>();
builder.Services.AddHostedService<KubernetesLogIngestionService>();

if (OperatingSystem.IsWindows())
{
    builder.Services.AddHostedService<WindowsEventLogIngestionService>();
}

var host = builder.Build();
host.Run();
