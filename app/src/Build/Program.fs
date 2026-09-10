open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO
open Fake.IO.FileSystemOperators
open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Security.Cryptography
open System.Text.Json

Environment.GetCommandLineArgs()
|> Array.tail
|> Array.toList
|> Context.FakeExecutionContext.Create false "build.fsx"
|> Context.Fake
|> Context.setExecutionContext

let srcDir = Path.getDirectory __SOURCE_DIRECTORY__
let rootDir = Path.getDirectory srcDir
let repoDir = Path.getDirectory rootDir
let appDir = srcDir </> "App"
let e2eDir = repoDir </> "e2e"
let outDir = appDir </> "out"
let wwwrootDir = outDir </> "wwwroot"
let hashedAssetExtensions =
    set [ ".css"; ".gif"; ".ico"; ".jpeg"; ".jpg"; ".js"; ".png"; ".svg"; ".webp"; ".woff"; ".woff2" ]

let toWebPath (root:string) (filePath:string) =
    let relativePath = Path.GetRelativePath(root, filePath).Replace(Path.DirectorySeparatorChar, '/')
    "/" + relativePath

let fingerprintedFilePath (filePath:string) (hash:string) =
    let dir = Path.GetDirectoryName(filePath)
    let name = Path.GetFileNameWithoutExtension(filePath)
    let ext = Path.GetExtension(filePath)
    Path.Combine(dir, $"{name}.{hash}{ext}")

let hashFileContents (filePath:string) =
    use stream = File.OpenRead(filePath)
    use sha256 = SHA256.Create()
    sha256.ComputeHash(stream)
    |> Convert.ToHexString
    |> fun hash -> hash.ToLowerInvariant().Substring(0, 12)

let inline (==>!) x y = x ==> y |> ignore

let fingerprintAssets (root:string) =
    let files =
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        |> Seq.filter (fun path -> hashedAssetExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        |> Seq.sort
        |> Seq.toList

    let manifest =
        files
        |> Seq.map (fun path ->
            let hash = hashFileContents path
            let fingerprintedPath = fingerprintedFilePath path hash
            File.Copy(path, fingerprintedPath, true)
            toWebPath root path, toWebPath root fingerprintedPath)
        |> Map.ofSeq

    let manifestPath = Path.Combine(root, "asset-manifest.json")
    File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest))

let exec command workDir args =
    CreateProcess.fromRawCommand command args
    |> CreateProcess.withWorkingDirectory workDir
    |> CreateProcess.ensureExitCode
    |> Proc.start

let availableLocalPort () =
    use listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    (listener.LocalEndpoint :?> IPEndPoint).Port

let environmentValue name fallback =
    Environment.GetEnvironmentVariable name
    |> Option.ofObj
    |> Option.filter (String.IsNullOrWhiteSpace >> not)
    |> Option.defaultValue fallback

let playwrightImage () =
    let image = File.ReadAllText(e2eDir </> "playwright-image.txt").Trim()
    let packageJson = File.ReadAllText(e2eDir </> "package.json")
    let packageVersion = BrowserE2E.playwrightPackageVersion packageJson
    BrowserE2E.verifyPlaywrightImage packageVersion image
    image

Target.create "Watch" <| fun _ ->
    LocalWatch.run Trace.trace appDir

Target.create "BuildCss" <| fun _ ->
    exec "tailwindcss" appDir [ "--input"; "./input.css"; "--output"; "./wwwroot/css/compiled.css"; "--minify" ]
    |> _.Wait()

Target.create "BuildBrowser" <| fun _ ->
    if not (Environment.GetEnvironmentVariable("SKIP_BROWSER_BUILD") = "true") then
        exec "npm" appDir [ "ci"; "--ignore-scripts" ] |> _.Wait()
        exec "npm" appDir [ "run"; "check" ] |> _.Wait()
        exec "npm" appDir [ "run"; "build" ] |> _.Wait()

Target.create "Test" <| fun _ ->
    exec "npm" appDir [ "ci"; "--ignore-scripts" ] |> _.Wait()
    exec "npm" appDir [ "run"; "check" ] |> _.Wait()
    exec "npm" appDir [ "test" ] |> _.Wait()
    exec "dotnet" rootDir [ "run"; "--project"; "src/Build.Tests/Build.Tests.fsproj" ] |> _.Wait()
    exec "dotnet" rootDir [ "run"; "--project"; "src/Tests/Tests.fsproj" ] |> _.Wait()

Target.create "TestE2E" <| fun _ ->
    let baseUrl = $"http://127.0.0.1:{availableLocalPort ()}"
    let stateDirectory =
        environmentValue "RUNNER_TEMP" (Path.GetTempPath())
        </> "andymeier-e2e"
    BrowserE2E.runLocal Trace.trace repoDir e2eDir (playwrightImage ()) baseUrl stateDirectory

Target.create "VerifyPublishedAnalytics" <| fun _ ->
    BrowserE2E.runPublished
        Trace.trace
        e2eDir
        (playwrightImage ())
        (environmentValue "SITE_E2E_BASE_URL" "https://andymeier.dev")

Target.create "Publish" <| fun _ ->
    Shell.cleanDir outDir
    exec "dotnet" appDir [
        "publish"
        "--output"; "./out"
        "--self-contained"; "false"
    ]
    |> _.Wait()
    fingerprintAssets wwwrootDir

Target.create "Default" (fun _ -> Target.listAvailable())

"BuildBrowser" ==>! "Publish"
"BuildCss" ==>! "Publish"
"BuildBrowser" ==>! "TestE2E"
"BuildCss" ==>! "TestE2E"

Target.runOrDefaultWithArguments "Default"
