using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Cotabby.Core.ExpressionAssist;
using Cotabby.Core.Focus;
using Cotabby.App.UI.Learning;

namespace Cotabby.App.UI.ExpressionAssist;

/// <summary>One native input window. The editor reads only its atomic keyboard-ownership snapshot;
/// it independently verifies the actual foreground HWND before authorizing host return.</summary>
internal sealed class ExpressionAssistWindow : Window, IExpressionAssistPanel
{
    private readonly ExpressionAssistView _view = new();
    private readonly nint _handle;
    private readonly ScreenRect _anchor;
    private int _ownsKeyboard;
    private bool _closing, _hiding;
    private ExpressionAssistSession? _session;

    internal ExpressionAssistWindow(ScreenRect caret)
    {
        _anchor = caret;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight; Background = Brushes.Transparent; AllowsTransparency = true;
        Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
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
    {
        var text = new TextBox { Text = candidate, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        var body = new StackPanel { Margin = new Thickness(16) };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(text);
        var copy = new Button { Content = "Copy candidate", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(12, 5, 12, 5) };
        copy.Click += (_, _) => { try { Clipboard.SetText(candidate); } catch (System.Runtime.InteropServices.ExternalException) { } };
        body.Children.Add(copy);
        var window = new Window { Title = "Expression assistance", Width = 400, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = body, ShowInTaskbar = false };
        window.Show();
    }
    void IExpressionAssistPanel.ShowUnavailable(string message)
    {
        var window = new Window { Title = "Expression assistance", Width = 400, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18) } };
        window.Show();
    }
    private void RefreshOwnership() => Volatile.Write(ref _ownsKeyboard, IsActive && _view.QueryBox.IsKeyboardFocusWithin ? 1 : 0);
}
