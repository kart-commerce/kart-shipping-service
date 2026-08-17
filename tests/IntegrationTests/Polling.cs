namespace Kart.Shipping.IntegrationTests;

/// <summary>The SHIP-1→SHIP-2→SHIP-3→read-model pipeline is genuinely asynchronous - tests poll for the expected end state instead of sleeping a fixed, flaky duration.</summary>
public static class Polling
{
    public static async Task<T> UntilAsync<T>(Func<Task<T?>> probe, Func<T, bool> isDone, TimeSpan? timeout = null) where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null && isDone(result))
            {
                return result;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Condition was not met within the timeout.");
    }
}
