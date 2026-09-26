namespace Playback.Api.Services;

internal static class ProviderResponseReader
{
    public static async Task<byte[]> ReadBounded(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("Provider response exceeds limit");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) return output.ToArray();
            if (output.Length + read > limit) throw new InvalidOperationException("Provider response exceeds limit");
            output.Write(buffer, 0, read);
        }
    }
}
