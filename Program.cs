using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

// ASP.NET Core integration, so the barcode function keeps using HttpRequest and IActionResult
builder.ConfigureFunctionsWebApplication();

// No Application Insights packages in the worker on purpose: its logs go through the Functions host,
// which applies host.json and sends them to Application Insights as the in-process model did.
// (The worker's own App Insights SDK would only send Warning and above by default, dropping the
// querystring log written for every request.)

builder.Build().Run();