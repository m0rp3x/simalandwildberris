using Shared.Enums;
using WBSL.Data.HttpClientFactoryExt;

namespace WBSL.Data.Extensions;

public static class HttpClientFactoryExtensions
{
    /// <summary>
    /// Перебирает список externalAccountId, создаёт для каждого HttpClient и «пингует» эндпоинт /ping.
    /// Возвращает первый клиент, который ответил 200, или null, если все неудачны.
    /// </summary>
    public static async Task<HttpClient?> GetValidClientAsync(
        this PlatformHttpClientFactory factory,
        ExternalAccountType platform,
        IEnumerable<int> externalAccountIds,
        string pingPath,
        CancellationToken ct)
    {
        foreach (var extId in externalAccountIds)
        {
            var client = await factory.CreateClientAsync(platform, extId, true);
            try
            {
                using var resp = await client.GetAsync(pingPath, ct);
                if (resp.IsSuccessStatusCode)
                    return client;
            }
            catch
            {
                // пропускаем любые ошибки и пробуем следующий аккаунт
            }
        }
        return null;
    }
}
