namespace Cotabby.Core.Context;

/// <summary>
/// Separates clipboard sequence changes made by an app-owned temporary write from later user
/// copies. The Win32 owner brackets only the short, exclusive clipboard mutation and reports the
/// sequence values observed while it held the clipboard; this policy itself has no OS calls.
/// </summary>
/// <remarks>
/// A paste lease can remain alive while a user copies something else. Hiding the whole lease
/// interval would hide that copy too, so this class subtracts only the exact sequence delta from
/// each own mutation and suppresses text only while the current sequence is still the app's text.
/// </remarks>
public sealed class ClipboardWriteAccounting
{
    private readonly object _gate = new();
    private uint _ownChangeCount;
    private uint? _ownTextSequence;
    private uint? _writeStartSequence;
    private uint _sequenceBeforeWrite;

    /// <summary>Return the visible sequence number after removing only recorded app-owned changes.</summary>
    public uint GetVisibleSequence(uint currentSequence)
    {
        lock (_gate)
        {
            if (_writeStartSequence is not null) return _sequenceBeforeWrite;
            ObserveExternalChange(currentSequence);
            return unchecked(currentSequence - _ownChangeCount);
        }
    }

    /// <summary>True when a read should be hidden because it would expose our in-flight or current text.</summary>
    public bool IsOwnText(uint currentSequence)
    {
        lock (_gate)
        {
            if (_writeStartSequence is not null) return true;
            ObserveExternalChange(currentSequence);
            return _ownTextSequence == currentSequence;
        }
    }

    /// <summary>
    /// Begin one clipboard mutation after the caller has opened the clipboard exclusively. The
    /// matching end call must use a sequence read taken before CloseClipboard, so another process
    /// cannot be counted as part of this write.
    /// </summary>
    public void BeginOwnWrite(uint sequenceBeforeWrite)
    {
        lock (_gate)
        {
            if (_writeStartSequence is not null)
            {
                throw new InvalidOperationException("Clipboard own writes cannot overlap.");
            }

            ObserveExternalChange(sequenceBeforeWrite);
            _writeStartSequence = sequenceBeforeWrite;
            _sequenceBeforeWrite = unchecked(sequenceBeforeWrite - _ownChangeCount);
        }
    }

    /// <summary>
    /// Complete an own mutation, recording only the sequence delta that happened while the
    /// caller held the clipboard open. Pass true only when <paramref name="sequenceAfterWrite">
    /// identifies temporary text that should remain hidden until a different copy appears.
    /// </summary>
    public void EndOwnWrite(uint sequenceAfterWrite, bool containsOwnText)
    {
        lock (_gate)
        {
            if (_writeStartSequence is not { } start) return;

            _ownChangeCount = unchecked(_ownChangeCount + sequenceAfterWrite - start);
            _ownTextSequence = containsOwnText ? sequenceAfterWrite : null;
            _writeStartSequence = null;
            _sequenceBeforeWrite = 0;
            ObserveExternalChange(sequenceAfterWrite);
        }
    }

    private void ObserveExternalChange(uint currentSequence)
    {
        // Clipboard sequence values are process-global and can wrap. Equality, not ordering, tells
        // us whether the sequence still describes the text we last put there.
        if (_ownTextSequence is { } ownTextSequence && ownTextSequence != currentSequence)
        {
            _ownTextSequence = null;
        }
    }
}
