using Microsoft.Azure.Cosmos;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(_ =>
    new CosmosClient(builder.Configuration["Cosmos:ConnectionString"]));

// The endpoint name becomes the input QUEUE name in Azure Service Bus.
var endpointConfiguration = new EndpointConfiguration("orders-worker");
endpointConfiguration.UseSerialization<SystemJsonSerializer>();

endpointConfiguration.UseTransport(new AzureServiceBusTransport(
    builder.Configuration["ServiceBus:ConnectionString"] ?? throw new InvalidOperationException("ServiceBus:ConnectionString not configured"),
    TopicTopology.Default));

// After all retries are exhausted, poison messages land here instead of
// being lost — a human (or ServicePulse) can inspect and replay them.
endpointConfiguration.SendFailedMessagesTo("error");

// Creates the queues/topics/subscriptions in the namespace on startup.
// Fine for a demo; in production this is usually a deployment-time step.
endpointConfiguration.EnableInstallers();

builder.Services.AddNServiceBusEndpoint(endpointConfiguration);

builder.Build().Run();
