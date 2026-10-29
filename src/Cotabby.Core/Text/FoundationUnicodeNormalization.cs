using System.Buffers;
using System.Text;

namespace Cotabby.Core.Text;

/// <summary>Foundation-compatible canonical normalization for valid Unicode text. Windows can
/// reject noncharacters even though they are valid Swift scalars; each is a combining-class-zero
/// boundary, so normalize surrounding runs while retaining the exact noncharacter.</summary>
internal static class FoundationUnicodeNormalization
{
    public static string Normalize(string value, NormalizationForm form = NormalizationForm.FormC)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder? result = null;
        int start = 0, offset = 0;
        while (offset < value.Length)
        {
            if (Rune.DecodeFromUtf16(value.AsSpan(offset), out var rune, out int width) != OperationStatus.Done)
                throw new ArgumentException("Text contains malformed UTF-16.", nameof(value));
            int scalar = rune.Value;
            if (scalar is >= 0xFDD0 and <= 0xFDEF || (scalar & 0xFFFF) is 0xFFFE or 0xFFFF)
            {
                result ??= new StringBuilder(value.Length);
                if (offset > start) result.Append(value[start..offset].Normalize(form));
                result.Append(value, offset, width);
                start = offset + width;
            }
            offset += width;
        }
        if (result is null) return value.Normalize(form);
        if (start < value.Length) result.Append(value[start..].Normalize(form));
        return result.ToString();
    }
}
