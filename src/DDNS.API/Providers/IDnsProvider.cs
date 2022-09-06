namespace DDns.API.Providers;

public interface IDnsProvider
{
    List<Record> GetList(DomainConfig config, string domain, string type);

    bool AddRecord(DomainConfig config, long priority, long ttl, string type, string value,
        string rr,
        string domainName);

    bool UpdateRecord(DomainConfig config, long priority, long ttl, string type, string value, string rr,
        string recordId);
}