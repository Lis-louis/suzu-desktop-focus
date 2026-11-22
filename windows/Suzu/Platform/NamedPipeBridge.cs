using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Suzu.Core;

namespace Suzu.Platform;

/// <summary>
/// 鈴秘書との連携（Windows 版）。macOS 版の DistributedNotificationCenter の代わりに、
/// 同じユーザーのプロセスからだけ接続できる名前付きパイプを使う。JSON の形式は macOS 版と同じ。
///   鈴秘書 → 鈴: \\.\pipe\jp.suzu.secretary.event に 1 行 1 イベントの JSON を書き込む
///   鈴 → 鈴秘書: \\.\pipe\jp.suzu.mascot.event に接続できたら 1 行の JSON を書き込む
/// 動作確認: powershell -File tools\send_event.ps1 '{"type":"speak","text":"おつかれさま！"}'
/// </summary>
internal sealed class NamedPipeBridge
{
    public const string InboundPipe = "jp.suzu.secretary.event";
    public const string OutboundPipe = "jp.suzu.mascot.event";

    public bool IsConnected { get; private set; }
    /// <summary>UI スレッドで呼ばれる</summary>
    public Action<SecretaryEvent>? OnEvent { get; set; }

    private CancellationTokenSource? _cts;
    private SynchronizationContext? _ui;

    public void Start()
    {
        if (_cts != null) return;
        _ui = SynchronizationContext.Current;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = Task.Run(() => ListenLoop(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        IsConnected = false;
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(InboundPipe, PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(server, new UTF8Encoding(false));
                string? line;
                while ((line = await reader.ReadLineAsync(token).ConfigureAwait(false)) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (SecretaryJson.Decode(line.Trim().TrimStart('\uFEFF')) is { } e) Dispatch(e);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Suzu] pipe error: {ex.Message}");
                try { await Task.Delay(1000, token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private void Dispatch(SecretaryEvent e)
    {
        void Run()
        {
            IsConnected = true;
            OnEvent?.Invoke(e);
        }
        if (_ui != null) _ui.Post(_ => Run(), null); else Run();
    }

    public void Send(MascotEvent e)
    {
        var json = SecretaryJson.Encode(e);
        _ = Task.Run(() =>
        {
            try
            {
                using var client = new NamedPipeClientStream(".", OutboundPipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(100); // 鈴秘書が動いていなければすぐ諦める
                var bytes = new UTF8Encoding(false).GetBytes(json + "\n");
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
            }
            catch (Exception) { /* 未接続 */ }
        });
    }
}
