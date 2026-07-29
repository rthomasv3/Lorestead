namespace Lorestead.Core.Sync
{
    // GaldrJson with the camelCase policy is the payload wire format (conventions.md);
    // this wrapper exists because the GaldrJson class shares its namespace name and the
    // options must be applied at every call site.
    public static class PayloadJson
    {
        private static readonly global::GaldrJson.GaldrJsonOptions Options = new global::GaldrJson.GaldrJsonOptions
        {
            PropertyNamingPolicy = global::GaldrJson.PropertyNamingPolicy.CamelCase,
        };

        public static string Serialize<T>(T value)
        {
            return global::GaldrJson.GaldrJson.Serialize<T>(value, Options);
        }

        public static T Deserialize<T>(string json)
        {
            return global::GaldrJson.GaldrJson.Deserialize<T>(json, Options);
        }
    }
}
