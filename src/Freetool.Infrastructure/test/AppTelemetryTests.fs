module Freetool.Infrastructure.Tests.AppTelemetryTests

open System
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open OpenTelemetry.Exporter
open OpenTelemetry.Logs
open OpenTelemetry.Metrics
open OpenTelemetry.Trace
open Freetool.Api.Telemetry
open Xunit

let private buildConfiguration values =
    ConfigurationBuilder().AddInMemoryCollection(values |> dict).Build()

let private configuredSettings = {
    OtlpConfigured = true
    OtlpEndpoint = Some(Uri("http://alloy.observability.svc.cluster.local:4318"))
    OtlpProtocol = OtlpExportProtocol.Grpc
    OtlpHeaders = "tenant_id=app-test"
    ResourceAttributes = [|
        Collections.Generic.KeyValuePair("service.namespace", box "internal-tools")
        Collections.Generic.KeyValuePair("internal_tools_app_id", box "test-app")
    |]
}

[<Fact>]
let app_telemetry_settings_derive_otlp_headers_and_resource_attributes_from_platform_env_vars () =
    let configuration =
        buildConfiguration [
            "OTEL_EXPORTER_OTLP_ENDPOINT", "http://alloy.observability.svc.cluster.local:4318"
            "OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf"
            "INTERNAL_TOOLS_APP_ID", "test-app"
            "INTERNAL_TOOLS_TENANT_ID", "app-test"
        ]

    let settings = AppTelemetrySettings.fromConfiguration configuration

    Assert.True(settings.OtlpConfigured)
    Assert.Equal(Some(Uri("http://alloy.observability.svc.cluster.local:4318")), settings.OtlpEndpoint)
    Assert.Equal(OtlpExportProtocol.HttpProtobuf, settings.OtlpProtocol)
    Assert.Equal("tenant_id=app-test", settings.OtlpHeaders)

    Assert.True(
        settings.ResourceAttributes
        |> Array.exists (fun attribute ->
            attribute.Key = "internal_tools_app_id"
            && attribute.Value :?> string = "test-app")
    )

    Assert.True(
        settings.ResourceAttributes
        |> Array.exists (fun attribute ->
            attribute.Key = "internal_tools_tenant_id"
            && attribute.Value :?> string = "app-test")
    )

[<Fact>]
let app_telemetry_settings_default_to_http_protobuf_and_empty_headers_when_tenant_env_vars_are_absent () =
    let configuration =
        buildConfiguration [ "OTEL_EXPORTER_OTLP_ENDPOINT", ""; "OTEL_EXPORTER_OTLP_PROTOCOL", "" ]

    let settings = AppTelemetrySettings.fromConfiguration configuration

    Assert.False(settings.OtlpConfigured)
    Assert.Equal(None, settings.OtlpEndpoint)
    Assert.Equal(OtlpExportProtocol.HttpProtobuf, settings.OtlpProtocol)
    Assert.Equal("", settings.OtlpHeaders)

[<Fact>]
let app_telemetry_settings_preserve_grpc_protocol_when_configured () =
    let configuration =
        buildConfiguration [
            "OTEL_EXPORTER_OTLP_ENDPOINT", "http://alloy.observability.svc.cluster.local:4317"
            "OTEL_EXPORTER_OTLP_PROTOCOL", "grpc"
        ]

    let settings = AppTelemetrySettings.fromConfiguration configuration

    Assert.True(settings.OtlpConfigured)
    Assert.Equal(Some(Uri("http://alloy.observability.svc.cluster.local:4317")), settings.OtlpEndpoint)
    Assert.Equal(OtlpExportProtocol.Grpc, settings.OtlpProtocol)

[<Fact>]
let configure_otlp_exporter_applies_signal_specific_otlp_http_endpoints_when_configured () =
    let options = OtlpExporterOptions()

    AppTelemetrySettings.configureOtlpExporter
        "traces"
        {
            configuredSettings with
                OtlpProtocol = OtlpExportProtocol.HttpProtobuf
        }
        options

    Assert.Equal(Uri("http://alloy.observability.svc.cluster.local:4318/v1/traces"), options.Endpoint)
    Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol)
    Assert.Equal("tenant_id=app-test", options.Headers)

[<Fact>]
let configure_otlp_exporter_preserves_otlp_http_endpoints_that_already_include_the_signal_path () =
    let options = OtlpExporterOptions()

    AppTelemetrySettings.configureOtlpExporter
        "logs"
        {
            configuredSettings with
                OtlpEndpoint = Some(Uri("http://alloy.observability.svc.cluster.local:4318/v1/logs"))
                OtlpProtocol = OtlpExportProtocol.HttpProtobuf
        }
        options

    Assert.Equal(Uri("http://alloy.observability.svc.cluster.local:4318/v1/logs"), options.Endpoint)
    Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol)
    Assert.Equal("tenant_id=app-test", options.Headers)

