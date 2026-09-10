module Build.Tests.Program

open Expecto
open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Threading.Tasks

let private packageJson = """{"devDependencies":{"@playwright/test":"1.62.1"}}"""
let private image = "mcr.microsoft.com/playwright:v1.62.1-noble@sha256:abc123"
let private accepted = """{"ok":1,"request_id":"sample1","nodes":{"de1.node.check-host.net":["de","Germany","Nuremberg"],"de4.node.check-host.net":["de","Germany","Frankfurt"],"us1.node.check-host.net":["us","USA","Los Angeles"]}}"""
let private results de1 de4 us =
    sprintf """{"de1.node.check-host.net":%s,"de4.node.check-host.net":%s,"us1.node.check-host.net":%s}""" de1 de4 us
let private row status = $"[[1,0.1,\"response\",{status}]]"
let private optIn = results (row "200") (row "\"200\"") (row "\"409\"")
let private defaultOn = results (row "409") (row "\"409\"") (row "200")

type private ProbeHandler(responses:(HttpStatusCode * string) list) =
    inherit HttpMessageHandler()
    let pending = Queue<HttpStatusCode * string>(responses)
    let requests = ResizeArray<Uri>()
    let requestTimes = ResizeArray<int64>()
    member _.Requests = requests |> Seq.toList
    member _.RequestGaps =
        requestTimes |> Seq.pairwise |> Seq.map (fun (first, second) -> Stopwatch.GetElapsedTime(first, second))
    override _.SendAsync(request, _) =
        requestTimes.Add(Stopwatch.GetTimestamp())
        Expect.equal request.Method HttpMethod.Get "probes are read-only"
        Expect.isNull request.Headers.Authorization "no credentials sent to the probe service"
        Expect.isFalse (request.Headers.Contains "CF-IPCountry") "no forged country header"
        Expect.equal request.RequestUri.Host "check-host.net" "only the documented probe API"
        requests.Add request.RequestUri
        let status, body = pending.Dequeue()
        Task.FromResult(new HttpResponseMessage(status, Content = new StringContent(body)))

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

        test "selects real German and US nodes for a public HTTPS policy check" {
            let url = RegionalAnalytics.checkUrl "https://andymeier.dev/" "opt-in"
            Expect.stringContains url "host=https%3A%2F%2Fandymeier.dev%2Fprivacy%2Fpolicy-check%2Fopt-in" "public URL"
            for node in [ "de1"; "de4"; "us1" ] do
                Expect.stringContains url $"node={node}.node.check-host.net" "explicit probe node"
            for site in [ "http://example.com"; "https://secret@example.com"; "https://example.com?token=secret"; "https://example.com#secret" ] do
                Expect.throws (fun () -> RegionalAnalytics.checkUrl site "opt-in" |> ignore) "rejects insecure or credential-bearing URLs"
            Expect.throws (fun () -> RegionalAnalytics.checkUrl "https://example.com" "unknown" |> ignore) "unknown policy mode"
        }

        test "requires the expected nodes to still be in the requested countries" {
            Expect.equal (RegionalAnalytics.acceptedRequest accepted) "sample1" "request ID"
            for invalid in [ accepted.Replace("\"de\"", "\"us\""); accepted.Replace("\"ok\":1", "\"ok\":0"); accepted.Replace("sample1", "") ] do
                Expect.throws (fun () -> RegionalAnalytics.acceptedRequest invalid |> ignore) "cannot silently substitute geography or an invalid request"
        }

        test "requires positive and opposite-mode results and handles numeric or string statuses" {
            Expect.isTrue (RegionalAnalytics.resultsReady "opt-in" optIn) "Germany opts in and US rejects that expectation"
            Expect.isTrue (RegionalAnalytics.resultsReady "default-on" defaultOn) "US defaults on and Germany rejects that expectation"
            Expect.throws (fun () -> RegionalAnalytics.resultsReady "opt-in" defaultOn |> ignore) "wrong consent policy fails"
            Expect.throws (fun () -> RegionalAnalytics.resultsReady "opt-in" (results (row "200") (row "200") (row "200")) |> ignore) "an always-200 endpoint cannot pass"
        }

        test "pending, missing, malformed, and failed probes cannot pass" {
            Expect.isFalse (RegionalAnalytics.resultsReady "opt-in" (results "null" (row "200") (row "409"))) "pending is not success"
            for response in [ "{}"; results "[]" (row "200") (row "409"); results "[[0,0.1,\"Timeout\"]]" (row "200") (row "409"); results (row "500") (row "200") (row "409"); results (row "null") (row "200") (row "409") ] do
                Expect.throws (fun () -> RegionalAnalytics.resultsReady "opt-in" response |> ignore) "fails closed"
        }

        test "runs both regional checks through the HTTP boundary and paces requests" {
            use handler = new ProbeHandler([
                HttpStatusCode.OK, accepted
                HttpStatusCode.OK, results "null" (row "200") (row "409")
                HttpStatusCode.OK, optIn
                HttpStatusCode.OK, accepted
                HttpStatusCode.OK, defaultOn ])
            use client = new HttpClient(handler)
            (RegionalAnalytics.verify ignore client "https://andymeier.dev").GetAwaiter().GetResult()
            Expect.equal handler.Requests.Length 5 "one pending poll plus both checks"
            Expect.stringContains handler.Requests.[0].Query "opt-in" "first policy expectation"
            Expect.stringContains handler.Requests.[3].Query "default-on" "second policy expectation"
            for gap in handler.RequestGaps do
                Expect.isGreaterThanOrEqual gap (TimeSpan.FromMilliseconds 1900.) "pace first poll, repeated polls, and next-mode creation"
        }

        test "probe service unavailability fails verification rather than skipping geography" {
            use handler = new ProbeHandler([ HttpStatusCode.ServiceUnavailable, "unavailable" ])
            use client = new HttpClient(handler)
            let mutable failed = false
            try (RegionalAnalytics.verify ignore client "https://andymeier.dev").GetAwaiter().GetResult()
            with :? HttpRequestException -> failed <- true
            Expect.isTrue failed "provider failure propagates"
        }

        test "poll rate limiting is diagnosed and fails without retrying or logging bodies" {
            use handler = new ProbeHandler([ HttpStatusCode.OK, accepted; HttpStatusCode.TooManyRequests, "private response sentinel" ])
            use client = new HttpClient(handler)
            let logs = ResizeArray<string>()
            let mutable failed = false
            try (RegionalAnalytics.verify logs.Add client "https://andymeier.dev").GetAwaiter().GetResult()
            with :? HttpRequestException as error -> failed <- error.StatusCode = Nullable HttpStatusCode.TooManyRequests
            Expect.isTrue failed "429 propagates rather than passing or retrying"
            Expect.equal handler.Requests.Length 2 "one creation and one poll; no retry"
            Expect.contains logs "Regional probe API poll: HTTP 429; Retry-After=absent." "failed boundary identified"
            Expect.isFalse (logs |> Seq.exists (fun line -> line.Contains "private response sentinel")) "response body stays out of logs"
        }

        test "exhausted polling fails closed after the fixed attempt budget" {
            let pending = results "null" "null" "null"
            use handler = new ProbeHandler((HttpStatusCode.OK, accepted) :: List.replicate 20 (HttpStatusCode.OK, pending))
            use client = new HttpClient(handler)
            let mutable failure = ""
            try (RegionalAnalytics.verify ignore client "https://andymeier.dev").GetAwaiter().GetResult()
            with :? InvalidOperationException as error -> failure <- error.Message
            Expect.stringContains failure "did not finish within 20 polls" "incomplete geography is not a pass"
            Expect.equal handler.Requests.Length 21 "one request plus exactly twenty result polls"
        }

        test "still verifies the actual deployed browser runner country" {
            Expect.equal
                (BrowserE2E.countryFromEdgeTrace "colo=BOS\nloc=US\ntls=TLSv1.3\n")
                (Some "US")
                "edge location"
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

            let e2eJob = deploy.Substring(deploy.IndexOf("  e2e:", StringComparison.Ordinal))
            Expect.isFalse (e2eJob.Contains("Authenticate Pulumi", StringComparison.Ordinal)) "E2E no longer loads cloud credentials"
            Expect.isFalse (e2eJob.Contains("id-token: write", StringComparison.Ordinal)) "E2E needs no identity token"
            Expect.stringContains deploy "./fake.sh VerifyPublishedAnalytics" "deploy delegates to Build"
            Expect.stringContains preview "./fake.sh TestE2E" "preview delegates to Build"
        }
    ]

let localWatchTests =
    testList "Local Watch configuration" [
        test "keeps one stable target and ownership record across worktrees" {
            Expect.equal LocalWatch.defaultUrl "http://127.0.0.1:5290" "reserved site URL"
            Expect.equal LocalWatch.defaultPort 5290 "reserved site port"
            Expect.equal (Path.GetFileName (LocalWatch.watcherPidFile "watch")) "andymeier-watch.pid" "one Watch target"
        }
        test "parses ownership records without losing the exact start identity" {
            for identity in [ "utc:639245672027002926"; "linux:boot-id:123456" ] do
                let ownership:LocalWatch.Ownership = { pid = 123; startIdentity = identity }
                Expect.equal (LocalWatch.formatOwnership ownership |> LocalWatch.parseOwnership) (Some ownership) "round trip"
            for malformed in [ ""; "junk"; "123"; "-1|start"; "0|start"; "2147483648|start"; "123|"; "123|start|extra" ] do
                Expect.isNone (LocalWatch.parseOwnership malformed) "malformed record ignored"
        }
    ]

[<EntryPoint>]
let main args = runTestsWithCLIArgs [] args (testList "Build" [ browserE2ETests; localWatchTests ])
