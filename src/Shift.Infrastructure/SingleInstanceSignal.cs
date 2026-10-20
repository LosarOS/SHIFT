using System.IO.Pipes;
using System.Text;
namespace Shift.Infrastructure;

/// <summary>Current-user-only, bounded activation signal. No command line or arbitrary launch requests.</summary>
public sealed class SingleInstanceSignal : IAsyncDisposable
{
    private readonly CancellationTokenSource shutdown = new();
    private readonly Task listening;
    public SingleInstanceSignal(string name, Action activated, Shift.Application.IAppLog log)
    {
        listening = Task.Run(async () =>
        {
            while (!shutdown.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false);
                    using var request = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
                    request.CancelAfter(TimeSpan.FromSeconds(2));
                    var payload = new byte[8];
                    var count = 0;
                    while (count < payload.Length)
                    {
                        var read = await server.ReadAsync(payload.AsMemory(count), request.Token).ConfigureAwait(false);
                        if (read == 0)
                            break;
                        count += read;
                    }
                    if (Encoding.ASCII.GetString(payload, 0, count) == "ACTIVATE")
                        activated();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    log.Error("Instance activation signal failed", ex);
                    try
                    {
                        await Task.Delay(250, shutdown.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                }
            }
        });
    }
    public static async Task<bool> SendAsync(string name, CancellationToken token = default)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000, token).ConfigureAwait(false);
            await client.WriteAsync(Encoding.ASCII.GetBytes("ACTIVATE"), token).ConfigureAwait(false);
            await client.FlushAsync(token).ConfigureAwait(false);
            return true;
        }
        catch (IOException) { return false; }
        catch (TimeoutException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public async ValueTask DisposeAsync()
    {
        await shutdown.CancelAsync().ConfigureAwait(false);
        await listening.ConfigureAwait(false);
        shutdown.Dispose();
    }
}
