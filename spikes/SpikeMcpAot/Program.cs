using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace SpikeMcpAot
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            int exitCode;

            if (args.Length == 1 && args[0] == "--stdio")
            {
                exitCode = await RunStdioServer();
            }
            else
            {
                exitCode = await RunDriver();
            }

            return exitCode;
        }

        private static McpServerTool CreateEchoTool()
        {
            return McpServerTool.Create(
                (string message) => $"Echo: {message}",
                new McpServerToolCreateOptions { Name = "echo" });
        }

        private static async Task<int> RunStdioServer()
        {
            await using McpServer server = McpServer.Create(
                new StdioServerTransport("SpikeMcpAot"),
                new McpServerOptions { ToolCollection = [CreateEchoTool()] });
            await server.RunAsync();
            return 0;
        }

        private static async Task<int> RunDriver()
        {
            int exitCode = 1;

            try
            {
                await TestStdio();
                Console.WriteLine("stdio transport round-trip OK");

                await TestHttp();
                Console.WriteLine("HTTP streamable transport round-trip OK");

                Console.WriteLine("PASS: manual tool registration worked over stdio and MapMcp under AOT.");
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex}");
            }

            return exitCode;
        }

        private static async Task TestStdio()
        {
            StdioClientTransport transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "SpikeMcpAot",
                Command = Environment.ProcessPath,
                Arguments = ["--stdio"]
            });

            await using (McpClient client = await McpClient.CreateAsync(transport))
            {
                await VerifyEcho(client);
            }
        }

        private static async Task TestHttp()
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(Array.Empty<string>());
            builder.Services.AddMcpServer()
                .WithHttpTransport()
                .WithTools(new McpServerTool[] { CreateEchoTool() });
            WebApplication app = builder.Build();
            app.Urls.Add("http://127.0.0.1:0");
            app.MapMcp();
            await app.StartAsync();

            try
            {
                string address = app.Urls.First();
                HttpClientTransport transport = new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(address),
                    TransportMode = HttpTransportMode.StreamableHttp
                });

                await using (McpClient client = await McpClient.CreateAsync(transport))
                {
                    await VerifyEcho(client);
                }
            }
            finally
            {
                await app.StopAsync();
            }
        }

        private static async Task VerifyEcho(McpClient client)
        {
            IList<McpClientTool> tools = await client.ListToolsAsync();
            McpClientTool echo = tools.First(t => t.Name == "echo");

            object result = await echo.InvokeAsync(new AIFunctionArguments { ["message"] = "hello spike" });
            if (result == null || !result.ToString().Contains("Echo: hello spike"))
            {
                throw new InvalidOperationException($"Unexpected echo result: {result}");
            }
        }
    }
}
