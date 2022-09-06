# ddns-api
通过云服务自部署 DDNS

## 部署 

``` 
docker run --name ddns-api -d -v [config]:/app/appsettings.json zlzforever/ddns-api
```

## 测试

```
http://localhost:5000/domains/ddns.zlzforever.com?token=hi
```