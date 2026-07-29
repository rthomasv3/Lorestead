using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using ModelContextProtocol.Client;
using Xunit;

namespace Lorestead.IntegrationTests
{
    // The OAuth layer for claude.ai custom connectors: discovery, authorization
    // code + PKCE against the preconfigured client, token exchange and rotation,
    // and the auth matrix (OAuth tokens open /mcp and nothing else; the
    // deployment bearer keeps working everywhere).
    public sealed class OAuthTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task DiscoveryDocsAreServedAnonymously()
        {
            using ServerFixture server = new ServerFixture();
            using HttpClient anonymous = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

            using JsonDocument resource = JsonDocument.Parse(
                await anonymous.GetStringAsync("/.well-known/oauth-protected-resource", Token));
            Assert.Equal($"{server.BaseUrl}/mcp", resource.RootElement.GetProperty("resource").GetString());
            Assert.Equal(server.BaseUrl, resource.RootElement.GetProperty("authorization_servers")[0].GetString());

            using JsonDocument authServer = JsonDocument.Parse(
                await anonymous.GetStringAsync("/.well-known/oauth-authorization-server", Token));
            Assert.Equal(server.BaseUrl, authServer.RootElement.GetProperty("issuer").GetString());
            Assert.Equal($"{server.BaseUrl}/authorize", authServer.RootElement.GetProperty("authorization_endpoint").GetString());
            Assert.Equal($"{server.BaseUrl}/token", authServer.RootElement.GetProperty("token_endpoint").GetString());

            // The RFC path-inserted variants answer too - clients differ.
            using HttpResponseMessage variant = await anonymous.GetAsync("/.well-known/oauth-protected-resource/mcp", Token);
            Assert.Equal(HttpStatusCode.OK, variant.StatusCode);
        }

        [Fact]
        public async Task AnUnauthenticatedMcpRequestAdvertisesTheResourceMetadata()
        {
            using ServerFixture server = new ServerFixture();
            using HttpClient anonymous = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

            using HttpResponseMessage response = await anonymous.PostAsync(
                "/mcp", new StringContent("{}", Encoding.UTF8, "application/json"), Token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(
                $"resource_metadata=\"{server.BaseUrl}/.well-known/oauth-protected-resource\"",
                response.Headers.WwwAuthenticate.ToString());
        }

        [Fact]
        public async Task TheFullAuthorizationCodeFlowReachesMcp()
        {
            using ServerFixture server = new ServerFixture();
            string verifier = NewVerifier();
            string code = await AuthorizeAndCaptureCode(server, verifier);

            using JsonDocument tokens = await ExchangeCode(server, code, verifier);
            string accessToken = tokens.RootElement.GetProperty("access_token").GetString();
            Assert.Equal("Bearer", tokens.RootElement.GetProperty("token_type").GetString());
            Assert.False(string.IsNullOrEmpty(tokens.RootElement.GetProperty("refresh_token").GetString()));

            await using McpClient client = await ConnectWithToken(server, accessToken);
            IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: Token);
            Assert.NotEmpty(tools);
        }

