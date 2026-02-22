using Polly;
using Polly.Extensions.Http;
using Sentinel.Web.Components;
using Sentinel.Web.Hubs;
using Sentinel.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();
builder.Services.AddSingleton<LogStreamState>();
builder.Services.AddSingleton<ApiClientStatus>();
builder.Services.AddTransient<ApiClientStatusHandler>();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseUrl = builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5104";
    client.BaseAddress = new Uri(baseUrl);
    var apiKey = builder.Configuration["Api:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }
})
    .AddHttpMessageHandler<ApiClientStatusHandler>()
    .AddPolicyHandler((sp, _) =>
    {
        var status = sp.GetRequiredService<ApiClientStatus>();
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                3,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)) +
                           TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500)),
                (outcome, delay, attempt, _) =>
                {
                    var reason = outcome.Exception?.Message ??
                                 $"{(int)outcome.Result.StatusCode} {outcome.Result.ReasonPhrase}";
                    status.MarkRetry(attempt, delay, reason);
                });
    });
builder.Services.AddHostedService<ApiStreamBridge>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<EventsHub>("/hubs/events");

app.Run();
