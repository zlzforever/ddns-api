# ddns-api
通过云服务自部署 DDNS

## 部署 

### docker

``` 
docker run --name ddns-api -d -v [config path]:/app/appsettings.json zlzforever/ddns-api
```

### docker compose

```
version: "3.3"
services:
  ddns:
    image: zlzforever/ddns-api
    container_name: ddns
    restart: always
    volumes:
      - ./ddns_conf/appsettings.json:/app/appsettings.json
    ports:
      - "9031:80"
```

## 配置文件

appsettings.json

```
{
  "AllowedHosts": "*",
  "Domains": {
    -- which domain support
    "test.yourdomain.com": {
      -- dns provider
      "Provider": "aliyun",
      -- dns provider access key
      "AccessKey": "",
      -- dns provider access secret
      "Secret": "",
      -- ddns api token
      "AccessToken": ""
    }
  },
  "Serilog": {
    "Using": [
      "Serilog.Sinks.RollingFile",
      "Serilog.Sinks.Async",
      "Serilog.Sinks.Console",
      "Serilog.Expressions",
      "Serilog.Enrichers.Environment",
      "Serilog.Enrichers.Thread"
    ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Information",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.Hosting.Lifetime": "Information",
        "Microsoft.AspNetCore.Cors.Infrastructure.CorsService": "Warning",
        "Microsoft.AspNetCore.Mvc": "Warning",
        "Microsoft.AspNetCore.Hosting": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss,fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "ApplicationInsights",
        "Args": {
          "restrictedToMinimumLevel": "Information",
          "telemetryConverter": "Serilog.Sinks.ApplicationInsights.Sinks.ApplicationInsights.TelemetryConverters.TraceTelemetryConverter, Serilog.Sinks.ApplicationInsights"
        }
      }
    ],
    "Enrich": [
      "FromLogContext",
      "WithMachineName",
      "WithThreadId",
      "WithProcessId"
    ],
    "Properties": {
      "Application": "DDNS.API",
      "MachineName": "%COMPUTERNAME%"
    }
  }
}
```

## 更新

比如在 linux 系统中

```
crontab -e
```
然后输入

```
*/5 * * * * curl https://ddns.xxx.com/domains/api.xxx.com?token=XXXXX

```
