using Cotabby.App.Localization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Cotabby.Win32.Vocabulary;
using System.Windows.Media.Imaging;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;
using Microsoft.Win32;

namespace Cotabby.App.UI.Review;

/// <summary>Card-ID layout editing stays in the borrowed session draft. Only explicit Save,
/// Send and Save template cross the shared Store's acknowledged transaction boundary.</summary>
internal sealed partial class CanonicalAnkiReviewWindow
{
    private WindowsVocabularyFileDrop? _layoutDrop;
    private readonly Dictionary<Guid, FrameworkElement> _layoutDropTargets = [];
    private readonly StackPanel _layoutPanel = new(), _parts = new();
    private readonly TextBox _cardName = new(), _templateName = new();
    private readonly ComboBox _layoutExample = new(), _applyLayout = new(), _addPart = new();
    private readonly Button _saveTemplate = new() { Content = InterfaceText.Get("Save as template") }, _addPartButton = new() { Content = InterfaceText.Get("Add content") };
    private sealed record SourceChoice(string Label, ReviewContentSource Source) { public override string ToString() => Label; }
    private sealed record LayoutChoice(string Label, ReviewLayout Layout) { public override string ToString() => Label; }
    internal StackPanel LayoutParts => _parts;
    internal ComboBox LayoutPreset => _applyLayout;
    internal TextBox CardNameBox => _cardName;
    internal TextBox TemplateNameBox => _templateName;
    internal Button SaveTemplateButton => _saveTemplate;
    internal ComboBox AddPartChoice => _addPart;
    internal Button AddPartButton => _addPartButton;

