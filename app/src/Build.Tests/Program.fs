module Build.Tests.Program

open Expecto
open System
open System.IO
open System.Text.Json

let private packageJson = """{"devDependencies":{"@playwright/test":"1.62.1"}}"""
let private image = "mcr.microsoft.com/playwright:v1.62.1-noble@sha256:abc123"

let browserE2ETests =
    testList "Browser E2E build automation" [
        test "requires the Playwright image to match the package version" {
            let version = BrowserE2E.playwrightPackageVersion packageJson
            Expect.equal version "1.62.1" "package version"
            BrowserE2E.verifyPlaywrightImage version image
            Expect.throws
                (fun () -> BrowserE2E.verifyPlaywrightImage version "mcr.microsoft.com/playwright:v1.61.0-noble")
                "mismatched image"
        }

        test "plans a pinned deployed Playwright container" {
            let command =
                BrowserE2E.playwrightCommand
                    "/repo/e2e"
                    image
                    "https://andymeier.dev"
                    "deployed"
                    (Some BrowserE2E.AnalyticsMode.DefaultOn)
                    "true"

            Expect.equal command.executable "docker" "container runtime"
            Expect.equal command.workingDirectory "/repo/e2e" "E2E directory"
            Expect.containsAll
                command.arguments
                [ "--rm"
                  "--init"
                  "--ipc=host"
                  "CI=true"
                  "E2E_ANALYTICS_ENABLED=true"
                  "E2E_SCOPE=deployed"
                  "SITE_E2E_BASE_URL=https://andymeier.dev"
                  "E2E_EXPECTED_ANALYTICS_MODE=default-on"
                  image
                  "npm"; "test"
                  "--project=firefox"
                  "--retries=0" ]
                "pinned production browser plan"
            Expect.isFalse (command.arguments |> List.contains "playwright") "does not install or invoke a global Playwright CLI"
        }

        test "local acceptance explicitly selects analytics for both browser and server" {
            for enabled in [ "false"; "true" ] do
                let server = BrowserE2E.localServerCommand "/repo" "http://127.0.0.1:5051" enabled
                let native = BrowserE2E.nativePlaywrightCommand "/repo/e2e" "http://127.0.0.1:5051" enabled
                let container = BrowserE2E.playwrightCommand "/repo/e2e" image "http://127.0.0.1:5051" "local" None enabled
                Expect.equal server.environment["ANALYTICS_ENABLED"] enabled "server switch is explicit"
                Expect.equal server.environment["PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT"] "https://otel.test" "intercepted public endpoint"
                Expect.equal native.environment["E2E_ANALYTICS_ENABLED"] enabled "native browser suite"
                Expect.contains container.arguments $"E2E_ANALYTICS_ENABLED={enabled}" "container browser suite"
        }

        test "production explicitly enables browser analytics without changing server export" {
            let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../.."))
            let deployment = File.ReadAllText(Path.Combine(root, "pulumi/src/k8s/deployment.ts"))
            Expect.stringContains deployment "{ name: 'ANALYTICS_ENABLED', value: 'true' }" "production opt-in is deliberate"
            Expect.stringContains deployment "{ name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: config.openTelemetryConfig.endpoint }" "server export stays independent"
        }

        test "builds deterministic Cloudflare Trace requests" {
            let body =
                BrowserE2E.traceRequestBody
                    "https://andymeier.dev/privacy/policy-check/opt-in"
                    "DE"
            use document = JsonDocument.Parse body
            let root = document.RootElement

            Expect.equal (root.GetProperty("method").GetString()) "GET" "method"
            Expect.equal
                (root.GetProperty("context").GetProperty("geoloc").GetProperty("iso_code").GetString())
                "DE"
                "simulated country"
            Expect.equal
                (root.GetProperty("url").GetString())
                "https://andymeier.dev/privacy/policy-check/opt-in"
                "policy expectation"
            Expect.isFalse (root.GetProperty("skip_response").GetBoolean()) "origin is exercised"
        }

        test "reads the supported origin status and edge country" {
            let response = """{"success":true,"result":{"status_code":200,"trace":[]}}"""
            Expect.equal (BrowserE2E.traceOriginStatus response) 200 "origin status"
            Expect.equal
                (BrowserE2E.countryFromEdgeTrace "colo=BOS\nloc=US\ntls=TLSv1.3\n")
                (Some "US")
                "edge location"
        }

        test "reads Cloudflare credentials from the fully qualified Pulumi stack" {
            let command =
                BrowserE2E.pulumiConfigCommand
                    "/repo/pulumi"
                    "meiermade/andymeier/prod"
                    "cloudflare:apiToken"

            Expect.equal command.executable "pulumi" "Pulumi CLI"
            Expect.equal command.workingDirectory "/repo/pulumi" "Pulumi project"
            Expect.sequenceEqual
                command.arguments
                [ "config"; "get"; "cloudflare:apiToken"; "--stack"; "meiermade/andymeier/prod" ]
                "config lookup"
        }

        test "keeps browser and region orchestration out of workflows" {
            let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "../../.."))
            let deploy = File.ReadAllText(Path.Combine(root, ".github/workflows/deploy.yml"))
            let preview = File.ReadAllText(Path.Combine(root, ".github/workflows/preview.yml"))

            for workflow in [ deploy; preview ] do
                Expect.isFalse (workflow.Contains("playwright install", StringComparison.Ordinal)) "browser downloads are absent"
                Expect.isFalse (workflow.Contains("npx playwright", StringComparison.Ordinal)) "Playwright CLI scripting is absent"
                Expect.isFalse (workflow.Contains("Install Tor", StringComparison.Ordinal)) "Tor installation is absent"
                Expect.isFalse (workflow.Contains("cdn-cgi/trace", StringComparison.Ordinal)) "location scripting is absent"

            Expect.stringContains deploy "./fake.sh VerifyPublishedAnalytics" "deploy delegates to Build"
            Expect.stringContains preview "./fake.sh TestE2E" "preview delegates to Build"
        }
    ]

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args browserE2ETests