[<Fact>]
let configure_otlp_exporter_preserves_nested_otlp_http_endpoints_that_already_end_with_the_signal_path () =
    let options = OtlpExporterOptions()

    AppTelemetrySettings.configureOtlpExporter
        "metrics"
        {
            configuredSettings with
                OtlpEndpoint = Some(Uri("http://alloy.observability.svc.cluster.local:4318/otlp/v1/metrics"))
                OtlpProtocol = OtlpExportProtocol.HttpProtobuf
        }
        options

    Assert.Equal(Uri("http://alloy.observability.svc.cluster.local:4318/otlp/v1/metrics"), options.Endpoint)
    Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol)
    Assert.Equal("tenant_id=app-test", options.Headers)

[<Fact>]
let configure_otlp_exporter_appends_otlp_http_signal_paths_onto_non_root_base_paths () =
    let options = OtlpExporterOptions()

    AppTelemetrySettings.configureOtlpExporter
        "metrics"
        {
            configuredSettings with
                OtlpEndpoint = Some(Uri("http://alloy.observability.svc.cluster.local:4318/otlp"))
                OtlpProtocol = OtlpExportProtocol.HttpProtobuf
        }
        options

    Assert.Equal(Uri("http://alloy.observability.svc.cluster.local:4318/otlp/v1/metrics"), options.Endpoint)
    Assert.Equal(OtlpExportProtocol.HttpProtobuf, options.Protocol)
    Assert.Equal("tenant_id=app-test", options.Headers)

[<Fact>]
let configure_otlp_exporter_preserves_grpc_endpoints_without_appending_signal_paths () =
    let options = OtlpExporterOptions()

    AppTelemetrySettings.configureOtlpExporter "traces" configuredSettings options

    Assert.Equal(configuredSettings.OtlpEndpoint.Value, options.Endpoint)
    Assert.Equal(OtlpExportProtocol.Grpc, options.Protocol)
    Assert.Equal("tenant_id=app-test", options.Headers)

[<Fact>]
let configure_otlp_exporter_leaves_endpoint_and_headers_unset_when_omitted () =
    let options = OtlpExporterOptions()

    AppTelemetrySettings.configureOtlpExporter
        "traces"
        {
            configuredSettings with
                OtlpConfigured = false
                OtlpEndpoint = None
                OtlpHeaders = ""
        }
        options

    Assert.Equal(Uri("http://localhost:4317"), options.Endpoint)
    Assert.Equal(OtlpExportProtocol.Grpc, options.Protocol)
    Assert.True(String.IsNullOrEmpty(options.Headers))

[<Fact>]
let add_open_telemetry_registers_providers_and_otel_logging_options () =
    let services = ServiceCollection()

    let settingsWithoutExporter = {
        configuredSettings with
            OtlpConfigured = false
            OtlpEndpoint = None
            OtlpHeaders = ""
    }

    AppTelemetrySettings.addOpenTelemetry services "Freetool.Api" "freetool-api" settingsWithoutExporter

    use provider = services.BuildServiceProvider()

    let loggerOptions =
        provider.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value

    use _tracerProvider = provider.GetRequiredService<TracerProvider>()
    use _meterProvider = provider.GetRequiredService<MeterProvider>()

    Assert.True(loggerOptions.IncludeScopes)
    Assert.True(loggerOptions.IncludeFormattedMessage)
    Assert.True(loggerOptions.ParseStateValues)

[<Fact>]
let add_open_telemetry_registers_otlp_exporters_when_observability_is_configured () =
    let services = ServiceCollection()

    AppTelemetrySettings.addOpenTelemetry services "Freetool.Api" "freetool-api" configuredSettings

    use provider = services.BuildServiceProvider()

    let loggerOptions =
        provider.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value

    use _tracerProvider = provider.GetRequiredService<TracerProvider>()
    use _meterProvider = provider.GetRequiredService<MeterProvider>()

    Assert.True(loggerOptions.IncludeScopes)
    Assert.True(loggerOptions.IncludeFormattedMessage)
    Assert.True(loggerOptions.ParseStateValues)

[<Fact>]
let add_configured_open_telemetry_reads_configuration_and_registers_telemetry_services () =
    let services = ServiceCollection()

    let configuration =
        buildConfiguration [ "INTERNAL_TOOLS_APP_ID", "test-app"; "INTERNAL_TOOLS_TENANT_ID", "app-test" ]

    AppTelemetrySettings.addConfiguredOpenTelemetry services "Freetool.Api" "freetool-api" configuration

    use provider = services.BuildServiceProvider()
    Assert.NotNull(provider.GetService<TracerProvider>())
    Assert.NotNull(provider.GetService<MeterProvider>())