using Microsoft.AspNetCore.Components.Server.Circuits;

namespace PayCalcDXPro.Web;

/// <summary>
/// スタンドアロン（ローカルPC）起動時に、ブラウザ／タブを閉じたら自動でサーバを終了させる仕組み。
/// Blazor Server の接続（SignalR 回線）数を数え、0 になって一定時間（猶予）再接続が無ければアプリを停止する。
/// 猶予を置くのは、ページ再読み込み（F5）や一時的な通信断では終了させないため。
/// ※オンプレ（複数ユーザー）配置では登録しない（Program.cs で standalone のときだけ登録）。
/// </summary>
public sealed class StandaloneLifetime
{
    private readonly IHostApplicationLifetime _life;
    private readonly ILogger<StandaloneLifetime> _log;
    private readonly object _gate = new();
    private readonly TimeSpan _grace = TimeSpan.FromSeconds(8);

    private int _active;          // 現在つながっている回線数
    private bool _everConnected;  // 一度でもブラウザが接続したか（起動直後の誤終了防止）
    private Timer? _timer;

    public StandaloneLifetime(IHostApplicationLifetime life, ILogger<StandaloneLifetime> log)
    {
        _life = life;
        _log = log;
    }

    /// <summary>ブラウザ回線が接続した。保留中の終了があれば取り消す。</summary>
    public void ConnectionUp()
    {
        lock (_gate)
        {
            _active++;
            _everConnected = true;
            CancelTimer();
        }
    }

    /// <summary>ブラウザ回線が切断した。0 になったら猶予後にアプリ終了を予約する。</summary>
    public void ConnectionDown()
    {
        lock (_gate)
        {
            if (_active > 0) _active--;
            if (_active == 0 && _everConnected) ArmTimer();
        }
    }

    private void ArmTimer()
    {
        CancelTimer();
        _timer = new Timer(_ =>
        {
            lock (_gate)
            {
                if (_active > 0) return; // 猶予中に再接続した＝終了しない。
            }
            _log.LogInformation("ブラウザが閉じられたため、給与計算DXPro（スタンドアロン）を終了します。");
            _life.StopApplication();
        }, null, _grace, Timeout.InfiniteTimeSpan);
    }

    private void CancelTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }
}

/// <summary>回線の接続/切断を <see cref="StandaloneLifetime"/> に橋渡しする CircuitHandler。</summary>
public sealed class ShutdownCircuitHandler : CircuitHandler
{
    private readonly StandaloneLifetime _life;

    public ShutdownCircuitHandler(StandaloneLifetime life) => _life = life;

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _life.ConnectionUp();
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _life.ConnectionDown();
        return Task.CompletedTask;
    }
}
