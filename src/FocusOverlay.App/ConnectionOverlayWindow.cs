using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FocusOverlay.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace FocusOverlay.App;

public sealed class ConnectionOverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoactivate = 0x08000000;
    private readonly ConnectionSurface surface = new();

    public ConnectionOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Content = surface;

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(handle, GwlExstyle);
            _ = SetWindowLong(
                handle,
                GwlExstyle,
                style | WsExTransparent | WsExToolwindow | WsExNoactivate);
        };
    }

    public void UpdateScene(
        IReadOnlyList<CardConnection> connections,
        IReadOnlyDictionary<Guid, Rect> cardBounds) =>
        surface.UpdateScene(connections, cardBounds, Left, Top);

    public void SetLiveConnection(Rect source, Point pointer, string color) =>
        surface.SetLive(source, pointer, color, Left, Top);

    public void ClearLiveConnection() => surface.ClearLive();

    public void Pulse(Guid connectionId) => surface.Pulse(connectionId);

    public void SetRelationLens(Guid? cardId) => surface.SetRelationLens(cardId);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int newStyle);
}

internal sealed class ConnectionSurface : FrameworkElement
{
    private static readonly DoubleCollection DashPattern = new([5, 5]);
    private IReadOnlyList<CardConnection> connections = [];
    private IReadOnlyDictionary<Guid, Rect> bounds = new Dictionary<Guid, Rect>();
    private double originX;
    private double originY;
    private (Rect Source, Point Pointer, string Color)? live;
    private Guid? pulseId;
    private Stopwatch? pulseWatch;
    private Guid? lensCardId;
    private Guid? transitionLensId;
    private Stopwatch? lensWatch;
    private bool lensEntering;
    private bool renderingSubscribed;

    public void UpdateScene(
        IReadOnlyList<CardConnection> nextConnections,
        IReadOnlyDictionary<Guid, Rect> nextBounds,
        double virtualLeft,
        double virtualTop)
    {
        connections = nextConnections.ToArray();
        bounds = new Dictionary<Guid, Rect>(nextBounds);
        originX = virtualLeft;
        originY = virtualTop;
        InvalidateVisual();
    }

    public void SetLive(Rect source, Point pointer, string color, double virtualLeft, double virtualTop)
    {
        originX = virtualLeft;
        originY = virtualTop;
        live = (source, pointer, color);
        InvalidateVisual();
    }

    public void ClearLive()
    {
        live = null;
        InvalidateVisual();
    }

    public void Pulse(Guid id)
    {
        pulseId = id;
        pulseWatch = Stopwatch.StartNew();
        if (!renderingSubscribed)
        {
            CompositionTarget.Rendering += Rendering;
            renderingSubscribed = true;
        }
    }

    public void SetRelationLens(Guid? cardId)
    {
        if (cardId == lensCardId && transitionLensId is null)
        {
            return;
        }

        transitionLensId = cardId ?? lensCardId;
        lensCardId = cardId;
        lensEntering = cardId is not null;
        lensWatch = Stopwatch.StartNew();
        SubscribeRendering();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        foreach (var relation in connections)
        {
            if (!bounds.TryGetValue(relation.SourceCardId, out var source) ||
                !bounds.TryGetValue(relation.TargetCardId, out var target))
            {
                continue;
            }

            DrawConnection(drawingContext, relation, source, target);
        }

        if (live is { } drag)
        {
            var start = AnchorToward(drag.Source, drag.Pointer);
            var localStart = ToLocal(start);
            var localEnd = ToLocal(drag.Pointer);
            var geometry = CreateCurve(localStart, localEnd);
            var brush = BrushFrom(drag.Color);
            drawingContext.DrawGeometry(
                null,
                new Pen(BrushFrom("#FFFFFF", 0.76), 6)
                {
                    DashStyle = new DashStyle(DashPattern, 0),
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                },
                geometry);
            drawingContext.DrawGeometry(
                null,
                new Pen(brush, 1.8)
                {
                    DashStyle = new DashStyle(DashPattern, 0),
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                },
                geometry);
            drawingContext.DrawEllipse(BrushFrom("#FFFFFF", 0.9), new Pen(brush, 1.8), localEnd, 4.5, 4.5);
        }
    }