    private void BuildLayoutControls()
    {
        _layoutPanel.Children.Add(VocabularyInputControls.Heading("Card layout"));
        _layoutPanel.Children.Add(VocabularyInputControls.Group(("Card name", _cardName), ("Apply template", _applyLayout), ("Review example", _layoutExample), ("Template name", _templateName), ("", _saveTemplate), ("Side", _side)));
        _layoutPanel.Children.Add(_parts);
        _addPart.ItemsSource = Enum.GetValues<ReviewContentSource>().Select(source => new SourceChoice(source.Label(), source)).ToArray(); _addPart.DisplayMemberPath = nameof(SourceChoice.Label); _addPart.SelectedValuePath = nameof(SourceChoice.Source); _addPart.SelectedIndex = 0; VocabularyNotebookTheme.Combo(_addPart);
        VocabularyNotebookTheme.Button(_addPartButton); VocabularyNotebookTheme.Button(_saveTemplate);
        _layoutPanel.Children.Add(VocabularyInputControls.Group(("Content source", _addPart), ("", _addPartButton)));
        _cardName.TextChanged += (_, _) => { if (!_updating) { _session.Plan = _session.Plan with { Name = _cardName.Text }; RefreshDraft(); } };
        _layoutExample.SelectionChanged += (_, _) => { if (!_updating) { _session.Plan = _session.Plan with { ExampleId = (_layoutExample.SelectedItem as Option)?.Id, FieldOverrides = new Dictionary<string, string>() }; RefreshDraft(); } };
        _applyLayout.SelectionChanged += (_, _) =>
        {
            if (_updating || _applyLayout.SelectedItem is not LayoutChoice choice) return;
            var (_, sense) = Current(); SetLayout(CanonicalReviewCardPolicy.Applying(choice.Layout, sense)); RenderLayout(); RefreshDraft();
        };
        _addPartButton.Click += (_, _) =>
        {
            if (_addPart.SelectedValue is not ReviewContentSource source || Layout.Parts.Count >= 64) return;
            var part = ReviewCardPart.Create(source); if (source is ReviewContentSource.SenseImage or ReviewContentSource.SenseText) part = part with { BlockId = Current().Sense.Content?.FirstOrDefault(b => ReviewLayoutPolicy.Matches(b, source))?.Id };
            SetParts(SideParts.Append(part).ToArray()); RenderLayout(); RefreshDraft();
        };
        _saveTemplate.IsEnabled = false; _templateName.TextChanged += (_, _) => _saveTemplate.IsEnabled = !string.IsNullOrWhiteSpace(_templateName.Text);
        _saveTemplate.Click += (_, _) =>
        {
            string name = _templateName.Text.Trim(); if (name.Length == 0) return;
            var template = new ReviewLayoutTemplate(Guid.NewGuid(), name, ReviewLayoutPolicy.Portable(Layout));
            var images = _session.ImportedImages; Start(async token => { await _session.Store.SaveTemplateAsync(template, images, token); _templateName.Text = ""; _windowNotice = "Template saved."; });
        };
    }
    private ReviewLayout Layout => _session.Plan.Layout ?? new ReviewLayout();
    private IReadOnlyList<ReviewCardPart> SideParts => _side.SelectedIndex == 1 ? Layout.Back : Layout.Front;
    private void SetLayout(ReviewLayout layout)
    {
        _session.Plan = _session.Plan with { Layout = layout, FieldOverrides = new Dictionary<string, string>() };
        var retained = VocabularyContentPolicy.Images(layout.CustomContent).Select(image => image.Filename).ToHashSet(StringComparer.Ordinal);
        _session.ImportedImages = _session.ImportedImages.Where(pair => retained.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
    private void SetParts(IReadOnlyList<ReviewCardPart> parts) { var layout = Layout; SetLayout(_side.SelectedIndex == 1 ? layout with { Back = parts } : layout with { Front = parts }); }
    private void ChangePart(Guid id, Func<ReviewCardPart, ReviewCardPart> update)
    { SetParts(SideParts.Select(p => p.Id == id ? update(p) : p).ToArray()); }
    private void ChangeBlock(Guid partId, Guid blockId, Func<VocabularyContentBlock, VocabularyContentBlock> update)
    { ChangePart(partId, p => p with { Content = p.Content.Select(b => b.Id == blockId ? update(b) : b).ToArray() }); RefreshDraft(); }
    private Button LayoutAction(string title, Action action)
    { var button = new Button { Content = title, Margin = new Thickness(0, 0, 6, 4) }; VocabularyNotebookTheme.Button(button); button.Click += (_, _) => action(); return button; }
    private void RenderLayout()
    {
        if (_session.CardId is null) return;
        bool previous = _updating; _updating = true;
        try
        {
            var (_, sense) = Current(); _cardName.Text = _session.Plan.Name ?? "";
            _layoutExample.Items.Clear(); _layoutExample.Items.Add(new Option("No context", null)); foreach (var example in sense.Examples) _layoutExample.Items.Add(new Option(example.Text, example.Id));
            _layoutExample.SelectedItem = _layoutExample.Items.Cast<Option>().FirstOrDefault(o => o.Id == _session.Plan.ExampleId);
            _applyLayout.Items.Clear(); for (int i = 0; i < 4; i++) _applyLayout.Items.Add(new LayoutChoice(new[] { "Expression → meaning", "Gap → expression", "Image → expression", "Blank card" }[i], ReviewLayoutPolicy.Preset(i)));
            foreach (var template in _session.Snapshot.Document.ReviewTemplates ?? []) _applyLayout.Items.Add(new LayoutChoice(template.Name, template.Layout));
            _applyLayout.SelectedIndex = -1;
            _parts.Children.Clear(); _layoutDropTargets.Clear(); var values = SideParts;
            for (int i = 0; i < values.Count; i++)
            {
                int index = i; var part = values[i]; var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; var header = new WrapPanel();
                header.Children.Add(new TextBlock { Text = part.Source.Label(), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 12, 0) });
                var up = LayoutAction("Move up", () => MovePart(index, -1)); up.IsEnabled = index > 0; header.Children.Add(up);
                var down = LayoutAction("Move down", () => MovePart(index, 1)); down.IsEnabled = index + 1 < SideParts.Count; header.Children.Add(down);
                header.Children.Add(LayoutAction("Delete", () => { SetParts(SideParts.Where(p => p.Id != part.Id).ToArray()); RenderLayout(); RefreshDraft(); })); panel.Children.Add(header);
                if (part.Source is ReviewContentSource.SenseText or ReviewContentSource.SenseImage)
                {
                    var select = new ComboBox(); VocabularyNotebookTheme.Combo(select); select.Items.Add(new Option("Choose…", null));
                    foreach (var block in sense.Content?.Where(b => ReviewLayoutPolicy.Matches(b, part.Source)) ?? [])
                        select.Items.Add(new Option(block.Content is VocabularyBlockContent.Text t ? t.Value.Text : block.Content is VocabularyBlockContent.Image image && image.Caption.Length > 0 ? image.Caption : "Image", block.Id));
                    select.SelectedItem = select.Items.Cast<Option>().FirstOrDefault(o => o.Id == part.BlockId); select.SelectionChanged += (_, _) => { if (!_updating) { ChangePart(part.Id, p => p with { BlockId = (select.SelectedItem as Option)?.Id }); RefreshDraft(); } }; panel.Children.Add(select);
                }
                if (part.Source is ReviewContentSource.SenseImage or ReviewContentSource.Custom)
                {
                    var caption = new CheckBox { Content = InterfaceText.Get("Show image caption"), IsChecked = part.ShowCaption }; caption.Click += (_, _) => { ChangePart(part.Id, p => p with { ShowCaption = caption.IsChecked == true }); RefreshDraft(); }; panel.Children.Add(caption);
                }
                if (part.Source is ReviewContentSource.ExpressionAudio or ReviewContentSource.SentenceAudio)
                {
                    var kind = part.Source == ReviewContentSource.ExpressionAudio ? ReviewAudioKind.Expression : ReviewAudioKind.Sentence;
                    panel.Children.Add(LayoutAction("Preview audio", () => Start(token => _session.PrepareAudioAsync(kind, true, token: token))));
                    panel.Children.Add(LayoutAction("Regenerate audio", () => Start(token => _session.PrepareAudioAsync(kind, false, true, token))));
                    if (_session.Audio.Speech is null) panel.Children.Add(VocabularyInputControls.Caption("Speech is unavailable. Remove this audio slot before sending."));
                }
                if (part.Source == ReviewContentSource.Custom) BuildCustomContent(panel, part);
                _parts.Children.Add(VocabularyInputControls.Group(("", panel)));
            }
            _addPartButton.IsEnabled = Layout.Parts.Count < 64;
        }
        finally { _updating = previous; }
    }
    private void MovePart(int index, int delta)
    { var values = SideParts.ToArray(); int other = index + delta; if (other < 0 || other >= values.Length) return; (values[index], values[other]) = (values[other], values[index]); SetParts(values); RenderLayout(); RefreshDraft(); }
    private void BuildCustomContent(StackPanel panel, ReviewCardPart part)
    {
        _layoutDropTargets[part.Id] = panel;
        CommandManager.AddPreviewExecutedHandler(panel, (_, e) => { if (e.Command == ApplicationCommands.Paste && PasteLayoutImage(part.Id)) e.Handled = true; });
        foreach (var block in part.Content)
        {
            var tools = new WrapPanel(); int index = part.Content.ToList().FindIndex(b => b.Id == block.Id);
            var up = LayoutAction("Move block up", () => MoveBlock(part.Id, block.Id, -1)); up.IsEnabled = index > 0; tools.Children.Add(up);
            var down = LayoutAction("Move block down", () => MoveBlock(part.Id, block.Id, 1)); down.IsEnabled = index + 1 < part.Content.Count; tools.Children.Add(down);
            tools.Children.Add(LayoutAction("Delete block", () => { ChangePart(part.Id, p => p with { Content = p.Content.Where(b => b.Id != block.Id).ToArray() }); RenderLayout(); RefreshDraft(); })); panel.Children.Add(tools);
            if (block.Content is VocabularyBlockContent.Text text)
            {
                var style = new ComboBox { ItemsSource = Enum.GetValues<VocabularyTextStyle>(), SelectedItem = text.Value.Style }; VocabularyNotebookTheme.Combo(style); style.SelectionChanged += (_, _) => { if (!_updating && style.SelectedItem is VocabularyTextStyle v) ChangeBlock(part.Id, block.Id, b => b with { Content = new VocabularyBlockContent.Text(((VocabularyBlockContent.Text)b.Content).Value with { Style = v }) }); }; panel.Children.Add(style);
                var format = new WrapPanel(); foreach (bool bold in new[] { true, false }) { var toggle = new CheckBox { Content = bold ? "Bold" : "Italic", IsChecked = bold ? text.Value.Bold : text.Value.Italic, Margin = new Thickness(0, 4, 12, 4) }; toggle.Click += (_, _) => ChangeBlock(part.Id, block.Id, b => { var value = ((VocabularyBlockContent.Text)b.Content).Value; return b with { Content = new VocabularyBlockContent.Text(bold ? value with { Bold = toggle.IsChecked == true } : value with { Italic = toggle.IsChecked == true }) }; }); format.Children.Add(toggle); } panel.Children.Add(format);
                var field = new TextBox { Text = text.Value.Text, AcceptsReturn = true, MinHeight = 65, TextWrapping = TextWrapping.Wrap }; VocabularyNotebookTheme.Input(field); field.TextChanged += (_, _) => { if (!_updating) ChangeBlock(part.Id, block.Id, b => b with { Content = new VocabularyBlockContent.Text(((VocabularyBlockContent.Text)b.Content).Value with { Text = field.Text }) }); }; panel.Children.Add(field);
            }
            else if (block.Content is VocabularyBlockContent.Image image)
            {
                panel.Children.Add(VocabularyInputControls.Caption("Image: " + image.Reference.Filename));
                var caption = new TextBox { Text = image.Caption }; VocabularyNotebookTheme.Input(caption); caption.TextChanged += (_, _) => { if (!_updating) ChangeBlock(part.Id, block.Id, b => b with { Content = ((VocabularyBlockContent.Image)b.Content) with { Caption = caption.Text } }); }; panel.Children.Add(caption);
            }
        }
        var add = new WrapPanel();
        var textButton = LayoutAction("Add text", () => { ChangePart(part.Id, p => p with { Content = p.Content.Append(VocabularyContentBlock.Create(new VocabularyBlockContent.Text(new()))).ToArray() }); RenderLayout(); RefreshDraft(); }); textButton.IsEnabled = part.Content.Count < VocabularyContentPolicy.MaximumBlocks; add.Children.Add(textButton);
        var paste = LayoutAction("Paste image", () => PasteLayoutImage(part.Id)); paste.IsEnabled = _clipboard is not null && part.Content.Count < VocabularyContentPolicy.MaximumBlocks; add.Children.Add(paste);
        var imageButton = LayoutAction("Add image", () => { var dialog = new OpenFileDialog { Title = "Add image", Filter = "Images|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif;*.ico;*.webp;*.heic;*.heif", Multiselect = false }; if (dialog.ShowDialog(this) == true) ImportLayoutImage(part.Id, dialog.FileName); }); imageButton.IsEnabled = part.Content.Count < VocabularyContentPolicy.MaximumBlocks; add.Children.Add(imageButton); panel.Children.Add(add);
    }
    private void MoveBlock(Guid partId, Guid blockId, int delta)
    {
        ChangePart(partId, p => { var blocks = p.Content.ToArray(); int index = Array.FindIndex(blocks, b => b.Id == blockId), other = index + delta; if (index < 0 || other < 0 || other >= blocks.Length) return p; (blocks[index], blocks[other]) = (blocks[other], blocks[index]); return p with { Content = blocks }; }); RenderLayout(); RefreshDraft();
    }
    internal void ImportLayoutImage(Guid partId, string path)
    {
        Start(async token => { var prepared = await _session.Store.PrepareImageFileAsync(path, token); AddPreparedLayoutImage(partId, prepared); });
    }
    internal bool PasteLayoutImage(Guid partId)
    {
        if (_closing || _operation is not null || _clipboard?.Capture() is not { HasImage: true } offer || !SideParts.Any(p => p.Id == partId && p.Content.Count < VocabularyContentPolicy.MaximumBlocks)) return false;
        Start(async token =>
        {
            var bytes = await _clipboard.ReadAsync(offer.Sequence, token); token.ThrowIfCancellationRequested(); if (bytes is null) return;
            var prepared = await _session.Store.PrepareImageAsync(bytes, token); AddPreparedLayoutImage(partId, prepared);
        }); return true;
    }
    private void AddPreparedLayoutImage(Guid partId, VocabularyPreparedImage prepared)
    {
        var images = new Dictionary<string, VocabularyPreparedImage>(_session.ImportedImages, StringComparer.Ordinal) { [prepared.Reference.Filename] = prepared }; _session.ImportedImages = images;
        ChangePart(partId, p => p with { Content = p.Content.Append(VocabularyContentBlock.Create(new VocabularyBlockContent.Image(prepared.Reference))).ToArray() }); RenderLayout(); RefreshDraft();
    }
    private void AttachLayoutFileDrop()
    {
        if (_closing || _layoutDrop is not null || HwndSource.FromVisual(this) is not HwndSource source) return;
        _layoutDrop = new(source, (path, point) =>
        {
            if (_closing || _operation is not null || !IsVisible || source.CompositionTarget is null || source.RootVisual is not Visual root) return;
            foreach (var (id, area) in _layoutDropTargets) if (VocabularyEntryEditorWindow.ContainsDropPoint(area, root, point, source.CompositionTarget.TransformFromDevice)
                && SideParts.Any(p => p.Id == id && p.Content.Count < VocabularyContentPolicy.MaximumBlocks)) { ImportLayoutImage(id, path); break; }
        });
    }
    private async Task PresentLayoutAsync(long version, CancellationToken token)
    {
        string? validation = await _session.ValidationMessageAsync(token); ReviewCardContent? content = null;
        try { var (entry, sense) = Current(); content = CanonicalReviewCardPolicy.Content(entry, sense, _session.Plan, _side.SelectedIndex == 1); }
        catch (AnkiReviewException error) { validation ??= error.Message; }
        if (version != _previewVersion || _closing) return;
        _validation.Text = validation ?? (_loadedAddress == _session.Controller.Settings.Address ? "" : "Refresh the Anki connection before sending."); _preview.Children.Clear();
        foreach (var part in (_side.SelectedIndex == 1 ? content?.Back : content?.Front) ?? [])
        {
            if (part is ReviewResolvedPart.Text text) _preview.Children.Add(new TextBlock { Text = text.Value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            else if (part is ReviewResolvedPart.Audio audio) _preview.Children.Add(VocabularyInputControls.Caption("🔊 " + audio.Kind.Label()));
            else if (part is ReviewResolvedPart.Blocks blocks) foreach (var block in blocks.Values)
            {
                if (block.Content is VocabularyBlockContent.Text textBlock) _preview.Children.Add(new TextBlock { Text = (textBlock.Value.Style == VocabularyTextStyle.Bullet ? "• " : "") + textBlock.Value.Text, TextWrapping = TextWrapping.Wrap, FontSize = textBlock.Value.Style == VocabularyTextStyle.Heading ? 17 : 13, FontWeight = textBlock.Value.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = textBlock.Value.Italic ? FontStyles.Italic : FontStyles.Normal });
                else if (block.Content is VocabularyBlockContent.Image image)
                {
                    try { byte[] bytes = _session.ImportedImages.TryGetValue(image.Reference.Filename, out var draft) ? draft.CopyBytes() : await _session.Store.ReadImageAsync(image.Reference, token); if (version != _previewVersion || _closing) return; using var stream = new MemoryStream(bytes); var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); _preview.Children.Add(new Image { Source = bitmap, MaxWidth = 360, MaxHeight = 180, HorizontalAlignment = HorizontalAlignment.Left }); if (image.Caption.Length > 0) _preview.Children.Add(VocabularyInputControls.Caption(image.Caption)); }
                    catch (OperationCanceledException) { throw; } catch { _preview.Children.Add(VocabularyInputControls.Caption("Image unavailable.")); }
                }
            }
        }
        UpdateEnabled();
    }
}
