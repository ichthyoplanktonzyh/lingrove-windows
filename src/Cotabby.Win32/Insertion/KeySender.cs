using System.Runtime.InteropServices;
using Cotabby.Core.Focus;
using Cotabby.Win32.Interop;
using static Cotabby.Win32.Interop.NativeMethods;

namespace Cotabby.Win32.Insertion;

/// <summary>
/// Dispatches one complete key batch to the target process that still owns the foreground. The
/// fresh process check narrows the race after Core's field snapshot revalidation; SendInput is
/// reported as successful only when Windows accepted every event in the batch.
/// </summary>
internal sealed class KeySender : IKeyboardEditDispatcher, IConditionalKeyboardEditDispatcher
{
    private readonly IKeyboardNativeApi _native;

    public KeySender() : this(new Win32KeyboardNativeApi()) { }

    internal KeySender(IKeyboardNativeApi native) =>
        _native = native ?? throw new ArgumentNullException(nameof(native));

    public bool SendUnicodeEdit(FocusedField target, int deleteCount, string text)
    {
        SyntheticKey[] batch = SyntheticKeys.Edit(HeldModifiers(), deleteCount, text).ToArray();
        return Send(target, batch, beforeSend: null);
    }

    public bool SendUnicodeEdit(FocusedField target, int deleteCount, string text, Func<bool> beforeSend)
    {
        ArgumentNullException.ThrowIfNull(beforeSend);
        SyntheticKey[] batch = SyntheticKeys.Edit(HeldModifiers(), deleteCount, text).ToArray();
        return Send(target, batch, beforeSend);
    }

    public bool SendPasteEdit(FocusedField target, int deleteCount, Func<bool> clipboardIsCurrent)
    {
        ArgumentNullException.ThrowIfNull(clipboardIsCurrent);
        SyntheticKey[] batch = SyntheticKeys.PasteEdit(HeldModifiers(), deleteCount).ToArray();
        return Send(target, batch, clipboardIsCurrent);
    }

    private bool IsTargetForeground(FocusedField target)
    {
        if (target.ProcessId <= 0) return false;
        return _native.ForegroundProcessId == (uint)target.ProcessId;
    }

    private IEnumerable<ushort> HeldModifiers() =>
        SyntheticKeys.Modifiers.Where(vk => (_native.GetAsyncKeyState(vk) & 0x8000) != 0);

    private bool Send(FocusedField target, SyntheticKey[] keys, Func<bool>? beforeSend)
    {
        if (keys.Length == 0) return true;

        var inputs = new INPUT[keys.Length];
        for (int i = 0; i < inputs.Length; i++)
        {
            SyntheticKey key = keys[i];
            uint flags = key.KeyUp ? KEYEVENTF_KEYUP : 0;
            if (key.VirtualKey == 0) flags |= KEYEVENTF_UNICODE;
            else if (key.VirtualKey is VK_RCONTROL or VK_RMENU or VK_LWIN or VK_RWIN)
            {
                flags |= KEYEVENTF_EXTENDEDKEY;
            }

            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new INPUTUNION
                {
                    ki = new KEYBDINPUT { wVk = key.VirtualKey, wScan = key.Character, dwFlags = flags },
                },
            };
        }

        // Construct the complete native array before checking foreground so the process check is
        // adjacent to dispatch, even for a larger bounded paste replacement.
        if (!IsTargetForeground(target)) return false;

        // Clipboard ownership may change while modifiers and the native batch are prepared. Core
        // checks before entering the backend too; this last predicate check narrows that race to
        // the small interval between this call and SendInput.
        if (beforeSend is not null && !beforeSend()) return false;
        // A native range receipt may require a host read. Activation can change during that
        // read, so foreground must still match after the final receipt, adjacent to SendInput.
        if (!IsTargetForeground(target)) return false;

        // Windows can accept only a prefix of the batch. Retrying the suffix could delete more
        // text or duplicate an insertion in a different host state, so any partial count fails.
        return _native.SendInput(inputs) == (uint)inputs.Length;
    }
}

internal interface IKeyboardNativeApi
{
    uint? ForegroundProcessId { get; }
    short GetAsyncKeyState(int virtualKey);
    uint SendInput(INPUT[] inputs);
}

internal sealed class Win32KeyboardNativeApi : IKeyboardNativeApi
{
    public uint? ForegroundProcessId
    {
        get
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return null;

            GetWindowThreadProcessId(foreground, out uint processId);
            return processId == 0 ? null : processId;
        }
    }

    public short GetAsyncKeyState(int virtualKey) => NativeMethods.GetAsyncKeyState(virtualKey);

    public uint SendInput(INPUT[] inputs) =>
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
}
