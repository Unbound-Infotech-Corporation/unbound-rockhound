namespace GeoMineralTrace_App.Helpers;

/// <summary>Assembles chunked base64 recording payloads from WebView2.</summary>
public sealed class RecordingChunkAssembler
{
    private byte[]? _buffer;
    private int _receivedChunks;
    private int _expectedChunks;
    private string? _mimeType;

    public bool IsComplete => _buffer is not null && _receivedChunks >= _expectedChunks && _expectedChunks > 0;

    public string? MimeType => _mimeType;

    public void Begin(string mimeType, int totalBytes, int totalChunks)
    {
        _mimeType = mimeType;
        _expectedChunks = Math.Max(1, totalChunks);
        _receivedChunks = 0;
        _buffer = totalBytes > 0 ? new byte[totalBytes] : [];
    }

    public void AddChunk(int index, string base64Data)
    {
        if (_buffer is null)
            throw new InvalidOperationException("Recording assembly not started.");

        var chunk = Convert.FromBase64String(base64Data);
        var offset = index * (384 * 1024);
        if (offset + chunk.Length > _buffer.Length)
        {
            var needed = offset + chunk.Length;
            Array.Resize(ref _buffer, needed);
        }

        Buffer.BlockCopy(chunk, 0, _buffer, offset, chunk.Length);
        _receivedChunks = Math.Max(_receivedChunks, index + 1);
    }

    public byte[] ToArray()
    {
        if (_buffer is null)
            throw new InvalidOperationException("No recording data.");
        return _buffer;
    }

    public void Reset()
    {
        _buffer = null;
        _receivedChunks = 0;
        _expectedChunks = 0;
        _mimeType = null;
    }
}
