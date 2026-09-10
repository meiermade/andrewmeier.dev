module LocalWatch

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Runtime.InteropServices
open System.Threading
open System.Threading.Tasks

[<Literal>]
let defaultUrl = "http://127.0.0.1:5290"

let dotnetArguments = [ "watch"; "run"; "--no-restore"; "--no-hot-reload"; "--non-interactive" ]
let prismArguments = [ "run"; "build:prism"; "--"; "--watch=forever" ]
let telemetryArguments = [ "run"; "build:telemetry"; "--"; "--watch=forever" ]
let cssArguments = [ "--input"; "./input.css"; "--output"; "./wwwroot/css/compiled.css"; "--watch=always" ]

type Ownership = { pid:int; startIdentity:string }

let validateUrl (url:string) =
    match Uri.TryCreate(url, UriKind.Absolute) with
    | true, uri when uri.Scheme = "http" && uri.Host = "127.0.0.1" && uri.Port > 0
                     && uri.UserInfo = "" && uri.AbsolutePath = "/" && uri.Query = "" && uri.Fragment = ""
                     && Text.RegularExpressions.Regex.IsMatch(url, @"\Ahttp://127\.0\.0\.1:[0-9]+/?\z") ->
        $"{uri.Scheme}://{uri.Host}:{uri.Port}"
    | _ -> invalidArg (nameof url) "Watch URL must be an exact http://127.0.0.1 URL with a nonzero port and no credentials, path, query, or fragment."

let configuredUrl () =
    Environment.GetEnvironmentVariable "ANDYMEIER_SERVER_URL"
    |> Option.ofObj
    |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.defaultValue defaultUrl
    |> validateUrl

let watcherPidFile watcherName =
    Path.Combine(Path.GetTempPath(), $"andymeier-{watcherName}.pid")

let formatOwnership ownership = $"{ownership.pid}|{ownership.startIdentity}"

let parseOwnership (text:string) =
    match text.Trim().Split('|') with
    | [| pid; identity |] ->
        match Int32.TryParse pid with
        | true, pid when pid > 0 && not (String.IsNullOrWhiteSpace identity) -> Some { pid = pid; startIdentity = identity }
        | _ -> None
    | _ -> None

let readOwnership watcherName =
    let path = watcherPidFile watcherName
    if File.Exists path then File.ReadAllText path |> parseOwnership else None

let processOwnership (child:Process) =
    let identity =
        if OperatingSystem.IsLinux() then
            let boot = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim()
            let stat = File.ReadAllText($"/proc/{child.Id}/stat")
            let fields = stat.Substring(stat.LastIndexOf(')') + 2).Split(' ')
            let started = UInt64.Parse fields[19]
            $"linux:{boot}:{started}"
        else
            $"utc:{child.StartTime.ToUniversalTime().Ticks}"
    { pid = child.Id; startIdentity = identity }

let private stopProcess (child:Process) =
    try
        if not child.HasExited then child.Kill(entireProcessTree = true)
    with :? InvalidOperationException when child.HasExited -> ()
    if not (child.WaitForExit(5000)) then
        invalidOp "Watch child did not exit within five seconds."

let stopExistingWatcher trace watcherName =
    match readOwnership watcherName with
    | Some ownership ->
        try
            use previous = Process.GetProcessById ownership.pid
            if not previous.HasExited && processOwnership previous = ownership then
                trace $"Stopping previous Andy Meier {watcherName} process tree {ownership.pid}."
                stopProcess previous
        with
        | :? ArgumentException
        | :? FileNotFoundException
        | :? DirectoryNotFoundException -> ()
    | None -> ()
    File.Delete(watcherPidFile watcherName)

let waitForPortAvailable watcherName serverUrl (timeout:TimeSpan) (token:CancellationToken) =
    let uri = Uri(validateUrl serverUrl)
    let elapsed = Stopwatch.StartNew()
    let rec wait () =
        token.ThrowIfCancellationRequested()
        try
            use listener = new TcpListener(IPAddress.Loopback, uri.Port)
            listener.Start()
        with :? SocketException ->
            if elapsed.Elapsed >= timeout then
                invalidOp $"{watcherName} cannot start because {serverUrl} is already in use. Stop its owner, then retry."
            token.WaitHandle.WaitOne(100) |> ignore
            wait ()
    wait ()

let runExclusiveWatcher trace watcherName serverUrl run =
    let serverUrl = validateUrl serverUrl
    use cancellation = new CancellationTokenSource()
    let cancelHandler = ConsoleCancelEventHandler(fun _ event -> event.Cancel <- true; cancellation.Cancel())
    Console.CancelKeyPress.AddHandler cancelHandler
    use termination =
        if OperatingSystem.IsWindows() then null
        else PosixSignalRegistration.Create(PosixSignal.SIGTERM, fun context -> context.Cancel <- true; cancellation.Cancel())
    use interruption =
        if OperatingSystem.IsWindows() then null
        else PosixSignalRegistration.Create(PosixSignal.SIGINT, fun context -> context.Cancel <- true; cancellation.Cancel())
    try
        stopExistingWatcher trace watcherName
        waitForPortAvailable watcherName serverUrl (TimeSpan.FromSeconds 5.) cancellation.Token
        use current = Process.GetCurrentProcess()
        let ownership = processOwnership current
        File.WriteAllText(watcherPidFile watcherName, formatOwnership ownership)
        try
            try run cancellation.Token
            with :? OperationCanceledException when cancellation.IsCancellationRequested -> ()
        finally
            if readOwnership watcherName = Some ownership then File.Delete(watcherPidFile watcherName)
    finally
        Console.CancelKeyPress.RemoveHandler cancelHandler

