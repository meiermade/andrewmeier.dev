module BrowserE2E

open System
open System.Diagnostics
open System.IO
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
type AnalyticsMode =
    | DefaultOn
    | OptIn

let modeValue = function
    | AnalyticsMode.DefaultOn -> "default-on"
    | AnalyticsMode.OptIn -> "opt-in"

let playwrightPackageVersion (packageJson:string) =
    use document = JsonDocument.Parse packageJson
    document.RootElement
        .GetProperty("devDependencies")
        .GetProperty("@playwright/test")
        .GetString()
    |> Option.ofObj
    |> Option.defaultWith (fun () -> invalidOp "@playwright/test must have an explicit version.")

let verifyPlaywrightImage packageVersion (image:string) =
    if not (image.Contains($":v{packageVersion}-", StringComparison.Ordinal)) then
        invalidOp $"Playwright image {image} does not match @playwright/test {packageVersion}."

let npmInstallCommand e2eDirectory =
    { BuildProcess.create "npm" [ "ci" ] with
        workingDirectory = e2eDirectory
        timeout = TimeSpan.FromMinutes 5. }

let playwrightCommand e2eDirectory image baseUrl scope expectedMode analyticsEnabled =
    let environment =
        [ "--env"; "CI=true"
          "--env"; $"E2E_ANALYTICS_ENABLED={analyticsEnabled}"
          "--env"; $"E2E_SCOPE={scope}"
          "--env"; $"SITE_E2E_BASE_URL={baseUrl}" ]
        @ (expectedMode
           |> Option.map (fun mode -> [ "--env"; $"E2E_EXPECTED_ANALYTICS_MODE={modeValue mode}" ])
           |> Option.defaultValue [])

    { BuildProcess.create "docker" (
        [ "run"
          "--rm"
          "--init"
          "--ipc=host" ]
        @ (if scope = "local" && OperatingSystem.IsLinux() then [ "--network=host" ] else [])
        @ environment
        @ [ "--volume"; $"{Path.GetFullPath e2eDirectory}:/work"
            "--workdir"; "/work"
            image
            "npm"; "test"; "--"
            "--project=firefox"
            "--retries=0" ]) with
        workingDirectory = e2eDirectory }

let nativePlaywrightCommand e2eDirectory baseUrl analyticsEnabled =
    { BuildProcess.create "npm" [ "test"; "--"; "--project=firefox"; "--retries=0" ] with
        workingDirectory = e2eDirectory
        environment =
            Map [ "CI", "true"
                  "E2E_ANALYTICS_ENABLED", analyticsEnabled
                  "E2E_SCOPE", "local"
                  "SITE_E2E_BASE_URL", baseUrl ] }

let pulumiConfigCommand pulumiDirectory stack key =
    { BuildProcess.create "pulumi" [ "config"; "get"; key; "--stack"; stack ] with
        workingDirectory = pulumiDirectory
        timeout = TimeSpan.FromMinutes 1. }

let traceRequestBody (url:string) countryCode =
    JsonSerializer.Serialize(
        {| method = "GET"
           url = url
           context = {| geoloc = {| iso_code = countryCode |} |}
           skip_response = false |})

let traceOriginStatus (responseJson:string) =
    use document = JsonDocument.Parse responseJson
    let root = document.RootElement
    if not (root.GetProperty("success").GetBoolean()) then
        invalidOp "Cloudflare Request Trace reported an unsuccessful API result."
    root.GetProperty("result").GetProperty("status_code").GetInt32()

let countryFromEdgeTrace (content:string) =
    content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
    |> Array.tryPick (fun line ->
        let parts = line.Trim().Split('=', 2)
        if parts.Length = 2 && parts[0] = "loc" then Some parts[1]
        else None)

let private runLogged log command =
    let result = BuildProcess.runChecked BuildProcess.run command
    if not (String.IsNullOrWhiteSpace result.standardOutput) then log result.standardOutput
    if not (String.IsNullOrWhiteSpace result.standardError) then log result.standardError
    result

let private readPulumiConfig pulumiDirectory stack key =
    pulumiConfigCommand pulumiDirectory stack key
    |> BuildProcess.runChecked BuildProcess.run
    |> _.standardOutput
    |> fun value ->
        if String.IsNullOrWhiteSpace value then invalidOp $"Pulumi config {key} is empty."
        value

let private waitForEndpoint log (url:string) =
    use client = new HttpClient(Timeout = TimeSpan.FromSeconds 5.)
    let mutable ready = false
    let mutable attempt = 1

    while not ready && attempt <= 60 do
        try
            use response = client.GetAsync(url).GetAwaiter().GetResult()
            ready <- response.IsSuccessStatusCode
        with
        | :? HttpRequestException -> ()
        | :? TaskCanceledException -> ()

        if not ready then Thread.Sleep 1000
        attempt <- attempt + 1

    if not ready then invalidOp $"Endpoint did not become ready at {url}."
    log $"Endpoint ready at {url}."

