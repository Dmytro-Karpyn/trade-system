namespace Trade.Api.Operations;

public static class ContainerHealth
{
    public static async Task<int> CheckAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        try
        {
            using var response = await client.GetAsync("http://127.0.0.1:8080/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException) { return 1; }
        catch (TaskCanceledException) { return 1; }
    }
}
