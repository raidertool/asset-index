using SteamKit2;
using SteamKit2.Authentication;

if (!Console.IsOutputRedirected)
{
    Console.Error.WriteLine("Steam authentication requires a redirected token destination.");
    return 2;
}

var username = Environment.GetEnvironmentVariable("STEAM_USERNAME");
var password = Environment.GetEnvironmentVariable("STEAM_PASSWORD");
Environment.SetEnvironmentVariable("STEAM_USERNAME", null);
Environment.SetEnvironmentVariable("STEAM_PASSWORD", null);
if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
{
    Console.Error.WriteLine("Steam username and password are required.");
    return 2;
}

try
{
    await Console.Out.WriteAsync(await CreateToken(username, password));
    return 0;
}
catch (Exception)
{
    // SDK exception messages can contain account or server response details.
    Console.Error.WriteLine("Steam authentication failed. Verify the account and unattended sign-in locally.");
    return 1;
}

static async Task<string> CreateToken(string username, string password)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var client = new SteamClient();
    var callbacks = new CallbackManager(client);
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var connectedSubscription = callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => connected.TrySetResult());
    using var disconnectedSubscription = callbacks.Subscribe<SteamClient.DisconnectedCallback>(_ =>
        connected.TrySetException(new InvalidOperationException("Steam connection closed.")));
    var callbackLoop = Task.Run(() =>
    {
        while (!timeout.IsCancellationRequested)
            callbacks.RunWaitCallbacks(TimeSpan.FromMilliseconds(250));
    });
    try
    {
        client.Connect();
        await connected.Task.WaitAsync(timeout.Token);
        var session = await client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
        {
            Username = username,
            Password = password,
            IsPersistentSession = false,
            DeviceFriendlyName = "Asset Index",
            Authenticator = new UnattendedAuthenticator(),
        }).WaitAsync(timeout.Token);
        var result = await session.PollingWaitForResultAsync(timeout.Token);
        if (string.IsNullOrEmpty(result.RefreshToken))
            throw new InvalidOperationException("Steam returned no session token.");
        return result.RefreshToken;
    }
    finally
    {
        timeout.Cancel();
        // Do not log on with this token before handing it to the depot client.
        client.Disconnect();
        await callbackLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}

sealed class UnattendedAuthenticator : IAuthenticator
{
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
        Task.FromException<string>(new InvalidOperationException("Interactive authentication required."));

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
        Task.FromException<string>(new InvalidOperationException("Interactive authentication required."));

    public Task<bool> AcceptDeviceConfirmationAsync() =>
        Task.FromException<bool>(new InvalidOperationException("Interactive authentication required."));
}