let processDefinition workDir command arguments environment =
    let start = ProcessStartInfo(command)
    start.UseShellExecute <- false
    start.WorkingDirectory <- workDir
    for argument in arguments do start.ArgumentList.Add argument
    for KeyValue(key, value) in environment do start.Environment[key] <- value
    start

let runPreparation (token:CancellationToken) (start:ProcessStartInfo) =
    token.ThrowIfCancellationRequested()
    use child = Process.Start start
    try
        child.WaitForExitAsync(token).WaitAsync(TimeSpan.FromMinutes 2.).GetAwaiter().GetResult()
        if child.ExitCode <> 0 then invalidOp $"{start.FileName} failed with exit code {child.ExitCode}."
    finally
        stopProcess child

let runForegroundProcesses (token:CancellationToken) definitions onReady =
    let children = ResizeArray<string * Process>()
    use lifetime = CancellationTokenSource.CreateLinkedTokenSource(token)
    let mutable monitor:Task = Task.CompletedTask
    try
        for name, start in definitions do
            lifetime.Token.ThrowIfCancellationRequested()
            children.Add(name, Process.Start(start:ProcessStartInfo))
        if children.Count = 0 then invalidArg (nameof definitions) "At least one watcher process is required."
        let exits =
            children
            |> Seq.map (fun (name, child) -> task {
                do! child.WaitForExitAsync()
                return name, child.ExitCode
            })
            |> Seq.toArray
        let firstExit = Task.WhenAny exits
        monitor <- task {
            let! _ = firstExit
            lifetime.Cancel()
        }
        try
            onReady lifetime.Token
            firstExit.WaitAsync(lifetime.Token).GetAwaiter().GetResult() |> ignore
        with :? OperationCanceledException when lifetime.IsCancellationRequested -> ()
        if not token.IsCancellationRequested && firstExit.IsCompleted then
            let name, code = firstExit.Result.Result
            invalidOp $"{name} exited unexpectedly with code {code}."
    finally
        let mutable failure = None
        for _, child in children do
            try stopProcess child
            with error -> if failure.IsNone then failure <- Some error
        monitor.GetAwaiter().GetResult()
        for _, child in children do child.Dispose()
        match failure with
        | Some error -> raise error
        | None -> ()

let waitForHealth (client:HttpClient) serverUrl timeout (token:CancellationToken) =
    use deadline = CancellationTokenSource.CreateLinkedTokenSource(token)
    deadline.CancelAfter(timeout:TimeSpan)
    let mutable healthy = false
    try
        while not healthy do
            deadline.Token.ThrowIfCancellationRequested()
            try
                use response = client.GetAsync($"{serverUrl}/health", deadline.Token).GetAwaiter().GetResult()
                healthy <- response.IsSuccessStatusCode
            with
            | :? HttpRequestException -> ()
            | :? OperationCanceledException when not deadline.IsCancellationRequested -> ()
            if not healthy then Task.Delay(100, deadline.Token).GetAwaiter().GetResult()
    with :? OperationCanceledException when not token.IsCancellationRequested ->
        invalidOp $"Andy Meier did not become healthy at {serverUrl} within {timeout}."

let run trace workDir =
    let serverUrl = configuredUrl ()
    let environment = Map [
        "ANALYTICS_ENABLED", "false"
        "ASPNETCORE_ENVIRONMENT", "Development"
        "DOTNET_USE_POLLING_FILE_WATCHER", "1"
        "SERVER_URL", serverUrl ]
    runExclusiveWatcher trace "watch" serverUrl (fun token ->
        runPreparation token (processDefinition workDir "dotnet" [ "restore" ] Map.empty)
        runPreparation token (processDefinition workDir "npm" [ "ci"; "--ignore-scripts" ] Map.empty)
        runForegroundProcesses token [
            "Prism assets", processDefinition workDir "npm" prismArguments Map.empty
            "Telemetry assets", processDefinition workDir "npm" telemetryArguments Map.empty
            "TailwindCSS", processDefinition workDir "tailwindcss" cssArguments Map.empty
            "Andy Meier Server", processDefinition workDir "dotnet" dotnetArguments environment
        ] (fun readyToken ->
            use client = new HttpClient(Timeout = TimeSpan.FromSeconds 1.)
            waitForHealth client serverUrl (TimeSpan.FromSeconds 60.) readyToken
            readyToken.ThrowIfCancellationRequested()
            trace $"Andy Meier is ready at {serverUrl}"))
