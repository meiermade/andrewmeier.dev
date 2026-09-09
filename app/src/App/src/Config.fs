namespace App

open System

module Env =
    let variable (key:string) =
        match Environment.GetEnvironmentVariable key with
        | value when String.IsNullOrEmpty value -> failwith $"Environment variable '{key}' is required"
        | value -> value

    let variableOrDefault (key:string) (defaultValue:string) =
        match Environment.GetEnvironmentVariable key with
        | value when String.IsNullOrEmpty value -> defaultValue
        | value -> value

type OpenTelemetryConfig =
    { endpoint:string }

module OpenTelemetryConfig =
    let load () =
        { endpoint = Env.variableOrDefault "OTEL_EXPORTER_OTLP_ENDPOINT" "http://localhost:4318" }

type ServerConfig =
    { url:string }

module ServerConfig =
    let load () =
        { url = Env.variableOrDefault "SERVER_URL" "https://localhost:5000" }

type AnalyticsConfig =
    { otelEndpoint:string option }

module AnalyticsConfig =
    let load () =
        let enabled = Env.variableOrDefault "ANALYTICS_ENABLED" "false" |> Boolean.Parse

        { otelEndpoint =
            if enabled then Env.variable "PUBLIC_OTEL_EXPORTER_OTLP_ENDPOINT" |> Some
            else None }

type Config =
    { analytics:AnalyticsConfig
      debug:bool
      appName:string
      server:ServerConfig
      openTelemetry:OpenTelemetryConfig }

module Config =
    let load () =
        { analytics = AnalyticsConfig.load ()
          debug = Env.variableOrDefault "DEBUG" "false" |> Boolean.Parse
          appName = "andymeier"
          server = ServerConfig.load ()
          openTelemetry = OpenTelemetryConfig.load () }
