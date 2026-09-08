namespace Lorestead.Core
{
    // One extension-to-type table for the places that have nothing but a filename
    // to go on: a markdown import naming its assets, and a file attached by path
    // from a clipboard or a drop. A file the webview handed over carries its own
    // type and never comes through here.
    public static class MimeTypes
    {
        public const string Default = "application/octet-stream";

        public static string FromExtension(string path)
        {
            string result;
            switch (Extension(path).ToLowerInvariant())
            {
                case ".png": result = "image/png"; break;
                case ".jpg":
                case ".jpeg": result = "image/jpeg"; break;
                case ".gif": result = "image/gif"; break;
                case ".webp": result = "image/webp"; break;
                case ".svg": result = "image/svg+xml"; break;
                case ".bmp": result = "image/bmp"; break;
                case ".pdf": result = "application/pdf"; break;
                case ".txt": result = "text/plain"; break;
                case ".csv": result = "text/csv"; break;
                case ".json": result = "application/json"; break;
                case ".xml": result = "application/xml"; break;
                case ".html": result = "text/html"; break;
                case ".zip": result = "application/zip"; break;
                case ".mp3": result = "audio/mpeg"; break;
                case ".wav": result = "audio/wav"; break;
                case ".mp4": result = "video/mp4"; break;
                case ".webm": result = "video/webm"; break;
                default: result = Default; break;
            }
            return result;
        }

        private static string Extension(string filename)
        {
            int dot = filename.LastIndexOf('.');
            return dot > 0 ? filename.Substring(dot) : string.Empty;
        }
    }
}
