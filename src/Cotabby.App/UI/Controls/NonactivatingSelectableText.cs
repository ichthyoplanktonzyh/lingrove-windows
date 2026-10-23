using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Cotabby.Core.Text;

namespace Cotabby.App.UI.Controls;

/// <summary>
/// Windows adapter for Mac LearningExplanationView's readonly selectable literal text. It owns
/// only mouse selection, translucent selection paint and a nonfocusable Popup Copy/Select all
/// menu. It never focuses a control, intercepts global Ctrl+C, edits inline runs or writes the
/// clipboard. A card supplies immutable document identity and handles the revocable Copy event.
/// Built-in WPF document selection requires keyboard focus, which a learning card must leave in
/// the writing host; this adapter uses TextPointer hit testing without that TextEditor machinery.
/// </summary>
public sealed class NonactivatingSelectableText : TextBlock
{
    public static readonly DependencyProperty DocumentIdentityProperty = DependencyProperty.Register(
        nameof(DocumentIdentity), typeof(object), typeof(NonactivatingSelectableText),
        new PropertyMetadata(null, (owner, _) => ((NonactivatingSelectableText)owner).ClearSelection()));
    public static readonly RoutedEvent CopyRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(CopyRequested), RoutingStrategy.Bubble, typeof(EventHandler<SelectedTextCopyEventArgs>), typeof(NonactivatingSelectableText));

    private LiteralTextSelection? _selection;
    private SelectedTextCopyLease? _lease;
    private Popup? _menu;
    private SelectionAdorner? _adorner;
    private AdornerLayer? _adornerLayer;
    private LiteralTextSelectionMap? _textMap;
    private string? _mappedSource;
    private object? _lastLayoutSignature;
    private int _anchor;
    private int _updatingContent;
    private bool _dragging;
    private static readonly Brush SelectionBrush = CreateSelectionBrush();

    static NonactivatingSelectableText()
    {
        FocusableProperty.OverrideMetadata(typeof(NonactivatingSelectableText), new FrameworkPropertyMetadata(false, null, (_, _) => false));
        // TextBlock seals OnPropertyChanged/OnRender. Add a text metadata callback and keep paint
        // in a separate mouse-transparent Adorner; do not replace WPF's shaping or inline layout.
        TextProperty.OverrideMetadata(typeof(NonactivatingSelectableText), new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (owner, change) =>
            {
                var text = (NonactivatingSelectableText)owner;
                if (text._updatingContent != 0) return;
                // TextContainer can publish a new deferred Text value for a formatting-only
                // change. Compare the resolved notification value, not Text (which would reenter
                // its property getter). Equal raw text must not revoke a styled example's lease.
                if (change.NewValue is string raw && text._selection is { } selected
                    && string.Equals(selected.Source, raw, StringComparison.Ordinal)) return;
                text._textMap = null; text._mappedSource = null; text.ClearSelection();
            }));
    }

    public NonactivatingSelectableText()
    {
        Cursor = Cursors.IBeam;
        IsVisibleChanged += (_, _) => { if (!IsVisible) ClearSelection(); };
        Unloaded += (_, _) => ClearSelection();
        LayoutUpdated += (_, _) => UpdateSelectionLayout();
    }

    public object? DocumentIdentity { get => GetValue(DocumentIdentityProperty); set => SetValue(DocumentIdentityProperty, value); }
    public event EventHandler<SelectedTextCopyEventArgs> CopyRequested
    { add => AddHandler(CopyRequestedEvent, value); remove => RemoveHandler(CopyRequestedEvent, value); }
    internal LiteralTextSelection? Selection => _selection;
    internal Popup? CopyMenu => _menu;

    /// <summary>
    /// Renderers may rebuild literal Runs for a new annotation without changing the document.
    /// Bracket that synchronous mutation so unchanged raw text retains its selection/receipt;
    /// changed text still revokes it. No TextPointer or old Run instance is retained.
    /// </summary>
    public void UpdateLiteralContent(Action update)
    {
        Dispatcher.VerifyAccess();
        _updatingContent++;
        try { update(); }
        finally
        {
            _updatingContent--;
            if (_updatingContent == 0 && !string.Equals(_mappedSource, Text, StringComparison.Ordinal)) { _textMap = null; _mappedSource = null; }
            if (_updatingContent == 0 && _selection is { } selected
                && (!LiteralRuns || !string.Equals(selected.Source, Text, StringComparison.Ordinal))) ClearSelection();
            InvalidateSelectionPaint();
        }
    }

    public void SetLiteralText(string text) => UpdateLiteralContent(() => Text = text);

    internal void Select(int anchor, int extent)
    {
        Dispatcher.VerifyAccess();
        // This adapter accepts literal Runs only. Embedded controls/hyperlinks need their own
        // interaction semantics and cannot silently become a plain-text clipboard projection.
        var selected = LiteralRuns ? TextMap()?.Select(anchor, extent) : null;
        if (_selection == selected) return;
        _lease?.Revoke(); _lease = null;
        _selection = selected;
        if (_menu is not null) _menu.IsOpen = false;
        InvalidateSelectionPaint();
    }

    public void ClearSelection()
    {
        Dispatcher.VerifyAccess();
        _lease?.Revoke(); _lease = null; _selection = null; _dragging = false;
        if (_menu is not null) { _menu.IsOpen = false; _menu = null; }
        if (IsMouseCaptured) ReleaseMouseCapture();
        RemoveSelectionPaint();
    }

    internal SelectedTextCopyLease? RequestCopy()
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || !LiteralRuns || DocumentIdentity is not { } identity || _selection is not { Length: > 0 } selected
            || !string.Equals(selected.Source, Text, StringComparison.Ordinal)) return null;
        _lease?.Revoke();
        var lease = _lease = new SelectedTextCopyLease(selected.Text, identity);
        RaiseEvent(new SelectedTextCopyEventArgs(CopyRequestedEvent, lease));
        return lease;
    }

    internal bool BeginSelection(Point point)
    {
        if (GetPositionFromPoint(point, snapToText: false) is not { } hit) return false;
        _anchor = Offset(hit);
        Select(_anchor, _anchor);
        return true;
    }
    internal void ExtendSelection(Point point)
    {
        if (GetPositionFromPoint(point, snapToText: true) is { } hit) Select(_anchor, Offset(hit));
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        if (!BeginSelection(args.GetPosition(this))) return;
        _dragging = CaptureMouse();
        args.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs args)
    {
        if (!_dragging || args.LeftButton != MouseButtonState.Pressed) return;
        ExtendSelection(args.GetPosition(this)); args.Handled = true;
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
    {
        if (!_dragging) return;
        ExtendSelection(args.GetPosition(this)); _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        args.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs args) { _dragging = false; base.OnLostMouseCapture(args); }
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs args) { OpenCopyMenu(); args.Handled = true; }

    internal void OpenCopyMenu()
    {
        Dispatcher.VerifyAccess();
        if (!IsVisible || DocumentIdentity is null) return;
        if (_menu is not null) _menu.IsOpen = false;
        var copy = MenuButton("Copy"); copy.IsEnabled = _selection is { Length: > 0 };
        var all = MenuButton("Select all");
        var stack = new StackPanel(); stack.Children.Add(copy); stack.Children.Add(all);
        var border = new Border { Child = stack, Padding = new Thickness(4), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBg");
        border.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        var menu = _menu = new Popup { Child = border, PlacementTarget = this, Placement = PlacementMode.MousePoint,
            StaysOpen = false, AllowsTransparency = true, Focusable = false };
        // Popup uses WS_EX_NOACTIVATE. Avoid ContextMenu/MenuBase: menu mode explicitly takes
        // keyboard focus even when its top-level popup does not activate the foreground window.
        var selectionAtOpen = _selection;
        object identityAtOpen = DocumentIdentity;
        string textAtOpen = Text;
        bool CurrentMenu() => ReferenceEquals(_menu, menu) && menu.IsOpen && Equals(identityAtOpen, DocumentIdentity)
            && ReferenceEquals(selectionAtOpen, _selection) && string.Equals(textAtOpen, Text, StringComparison.Ordinal);
        copy.Click += (_, _) => { if (CurrentMenu()) RequestCopy(); menu.IsOpen = false; };
        all.Click += (_, _) => { if (CurrentMenu()) Select(0, Text.Length); menu.IsOpen = false; };
        menu.IsOpen = true;
    }

    private static Button MenuButton(string title)
    {
        var button = new Button { Content = title, Focusable = false, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        button.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
        return button;
    }

    private int Offset(TextPointer pointer) => new TextRange(ContentStart, pointer).Text.Length;
    private bool LiteralRuns => Inlines.Cast<Inline>().All(inline => inline is Run);
    private LiteralTextSelectionMap? TextMap()
    {
        string source = Text;
        if (!string.Equals(_mappedSource, source, StringComparison.Ordinal))
        { _mappedSource = source; _textMap = LiteralTextSelectionMap.Create(source); }
        return _textMap;
    }
    internal TextPointer? PointerAt(int offset)
    {
        var cursor = ContentStart;
        while (cursor is not null)
        {
            if (cursor.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                int length = cursor.GetTextRunLength(LogicalDirection.Forward);
                if (offset <= length) return cursor.GetPositionAtOffset(offset, LogicalDirection.Forward);
                offset -= length;
            }
            cursor = cursor.GetNextContextPosition(LogicalDirection.Forward);
        }
        return offset == 0 ? ContentEnd : null;
    }

    /// <summary>
    /// Paint grapheme cells rather than one logical left/right span: a bidi line can have disjoint
    /// visual runs. Limit traversal to visible lines inside ancestor scroll viewports so a long
    /// explanation does not redraw every offscreen character on each pointer move.
    /// </summary>
    internal IReadOnlyList<Rect> SelectionRectangles()
    {
        if (_selection is not { Length: > 0 } selected || !string.Equals(selected.Source, Text, StringComparison.Ordinal)) return [];
        Rect visible = VisibleBounds();
        if (visible.IsEmpty || visible.Height <= 0) return [];
        int first = selected.Start, last = first + selected.Length;
        var top = GetPositionFromPoint(new Point(visible.Left, visible.Top), true)?.GetLineStartPosition(0);
        var bottom = GetPositionFromPoint(new Point(visible.Right, visible.Bottom), true)?.GetLineStartPosition(1);
        if (top is not null) first = Math.Max(first, Offset(top));
        if (bottom is not null) last = Math.Min(last, Offset(bottom));
        var cells = new List<Rect>();
        if (TextMap() is not { } map) return [];
        for (int index = map.ElementAtOrBefore(first); index < map.ElementCount; index++)
        {
            int start = map.Boundary(index), end = map.Boundary(index + 1);
            if (start >= last) break;
            if (start < first || end > last) continue;
            if (PointerAt(start) is not { } a || PointerAt(end) is not { } b) continue;
            Rect left = a.GetCharacterRect(LogicalDirection.Forward), right = b.GetCharacterRect(LogicalDirection.Backward);
            if (left.IsEmpty || right.IsEmpty || Math.Abs(left.Top - right.Top) > 0.5) continue;
            var cell = new Rect(Math.Min(left.X, right.X), left.Y, Math.Abs(right.X - left.X), Math.Max(left.Height, right.Height));
            cell.Intersect(visible);
            if (!cell.IsEmpty && cell.Width > 0 && cell.Height > 0) cells.Add(cell);
        }
        // Coalesce overlapping cells before painting, otherwise ligatures/bidi overlap darkens
        // selection repeatedly. Keep distinct visual runs and wrapped lines separate.
        var result = new List<Rect>();
        foreach (var cell in cells.OrderBy(value => value.Y).ThenBy(value => value.X))
        {
            if (result.Count > 0 && Math.Abs(result[^1].Top - cell.Top) < 0.25
                && Math.Abs(result[^1].Height - cell.Height) < 0.25 && result[^1].Right >= cell.Left - 0.25)
            { var merged = result[^1]; merged.Union(cell); result[^1] = merged; }
            else result.Add(cell);
        }
        return result;
    }

    private void InvalidateSelectionPaint()
    {
        if (_selection is not { Length: > 0 }) { RemoveSelectionPaint(); return; }
        if (_adorner is null && AdornerLayer.GetAdornerLayer(this) is { } layer)
        {
            _adornerLayer = layer;
            _adorner = new SelectionAdorner(this) { IsHitTestVisible = false, Focusable = false };
            layer.Add(_adorner);
        }
        _adorner?.InvalidateVisual();
    }
    private void RemoveSelectionPaint()
    {
        if (_adorner is not null) _adornerLayer?.Remove(_adorner);
        _adorner = null; _adornerLayer = null;
        _lastLayoutSignature = null;
    }
    private Rect VisibleBounds()
    {
        Rect visible = new(RenderSize);
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(this); ancestor is Visual; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is ScrollContentPresenter viewport)
                visible.Intersect(viewport.TransformToVisual(this).TransformBounds(new Rect(viewport.RenderSize)));
        return visible;
    }
    private void UpdateSelectionLayout()
    {
        if (_adorner is null) return;
        var signature = (RenderSize, VisibleBounds(), FontFamily, FontSize, FontWeight, FontStyle,
            FontStretch, FlowDirection, TextAlignment, TextWrapping, TextTrimming, LineHeight, LineStackingStrategy, Padding, Language);
        if (Equals(_lastLayoutSignature, signature)) return;
        _lastLayoutSignature = signature;
        // InvalidateVisual also invalidates arrange. Calling it on every LayoutUpdated would
        // sustain a render/layout loop and starve input/idle. Scroll/font/size changes need one
        // repaint; a matching layout signature must leave the dispatcher idle afterward.
        _adorner.InvalidateVisual();
    }
    private sealed class SelectionAdorner(NonactivatingSelectableText owner) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext context)
        { foreach (var rectangle in owner.SelectionRectangles()) context.DrawRectangle(SelectionBrush, null, rectangle); }
    }
    private static Brush CreateSelectionBrush()
    { var brush = new SolidColorBrush(Color.FromArgb(65, 0, 122, 255)); brush.Freeze(); return brush; }
}
