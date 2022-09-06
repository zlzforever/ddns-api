using System.Text;
using Aliyun.Acs.Alidns.Model.V20150109;
using Aliyun.Acs.Core;
using Aliyun.Acs.Core.Http;
using Aliyun.Acs.Core.Profile;

namespace DDns.API.Providers.Aliyun;

public class AliyunDnsProvider : IDnsProvider
{
    private readonly ILogger<AliyunDnsProvider> _logger;

    public AliyunDnsProvider(ILogger<AliyunDnsProvider> logger)
    {
        _logger = logger;
    }

    public List<Record> GetList(DomainConfig config, string type, string domain)
    {
        try
        {
            var client = CreateClient(config);
            // Create the request
            var request = new DescribeSubDomainRecordsRequest
            {
                SubDomain = domain,
                Type = "A"
            };
            // Initiate the request and get the response
            var response = client.GetAcsResponse(request);
            if (response.DomainRecords == null)
            {
                return new List<Record>();
            }

            return response.DomainRecords.Select(x => new Record
            {
                Id = x.RecordId,
                Value = x._Value,
            }).ToList();
        }
        catch (Exception e)
        {
            _logger.LogInformation(
                $"get domain record list failed: {e.Message}");
            return null;
        }
    }

    public bool AddRecord(DomainConfig config, long priority, long ttl, string type, string value, string rr,
        string domainName)
    {
        var client = CreateClient(config);
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
            _logger.LogInformation(
                $"add domain record {rr}.{domainName} {value} success");
            return true;
        }
        else
        {
            _logger.LogError(
                $"add domain record {rr}.{domainName} {value} failed: {Encoding.UTF8.GetString(response.Content)}");

            return false;
        }
    }

    public bool UpdateRecord(DomainConfig config, long priority, long ttl, string type, string value, string rr,
        string recordId)
    {
        var client = CreateClient(config);
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
            _logger.LogInformation(
                $"update domain record {rr} {value} success");
            return true;
        }
        else
        {
            _logger.LogError(
                $"update domain record {rr} {value} failed: {Encoding.UTF8.GetString(response.Content)}");
            return false;
        }
    }

    private DefaultAcsClient CreateClient(DomainConfig config)
    {
        IClientProfile profile = DefaultProfile.GetProfile(
            "cn-hangzhou",
            config.AccessKey,
            config.Secret);
        return new DefaultAcsClient(profile);
    }
}