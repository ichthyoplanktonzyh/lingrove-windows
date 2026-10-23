using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyExportZipTests
{
    [Fact]
    public void IndependentZipReaderRecoversSortedStoredEntriesAndKnownCrc()
    {
        string imageName = "lg-image-" + new string('a', 64) + ".png";
        byte[] image = Encoding.ASCII.GetBytes("123456789");
        var package = VocabularyExportPolicy.Package([], new() { Format = VocabularyExportFormat.Json, Images = true })
            .WithImages(new Dictionary<string, byte[]> { [imageName] = image });
        byte[] bytes = VocabularyExportZip.Data(package);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(new[] { package.DataFilename, "images/" + imageName }, archive.Entries.Select(entry => entry.FullName));
        for (int i = 0; i < archive.Entries.Count; i++)
        {
            var entry = archive.Entries[i]; Assert.Equal(entry.Length, entry.CompressedLength);
            Assert.Equal(new DateTime(1980, 1, 1), entry.LastWriteTime.DateTime);
            using var content = entry.Open(); using var recovered = new MemoryStream(); content.CopyTo(recovered);
            Assert.Equal(i == 0 ? package.CopyData() : image, recovered.ToArray());
        }
        int secondOffset = 30 + Encoding.UTF8.GetByteCount(package.DataFilename) + package.DataByteCount;
        Assert.Equal(0x04034b50U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(secondOffset)));
        Assert.Equal(0xcbf43926U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(secondOffset + 14)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(secondOffset + 8))); // Stored.
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(secondOffset + 6))); // Original flags.
    }
    [Fact]
    public void RepeatedEncodingOwnsItsResultAndLeavesPackageUnchanged()
    {
        var package = VocabularyExportPolicy.Package([], new() { Format = VocabularyExportFormat.Csv });
        byte[] original = package.CopyData(), first = VocabularyExportZip.Data(package), second = VocabularyExportZip.Data(package);
        Assert.Equal(first, second); first[0] ^= 0xff;
        Assert.Equal(second, VocabularyExportZip.Data(package)); Assert.Equal(original, package.CopyData());
    }
}
