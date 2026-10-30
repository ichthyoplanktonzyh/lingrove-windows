using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using Cotabby.App.Localization;
using Cotabby.App.Speech;
using Cotabby.App.UI.Vocabulary;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Microsoft.Win32;

namespace Cotabby.App.UI.Settings;

/// <summary>Settings is a borrower of the application speech owner. Opening it enumerates the
/// selected provider's catalog only; preview and file selection are explicit user actions.</summary>
internal sealed class SpeechConfigurationWindow : Window
{
    private sealed record VoiceChoice(string Id, string Name) { public override string ToString() => Name; }
    private readonly LearningSpeechController _speech;
    private readonly Func<SpeechModelFiles>? _models;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<Task> _transfers = [];
    private readonly Guid _owner = Guid.NewGuid();
    private readonly StackPanel _body = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _destination = new() { FlowDirection = FlowDirection.LeftToRight, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private Button? _test;
    private PasswordBox? _key;
    private string _keyDraft = "";
    private bool _keyDirty;
    private LearningLanguage _language = LearningLanguage.English;
    private bool _rebuilding, _closed;
    private LearningSpeechProviderKind? _renderedProvider;
    private SpeechTarget Target => new(_owner, "settings.testVoice." + _language.Code());
    internal SpeechConfigurationWindow(LearningSpeechController speech, Func<SpeechModelFiles>? models = null)
    {
        _speech = speech; _models = models; Title = InterfaceText.Get("Speech", "Localizable"); Width = 700; Height = 680; MinWidth = 520; MinHeight = 450; ShowInTaskbar = false;
        VocabularyNotebookTheme.Follow(this);
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(22) };
        Loaded += (_, _) => { if (_speech.Settings.SelectedProvider == LearningSpeechProviderKind.System && !_speech.CatalogIsLoaded) _speech.RefreshVoices(); Render(); };
        _speech.PropertyChanged += Changed;
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _speech.PropertyChanged -= Changed; _speech.Stop(_owner); _key?.Clear(); _key = null; _keyDraft = ""; _ = Task.WhenAll(_transfers.ToArray()).ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default); };
    }
    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs args) { if (!_closed && !_rebuilding) { if (_renderedProvider != _speech.Settings.SelectedProvider) Render(); else RenderStatus(); } }
    private void Render()
    {
        if (_closed) return; _rebuilding = true;
        try
        {
            _renderedProvider = _speech.Settings.SelectedProvider; _body.Children.Clear(); Heading("Current configuration");
            var provider = new ComboBox { ItemsSource = Enum.GetValues<LearningSpeechProviderKind>(), SelectedItem = _speech.Settings.SelectedProvider, MinWidth = 200,
                ItemTemplate = InterfaceEnumLabels.Create<LearningSpeechProviderKind>(kind => kind switch { LearningSpeechProviderKind.System => "System speech", LearningSpeechProviderKind.Local => "Local speech model", _ => "TTS endpoint" }) };
            VocabularyNotebookTheme.Combo(provider); provider.SelectionChanged += (_, _) => { if (provider.SelectedItem is LearningSpeechProviderKind kind) { _speech.Settings.SelectProvider(kind); if (kind == LearningSpeechProviderKind.System) _speech.RefreshVoices(); Render(); } };
            Row("Run using", provider);
            _body.Children.Add(_destination);
            switch (_speech.Settings.SelectedProvider)
            {
                case LearningSpeechProviderKind.System: SystemControls(); break;
                case LearningSpeechProviderKind.Local: LocalControls(); break;
                case LearningSpeechProviderKind.Endpoint: EndpointControls(); break;
            }
            Heading("Test voice");
            var rate = new Slider { Minimum = .5, Maximum = 1.5, Value = _speech.Settings.RateMultiplier, Width = 200, TickFrequency = .1, HorizontalAlignment = HorizontalAlignment.Left }; rate.ValueChanged += (_, _) => _speech.Settings.SetRateMultiplier(rate.Value); Row("Playback rate", rate);
            var language = new ComboBox { ItemsSource = LearningLanguages.All, SelectedItem = _language, MinWidth = 200,
                ItemTemplate = InterfaceEnumLabels.Create<LearningLanguage>(value => value.Label()) }; VocabularyNotebookTheme.Combo(language);
            language.SelectionChanged += (_, _) => { if (language.SelectedItem is LearningLanguage value) { _speech.Stop(_owner); _language = value; Render(); } }; Row("Audition language", language);
            _body.Children.Add(new TextBlock { Text = Phrase(_language), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
            _test = Button("Test voice", TestVoice); _test.Name = "speechTestVoice"; _body.Children.Add(_test);
            _body.Children.Add(_status); RenderStatus();
        }
        finally { _rebuilding = false; }
    }
    private void SystemControls()
    {
        Heading("Voice"); var choices = _speech.Voices(_language);
        string? saved = _speech.Settings.VoiceId(_language); string? selected = choices.FirstOrDefault(v => v.Id == saved)?.Id ?? choices.FirstOrDefault(v => !v.IsNovelty)?.Id;
        var voices = new ComboBox { ItemsSource = choices.Select(v => new VoiceChoice(v.Id, v.Name)).ToArray(), DisplayMemberPath = "Name", SelectedValuePath = "Id", SelectedValue = selected, MinWidth = 200, IsEnabled = choices.Count > 0 };
        VocabularyNotebookTheme.Combo(voices); voices.SelectionChanged += (_, _) => { if (voices.SelectedValue is string id) _speech.Settings.SetVoiceId(id, _language); }; Row("System voice", voices);
        if (choices.Count == 0) _body.Children.Add(new TextBlock { Text = _speech.VoiceCatalogError ?? InterfaceText.Get("No compatible system voice is installed for this language.", "Localizable"), TextWrapping = TextWrapping.Wrap });
        else if (saved is not null && choices.All(v => v.Id != saved)) _body.Children.Add(new TextBlock { Text = InterfaceText.Get("The saved voice is unavailable. An available voice will be used until you choose another.", "Localizable"), TextWrapping = TextWrapping.Wrap });
    }
    private void LocalControls()
    {
        Heading("Local speech model");
        Asset("Model", _speech.Settings.LocalModelPath, path => { _speech.SelectLocalModel(path); Render(); }, InterfaceText.Get("GGUF model") + "|*.gguf");
        Asset("Decoder", _speech.Settings.LocalProjectionPath, path => { _speech.Settings.SetLocalProjectionPath(path); Render(); }, InterfaceText.Get("GGUF decoder") + "|*.gguf");
        Asset("Reference voice (optional)", _speech.Settings.ReferenceAudioPath, path => { _speech.Settings.SetReferenceAudioPath(path); Render(); }, InterfaceText.Get("Audio") + "|*.wav;*.mp3");
        _body.Children.Add(new TextBlock { Text = InterfaceText.Get("Local speech loads its own model only when requested.", "Localizable"), TextWrapping = TextWrapping.Wrap });
        if (!System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Helpers", "lingrove-tts-helper.exe")))
            _body.Children.Add(new TextBlock { Text = InterfaceText.Get("The local speech helper is not available in this build.", "Localizable"), TextWrapping = TextWrapping.Wrap });
        if (_models is not null)
        {
            Heading("Installed speech models"); _speech.RefreshLocalModels();
            foreach (var model in _models().InstalledModelOptions())
            {
                var row = new WrapPanel(); row.Children.Add(new TextBlock { Text = model.DisplayName, Width = 240, TextWrapping = TextWrapping.Wrap });
                if (!SpeechModelPairingPolicy.IsKnownDecoder(model.Filename)) row.Children.Add(Button("Select", () => { _speech.SelectLocalModel(model.Url.LocalPath); Render(); }));
                var remove = Button("Delete", () => DeleteModel(model.Filename)); remove.IsEnabled = !_models().IsProtected(model.Filename); row.Children.Add(remove); _body.Children.Add(row);
            }
            _body.Children.Add(Button("Import GGUF…", ImportModels));
        }
        SamplingControls();
    }
    private void SamplingControls()
    {
        string identity = _speech.Settings.ParameterIdentity; var sampling = _speech.Settings.GenerationProfile.Sampling;
        var content = new StackPanel(); var disclosure = new Expander { Header = InterfaceText.Get("Advanced sampling", "Localizable"), Content = content, Margin = new Thickness(0, 10, 0, 10) }; _body.Children.Add(disclosure);
        void Number(string label, string value, Func<string, LearningModelSamplingOverrides, LearningModelSamplingOverrides?> parse)
        {
            var box = new TextBox { Text = value, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = InterfaceText.Get("Leave empty for the recommended value.", "Localizable") }; VocabularyNotebookTheme.Input(box);
            box.LostKeyboardFocus += (_, _) => { var updated = parse(box.Text, _speech.Settings.GenerationProfile.Sampling); if (updated is not null) _speech.Settings.SetSamplingOverrides(updated, identity); else _status.Text = InterfaceText.Get("Enter a valid number or leave the value empty.", "Localizable"); };
            content.Children.Add(new TextBlock { Text = InterfaceText.Get(label), Margin = new Thickness(0, 8, 0, 4) }); content.Children.Add(box);
        }
        static double? ParseDouble(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) ? n : null;
        Number("Temperature", sampling.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "", (v, s) => v.Trim().Length == 0 ? s with { Temperature = null } : ParseDouble(v) is { } n ? s with { Temperature = n } : null);
        Number("Top P", sampling.TopP?.ToString(CultureInfo.InvariantCulture) ?? "", (v, s) => v.Trim().Length == 0 ? s with { TopP = null } : ParseDouble(v) is { } n ? s with { TopP = n } : null);
        Number("Top K", sampling.TopK?.ToString(CultureInfo.InvariantCulture) ?? "", (v, s) => v.Trim().Length == 0 ? s with { TopK = null } : long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? s with { TopK = n } : null);
        Number("Repetition penalty", sampling.RepetitionPenalty?.ToString(CultureInfo.InvariantCulture) ?? "", (v, s) => v.Trim().Length == 0 ? s with { RepetitionPenalty = null } : ParseDouble(v) is { } n ? s with { RepetitionPenalty = n } : null);
        var seed = new ComboBox { ItemsSource = Enum.GetValues<LearningModelSeedMode>(), SelectedItem = sampling.SeedMode, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Left,
            ItemTemplate = InterfaceEnumLabels.Create<LearningModelSeedMode>(value => value.ToString(), "Learning") }; VocabularyNotebookTheme.Combo(seed);
        seed.SelectionChanged += (_, _) => { if (seed.SelectedItem is LearningModelSeedMode mode) _speech.Settings.SetSamplingOverrides(_speech.Settings.GenerationProfile.Sampling with { SeedMode = mode }, identity); };
        content.Children.Add(new TextBlock { Text = InterfaceText.Get("Seed mode", "Localizable"), Margin = new Thickness(0, 8, 0, 4) }); content.Children.Add(seed);
        Number("Seed value", sampling.FixedSeed.ToString(CultureInfo.InvariantCulture), (v, s) => uint.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint n) ? s with { FixedSeed = n } : null);
    }
    private void ImportModels()
    {
        var dialog = new OpenFileDialog { Filter = InterfaceText.Get("GGUF models") + "|*.gguf", Multiselect = true }; if (dialog.ShowDialog(this) != true) return;
        Track(Import()); async Task Import() { try { var result = await _models!().ImportAsync(dialog.FileNames, _lifetime.Token); if (!_closed) { Render(); _status.Text = InterfaceText.Format("Imported {0}; skipped {1}; failed {2}.", result.Imported, result.Skipped, result.Failed); } }
        catch (OperationCanceledException) { } catch { if (!_closed) _status.Text = InterfaceText.Get("Could not import the selected models.", "Localizable"); } }
    }
    private void DeleteModel(string name)
    {
        if (MessageBox.Show(this, InterfaceText.Format("Remove %@ from Lingrove's local models folder?", name), InterfaceText.Get("Delete model?"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Track(Delete()); async Task Delete() { try { bool deleted = await _models!().DeleteAsync(name, _lifetime.Token); if (!_closed) { Render(); if (!deleted) _status.Text = InterfaceText.Get("This model is in use.", "Localizable"); } }
        catch (OperationCanceledException) { } catch { if (!_closed) _status.Text = InterfaceText.Get("Could not delete this model.", "Localizable"); } }
    }
    private void Track(Task task) { _transfers.Add(task); _ = Remove(); async Task Remove() { try { await task; } finally { _transfers.Remove(task); } } }
    private void EndpointControls()
    {
        Heading("Speech endpoint");
        _body.Children.Add(new TextBlock { Text = InterfaceText.Get("A speech request sends the selected phrase to this destination.", "Localizable"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        Input("Server URL", _speech.Settings.EndpointBaseUrl, _speech.Settings.SetEndpointBaseUrl);
        Input("Model", _speech.Settings.EndpointModel, _speech.Settings.SetEndpointModel);
        Input("Voice", _speech.Settings.EndpointVoice, _speech.Settings.SetEndpointVoice);
        var password = _key = new PasswordBox { MinWidth = 200, Password = _keyDraft }; password.PasswordChanged += (_, _) => { _keyDraft = password.Password; _keyDirty = true; }; var panel = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(password);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(Button("Save key", () => { try { if (!_keyDirty) { message.Text = InterfaceText.Get("Enter a key to save.", "Localizable"); return; } SaveKey(); message.Text = InterfaceText.Get("Key saved.", "Localizable"); } catch { message.Text = InterfaceText.Get("Unable to save the key.", "Localizable"); } }));
        panel.Children.Add(Button("Clear key", () => { try { _speech.Settings.SaveEndpointApiKey(null); password.Clear(); _keyDraft = ""; _keyDirty = false; message.Text = InterfaceText.Get("Key removed.", "Localizable"); } catch { message.Text = InterfaceText.Get("Unable to remove the key.", "Localizable"); } }));
        Row("API key", panel); _body.Children.Add(message);
    }
    private void SaveKey() { if (!_keyDirty) return; _speech.Settings.SaveEndpointApiKey(_keyDraft); _keyDirty = false; }
    private void TestVoice()
    {
        try { if (_speech.Settings.SelectedProvider == LearningSpeechProviderKind.Endpoint) SaveKey(); _speech.Toggle(new(Target, Phrase(_language), _language, LanguageMode: SpeechLanguageMode.Fixed)); }
        catch { _status.Text = InterfaceText.Get("Unable to save the speech endpoint key.", "Localizable"); }
    }
    private void RenderStatus() { _destination.Text = _speech.DestinationDescription; if (_test is not null) _test.Content = InterfaceText.Get(_speech.IsPlaying(Target) ? "Stop" : "Test voice"); _status.Text = _speech.Error(Target) ?? (_speech.IsPreparing(Target) ? InterfaceText.Get("Generating speech…") : _speech.IsPlaying(Target) ? InterfaceText.Get("Playing") : _speech.ConfigurationError ?? ""); }
    private void Input(string label, string value, Action<string> save)
    { var box = new TextBox { Text = value, MinWidth = 240, FlowDirection = FlowDirection.LeftToRight }; VocabularyNotebookTheme.Input(box); box.LostKeyboardFocus += (_, _) => save(box.Text); Row(label, box); }
    private void Asset(string label, string path, Action<string> save, string filter)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(new TextBlock { Text = path.Length == 0 ? InterfaceText.Get("Not selected", "Localizable") : System.IO.Path.GetFileName(path), Width = 220, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(Button("Choose…", () => { var dialog = new OpenFileDialog { Filter = filter, Multiselect = false }; if (dialog.ShowDialog(this) == true) { try { save(dialog.FileName); } catch { _status.Text = InterfaceText.Get("Unable to select this file.", "Localizable"); } } }));
        if (path.Length > 0) panel.Children.Add(Button("Clear", () => save(""))); Row(label, panel);
    }
    private void Heading(string title) => _body.Children.Add(new TextBlock { Text = InterfaceText.Get(title), FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) });
    private void Row(string label, UIElement control) { var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(new TextBlock { Text = InterfaceText.Get(label), Margin = new Thickness(0, 0, 0, 4) }); panel.Children.Add(control); _body.Children.Add(panel); }
    private static Button Button(string text, Action click) { var button = new Button { Content = InterfaceText.Get(text), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 8, 6) }; VocabularyNotebookTheme.Button(button); button.Click += (_, _) => click(); return button; }
    private static string Phrase(LearningLanguage language) => language switch
    {
        LearningLanguage.English => "Hello. This is a voice preview.", LearningLanguage.Spanish => "Hola. Esta es una prueba de voz.", LearningLanguage.French => "Bonjour. Ceci est un essai vocal.", LearningLanguage.German => "Hallo. Dies ist eine Sprachprobe.", LearningLanguage.Italian => "Ciao. Questa è una prova vocale.", LearningLanguage.Portuguese => "Olá. Esta é uma amostra de voz.", LearningLanguage.Dutch => "Hallo. Dit is een stemtest.", LearningLanguage.Russian => "Здравствуйте. Это проверка голоса.", LearningLanguage.Chinese => "你好，这是语音试听。", LearningLanguage.TraditionalChinese => "你好，這是語音試聽。", LearningLanguage.Japanese => "こんにちは。音声の試聴です。", LearningLanguage.Korean => "안녕하세요. 음성 미리 듣기입니다.", LearningLanguage.Hindi => "नमस्ते। यह आवाज़ का नमूना है।", LearningLanguage.Arabic => "مرحبًا، هذه عينة صوتية.", _ => ""
    };
}
