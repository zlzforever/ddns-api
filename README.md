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
    "test.yourdomain.com": {
      "Provider": "aliyun",
      "AccessKey": "",
      "Secret": "",
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
