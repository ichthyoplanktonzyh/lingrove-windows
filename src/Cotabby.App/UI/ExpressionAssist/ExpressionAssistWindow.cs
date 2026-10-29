using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.App.UI.Learning;
using Microsoft.Win32;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>One native input window. The editor reads only its atomic keyboard-ownership snapshot;
/// it independently verifies the actual foreground HWND before authorizing host return.</summary>
internal sealed class ExpressionAssistWindow : Window, IExpressionAssistPanel
{
    private readonly ExpressionAssistView _view = new();
    private readonly nint _handle;
    private readonly ScreenRect _anchor;
    private readonly Action<string, string> _showFailure;
    private readonly Action<string> _showUnavailable;
    private int _ownsKeyboard;
    private bool _closing, _hiding;
    private ExpressionAssistSession? _session;

    internal ExpressionAssistWindow(ScreenRect caret, Action<string, string> showFailure, Action<string> showUnavailable)
    {
        _anchor = caret;
        _showFailure = showFailure; _showUnavailable = showUnavailable;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight; Background = Brushes.Transparent; AllowsTransparency = true;
        Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Cotabby;component/UI/Theme.xaml", UriKind.Relative) });
        WindowTheme.Follow(this); ApplyContrast();
        void Changed(object sender, UserPreferenceChangedEventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            try { Dispatcher.BeginInvoke(() => { if (!_closing) ApplyContrast(); }, DispatcherPriority.Loaded); }
            catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) { }
        }
        SystemEvents.UserPreferenceChanged += Changed;
        Closed += (_, _) => SystemEvents.UserPreferenceChanged -= Changed;
        Content = _view;
        _handle = new WindowInteropHelper(this).EnsureHandle();
        Activated += (_, _) => RefreshOwnership();
        Deactivated += (_, _) => { RefreshOwnership(); if (!_hiding && !_closing) _session?.FocusLost(); };
        _view.QueryBox.GotKeyboardFocus += (_, _) => RefreshOwnership();
        _view.QueryBox.LostKeyboardFocus += (_, _) => RefreshOwnership();
        _view.QueryChanged += (text, composing) => _session?.QueryChanged(text, composing);
        _view.Submit += () => _session?.Submit();
        _view.Accept += candidate => _session?.Accept(candidate);
        _view.Cancel += () => _session?.Cancel();
        Closed += (_, _) => { _closing = true; Volatile.Write(ref _ownsKeyboard, 0); _session?.Invalidate(); };
    }

    private void ApplyContrast()
    {
        bool high = SystemParameters.HighContrast;
        Resources["ExpressionSelected"] = high ? SystemColors.WindowBrush : (Brush)FindResource("KeycapBg");
        Resources["ExpressionSelectedBorder"] = high ? SystemColors.HighlightBrush : (Brush)FindResource("AccentBrush");
        if (!high) return;
        Resources["CardBg"] = SystemColors.WindowBrush; Resources["CardBorder"] = SystemColors.WindowTextBrush;
        Resources["TextPrimary"] = SystemColors.WindowTextBrush; Resources["TextSecondary"] = SystemColors.WindowTextBrush;
        Resources["InputBg"] = SystemColors.WindowBrush; Resources["KeycapBg"] = SystemColors.WindowBrush;
    }

    internal void Bind(ExpressionAssistSession session) => _session = session;
    internal ExpressionAssistView View => _view;
    nint IExpressionAssistPanel.WindowHandle => _handle;
    bool IExpressionAssistPanel.OwnsKeyboard => Volatile.Read(ref _ownsKeyboard) != 0;
    bool IExpressionAssistPanel.IsVisible => IsVisible;
    Task IExpressionAssistPanel.ShowAsync(ExpressionEditorReceipt receipt, string initialQuery, Func<bool> mayActivate)
    {
        if (!mayActivate()) return Task.CompletedTask;
        if (!IsVisible) _view.ResetComposition();
        _view.SetQuery(initialQuery); Show(); UpdateLayout(); LearningPanelPlacement.Position(this, _anchor);
        if (!mayActivate()) { ((IExpressionAssistPanel)this).Hide(false); return Task.CompletedTask; }
        Activate(); _view.FocusQuery(); RefreshOwnership();
        return Dispatcher.InvokeAsync(RefreshOwnership, DispatcherPriority.Input).Task;
    }
    void IExpressionAssistPanel.Update(ExpressionAssistResult? result, string? message, bool loading)
    {
        _view.Update(result, message, loading);
        if (IsVisible) { UpdateLayout(); LearningPanelPlacement.Position(this, _anchor); }
    }
    void IExpressionAssistPanel.Hide(bool restoringHost)
    {
        _view.ResetComposition();
        if (_closing) return;
        _hiding = true;
        try { Volatile.Write(ref _ownsKeyboard, 0); Hide(); }
        finally { _hiding = false; }
    }
    void IExpressionAssistPanel.ShowFailure(string candidate, string message)
        => _showFailure(candidate, message);
    void IExpressionAssistPanel.ShowUnavailable(string message)
        => _showUnavailable(message);
    private void RefreshOwnership() => Volatile.Write(ref _ownsKeyboard, IsActive && _view.QueryBox.IsKeyboardFocusWithin ? 1 : 0);
}
