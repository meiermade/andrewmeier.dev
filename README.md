# Andy Meier

[![Deploy](https://github.com/meiermade/andymeier/actions/workflows/deploy.yml/badge.svg)](https://github.com/meiermade/andymeier/actions/workflows/deploy.yml)

Personal website for Andy Meier built with F#, Giraffe, Datastar, and Tailwind CSS.

## Structure

- `app/` - F# web application
  - `src/App/` - Main application and FSharp.ViewEngine article source
  - `src/Build/` - FAKE build script
  - `src/Tests/` - Expecto tests
- `pulumi/` - Infrastructure as code (Cloudflare and Kubernetes)

## Development

```bash
cd app
dotnet tool restore
dotnet paket restore
./fake.sh Watch
```

Articles are authored directly in `app/src/App/src/Articles/Posts` with FSharp.ViewEngine. `Watch` uses those source-controlled articles and requires no content-service credentials.

### Browser analytics

Browser analytics is disabled when `ANALYTICS_ENABLED` is absent or `false`, including normal local development. Disabled pages retain usable consent controls but omit the public endpoint and telemetry-module attributes; accepting consent cannot enable telemetry in this environment.

To test browser analytics explicitly, set both `ANALYTICS_ENABLED=true` and `PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT` to your intended public collector. An enabled environment without the endpoint fails startup. This switch only makes browser analytics available: the existing regional policy and visitor choice still govern collection and withdrawal.

Server logs, traces, and metrics use `OTEL_EXPORTER_OTLP_ENDPOINT` independently; the browser switch does not disable server observability. Production explicitly enables browser analytics in its deployment environment. `cd app && ./fake.sh TestE2E` runs a disabled-server acceptance suite first, then the enabled consent/article suite using intercepted browser OTLP requests.

## Publishing articles

1. Add a post module under `app/src/App/src/Articles/Posts` and register it in `Articles/Catalog.fs`.
2. Upload article images to `gs://assets.meiermade.com/andymeier/articles/<permalink>/` using content-hashed filenames.
3. Reference each image's `https://assets.meiermade.com/andymeier/...` URL from the post.

Article assets are manually published and cached, so changing an image requires a new filename.

## Testing

```bash
cd app
./fake.sh Test
```
