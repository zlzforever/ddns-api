using System.ComponentModel.DataAnnotations;
using DDns.API;
using DDns.API.Providers;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Host.ConfigureServices((context, services) =>
{
    var types = typeof(DnsProviderFactory).Assembly.GetTypes();
    var interfaceType = typeof(IDnsProvider);
    foreach (var type in types)
    {
        if (type != interfaceType && type.IsAssignableTo(interfaceType))
        {
            services.AddSingleton(type, type);
        }
    }

    services.AddHttpContextAccessor();
    services.AddHttpClient();
    services.AddSingleton<DnsProviderFactory>();
    services.AddMemoryCache();

    var serilogSection = context.Configuration.GetSection("Serilog");
    if (serilogSection.GetChildren().Any())
    {
        Log.Logger = new LoggerConfiguration().ReadFrom
            .Configuration(context.Configuration)
            .CreateLogger();
    }
    else
    {
        var logFile = Environment.GetEnvironmentVariable("LOG");
        if (string.IsNullOrEmpty(logFile))
        {
            logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs/ddns.log");
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console().WriteTo.RollingFile(logFile)
            .CreateLogger();
    }
});
var app = builder.Build();

app.MapGet("/domains/{domain}",
    async ([FromRoute, Required, StringLength(100)] string domain, [FromQuery, Required, StringLength(32)] string token,
        [FromServices] IHttpContextAccessor httpContextAccessor) =>
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            Log.Logger.Error("HttpContext is null");
            return;
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DDns.API");

        var ip = await GetIp(context);

        if (string.IsNullOrWhiteSpace(ip))
        {
            context.Response.StatusCode = 500;
            Log.Logger.Error("IP is null or empty");
            return;
        }

        var displayUrl = context.Request.GetDisplayUrl();

        if (string.IsNullOrWhiteSpace(token))
        {
            context.Response.StatusCode = 403;
            logger.LogError($"{displayUrl}, access token not found");
            return;
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            context.Response.StatusCode = 400;
            logger.LogError($"{displayUrl}, domain token not found");
            return;
        }

        var tuple = SplitDomain(domain);
        if (tuple == default || string.IsNullOrWhiteSpace(tuple.RR) || string.IsNullOrWhiteSpace(tuple.Domain))
        {
            context.Response.StatusCode = 400;
            logger.LogError($"{displayUrl}, domain is invalid");
            return;
        }

        var domainConfig = GetDomainConfig(context.RequestServices, domain);

        if (domainConfig == null)
        {
            context.Response.StatusCode = 403;
            logger.LogError($"{displayUrl}, domain not exists");
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.Provider))
        {
            context.Response.StatusCode = 500;
            logger.LogError($"{displayUrl}, dns provider not found");
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.AccessKey))
        {
            context.Response.StatusCode = 500;
            logger.LogError($"{displayUrl}, dns provider access key not found");
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.Secret))
        {
            context.Response.StatusCode = 500;
            logger.LogError($"{displayUrl}, dns provider access secret key not found");
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.AccessToken))
        {
            context.Response.StatusCode = 500;
            logger.LogError($"{displayUrl}, domain token not found");
            return;
        }

        if (domainConfig.AccessToken != token)
        {
            context.Response.StatusCode = 403;
            logger.LogError($"{displayUrl}, access token is invalid");
            return;
        }

        var providerFactory = context.RequestServices.GetRequiredService<DnsProviderFactory>();
        var provider = providerFactory.Create(domainConfig.Provider);

        List<Record> records;
        try
        {
            records = provider.GetList(domainConfig, domain, "A");
        }
        catch (Exception e)
        {
            context.Response.StatusCode = 500;
            logger.LogError($"{displayUrl}, request records failed: {e}");
            return;
        }

        if (records.Count == 0)
        {
            if (!provider.AddRecord(domainConfig, 5, 600, "A", ip, tuple.RR, tuple.Domain))
            {
                context.Response.StatusCode = 500;
                logger.LogError($"{displayUrl}, add record failed");
            }
            else
            {
                logger.LogInformation($"{displayUrl}, add record success");
            }
        }
        else
        {
            var record = records[0];
            if (ip != record.Value)
            {
                if (!provider.UpdateRecord(domainConfig, 5, 600, "A", ip, tuple.RR, record.Id))
                {
                    context.Response.StatusCode = 500;
                    logger.LogError($"{displayUrl}, update record failed");
                }
                else
                {
                    logger.LogInformation($"{displayUrl}, update record success");
                }
            }
            else
            {
                logger.LogInformation($"{displayUrl}, there is nothing to change");
            }
        }
    });

app.Run();

static DomainConfig GetDomainConfig(IServiceProvider services, string domain)
{
    var memoryCache = services.GetRequiredService<IMemoryCache>();
    return memoryCache.GetOrCreate($"DOMAIN_{domain}", (entry =>
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var domainConfig = configuration.GetSection($"Domains:{domain}").Get<DomainConfig>();
        entry.SetValue(domainConfig);
        entry.SetSlidingExpiration(TimeSpan.FromMinutes(1));
        return domainConfig;
    }));
}

static async Task<string> GetIp(HttpContext httpContext)
{
    var hostEnvironment = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
    if (hostEnvironment.IsDevelopment())
    {
        var httpClient = httpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("DDNS");
        var result = await httpClient.GetStringAsync("https://ip.tool.lu");
        var pieces = result.Split(new[] { ":", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        return pieces[1].Trim();
    }
    else
    {
        var ip = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (string.IsNullOrEmpty(ip))
        {
            ip = httpContext.Connection.RemoteIpAddress?.ToString();
        }

        return ip;
    }
}

static (string RR, string Domain) SplitDomain(string str)
{
    var pieces = str.Split(".");
    if (pieces.Length < 3)
    {
        return default;
    }

    var count = pieces.Length - 2;
    var rr = string.Join(".", pieces[..count]);
    var domain = string.Join(".", pieces[count..]);
    return (rr, domain);
}