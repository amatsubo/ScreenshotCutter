using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenshotCutter.Interop;
using ScreenshotCutter.Models;
using ScreenshotCutter.Services;

namespace ScreenshotCutter.Views;

/// <summary>
/// 対象モニター全面を等倍で覆い、切り出し矩形を調整するオーバーレイ
/// （確定仕様書 4.6.3）。
/// </summary>
public partial class CropOverlayWindow : Window
{
    /// <summary>ハンドルの一辺（物理ピクセル）。</summary>
    private const int HandleSize = 10;

    /// <summary>ハンドルの当たり判定の半径。見た目より少し広めにする。</summary>
    private const int HandleHitRadius = 9;

    /// <summary>数値パネルを画面端から離す余白。</summary>
    private const double PanelMargin = 28;

    /// <summary>初期矩形が未設定のときに提示する、中央 80% の枠。</summary>
    private const double DefaultRectRatio = 0.8;

    /// <summary>
    /// 新規作成とみなすまでの最小ドラッグ距離（物理ピクセル）。
    /// これ未満の移動は「ただのクリック」として扱い、既存の矩形を保持する。
    /// </summary>
    private const double CreateDragThreshold = 3.0;

    private enum DragMode
    {
        None,
        Create,
        Move,
        ResizeNorth,
        ResizeSouth,
        ResizeWest,
        ResizeEast,
        ResizeNorthWest,
        ResizeNorthEast,
        ResizeSouthWest,
        ResizeSouthEast,
    }

    private readonly MonitorInfo _monitor;
    private readonly ScreenCaptureService _captureService;
    private readonly Dictionary<DragMode, Rectangle> _handles = [];

    private PixelRect _rect;
    private DragMode _dragMode = DragMode.None;
    private PixelRect _dragOriginRect;
    private Point _dragOrigin;

    // 数値入力とドラッグ操作が相互に書き戻して無限ループするのを防ぐ。
    private bool _suppressTextSync;

    /// <summary>確定された切り出し矩形（モニター相対の物理ピクセル）。</summary>
    public PixelRect SelectedRect => _rect;

    public CropOverlayWindow(MonitorInfo monitor, ScreenCaptureService captureService, PixelRect? initialRect)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(captureService);

        _monitor = monitor;
        _captureService = captureService;

        InitializeComponent();

        _rect = ResolveInitialRect(initialRect, monitor);

        // Canvas をモニターと同じ物理ピクセル数の座標系にする。
        RootCanvas.Width = monitor.Width;
        RootCanvas.Height = monitor.Height;

        // DPI 倍率の逆数を掛けることで、Canvas 上の 1 単位が
        // 画面上の 1 物理ピクセルと一致する。
        var inverseScale = 1.0 / monitor.ScaleFactor;
        RootScale.ScaleX = inverseScale;
        RootScale.ScaleY = inverseScale;

        CreateHandles();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;

        RootCanvas.MouseLeftButtonDown += OnCanvasMouseLeftButtonDown;
        RootCanvas.MouseMove += OnCanvasMouseMove;
        RootCanvas.MouseLeftButtonUp += OnCanvasMouseLeftButtonUp;
        RootCanvas.LostMouseCapture += OnCanvasLostMouseCapture;

        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnKeyDown;

        ConfirmButton.Click += (_, _) => Confirm();
        CancelButton.Click += (_, _) => Cancel();
        RecaptureButton.Click += (_, _) => RecaptureBackground();

