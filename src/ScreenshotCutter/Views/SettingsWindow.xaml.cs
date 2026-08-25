using System.Windows;
using Microsoft.Win32;
using ScreenshotCutter.Models;
using ScreenshotCutter.Services;
using ScreenshotCutter.ViewModels;

namespace ScreenshotCutter.Views;

/// <summary>
/// 設定ウィンドウ（確定仕様書 4.3）。
/// </summary>
/// <remarks>
/// 「×」で閉じてもアプリは終了せず、トレイに常駐したままになる
/// （確定仕様書 4.3.4）。終了はトレイメニューからのみ。
/// </remarks>
public partial class SettingsWindow : Window
{
    /// <summary>識別オーバーレイの表示時間。</summary>
    private static readonly TimeSpan IdentifyDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 切り出しオーバーレイを出す前に、自ウィンドウが画面から消えるのを待つ時間。
    /// これを省くと設定ウィンドウが背景に写り込む。
    /// </summary>
    private static readonly TimeSpan HideBeforeCaptureDelay = TimeSpan.FromMilliseconds(160);

    private readonly AppController _controller;
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(AppController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        _controller = controller;

        InitializeComponent();

        _viewModel = new SettingsViewModel(
            controller.Settings.Clone(),
            controller.MonitorService,
            controller.HotkeyError);

        DataContext = _viewModel;

        SettingsPathText.Text = $"設定ファイル: {AppPaths.SettingsFile}";

        OkButton.Click += OnOkClicked;
        CancelButton.Click += (_, _) => Close();
        ApplyButton.Click += OnApplyClicked;
        BrowseFolderButton.Click += OnBrowseFolderClicked;
        IdentifyButton.Click += OnIdentifyClicked;
        RefreshMonitorsButton.Click += (_, _) => _viewModel.RefreshMonitors();
        ConfigureCropButton.Click += OnConfigureCropClicked;

        Activated += (_, _) => _viewModel.RefreshFileNamePreview();
    }

    /// <summary>
    /// トレイメニューのトグルで設定が変わったときに、表示を同期する。
    /// 開いたままの画面が古い値を持ち続け、OK で上書きしてしまうのを防ぐ。
    /// </summary>
    public void ReloadFromSettings(AppSettings settings) => _viewModel.LoadFrom(settings);

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        if (ApplyChanges())
        {
            Close();
        }
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e) => ApplyChanges();

    /// <summary>設定を確定して保存する。成功した場合のみ true。</summary>
    private bool ApplyChanges()
    {
        var validationError = _viewModel.Validate();
        if (validationError is not null)
        {
            MessageBox.Show(this, validationError, "設定を確認してください", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var updated = _viewModel.BuildSettings(_controller.Settings);
        var error = _controller.ApplySettings(updated);

        // ホットキーの登録可否は保存後に確定するため、ここで取り直す。
        _viewModel.HotkeyError = _controller.HotkeyError;

        if (error is not null)
        {
            MessageBox.Show(this, error, "設定の保存", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private void OnBrowseFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "保存先フォルダーの選択",
            Multiselect = false,
        };

        // 現在の設定値が実在する場合のみ初期位置に使う。
        try
        {
            if (System.IO.Directory.Exists(_viewModel.OutputFolder))
            {
                dialog.InitialDirectory = _viewModel.OutputFolder;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("保存先フォルダーの初期位置を設定できませんでした。", ex);
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.OutputFolder = dialog.FolderName;
        }
    }

    private void OnIdentifyClicked(object sender, RoutedEventArgs e)
    {
        // 表示中に構成が変わっている可能性があるため、最新の一覧で出す。
        _viewModel.RefreshMonitors();
        MonitorIdentifyWindow.ShowAll([.. _viewModel.Monitors], IdentifyDuration);
    }

    private void OnConfigureCropClicked(object sender, RoutedEventArgs e)
    {
        var monitor = _viewModel.SelectedMonitor;

        if (monitor is null)
        {
            MessageBox.Show(
                this, "先に撮影対象のモニターを選択してください。", "切り出し領域の設定",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // 自分自身が背景に写り込まないよう、キャプチャ前に隠す
            // （確定仕様書 4.6.3.3）。
            Visibility = Visibility.Hidden;
            WaitForRepaint();

            var background = _controller.CaptureService.Capture(monitor);

            var overlay = new CropOverlayWindow(
                monitor,
                _controller.CaptureService,
                _viewModel.CropRect.IsValid ? _viewModel.CropRect : null)
            {
                Owner = null,
            };

            overlay.SetBackground(background);

            if (overlay.ShowDialog() == true)
            {
                _viewModel.CropRect = overlay.SelectedRect;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("切り出し領域の設定画面を開けませんでした。", ex);
            MessageBox.Show(
                this, $"切り出し領域の設定を開けませんでした。\n\n{ex.Message}", "切り出し領域の設定",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Visibility = Visibility.Visible;
            Activate();
        }
    }

    /// <summary>ウィンドウが実際に画面から消えるまで待つ。</summary>
    private void WaitForRepaint()
    {
        UpdateLayout();

        // 描画キューを空にしてから、コンポジタが反映する分だけ待つ。
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        Thread.Sleep(HideBeforeCaptureDelay);
    }
}
