namespace Lorestead.Core.Entities
{
    public sealed class OAuthCode
    {
        public string CodeChallenge { get; set; }
        public string RedirectUri { get; set; }
        public long ExpiresAt { get; set; }
    }
}
