using DDns.API.Providers.Aliyun;

namespace DDns.API.Providers;

public class DnsProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public DnsProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IDnsProvider Create(string provider)
    {
        return provider switch
        {
            "aliyun" => _serviceProvider.GetRequiredService<AliyunDnsProvider>(),
            _ => throw new NotImplementedException()
        };
    }
}