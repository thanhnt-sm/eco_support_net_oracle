public static class HealthHostBinding
{
    public static bool IsLoopbackOnly(string urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return false;
        }

        var endpoints = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return endpoints.Length > 0 && endpoints.All(url =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)));
    }
}
