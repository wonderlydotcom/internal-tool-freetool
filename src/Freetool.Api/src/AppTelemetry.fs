namespace Freetool.Api.Telemetry

open System
open System.Collections.Generic
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open OpenTelemetry.Exporter
open OpenTelemetry.Logs
open OpenTelemetry.Metrics
open OpenTelemetry.Resources
open OpenTelemetry.Trace

type AppTelemetrySettings = {
    OtlpConfigured: bool
    OtlpEndpoint: Uri option
    OtlpProtocol: OtlpExportProtocol
    OtlpHeaders: string
    ResourceAttributes: KeyValuePair<string, obj> array
}

[<RequireQualifiedAccess>]
module AppTelemetrySettings =
    let private otlpSignalPath =
        function
        | "logs" -> "/v1/logs"
        | "metrics" -> "/v1/metrics"
        | _ -> "/v1/traces"

    let private withSignalPath (endpoint: Uri) (signal: string) =
        let builder = UriBuilder(endpoint)
        let signalPath = otlpSignalPath signal
        let currentPath = builder.Path.TrimEnd('/')

        if
            String.Equals(currentPath, signalPath, StringComparison.OrdinalIgnoreCase)
            || currentPath.EndsWith(signalPath, StringComparison.OrdinalIgnoreCase)
        then
            builder.Uri
        elif String.IsNullOrWhiteSpace currentPath || currentPath = "/" then
            builder.Path <- signalPath
            builder.Uri
        else
            builder.Path <- $"{currentPath}{signalPath}"
            builder.Uri

    let private configuredOrEmpty (configuration: IConfiguration) (key: string) =
        configuration[key]
        |> Option.ofObj
        |> Option.defaultValue String.Empty
        |> fun value -> value.Trim()

    let fromConfiguration (configuration: IConfiguration) =
        let otlpEndpoint = configuredOrEmpty configuration "OTEL_EXPORTER_OTLP_ENDPOINT"
        let otlpProtocol = configuredOrEmpty configuration "OTEL_EXPORTER_OTLP_PROTOCOL"
        let tenantId = configuredOrEmpty configuration "INTERNAL_TOOLS_TENANT_ID"
        let appId = configuredOrEmpty configuration "INTERNAL_TOOLS_APP_ID"

        let resourceAttributes = ResizeArray<KeyValuePair<string, obj>>()
        resourceAttributes.Add(KeyValuePair("service.namespace", box "internal-tools"))

        if appId <> String.Empty then
            resourceAttributes.Add(KeyValuePair("internal_tools_app_id", box appId))

        if tenantId <> String.Empty then
            resourceAttributes.Add(KeyValuePair("internal_tools_tenant_id", box tenantId))

        {
            OtlpConfigured = otlpEndpoint <> String.Empty
            OtlpEndpoint =
                if otlpEndpoint = String.Empty then
                    None
                else
                    Some(Uri(otlpEndpoint, UriKind.Absolute))
            OtlpProtocol =
                match otlpProtocol.ToLowerInvariant() with
                | "grpc" -> OtlpExportProtocol.Grpc
                | _ -> OtlpExportProtocol.HttpProtobuf
            OtlpHeaders =
                if tenantId = String.Empty then
                    String.Empty
                else
                    $"tenant_id={tenantId}"
            ResourceAttributes = resourceAttributes.ToArray()
        }

    let configureOtlpExporter (signal: string) (settings: AppTelemetrySettings) (options: OtlpExporterOptions) =
        match settings.OtlpEndpoint with
        | Some endpoint when settings.OtlpProtocol = OtlpExportProtocol.HttpProtobuf ->
            options.Endpoint <- withSignalPath endpoint signal
        | Some endpoint -> options.Endpoint <- endpoint
        | None -> ()

        options.Protocol <- settings.OtlpProtocol

        if settings.OtlpHeaders <> String.Empty then
            options.Headers <- settings.OtlpHeaders

    let addOpenTelemetry
        (services: IServiceCollection)
        (activitySourceName: string)
        (serviceName: string)
        (settings: AppTelemetrySettings)
        =
        services.AddLogging(fun logging ->
            logging.AddOpenTelemetry(fun options ->
                options.IncludeScopes <- true
                options.IncludeFormattedMessage <- true
                options.ParseStateValues <- true

                if settings.OtlpConfigured then
                    options.AddOtlpExporter(configureOtlpExporter "logs" settings) |> ignore)
            |> ignore)
        |> ignore

        services
            .AddOpenTelemetry()
            .ConfigureResource(fun resource ->
                resource.AddService(serviceName) |> ignore
                resource.AddAttributes(settings.ResourceAttributes) |> ignore)
            .WithTracing(fun tracing ->
                tracing
                    .AddSource(activitySourceName)
                    .AddAspNetCoreInstrumentation(fun options -> options.RecordException <- true)
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddHttpClientInstrumentation(fun options -> options.RecordException <- true)
                |> ignore

                if settings.OtlpConfigured then
                    tracing.AddOtlpExporter(configureOtlpExporter "traces" settings) |> ignore)
            .WithMetrics(fun metrics ->
                metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation() |> ignore

                if settings.OtlpConfigured then
                    metrics.AddOtlpExporter(configureOtlpExporter "metrics" settings) |> ignore)
        |> ignore

    let addConfiguredOpenTelemetry
        (services: IServiceCollection)
        (activitySourceName: string)
        (serviceName: string)
        (configuration: IConfiguration)
        =
        configuration
        |> fromConfiguration
        |> addOpenTelemetry services activitySourceName serviceName