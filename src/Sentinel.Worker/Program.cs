using Sentinel.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.AddHttpClient();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
