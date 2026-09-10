module RegionalAnalytics

open System
open System.Net.Http
open System.Text.Json
open System.Threading
open System.Threading.Tasks

// Real HTTPS requests, not synthetic geolocation or caller-supplied country headers.
// Check-Host receives only the public, read-only policy-check URLs.
let private nodes =
    [ "de1.node.check-host.net", "de"
      "de4.node.check-host.net", "de"
      "us1.node.check-host.net", "us" ]

let checkUrl (baseUrl:string) mode =
    let site = Uri(baseUrl, UriKind.Absolute)
    if site.Scheme <> "https" || site.UserInfo <> "" || site.Query <> "" || site.Fragment <> "" then
        invalidArg (nameof baseUrl) "Regional probes require a public HTTPS base URL without credentials, query, or fragment."
    if mode <> "opt-in" && mode <> "default-on" then invalidArg (nameof mode) "Unknown analytics mode."
    let target = Uri.EscapeDataString($"{baseUrl.TrimEnd('/')}/privacy/policy-check/{mode}")
    let selected = nodes |> List.map (fun (node, _) -> $"&node={node}") |> String.concat ""
    $"https://check-host.net/check-http?host={target}{selected}"

let acceptedRequest (json:string) =
    use document = JsonDocument.Parse json
    let root = document.RootElement
    if root.GetProperty("ok").GetInt32() <> 1 then invalidOp "Regional probe request was not accepted."
    let actualNodes = root.GetProperty("nodes")
    if actualNodes.EnumerateObject() |> Seq.length <> nodes.Length then
        invalidOp "Regional probe service selected an unexpected number of nodes."
    for node, country in nodes do
        let location = actualNodes.GetProperty(node)
        if location.[0].GetString() <> country then
            invalidOp $"Regional probe node {node} is not in the required country {country}."
    let requestId = root.GetProperty("request_id").GetString()
    if String.IsNullOrWhiteSpace requestId then invalidOp "Regional probe request ID is missing."
    requestId

let resultsReady mode (json:string) =
    if mode <> "opt-in" && mode <> "default-on" then invalidArg (nameof mode) "Unknown analytics mode."
    use document = JsonDocument.Parse json
    let root = document.RootElement
    let mutable ready = true
    for node, country in nodes do
        let result = root.GetProperty(node)
        if result.ValueKind = JsonValueKind.Null then ready <- false
        else
            if result.ValueKind <> JsonValueKind.Array || result.GetArrayLength() <> 1 then
                invalidOp $"Regional probe {node} returned an invalid result."
            let row = result.[0]
            if row.ValueKind <> JsonValueKind.Array || row.GetArrayLength() < 4 then
                invalidOp $"Regional probe {node} failed before receiving an HTTP status."
            let status =
                match row.[3].ValueKind with
                | JsonValueKind.Number -> row.[3].GetInt32()
                | JsonValueKind.String ->
                    match Int32.TryParse(row.[3].GetString()) with
                    | true, value -> value
                    | _ -> invalidOp $"Regional probe {node} returned an invalid HTTP status."
                | _ -> invalidOp $"Regional probe {node} returned no HTTP status."
            let expectedMode = if country = "de" then "opt-in" else "default-on"
            let expectedStatus = if mode = expectedMode then 200 else 409
            if status <> expectedStatus then
                invalidOp $"Regional {country} probe {node} expected HTTP {expectedStatus} for {mode}, received {status}."
    ready

let verify log (client:HttpClient) baseUrl = task {
    use deadline = new CancellationTokenSource(TimeSpan.FromSeconds 60.)
    let mutable firstRequest = true
    let getJson (url:string) = task {
        // Pace every transition, including creation -> first poll and next-mode creation.
        // Waiting only after pending results allowed immediate follow-up requests to hit 429 in CI.
        if not firstRequest then do! Task.Delay(2000, deadline.Token)
        firstRequest <- false
        use request = new HttpRequestMessage(HttpMethod.Get, url)
        request.Headers.Accept.ParseAdd "application/json"
        let phase = if request.RequestUri.AbsolutePath = "/check-http" then "create" else "poll"
        log $"Regional probe API {phase}: sending request."
        use! response = client.SendAsync(request, deadline.Token)
        let retryAfter = if isNull response.Headers.RetryAfter then "absent" else string response.Headers.RetryAfter
        log $"Regional probe API {phase}: HTTP {int response.StatusCode}; Retry-After={retryAfter}."
        response.EnsureSuccessStatusCode() |> ignore
        return! response.Content.ReadAsStringAsync(deadline.Token)
    }
    for mode in [ "opt-in"; "default-on" ] do
        let! accepted = getJson (checkUrl baseUrl mode)
        let requestId = acceptedRequest accepted
        let resultUrl = $"https://check-host.net/check-result/{Uri.EscapeDataString requestId}"
        let mutable ready = false
        let mutable polls = 0
        while not ready && polls < 20 do
            let! results = getJson resultUrl
            ready <- resultsReady mode results
            polls <- polls + 1
        if not ready then invalidOp $"Regional {mode} probes did not finish within 20 polls."
        log $"Real DE/US probes verified {mode}, including opposite-mode rejection: {resultUrl}"
}
