using Kart.Shared.Configuration;
using Kart.Shared.ErrorHandling;
using Kart.Shared.Observability;
using Kart.Shipping.Api;
using Kart.Shipping.Api.Endpoints;
using Kart.Shipping.Api.HealthChecks;
using Kart.Shipping.Application;
using Kart.Shipping.Domain.Exceptions;
using Kart.Shipping.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using FluentValidation;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// kart-conventions.md Configuration Management: GlobalConfig external-secrets-file bootstrap,
// shared across every service - never reimplemented per service.
builder.AddKartGlobalConfig("kart-shipping-service");

// kart-conventions.md Observability section: Serilog + OpenTelemetry SDK behind one DI call.
// kart-shipping-service is one of the four Order-Saga services mandated to sample 100% of traces
// (kart-conventions.md) - the sampling ratio is left at Kart.Shared.Observability's own default
// (1.0), never overridden down for this service.
builder.AddKartObservability("kart-shipping-service");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// /health/live: process is up, no dependency check. /health/ready: this service's job depends on
// Postgres being reachable AND migrated, and Mongo being reachable (the CQRS read side) - matching
// kart-infra's service-chart probe convention.
builder.Services.AddHealthChecks()
    .AddCheck<ShippingDbHealthCheck>("shipping-db", tags: ["ready"]);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// kart-conventions.md Error Handling: one global exception handler, one consistent Problem
// envelope - domain/business errors use the Result pattern instead (Api/Common/ResultExtensions).
builder.Services.AddKartErrorHandling(options => options
    .Map<InvalidShipmentTransitionException>(StatusCodes.Status409Conflict, "invalid_shipment_transition")
    .Map<ValidationException>(StatusCodes.Status400BadRequest, "validation_error"));

var app = builder.Build();

await StartupConnectivityChecks.RunAsync(app);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseKartErrorHandling();
app.UseHttpsRedirection();

// Per-HTTP-request Information log (method/path/status/elapsed) - the RED-style access log
// observability-standards.md expects on every endpoint, for free.
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

// Prometheus scrape target (observability-standards.md's mandatory `/metrics`).
app.MapPrometheusScrapingEndpoint();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

// api-contract.yaml: every versioned business endpoint starts at /v1 (kart-conventions.md, "API
// Versioning"). Internal/ops-only surface - never routed through the public API Gateway or
// kart-admin-service (ddd-model.md Modeling Decision #8).
app.MapShipmentEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in IntegrationTests/ContractTests.
public partial class Program;
