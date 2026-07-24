using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.DataAccess.Migrations;
using SylvaNote.Server;
using SylvaNote.Server.Services;

namespace SylvaNote.IntegrationTests
{
    // Boots the real server pipeline (Kestrel, auth, GaldrJson, endpoints) on a random
    // port over an in-memory server DB.
    public sealed class ServerFixture : IDisposable
    {
        public const string Token = "test-token";

        private readonly WebApplication _app;

        public ConnectionManager ConnectionManager { get; }
        public string BaseUrl { get; }
        public HttpClient Client { get; }

        public ServerFixture()
        {
            ConnectionManager = new ConnectionManager();
            ConnectionManager.OpenInMemory($"serverdb-{Guid.NewGuid():N}", MigrationSets.Server());

            ServerConfig config = new ServerConfig { Token = Token };
            _app = ServerApp.Create(config, ConnectionManager, Array.Empty<string>());
            _app.Urls.Add("http://127.0.0.1:0");
            _app.StartAsync().GetAwaiter().GetResult();

            BaseUrl = _app.Urls.First();
            Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }

        public void Dispose()
        {
            Client.Dispose();
            _app.StopAsync().GetAwaiter().GetResult();
            ConnectionManager.Close();
        }
    }
}
