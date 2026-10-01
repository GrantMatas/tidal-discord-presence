using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TidalDiscordPresence;

internal sealed class DiscordRpcClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private string? _applicationId;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsConnected => _pipe is { IsConnected: true };
    public JsonElement? LastActivityResponse { get; private set; }

    public Task SetActivityAsync(string applicationId, TrackInfo track, string fallbackImage,
        ArtworkMatch? artwork, CancellationToken cancellationToken)
    {
        var image = artwork?.ImageUrl ?? fallbackImage;
        return SendAsync(applicationId, new
        {
            cmd = "SET_ACTIVITY",
            args = new
            {
                pid = Environment.ProcessId,
                activity = new
                {
                    type = 2,
                    status_display_type = 2,
                    details = Limit($"TIDAL • {track.Title} by {track.Artist}", 128),
                    state = Limit(track.IsPlaying
                        ? track.Album
                        : $"Paused • {track.Album}", 128),
                    timestamps = track.StartUnix.HasValue
                        ? new { start = track.StartUnix.Value, end = track.EndUnix }
                        : null,
                    assets = string.IsNullOrWhiteSpace(image) ? null : new
                    {
                        large_image = image
                    },
                    buttons = artwork is null ? null : new[]
                    {
                        new { label = artwork.Source switch { "Apple" => "View on iTunes", "Deezer" => "View on Deezer", _ => "View album" }, url = artwork.TrackUrl }
                    },
                    instance = false
                }
            },
            nonce = Guid.NewGuid().ToString("N")
        }, cancellationToken, includeNulls: !track.IsPlaying);
    }

    public Task ClearActivityAsync(string applicationId, CancellationToken cancellationToken) =>
        SendAsync(applicationId, new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity = (object?)null },
            nonce = Guid.NewGuid().ToString("N")
        }, cancellationToken, includeNulls: true);

    private async Task SendAsync(string applicationId, object payload, CancellationToken cancellationToken, bool includeNulls = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            await EnsureConnectedAsync(applicationId, timeout.Token);
            var data = JsonSerializer.SerializeToUtf8Bytes(payload, includeNulls ? null : JsonOptions);
            using var request = JsonDocument.Parse(data);
            var nonce = request.RootElement.GetProperty("nonce").GetString();
            await WriteFrameAsync(1, data, timeout.Token);
            while (true)
            {
                using var response = await ReadFrameAsync(timeout.Token);
                ThrowIfError(response);
                if (response.RootElement.TryGetProperty("nonce", out var replyNonce) &&
                    replyNonce.GetString() == nonce)
                {
                    LastActivityResponse = response.RootElement.TryGetProperty("data", out var responseData) ? responseData.Clone() : null;
                    return;
                }
            }
        }
        catch
        {
            DisposePipe();
            throw;
        }
    }

    private async Task EnsureConnectedAsync(string applicationId, CancellationToken cancellationToken)
    {
        if (IsConnected && _applicationId == applicationId) return;
        DisposePipe();
        for (var i = 0; i < 10; i++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(200, cancellationToken);
                _pipe = pipe;
                _applicationId = applicationId;
                await WriteFrameAsync(0, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, client_id = applicationId }), cancellationToken);
                while (true)
                {
                    using var response = await ReadFrameAsync(cancellationToken);
                    ThrowIfError(response);
                    if (response.RootElement.TryGetProperty("evt", out var evt) && evt.GetString() == "READY")
                        return;
                }
            }
            catch (TimeoutException)
            {
                pipe.Dispose();
                DisposePipe();
            }
            catch
            {
                pipe.Dispose();
                DisposePipe();
                throw;
            }
        }
        throw new IOException("Open the Discord desktop app.");
    }

    private async Task WriteFrameAsync(int opcode, byte[] data, CancellationToken cancellationToken)
    {
        var pipe = _pipe ?? throw new IOException("Discord disconnected.");
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), data.Length);
        await pipe.WriteAsync(header, cancellationToken);
        await pipe.WriteAsync(data, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    private async Task<JsonDocument> ReadFrameAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pipe = _pipe ?? throw new IOException("Discord disconnected.");
            var header = new byte[8];
            await pipe.ReadExactlyAsync(header, cancellationToken);
            var opcode = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
            if (length < 0 || length > 1024 * 1024) throw new IOException("Invalid Discord IPC frame.");
            var buffer = new byte[length];
            await pipe.ReadExactlyAsync(buffer, cancellationToken);
            if (opcode == 2) throw new IOException("Discord closed the connection. Check the Application ID.");
            if (opcode == 3)
            {
                await WriteFrameAsync(4, buffer, cancellationToken);
                continue;
            }
            if (opcode == 1) return JsonDocument.Parse(buffer);
        }
    }

    private static void ThrowIfError(JsonDocument response)
    {
        if (response.RootElement.TryGetProperty("evt", out var evt) && evt.GetString() == "ERROR")
        {
            var message = response.RootElement.TryGetProperty("data", out var data) &&
                          data.TryGetProperty("message", out var detail) ? detail.GetString() : "Activity was rejected.";
            throw new IOException($"Discord: {message}");
        }
    }

    private static string Limit(string value, int maxLength) =>
        value.Length > maxLength ? value[..(maxLength - 1)] + "…" : value;

    private void DisposePipe()
    {
        _pipe?.Dispose();
        _pipe = null;
        _applicationId = null;
    }

    public void Dispose() => DisposePipe();
}
