using Sentinel.Worker;
using Sentinel.Worker.Ingestion;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<IngestionOptions>(
    builder.Configuration.GetSection(IngestionOptions.SectionName));
builder.Services.AddHttpClient<IngestionApiClient>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<FileTailIngestionService>();
builder.Services.AddHostedService<JournaldIngestionService>();
builder.Services.AddHostedService<MacUnifiedLogIngestionService>();

var host = builder.Build();
host.Run();
