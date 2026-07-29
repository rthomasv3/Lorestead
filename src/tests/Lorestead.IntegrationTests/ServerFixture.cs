using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Lorestead.Core.DataAccess;
using Lorestead.Core.DataAccess.Migrations;
using Lorestead.Server;
using Lorestead.Server.Services;

namespace Lorestead.IntegrationTests
{
    // Boots the real server pipeline (Kestrel, auth, GaldrJson, endpoints) on a random
    // port over an in-memory server DB.
    public sealed class ServerFixture : IDisposable
    {
        public const string Token = "test-token";
        public const string OAuthClientId = "test-client";
        public const string OAuthClientSecret = "test-client-secret";
        public const string OAuthRedirectUri = "https://claude.test/api/mcp/auth_callback";

        private readonly WebApplication _app;

        public ConnectionManager ConnectionManager { get; }
        public string BaseUrl { get; }
        public HttpClient Client { get; }

        public ServerFixture()
        {
            ConnectionManager = new ConnectionManager();
            ConnectionManager.OpenInMemory($"serverdb-{Guid.NewGuid():N}", MigrationSets.Server());

            ServerConfig config = new ServerConfig
            {
                Token = Token,
                OAuthClientId = OAuthClientId,
                OAuthClientSecret = OAuthClientSecret,
                OAuthRedirectUris = new[] { OAuthRedirectUri },
            };
            _app = ServerApp.Create(config, ConnectionManager, Array.Empty<string>());
            _app.Urls.Add("http://127.0.0.1:0");
            _app.StartAsync().GetAwaiter().GetResult();

            BaseUrl = _app.Urls.First();
            // The issuer is only knowable after Kestrel picks its random port; the
            // endpoints read PublicUrl per request, so setting it here is safe.
            config.PublicUrl = BaseUrl;
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