    private void DrawConnection(
        DrawingContext dc,
        CardConnection relation,
        Rect sourceBounds,
        Rect targetBounds)
    {
        var sourceCenter = Center(sourceBounds);
        var targetCenter = Center(targetBounds);
        var start = ToLocal(AnchorToward(sourceBounds, targetCenter));
        var end = ToLocal(AnchorToward(targetBounds, sourceCenter));
        var geometry = CreateCurve(start, end);
        var lensAmount = LensAmount();
        var effectiveLens = transitionLensId ?? lensCardId;
        var inLens = effectiveLens is Guid selected && RelationLens.Includes(relation, selected);
        var opacity = RelationLens.ConnectionOpacity(relation, effectiveLens, lensAmount);
        var brush = BrushFrom(relation.Color, opacity);
        var pulse = relation.Id == pulseId && pulseWatch is not null
            ? Math.Max(0, 1 - pulseWatch.Elapsed.TotalMilliseconds / 480)
            : 0;
        var bounce = pulse > 0 ? Math.Sin((1 - pulse) * Math.PI * 3) * pulse : 0;
        var thickness = 2.2 + Math.Abs(bounce) * 2.4;

        var halo = new Pen(BrushFrom("#FFFFFF", 0.78 * opacity), thickness + 4.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var stroke = new Pen(brush, Math.Max(1.65, thickness - 0.45))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        dc.DrawGeometry(null, halo, geometry);
        dc.DrawGeometry(null, stroke, geometry);
        if (relation.Direction is ConnectionDirection.Forward or ConnectionDirection.Both)
        {
            var tangent = CurveEndTangent(start, end);
            var tip = Inset(end, tangent, 7);
            DrawArrow(dc, brush, tip, tangent, 7 + pulse * 2);
        }

        if (relation.Direction is ConnectionDirection.Backward or ConnectionDirection.Both)
        {
            var tangent = -CurveStartTangent(start, end);
            var tip = Inset(start, tangent, 7);
            DrawArrow(dc, brush, tip, tangent, 7 + pulse * 2);
        }

        if (inLens && lensAmount > 0.01)
        {
            DrawPort(dc, start, relation.Color, lensAmount);
            DrawPort(dc, end, relation.Color, lensAmount);
            if (!string.IsNullOrWhiteSpace(relation.Label))
            {
                DrawLabel(dc, relation.Label.Trim(), CurveMidpoint(start, end), relation.Color, lensAmount);
            }
        }

        if (pulse > 0)
        {
            var progress = 1 - pulse;
            var dot = PointOnCurve(start, end, EaseOutBack(Math.Min(1, progress)));
            dc.DrawEllipse(brush, null, dot, 4.5 + pulse * 2, 4.5 + pulse * 2);
            dc.DrawEllipse(null, new Pen(brush, 1.3), end, 9 + progress * 10, 9 + progress * 10);
        }
    }

    private void Rendering(object? sender, EventArgs e)
    {
        if (pulseWatch is null || pulseWatch.ElapsedMilliseconds >= 500)
        {
            pulseId = null;
            pulseWatch = null;
        }

        if (lensWatch is not null && lensWatch.ElapsedMilliseconds >= 190)
        {
            lensWatch = null;
            transitionLensId = null;
        }

        if (pulseWatch is null && lensWatch is null)
        {
            CompositionTarget.Rendering -= Rendering;
            renderingSubscribed = false;
        }

        InvalidateVisual();
    }

    private static void DrawLabel(DrawingContext dc, string text, Point center, string color, double opacity)
    {
        var accent = BrushFrom(color, opacity);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            accent,
            1);
        formatted.MaxTextWidth = 180;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        var rect = new Rect(
            center.X - formatted.Width / 2 - 8,
            center.Y - formatted.Height / 2 - 5,
            formatted.Width + 16,
            formatted.Height + 10);
        dc.DrawRoundedRectangle(
            BrushFrom("#FAF9F6", 0.96 * opacity),
            new Pen(accent, 1),
            rect,
            8,
            8);
        dc.DrawText(formatted, new Point(rect.Left + 8, rect.Top + 5));
    }

