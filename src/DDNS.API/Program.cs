using System.ComponentModel.DataAnnotations;
using System.Text;
using Aliyun.Acs.Alidns.Model.V20150109;
using Aliyun.Acs.Core;
using Aliyun.Acs.Core.Http;
using Aliyun.Acs.Core.Profile;
using DDNS.API;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Events;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
builder.Host.ConfigureServices((context, services) =>
{
    services.AddHttpContextAccessor();
    services.AddHttpClient();
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
    ([FromRoute, Required, StringLength(100)] string domain, [FromQuery, Required, StringLength(32)] string token,
        [FromServices] IHttpContextAccessor httpContextAccessor) =>
    {
        var context = httpContextAccessor.HttpContext;
        if (context == null)
        {
            Log.Logger.Error("HttpContext is null");
            return;
        }

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DDNS");

        var ip = GetIp(context).Result;

        if (string.IsNullOrWhiteSpace(ip))
        {
            Log.Logger.Error("IP is not know");
            context.Response.StatusCode = 400;
            return;
        }

        var configuration = context.RequestServices.GetRequiredService<IConfiguration>();

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogError($"{context.Request.GetDisplayUrl()} is missing access token");
            context.Response.StatusCode = 403;
            return;
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            logger.LogError($"{context.Request.GetDisplayUrl()} is missing domain");
            context.Response.StatusCode = 400;
            return;
        }

        var tuple = SplitDomainStr(domain);
        if (tuple == default || string.IsNullOrWhiteSpace(tuple.RR) || string.IsNullOrWhiteSpace(tuple.Domain))
        {
            logger.LogError($"{context.Request.GetDisplayUrl()} is invalid");
            context.Response.StatusCode = 400;
            return;
        }

        var domainConfig = configuration.GetSection($"Domains:{domain}").Get<DomainConfig>();
        if (domainConfig == null)
        {
            logger.LogError($"{context.Request.GetDisplayUrl()} is missing domain config");
            context.Response.StatusCode = 403;
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.Provider))
        {
            logger.LogError($"domain {domain} is missing dns provider");
            context.Response.StatusCode = 500;
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.AccessKey))
        {
            logger.LogError($"domain {domain} is missing accessKey");
            context.Response.StatusCode = 500;
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.Secret))
        {
            logger.LogError($"domain {domain} is missing accessKeySecret");
            context.Response.StatusCode = 500;
            return;
        }

        if (string.IsNullOrWhiteSpace(domainConfig.AccessToken))
        {
            logger.LogError($"domain {domain} is missing accessToken");
            context.Response.StatusCode = 500;
            return;
        }

        if (domainConfig.AccessToken != token)
        {
            logger.LogError("accessToken is invalid");
            context.Response.StatusCode = 403;
            return;
        }

        IClientProfile profile = DefaultProfile.GetProfile(
            "cn-hangzhou",
            domainConfig.AccessKey,
            domainConfig.Secret);
        DefaultAcsClient client = new DefaultAcsClient(profile);

        // Create the request
        var request = new DescribeSubDomainRecordsRequest
        {
            SubDomain = domain,
            Type = "A"
        };
        // Initiate the request and get the response
        var response = client.GetAcsResponse(request);

        if (response.TotalCount == 0)
        {
            if (!AddRecord(logger, client, 5, 600, "A", ip, tuple.RR, tuple.Domain))
            {
                context.Response.StatusCode = 500;
            }
        }
        else
        {
            var record = response.DomainRecords[0];
            var recordValue = record._Value;
            if (ip != recordValue)
            {
                if (!UpdateRecord(logger, client, 5, 600, "A", ip, tuple.RR, record.RecordId))
                {
                    context.Response.StatusCode = 500;
                }
            }
            else
            {
                logger.LogInformation(
                    $"domain record {domain}|{ip} is no change");
            }
        }
    });

app.Run();

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

static (string RR, string Domain) SplitDomainStr(string str)
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

static bool UpdateRecord(ILogger logger, DefaultAcsClient client, long priority, long ttl, string type, string value,
    string rr, string recordId)
{
    var updateDomainRecordRequest = new UpdateDomainRecordRequest
    {
        AcceptFormat = FormatType.JSON,
        Type = type,
        _Value = value,
        Priority = priority,
        TTL = ttl,
        RR = rr,
        RecordId = recordId
    };
    var response = client.DoAction(updateDomainRecordRequest);
    if (response.isSuccess())
    {
        logger.LogInformation(
            $"update domain record {rr} {value} success");
        return true;
    }
    else
    {
        logger.LogError(
            $"update domain record {rr} {value} failed: {Encoding.UTF8.GetString(response.Content)}");
        return false;
    }
}

static bool AddRecord(ILogger logger, DefaultAcsClient client, long priority, long ttl, string type, string value,
    string rr,
    string domainName)
{
    var addDomainRecordRequest = new AddDomainRecordRequest
    {
        AcceptFormat = FormatType.JSON,
        Priority = priority,
        TTL = ttl,
        Type = type,
        _Value = value,
        RR = rr,
        DomainName = domainName
    };
    var response = client.DoAction(addDomainRecordRequest);
    if (response.isSuccess())
    {
        logger.LogInformation(
            $"add domain record {rr}.{domainName} {value} success");
        return true;
    }
    else
    {
        logger.LogError(
            $"add domain record {rr}.{domainName} {value} failed: {Encoding.UTF8.GetString(response.Content)}");

        return false;
    }
}