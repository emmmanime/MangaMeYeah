using System.Windows;

namespace MangaMeYeah.Services;

public enum GestureAction
{
    None = 0,
    FullScreenToggle,
    AlignHeightTwoPage,
    FitEither,
    ThumbnailCustom,
    JumpForwardFixed,
    JumpBackwardFixed,
    SpecifiedScale,
    QuickSave1,
    QuickSave2,
    QuickSave3,
    QuickSave4
}

public sealed class MouseGestureEngine
{
    private const double UnitLength = 30.0;
    private const double SplitPauseMs = 190.0;

    private readonly List<Point> _trailPoints = [];
    private readonly List<string> _directions = [];
    private Point _anchorPoint;
    private DateTime _lastMoveTimeUtc;
    private bool _pauseRecordedForRepeat;

    public IReadOnlyList<Point> TrailPoints => _trailPoints;
    public string CurrentGesturePattern => string.Concat(_directions);
    public bool HasMovedBeyondThreshold => _directions.Count > 0;

    public void Start(Point startPoint)
    {
        _trailPoints.Clear();
        _directions.Clear();
        _trailPoints.Add(startPoint);
        _anchorPoint = startPoint;
        _lastMoveTimeUtc = DateTime.UtcNow;
        _pauseRecordedForRepeat = false;
    }

    public bool AddPoint(Point pt)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastMoveTimeUtc).TotalMilliseconds >= SplitPauseMs)
        {
            _pauseRecordedForRepeat = true;
            _anchorPoint = _trailPoints.Count > 0 ? _trailPoints[^1] : pt;
        }
        _lastMoveTimeUtc = now;
        _trailPoints.Add(pt);

        double dx = pt.X - _anchorPoint.X;
        double dy = pt.Y - _anchorPoint.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < UnitLength)
        {
            return false;
        }

        string dir = Math.Abs(dx) >= Math.Abs(dy)
            ? (dx > 0 ? "→" : "←")
            : (dy > 0 ? "↓" : "↑");

        _anchorPoint = pt;

        if (_directions.Count == 0)
        {
            _directions.Add(dir);
            _pauseRecordedForRepeat = false;
            return true;
        }

        string lastDir = _directions[^1];
        if (lastDir != dir)
        {
            if (_directions.Count < 4)
            {
                _directions.Add(dir);
            }
            _pauseRecordedForRepeat = false;
            return true;
        }
        else if (_pauseRecordedForRepeat && _directions.Count == 1)
        {
            // Support →→ and ←← after a brief pause (MangaMeeya GestureSplitTime=200ms)
            _directions.Add(dir);
            _pauseRecordedForRepeat = false;
            return true;
        }

        return false;
    }

    public static (GestureAction Action, string Description) ResolveGesture(string pattern)
    {
        return pattern switch
        {
            "↑" => (GestureAction.FullScreenToggle, "フルスクリーン切り替え"),
            "↓" => (GestureAction.AlignHeightTwoPage, "縦サイズを揃える (2ページ表示用)"),
            "←" => (GestureAction.FitEither, "縦横どちらか合わせる"),
            "→" => (GestureAction.ThumbnailCustom, "サムネイル表示 (カスタム)"),
            "→→" => (GestureAction.JumpForwardFixed, "固定ページ進む"),
            "←←" => (GestureAction.JumpBackwardFixed, "固定ページ戻る"),
            "↑↓" or "↓↑" => (GestureAction.SpecifiedScale, "指定スケール表示 (170%)"),
            "↑←" or "←↑" => (GestureAction.QuickSave1, "クイックセーブ1"),
            "↑→" or "→↑" => (GestureAction.QuickSave2, "クイックセーブ2"),
            "↓←" or "←↓" => (GestureAction.QuickSave3, "クイックセーブ3"),
            "↓→" or "→↓" => (GestureAction.QuickSave4, "クイックセーブ4"),
            _ => (GestureAction.None, string.IsNullOrEmpty(pattern) ? "" : "未割り当て")
        };
    }
}
