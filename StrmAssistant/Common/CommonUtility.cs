using System;

namespace StrmAssistant
{
    public static class CommonUtility
    {
        public static bool IsValidHttpUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            {
                return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }

        public static bool IsValidMovieDbApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length != 32) return false;

            foreach (var c in apiKey)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
