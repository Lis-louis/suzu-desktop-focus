# 鈴秘書の代わりにイベントを送る開発用ツール（Windows 版）。
# 例: powershell -File tools\send_event.ps1 '{"type":"speak","text":"おつかれさま！"}'
param([string]$Json = '{"type":"speak","text":"こんにちは、鈴です。"}')
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "jp.suzu.secretary.event", [System.IO.Pipes.PipeDirection]::Out)
try {
    $pipe.Connect(2000)
    $bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes($Json + "`n")
    $pipe.Write($bytes, 0, $bytes.Length)
    $pipe.Flush()
    Write-Host "sent: $Json"
} catch {
    Write-Host "鈴が起動していないか、接続できませんでした: $($_.Exception.Message)"
} finally {
    $pipe.Dispose()
}