        [Fact]
        public async Task BasicClientAuthenticationWorksAtTheTokenEndpoint()
        {
            using ServerFixture server = new ServerFixture();
            string verifier = NewVerifier();
            string code = await AuthorizeAndCaptureCode(server, verifier);

            using HttpClient plain = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/token");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{ServerFixture.OAuthClientId}:{ServerFixture.OAuthClientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = ServerFixture.OAuthRedirectUri,
            });

            using HttpResponseMessage response = await plain.SendAsync(request, Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RefreshRotatesAndTheOldRefreshTokenDies()
        {
            using ServerFixture server = new ServerFixture();
            string verifier = NewVerifier();
            string code = await AuthorizeAndCaptureCode(server, verifier);
            using JsonDocument first = await ExchangeCode(server, code, verifier);
            string oldRefresh = first.RootElement.GetProperty("refresh_token").GetString();

            using JsonDocument second = await PostToken(server, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = oldRefresh,
                ["client_id"] = ServerFixture.OAuthClientId,
                ["client_secret"] = ServerFixture.OAuthClientSecret,
            }, HttpStatusCode.OK);
            string newAccess = second.RootElement.GetProperty("access_token").GetString();

            // The rotated pair works; the spent refresh token does not.
            await using McpClient client = await ConnectWithToken(server, newAccess);
            Assert.NotEmpty(await client.ListToolsAsync(cancellationToken: Token));

            using JsonDocument replay = await PostToken(server, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = oldRefresh,
                ["client_id"] = ServerFixture.OAuthClientId,
                ["client_secret"] = ServerFixture.OAuthClientSecret,
            }, HttpStatusCode.BadRequest);
            Assert.Equal("invalid_grant", replay.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task TheAuthorizationCodeIsSingleUse()
        {
            using ServerFixture server = new ServerFixture();
            string verifier = NewVerifier();
            string code = await AuthorizeAndCaptureCode(server, verifier);

            using JsonDocument first = await ExchangeCode(server, code, verifier);
            Assert.True(first.RootElement.TryGetProperty("access_token", out _));

            using JsonDocument replay = await PostToken(server, CodeExchangeForm(code, verifier), HttpStatusCode.BadRequest);
            Assert.Equal("invalid_grant", replay.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task AWrongVerifierIsRejected()
        {
            using ServerFixture server = new ServerFixture();
            string code = await AuthorizeAndCaptureCode(server, NewVerifier());

            using JsonDocument response = await PostToken(server, CodeExchangeForm(code, NewVerifier()), HttpStatusCode.BadRequest);
            Assert.Equal("invalid_grant", response.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task AWrongClientSecretIsRejected()
        {
            using ServerFixture server = new ServerFixture();
            string verifier = NewVerifier();
            string code = await AuthorizeAndCaptureCode(server, verifier);

            Dictionary<string, string> form = CodeExchangeForm(code, verifier);
            form["client_secret"] = "not-the-secret";
            using JsonDocument response = await PostToken(server, form, HttpStatusCode.Unauthorized);
            Assert.Equal("invalid_client", response.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task AnOAuthTokenOpensNothingButMcp()
        {
            using ServerFixture server = new ServerFixture();
            string verifier = NewVerifier();
            string code = await AuthorizeAndCaptureCode(server, verifier);
            using JsonDocument tokens = await ExchangeCode(server, code, verifier);
            string accessToken = tokens.RootElement.GetProperty("access_token").GetString();

            using HttpClient asAgent = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
            asAgent.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            using HttpResponseMessage changes = await asAgent.GetAsync("/changes", Token);
            Assert.Equal(HttpStatusCode.Unauthorized, changes.StatusCode);
            using HttpResponseMessage status = await asAgent.GetAsync("/status", Token);
            Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
        }

        [Fact]
        public async Task AnUnknownRedirectUriGetsA400NotARedirect()
        {
            using ServerFixture server = new ServerFixture();
            using HttpClient noRedirects = NonRedirectingClient(server);

            string url = "/authorize?response_type=code"
                + $"&client_id={ServerFixture.OAuthClientId}"
                + "&redirect_uri=" + Uri.EscapeDataString("https://attacker.example/steal")
                + "&code_challenge=abc&code_challenge_method=S256";
            using HttpResponseMessage response = await noRedirects.GetAsync(url, Token);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // --- Helpers ---

        private static string NewVerifier()
        {
            return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        }

        private static HttpClient NonRedirectingClient(ServerFixture server)
        {
            HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false };
            return new HttpClient(handler) { BaseAddress = new Uri(server.BaseUrl) };
        }

        private static async Task<string> AuthorizeAndCaptureCode(ServerFixture server, string verifier)
        {
            string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string url = "/authorize?response_type=code"
                + $"&client_id={ServerFixture.OAuthClientId}"
                + "&redirect_uri=" + Uri.EscapeDataString(ServerFixture.OAuthRedirectUri)
                + $"&code_challenge={challenge}&code_challenge_method=S256"
                + "&state=xyz";

            using HttpClient noRedirects = NonRedirectingClient(server);
            using HttpResponseMessage response = await noRedirects.GetAsync(url, Token);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);

            Uri location = response.Headers.Location;
            Assert.StartsWith(ServerFixture.OAuthRedirectUri, location.ToString());
            System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(location.Query);
            Assert.Equal("xyz", query["state"]);
            Assert.Equal(server.BaseUrl, query["iss"]);
            Assert.False(string.IsNullOrEmpty(query["code"]));
            return query["code"];
        }

        private static Dictionary<string, string> CodeExchangeForm(string code, string verifier)
        {
            return new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = ServerFixture.OAuthRedirectUri,
                ["client_id"] = ServerFixture.OAuthClientId,
                ["client_secret"] = ServerFixture.OAuthClientSecret,
            };
        }

        private static async Task<JsonDocument> ExchangeCode(ServerFixture server, string code, string verifier)
        {
            return await PostToken(server, CodeExchangeForm(code, verifier), HttpStatusCode.OK);
        }

        private static async Task<JsonDocument> PostToken(ServerFixture server, Dictionary<string, string> form, HttpStatusCode expected)
        {
            using HttpClient plain = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };
            using HttpResponseMessage response = await plain.PostAsync("/token", new FormUrlEncodedContent(form), Token);
            Assert.Equal(expected, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        }

        private static async Task<McpClient> ConnectWithToken(ServerFixture server, string accessToken)
        {
            HttpClientTransport transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri($"{server.BaseUrl}/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {accessToken}",
                },
            });
            return await McpClient.CreateAsync(transport, cancellationToken: Token);
        }
    }
}