        foreach (var box in new[] { XBox, YBox, WidthBox, HeightBox })
        {
            box.LostFocus += OnNumericBoxLostFocus;
            box.KeyDown += OnNumericBoxKeyDown;
            box.PreviewTextInput += OnNumericBoxPreviewTextInput;
        }
    }

    private static PixelRect ResolveInitialRect(PixelRect? initialRect, MonitorInfo monitor)
    {
        if (initialRect is { } candidate && candidate.IsValid)
        {
            // 前回設定時から解像度が変わっている可能性があるため収める。
            var clamped = CropCalculator.Clamp(candidate, monitor.Width, monitor.Height);
            if (clamped.IsValid)
            {
                return clamped;
            }
        }

        // 未設定のときは中央 80%。画面いっぱいだとハンドルが端に来て掴みにくい。
        var width = Math.Max(1, (int)(monitor.Width * DefaultRectRatio));
        var height = Math.Max(1, (int)(monitor.Height * DefaultRectRatio));

        return new PixelRect(
            (monitor.Width - width) / 2,
            (monitor.Height - height) / 2,
            width,
            height);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // WPF の Left/Top は論理単位で、マルチ DPI 環境では意図した位置に
        // ならないことがある。物理ピクセルで直接配置する。
        var handle = new WindowInteropHelper(this).Handle;

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            _monitor.VirtualBounds.X,
            _monitor.VirtualBounds.Y,
            _monitor.Width,
            _monitor.Height,
            NativeMethods.SWP_SHOWWINDOW);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        UpdateVisuals();
    }

    /// <summary>撮影済みの背景画像を差し込む。</summary>
    public void SetBackground(System.Windows.Media.Imaging.BitmapSource background)
    {
        ArgumentNullException.ThrowIfNull(background);

        BackgroundImage.Source = background;
        BackgroundImage.Width = _monitor.Width;
        BackgroundImage.Height = _monitor.Height;
    }

    private void RecaptureBackground()
    {
        try
        {
            // 自分自身が写り込まないよう、いったん隠してから取り直す。
            Visibility = Visibility.Hidden;
            UpdateLayout();

            // 非表示が画面に反映されるまで少し待つ。
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            Thread.Sleep(120);

            SetBackground(_captureService.Capture(_monitor));
        }
        catch (Exception ex)
        {
            Logger.Error("背景の再取得に失敗しました。", ex);
        }
        finally
        {
            Visibility = Visibility.Visible;
            Activate();
            Focus();
        }
    }

    // ------------------------------------------------------------- handles

    private void CreateHandles()
    {
        foreach (var mode in new[]
                 {
                     DragMode.ResizeNorthWest, DragMode.ResizeNorth, DragMode.ResizeNorthEast,
                     DragMode.ResizeWest, DragMode.ResizeEast,
                     DragMode.ResizeSouthWest, DragMode.ResizeSouth, DragMode.ResizeSouthEast,
                 })
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x2D, 0x2D)),
                StrokeThickness = 1.5,
                IsHitTestVisible = false,
            };

            _handles[mode] = handle;
            HandleLayer.Children.Add(handle);
        }
    }

    private Point HandleCenter(DragMode mode)
    {
        var centerX = _rect.X + (_rect.Width / 2.0);
        var centerY = _rect.Y + (_rect.Height / 2.0);

        return mode switch
        {
            DragMode.ResizeNorthWest => new Point(_rect.X, _rect.Y),
            DragMode.ResizeNorth => new Point(centerX, _rect.Y),
            DragMode.ResizeNorthEast => new Point(_rect.Right, _rect.Y),
            DragMode.ResizeWest => new Point(_rect.X, centerY),
            DragMode.ResizeEast => new Point(_rect.Right, centerY),
            DragMode.ResizeSouthWest => new Point(_rect.X, _rect.Bottom),
            DragMode.ResizeSouth => new Point(centerX, _rect.Bottom),
            DragMode.ResizeSouthEast => new Point(_rect.Right, _rect.Bottom),
            _ => new Point(centerX, centerY),
        };
    }

    // ------------------------------------------------------------ visuals

    private void UpdateVisuals()
    {
        Canvas.SetLeft(CropBorder, _rect.X);
        Canvas.SetTop(CropBorder, _rect.Y);
        CropBorder.Width = _rect.Width;
        CropBorder.Height = _rect.Height;

        UpdateDimOverlay();
        UpdateGuideLines();

        foreach (var (mode, handle) in _handles)
        {
            var center = HandleCenter(mode);
            Canvas.SetLeft(handle, center.X - (HandleSize / 2.0));
            Canvas.SetTop(handle, center.Y - (HandleSize / 2.0));
        }

        SyncNumericBoxes();
        UpdatePanelPlacement();
    }

    /// <summary>切り出し範囲の外側だけを暗くする。</summary>
    private void UpdateDimOverlay()
    {
        var outer = new RectangleGeometry(new Rect(0, 0, _monitor.Width, _monitor.Height));
        var inner = new RectangleGeometry(new Rect(_rect.X, _rect.Y, _rect.Width, _rect.Height));

        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(outer);
        group.Children.Add(inner);
        group.Freeze();

        DimPath.Data = group;
    }

    /// <summary>枠の各辺から画面端まで伸びるガイド線を引く。</summary>
    private void UpdateGuideLines()
    {
        SetLine(GuideTop, 0, _rect.Y, _monitor.Width, _rect.Y);
        SetLine(GuideBottom, 0, _rect.Bottom, _monitor.Width, _rect.Bottom);
        SetLine(GuideLeft, _rect.X, 0, _rect.X, _monitor.Height);
        SetLine(GuideRight, _rect.Right, 0, _rect.Right, _monitor.Height);

        static void SetLine(Line line, double x1, double y1, double x2, double y2)
        {
            line.X1 = x1;
            line.Y1 = y1;
            line.X2 = x2;
            line.Y2 = y2;
        }
    }

    /// <summary>
    /// 数値パネルが切り出し枠に重ならない位置を選ぶ（確定仕様書 4.6.3.6）。
    /// どの角でも重なる場合は、重なりが最も小さい角に置く。
    /// </summary>
    private void UpdatePanelPlacement()
    {
        NumericPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = NumericPanel.DesiredSize;

        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var candidates = new[]
        {
            new Point(PanelMargin, PanelMargin),
            new Point(_monitor.Width - size.Width - PanelMargin, PanelMargin),
            new Point(PanelMargin, _monitor.Height - size.Height - PanelMargin),
            new Point(_monitor.Width - size.Width - PanelMargin, _monitor.Height - size.Height - PanelMargin),
        };

        var cropRect = new Rect(_rect.X, _rect.Y, _rect.Width, _rect.Height);

        var best = candidates[0];
        var bestOverlap = double.MaxValue;

        foreach (var candidate in candidates)
        {
            var panelRect = new Rect(candidate.X, candidate.Y, size.Width, size.Height);
            var intersection = Rect.Intersect(panelRect, cropRect);
            var overlap = intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;

            if (overlap < bestOverlap)
            {
                bestOverlap = overlap;
                best = candidate;

                if (overlap == 0)
                {
                    break;
                }
            }
        }

        Canvas.SetLeft(NumericPanel, Math.Max(0, best.X));
        Canvas.SetTop(NumericPanel, Math.Max(0, best.Y));
    }

    // --------------------------------------------------------------- mouse

    private DragMode HitTest(Point point)
    {
        foreach (var (mode, _) in _handles)
        {
            var center = HandleCenter(mode);
            if (Math.Abs(point.X - center.X) <= HandleHitRadius
                && Math.Abs(point.Y - center.Y) <= HandleHitRadius)
            {
                return mode;
            }
        }

        var inside = point.X >= _rect.X && point.X <= _rect.Right
                     && point.Y >= _rect.Y && point.Y <= _rect.Bottom;

        return inside ? DragMode.Move : DragMode.Create;
    }

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // パネル上のクリックは操作対象外。
        if (IsWithinPanel(e.OriginalSource))
        {
            return;
        }

        var point = e.GetPosition(RootCanvas);

        _dragMode = HitTest(point);
        _dragOrigin = point;
        _dragOriginRect = _rect;

        // 新規作成モードでも、この時点では矩形を書き換えない。
        // 枠外を誤ってクリックしただけで既存の矩形が 1x1 に潰れてしまうため、
        // 実際にドラッグが始まってから作り直す。
        RootCanvas.CaptureMouse();
        Focus();
        e.Handled = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(RootCanvas);

        if (_dragMode == DragMode.None)
        {
            Cursor = CursorFor(HitTest(point));
            return;
        }

        var dx = (int)Math.Round(point.X - _dragOrigin.X);
        var dy = (int)Math.Round(point.Y - _dragOrigin.Y);

        if (_dragMode == DragMode.Create)
        {
            // しきい値を超えるまでは、既存の矩形を保ったまま何もしない。
            if (Math.Abs(point.X - _dragOrigin.X) < CreateDragThreshold
                && Math.Abs(point.Y - _dragOrigin.Y) < CreateDragThreshold)
            {
                return;
            }

            _rect = CropCalculator.Fit(
                PixelRect.FromPoints(
                    (int)Math.Round(_dragOrigin.X),
                    (int)Math.Round(_dragOrigin.Y),
                    (int)Math.Round(point.X),
                    (int)Math.Round(point.Y)),
                _monitor.Width,
                _monitor.Height);
        }
        else
        {
            _rect = _dragMode == DragMode.Move
                ? CropCalculator.Offset(_dragOriginRect, dx, dy, _monitor.Width, _monitor.Height)
                : ResizeRect(_dragOriginRect, _dragMode, dx, dy);
        }

        UpdateVisuals();
        e.Handled = true;
    }

    private PixelRect ResizeRect(PixelRect origin, DragMode mode, int dx, int dy)
    {
        var left = origin.X;
        var top = origin.Y;
        var right = origin.Right;
        var bottom = origin.Bottom;

        if (mode is DragMode.ResizeNorth or DragMode.ResizeNorthWest or DragMode.ResizeNorthEast)
        {
            top += dy;
        }

        if (mode is DragMode.ResizeSouth or DragMode.ResizeSouthWest or DragMode.ResizeSouthEast)
        {
            bottom += dy;
        }

        if (mode is DragMode.ResizeWest or DragMode.ResizeNorthWest or DragMode.ResizeSouthWest)
        {
            left += dx;
        }

        if (mode is DragMode.ResizeEast or DragMode.ResizeNorthEast or DragMode.ResizeSouthEast)
        {
            right += dx;
        }

        // 反対側の辺を追い越しても破綻しないよう、点として正規化する。
        var normalized = PixelRect.FromPoints(left, top, right, bottom);
        return CropCalculator.Fit(normalized, _monitor.Width, _monitor.Height);
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndDrag();
        e.Handled = true;
    }

    private void OnCanvasLostMouseCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        _dragMode = DragMode.None;

        if (RootCanvas.IsMouseCaptured)
        {
            RootCanvas.ReleaseMouseCapture();
        }

        UpdateVisuals();
    }

    private bool IsWithinPanel(object? source)
    {
        if (source is not DependencyObject element)
        {
            return false;
        }

        while (element is not null)
        {
            if (ReferenceEquals(element, NumericPanel))
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private static Cursor CursorFor(DragMode mode) => mode switch
    {
        DragMode.Move => Cursors.SizeAll,
        DragMode.ResizeNorth or DragMode.ResizeSouth => Cursors.SizeNS,
        DragMode.ResizeWest or DragMode.ResizeEast => Cursors.SizeWE,
        DragMode.ResizeNorthWest or DragMode.ResizeSouthEast => Cursors.SizeNWSE,
        DragMode.ResizeNorthEast or DragMode.ResizeSouthWest => Cursors.SizeNESW,
        _ => Cursors.Cross,
    };

    // ------------------------------------------------------------ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc は入力欄にフォーカスがあっても取り消しとして働かせる。
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
            return;
        }

        // 入力欄で Enter を押したときは値の確定のみ。ウィンドウは閉じない。
        if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox box)
        {
            ApplyNumericBox(box);
            e.Handled = true;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // 入力欄がキャレット移動に使うため、ここには矢印キーは届かない。
        var step = 1;

        switch (e.Key)
        {
            case Key.Enter:
                Confirm();
                e.Handled = true;
                return;

            case Key.Left:
                Nudge(-step, 0, e.KeyboardDevice.Modifiers);
                break;

            case Key.Right:
                Nudge(step, 0, e.KeyboardDevice.Modifiers);
                break;

            case Key.Up:
                Nudge(0, -step, e.KeyboardDevice.Modifiers);
                break;

            case Key.Down:
                Nudge(0, step, e.KeyboardDevice.Modifiers);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>矢印キーで 1px 移動、Shift + 矢印で 1px サイズ変更。</summary>
    private void Nudge(int dx, int dy, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            var resized = _rect with
            {
                Width = _rect.Width + dx,
                Height = _rect.Height + dy,
            };

            _rect = CropCalculator.Fit(resized, _monitor.Width, _monitor.Height);
        }
        else
        {
            _rect = CropCalculator.Offset(_rect, dx, dy, _monitor.Width, _monitor.Height);
        }

        UpdateVisuals();
    }

    // ------------------------------------------------------- numeric input

    private void SyncNumericBoxes()
    {
        _suppressTextSync = true;

        try
        {
            XBox.Text = _rect.X.ToString(CultureInfo.InvariantCulture);
            YBox.Text = _rect.Y.ToString(CultureInfo.InvariantCulture);
            WidthBox.Text = _rect.Width.ToString(CultureInfo.InvariantCulture);
            HeightBox.Text = _rect.Height.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressTextSync = false;
        }
    }

    private void OnNumericBoxPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // 数字以外は入力させない。
        foreach (var c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void OnNumericBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            ApplyNumericBox(box);
            e.Handled = true;
        }
    }

    private void OnNumericBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
        {
            ApplyNumericBox(box);
        }
    }

    /// <summary>
    /// 入力値を矩形へ反映する。範囲外の値はエラーにせず、
    /// 有効範囲へ丸める（確定仕様書 4.6.2.3）。
    /// </summary>
    private void ApplyNumericBox(TextBox box)
    {
        if (_suppressTextSync)
        {
            return;
        }

        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            // 解釈できない入力は現在値へ戻す。
            SyncNumericBoxes();
            return;
        }

        var updated = (box.Tag as string) switch
        {
            "X" => _rect with { X = value },
            "Y" => _rect with { Y = value },
            "W" => _rect with { Width = Math.Max(CropCalculator.MinimumSize, value) },
            "H" => _rect with { Height = Math.Max(CropCalculator.MinimumSize, value) },
            _ => _rect,
        };

        _rect = CropCalculator.Fit(updated, _monitor.Width, _monitor.Height);
        UpdateVisuals();
    }

    // --------------------------------------------------------------- close

    private void Confirm()
    {
        if (!_rect.IsValid)
        {
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel()
    {
        DialogResult = false;
        Close();
    }
}
