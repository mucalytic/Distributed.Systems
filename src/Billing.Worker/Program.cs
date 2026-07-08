using Microsoft.Azure.Cosmos;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(_ =>
    new CosmosClient(builder.Configuration["Cosmos:ConnectionString"]));

var endpointConfiguration = new EndpointConfiguration("billing-worker");
endpointConfiguration.UseSerialization<SystemJsonSerializer>();

endpointConfiguration.UseTransport(new AzureServiceBusTransport(
    builder.Configuration["ServiceBus:ConnectionString"] ?? throw new InvalidOperationException("ServiceBus:ConnectionString not configured"),
    TopicTopology.Default));

endpointConfiguration.SendFailedMessagesTo("error");
endpointConfiguration.EnableInstallers();

builder.Services.AddNServiceBusEndpoint(endpointConfiguration);

builder.Build().Run();