    private static void DrawPort(DrawingContext dc, Point center, string color, double opacity)
    {
        dc.DrawEllipse(
            BrushFrom("#FAF9F6", opacity),
            new Pen(BrushFrom(color, opacity), 2),
            center,
            5,
            5);
    }

    private static Point Inset(Point point, Vector tangent, double distance)
    {
        if (tangent.Length < 0.01)
        {
            return point;
        }

        tangent.Normalize();
        return point - tangent * distance;
    }

    private static void DrawArrow(DrawingContext dc, Brush brush, Point tip, Vector tangent, double size)
    {
        if (tangent.Length < 0.01)
        {
            return;
        }

        tangent.Normalize();
        var normal = new Vector(-tangent.Y, tangent.X);
        var back = tip - tangent * size;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(tip, true, true);
            context.LineTo(back + normal * size * 0.52, true, false);
            context.LineTo(back - normal * size * 0.52, true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static PathGeometry CreateCurve(Point start, Point end)
    {
        var (control1, control2) = Controls(start, end);
        return new PathGeometry([
            new PathFigure(start, [new BezierSegment(control1, control2, end, true)], false)
        ]);
    }

    private static (Point First, Point Second) Controls(Point start, Point end)
    {
        var distance = Math.Max(54, Math.Min(220, Math.Abs(end.X - start.X) * 0.55 + 40));
        var direction = end.X >= start.X ? 1 : -1;
        return (
            new Point(start.X + distance * direction, start.Y),
            new Point(end.X - distance * direction, end.Y));
    }

    private static Vector CurveStartTangent(Point start, Point end) => Controls(start, end).First - start;

    private static Vector CurveEndTangent(Point start, Point end) => end - Controls(start, end).Second;

    private static Point CurveMidpoint(Point start, Point end) => PointOnCurve(start, end, 0.5);

    private static Point PointOnCurve(Point start, Point end, double t)
    {
        var (c1, c2) = Controls(start, end);
        var u = 1 - t;
        return new Point(
            u * u * u * start.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * end.X,
            u * u * u * start.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * end.Y);
    }

    private static Point AnchorToward(Rect rect, Point target)
    {
        var center = Center(rect);
        var dx = target.X - center.X;
        var dy = target.Y - center.Y;
        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01)
        {
            return center;
        }

        var scaleX = Math.Abs(dx) < 0.01 ? double.PositiveInfinity : rect.Width / 2 / Math.Abs(dx);
        var scaleY = Math.Abs(dy) < 0.01 ? double.PositiveInfinity : rect.Height / 2 / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);
        return new Point(center.X + dx * scale, center.Y + dy * scale);
    }

    private static Point Center(Rect rect) => new(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);

    private Point ToLocal(Point point) => new(point.X - originX, point.Y - originY);

    private static Brush BrushFrom(string value, double opacity = 1)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Opacity = Math.Clamp(opacity, 0, 1);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(105, 123, 232));
        }
    }

    private double LensAmount()
    {
        if (lensWatch is null)
        {
            return lensCardId is null ? 0 : 1;
        }

        var progress = Math.Clamp(lensWatch.Elapsed.TotalMilliseconds / 180, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        return lensEntering ? eased : 1 - eased;
    }

    private void SubscribeRendering()
    {
        if (renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering += Rendering;
        renderingSubscribed = true;
    }

    private static double EaseOutBack(double x)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }
}
