using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Actual WIC/PNG operations, without a notebook write or source file. The shared corpus executes
/// the original ImageIO boundary on identical bytes; encoder bytes themselves are platform-owned.
/// Trusted WPF encoders create extra orientation/multiframe inputs; production never enumerates
/// untrusted WPF Frames. The dispatcher and Task.Run cases exercise both COM apartment paths.
/// </summary>
[Collection("WPF views")]
public sealed class VocabularyImagePreparationTests(WpfDispatcher dispatcher)
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "fixtures", "parity", "vocabulary-image-preparation-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(row => new object[] { row.GetProperty("id").GetString()! });
    [Theory]
    [MemberData(nameof(Cases))]
    public Task PinnedMacOwnsFrameSizeAdmissionAndPreparedPngReference(string id) => dispatcher.Run(() =>
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(value => value.GetProperty("id").GetString() == id);
        Assert.Equal("Cotabby/Services/Vocabulary/VocabularyImageStorage.swift", row.GetProperty("macSource").GetString());
        byte[] input = id == "input-budget" ? new byte[24 * 1024 * 1024 + 1] : Convert.FromBase64String(row.GetProperty("input").GetString()!);
        if (id == "input-budget") Array.Fill(input, (byte)1);
        object observation;
        try
        {
            var prepared = WindowsVocabularyImagePreparer.Prepare(input); var data = prepared.CopyBytes(); var reference = prepared.Reference;
            WindowsVocabularyImagePreparer.Validate(data, reference);
            observation = new { valid = true, width = reference.Width, height = reference.Height, matchingCount = data.Length == reference.ByteCount,
                matchingDigest = Convert.ToHexStringLower(SHA256.HashData(data)) == reference.Digest, withinByteBudget = data.Length <= VocabularyContentPolicy.MaximumImageBytes,
                png = data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) };
        }
        catch (VocabularyException error) when (error.Failure == VocabularyFailure.InvalidContent) { observation = new { valid = false, error = "invalidContent" }; }
        var actual = JsonSerializer.SerializeToNode(observation);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetProperty("expected").GetRawText()), actual), $"{id}: {actual}");
    });

    [Theory]
    [InlineData(1, 3, 2, "abcdef")]
    [InlineData(2, 3, 2, "cbafed")]
    [InlineData(3, 3, 2, "fedcba")]
    [InlineData(4, 3, 2, "defabc")]
    [InlineData(5, 2, 3, "adbecf")]
    [InlineData(6, 2, 3, "daebfc")]
    [InlineData(7, 2, 3, "fcebda")]
    [InlineData(8, 2, 3, "cfbead")]
    public Task NativeExifOrientationAppliesToActualColoredPixelsAndDoesNotCopyTheDescription(int orientation, int width, int height, string order) => dispatcher.Run(() =>
    {
        var metadata = new BitmapMetadata("tiff"); metadata.SetQuery("/ifd/{ushort=274}", (ushort)orientation);
        metadata.SetQuery("/ifd/{ushort=270}", "private source description");
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.None }; encoder.Frames.Add(BitmapFrame.Create(Pattern(), null, metadata, null));
        using var input = new MemoryStream(); encoder.Save(input);
        var prepared = WindowsVocabularyImagePreparer.Prepare(input.ToArray()); Assert.Equal(width, prepared.Reference.Width); Assert.Equal(height, prepared.Reference.Height);
        byte[] data = prepared.CopyBytes(); WindowsVocabularyImagePreparer.Validate(data, prepared.Reference);
        using var output = new MemoryStream(data); var frame = new PngBitmapDecoder(output, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); var pixels = new byte[width * height * 4]; converted.CopyPixels(pixels, width * 4, 0);
        Assert.Equal(order, string.Concat(Enumerable.Range(0, width * height).Select(index => Letter(pixels.AsSpan(index * 4, 4)))));
        Assert.DoesNotContain(Chunks(data), value => value is "tEXt" or "iTXt" or "zTXt" or "eXIf");
    });

    [Fact]
    public Task OnlyFrameZeroIsDecodedFromAnActualMultiframeTiffAndAnMtaWorkerCanPrepareIt() => dispatcher.RunAsync(async () =>
    {
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.None };
        encoder.Frames.Add(BitmapFrame.Create(Pattern()));
        for (int index = 0; index < 128; index++) encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4)));
        using var input = new MemoryStream(); encoder.Save(input); byte[] bytes = input.ToArray();
        var prepared = await Task.Run(() => WindowsVocabularyImagePreparer.Prepare(bytes));
        Assert.Equal(3, prepared.Reference.Width); Assert.Equal(2, prepared.Reference.Height); WindowsVocabularyImagePreparer.Validate(prepared.CopyBytes(), prepared.Reference);
    });

    [Theory]
    [InlineData("hash")]
    [InlineData("count")]
    [InlineData("dimensions")]
    [InlineData("not-png")]
    public Task ValidationRejectsAReferenceThatDoesNotDescribeTheExactPng(string mode) => dispatcher.Run(() =>
    {
        var row = Corpus.GetProperty("cases")[0]; var prepared = WindowsVocabularyImagePreparer.Prepare(Convert.FromBase64String(row.GetProperty("input").GetString()!));
        byte[] bytes = prepared.CopyBytes(); var reference = prepared.Reference;
        switch (mode)
        {
            case "hash": reference = reference with { Digest = new string('0', 64) }; break;
            case "count": reference = reference with { ByteCount = bytes.Length + 1 }; break;
            case "dimensions": reference = reference with { Width = reference.Width + 1 }; break;
            case "not-png": bytes[0] = 0; reference = reference with { Digest = Convert.ToHexStringLower(SHA256.HashData(bytes)) }; break;
        }
        Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(() => WindowsVocabularyImagePreparer.Validate(bytes, reference)).Failure);
    });

    [Fact]
    public Task AnEmbeddedSrgbContextIsAppliedBeforeSourceMetadataIsRemoved() => dispatcher.Run(() =>
    {
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.None };
        var contexts = new System.Collections.ObjectModel.ReadOnlyCollection<ColorContext>([new ColorContext(PixelFormats.Bgra32)]);
        encoder.Frames.Add(BitmapFrame.Create(Pattern(), null, null, contexts)); using var input = new MemoryStream(); encoder.Save(input); byte[] bytes = input.ToArray();
        using (var original = new WindowsVocabularyImageFrame(bytes)) Assert.True(original.ColorContextCount > 0);
        var prepared = WindowsVocabularyImagePreparer.Prepare(bytes); byte[] data = prepared.CopyBytes(); WindowsVocabularyImagePreparer.Validate(data, prepared.Reference);
        using var output = new MemoryStream(data); var frame = new PngBitmapDecoder(output, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0); var pixels = new byte[24]; converted.CopyPixels(pixels, 12, 0);
        Assert.Equal("abcdef", string.Concat(Enumerable.Range(0, 6).Select(index => Letter(pixels.AsSpan(index * 4, 4)))));
        Assert.DoesNotContain(Chunks(data), value => value is "iCCP" or "tEXt" or "eXIf");
    });

    [Fact]
    public Task HighEntropyPixelsCannotBypassTheEncodedPngByteCeiling() => dispatcher.Run(() =>
    {
        var pixels = new byte[1600 * 1600 * 4]; new Random(714).NextBytes(pixels);
        var source = BitmapSource.Create(1600, 1600, 96, 96, PixelFormats.Bgra32, null, pixels, 1600 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var input = new MemoryStream(); encoder.Save(input);
        Assert.InRange(input.Length, VocabularyContentPolicy.MaximumImageBytes + 1L, WindowsVocabularyImagePreparer.MaximumInputBytes);
        Assert.Equal(VocabularyFailure.InvalidContent, Assert.Throws<VocabularyException>(() => WindowsVocabularyImagePreparer.Prepare(input.ToArray())).Failure);
    });

    [Fact]
    public Task CancellationBeforeNativeAdmissionDoesNotProduceAPreparedDraft() => dispatcher.Run(() =>
    {
        using var lifetime = new CancellationTokenSource(); lifetime.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsVocabularyImagePreparer.Prepare([], lifetime.Token));
    });
    private static BitmapSource Pattern()
    {
        var bgra = "abcdef".SelectMany(Color).ToArray();
        return BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, bgra, 12);
    }
    private static byte[] Color(char letter) => letter switch
    {
        'a' => [0,0,255,255], 'b' => [0,255,0,255], 'c' => [255,0,0,255],
        'd' => [0,255,255,255], 'e' => [255,0,255,255], 'f' => [255,255,0,255], _ => throw new InvalidOperationException(),
    };
    private static char Letter(ReadOnlySpan<byte> pixel)
    { foreach (char letter in "abcdef") if (pixel.SequenceEqual(Color(letter))) return letter; throw new InvalidOperationException("Unexpected normalized test pixel."); }
    private static IEnumerable<string> Chunks(byte[] bytes)
    {
        int offset = 8;
        while (offset < bytes.Length)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            yield return System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4); offset = checked(offset + 12 + length);
        }
    }
}
