module App.Tests.ConfigTests

open System
open App
open Expecto

let private withEnvironment values action =
    let original = values |> List.map (fun (key, _) -> key, Environment.GetEnvironmentVariable key)
    try
        for key, value in values do Environment.SetEnvironmentVariable(key, value)
        action ()
    finally
        for key, value in original do Environment.SetEnvironmentVariable(key, value)

[<Tests>]
let tests =
    testList "Browser analytics configuration" [
        test "absent and false disable browser analytics even with a public endpoint" {
            for enabled in [ null; ""; "false"; "FALSE" ] do
                for endpoint in [ null; ""; "https://otel.test" ] do
                    withEnvironment
                        [ "ANALYTICS_ENABLED", enabled
                          "PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT", endpoint ]
                        (fun () -> Expect.isNone (AnalyticsConfig.load ()).otelEndpoint "disabled by default")
        }

        test "true enables the explicitly configured public endpoint" {
            for enabled in [ "true"; "TRUE" ] do
                withEnvironment
                    [ "ANALYTICS_ENABLED", enabled
                      "PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT", "https://otel.test" ]
                    (fun () -> Expect.equal (AnalyticsConfig.load ()).otelEndpoint (Some "https://otel.test") "explicit endpoint")
        }

        test "enabled analytics requires a public endpoint instead of a localhost fallback" {
            for endpoint in [ null; "" ] do
                withEnvironment
                    [ "ANALYTICS_ENABLED", "true"
                      "PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT", endpoint ]
                    (fun () ->
                        Expect.throwsC
                            (fun () -> AnalyticsConfig.load () |> ignore)
                            (fun error -> Expect.stringContains error.Message "PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT" "names the missing setting"))
        }

        test "invalid switch values fail rather than silently enabling analytics" {
            withEnvironment [ "ANALYTICS_ENABLED", "yes" ] (fun () ->
                Expect.throwsT<FormatException> (fun () -> AnalyticsConfig.load () |> ignore) "Boolean.Parse contract")
        }

        test "browser switch does not change server observability configuration" {
            for enabled in [ null; "false"; "true" ] do
                withEnvironment
                    [ "ANALYTICS_ENABLED", enabled
                      "PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT", "https://otel.test"
                      "OTEL_EXPORTER_OTLP_ENDPOINT", "http://collector:4318" ]
                    (fun () -> Expect.equal (Config.load ()).openTelemetry.endpoint "http://collector:4318" "independent server exporter")
        }
    ]
    |> testSequenced
