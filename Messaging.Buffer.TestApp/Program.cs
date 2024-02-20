﻿using Messaging.Buffer.Service;
using Messaging.Buffer.TestApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class Program
{
    public static void Main(string[] args)
    {
        IConfiguration Configuration = new ConfigurationBuilder()
       .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
       .Build();

        // Services
        var serviceProvider = new ServiceCollection()
            .AddSingleton<Application>()
            .AddLogging(x => { x.AddConsole(); x.SetMinimumLevel(LogLevel.Trace); })

            // Register the service and any buffer
            .AddMessagingBuffer(Configuration, "Redis")
            .AddBuffer<HelloWorldRequestBuffer, HelloWorldRequest, HelloWorldResponse>()
            .AddBuffer<TotalCoundRequestBuffer, TotalCountRequest, TotalCountResponse>()

            .BuildServiceProvider();

        var app = serviceProvider.GetService<Application>();

        // Test 1: Send a Hello World request. Each instance respond with Hello World. Initial request display a list of all responses
        app.RunHelloWorld();

        // Test 2: Send a TotalCount request. The result is the total of all response + an initial value in
        app.RunTotalCount();

        while (true) { }
    }
}