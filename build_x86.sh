docker buildx build --platform linux/amd64 --target final -f src/DDNS.API/Dockerfile -t zlzforever/ddns-api .
docker push zlzforever/ddns-api