let private startProcess (command:BuildProcess.Command) (writer:TextWriter) =
    let startInfo = ProcessStartInfo(command.executable)
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.WorkingDirectory <- command.workingDirectory
    for argument in command.arguments do startInfo.ArgumentList.Add argument
    for KeyValue(key, value) in command.environment do startInfo.Environment[key] <- value

    let child = new Process(StartInfo = startInfo, EnableRaisingEvents = true)
    child.OutputDataReceived.Add(fun event -> if not (isNull event.Data) then lock writer (fun () -> writer.WriteLine event.Data))
    child.ErrorDataReceived.Add(fun event -> if not (isNull event.Data) then lock writer (fun () -> writer.WriteLine event.Data))
    if not (child.Start()) then invalidOp $"Unable to start {command.executable}."
    child.BeginOutputReadLine()
    child.BeginErrorReadLine()
    child

let private stopProcess (child:Process) =
    try
        if not child.HasExited then
            child.Kill(entireProcessTree = true)
            child.WaitForExit(5000) |> ignore
    with :? InvalidOperationException -> ()

let localServerCommand rootDirectory baseUrl analyticsEnabled =
    { BuildProcess.create "dotnet" [
        "run"
        "--no-build"
        "--project"; Path.Combine(rootDirectory, "app", "src", "App", "App.fsproj") ] with
        workingDirectory = Path.Combine(rootDirectory, "app")
        environment =
            Map [ "ANALYTICS_ENABLED", analyticsEnabled
                  "ASPNETCORE_ENVIRONMENT", "Development"
                  "OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:4318"
                  "PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT", "https://otel.test"
                  "SERVER_URL", baseUrl ] }

let buildAppCommand rootDirectory =
    { BuildProcess.create "dotnet" [ "build"; "src/App/App.fsproj"; "--nologo"; "--verbosity"; "minimal" ] with
        workingDirectory = Path.Combine(rootDirectory, "app") }

let runLocal log rootDirectory e2eDirectory image baseUrl stateDirectory =
    runLogged log (buildAppCommand rootDirectory) |> ignore
    runLogged log (npmInstallCommand e2eDirectory) |> ignore
    Directory.CreateDirectory stateDirectory |> ignore
    for analyticsEnabled in [ "false"; "true" ] do
        let logPath = Path.Combine(stateDirectory, $"server-analytics-{analyticsEnabled}.log")
        use writer = new StreamWriter(logPath, append = false, AutoFlush = true)
        use server = startProcess (localServerCommand rootDirectory baseUrl analyticsEnabled) writer

        try
            waitForEndpoint log $"{baseUrl}/health"
            let command =
                if OperatingSystem.IsLinux() then
                    playwrightCommand e2eDirectory image baseUrl "local" None analyticsEnabled
                else
                    nativePlaywrightCommand e2eDirectory baseUrl analyticsEnabled
            runLogged log command |> ignore
        finally
            stopProcess server

let private verifyCloudflareTrace log accountId apiToken baseUrl countryCode expectedMode =
    use client = new HttpClient(Timeout = TimeSpan.FromSeconds 30.)
    client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", apiToken)
    let expected = modeValue expectedMode
    let checkUrl = $"{baseUrl}/privacy/policy-check/{expected}"
    use content = new StringContent(traceRequestBody checkUrl countryCode, Encoding.UTF8, "application/json")
    use response =
        client.PostAsync(
            $"https://api.cloudflare.com/client/v4/accounts/{accountId}/request-tracer/trace",
            content)
            .GetAwaiter()
            .GetResult()
    let responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if not response.IsSuccessStatusCode then
        invalidOp $"Cloudflare Request Trace returned HTTP {int response.StatusCode}: {responseBody}"
    let originStatus = traceOriginStatus responseBody
    if originStatus <> 200 then
        invalidOp $"Cloudflare {countryCode} trace expected {expected}, but the origin returned HTTP {originStatus}."
    log $"Cloudflare {countryCode} trace verified {expected}."

let private verifyActualUsContext log baseUrl =
    use client = new HttpClient(Timeout = TimeSpan.FromSeconds 15.)
    let trace = client.GetStringAsync($"{baseUrl}/cdn-cgi/trace").GetAwaiter().GetResult()
    match countryFromEdgeTrace trace with
    | Some "US" -> log "Cloudflare confirmed the deployed browser runner is in the U.S."
    | country ->
        let actual = country |> Option.defaultValue "unknown"
        invalidOp $"Expected a U.S. deployed browser context, received {actual}."

let runPublished log e2eDirectory pulumiDirectory stack image baseUrl =
    waitForEndpoint log $"{baseUrl}/health"
    runLogged log (npmInstallCommand e2eDirectory) |> ignore
    let accountId = readPulumiConfig pulumiDirectory stack "cloudflare:accountId"
    let apiToken = readPulumiConfig pulumiDirectory stack "cloudflare:apiToken"
    verifyCloudflareTrace log accountId apiToken baseUrl "US" AnalyticsMode.DefaultOn
    verifyCloudflareTrace log accountId apiToken baseUrl "DE" AnalyticsMode.OptIn
    verifyActualUsContext log baseUrl
    playwrightCommand e2eDirectory image baseUrl "deployed" (Some AnalyticsMode.DefaultOn) "true"
    |> runLogged log
    |> ignore
