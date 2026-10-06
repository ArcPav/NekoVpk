using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NekoVpk.Core;

public static class ImageHttp
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly SemaphoreSlim Gate = new(6);

    public static async Task<byte[]> GetBytesAsync(string url)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Client.GetByteArrayAsync(url).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
