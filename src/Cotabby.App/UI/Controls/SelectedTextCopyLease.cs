using System.Windows;

namespace Cotabby.App.UI.Controls;

/// <summary>
/// A readonly selection's revocable copy receipt. The WPF control owns revocation; an App worker
/// reads only this detached text/identity and atomic flag, never a Dispatcher-bound TextPointer.
/// Text, document, selection or visibility changes revoke pending copies. A granted native copy
/// transaction may finish after revocation under IClipboardTextWriter's commit contract.
/// </summary>
public sealed class SelectedTextCopyLease
{
    private int _current = 1;
    internal SelectedTextCopyLease(string text, object documentIdentity) { Text = text; DocumentIdentity = documentIdentity; }
    public string Text { get; }
    public object DocumentIdentity { get; }
    public bool IsCurrent => Volatile.Read(ref _current) == 1;
    internal void Revoke() => Interlocked.Exchange(ref _current, 0);
}

/// <summary>Bubbles a frozen user Copy intent to the owning card; the control performs no OS write.</summary>
public sealed class SelectedTextCopyEventArgs(RoutedEvent routedEvent, SelectedTextCopyLease lease) : RoutedEventArgs(routedEvent)
{
    public SelectedTextCopyLease Lease { get; } = lease;
    protected override void InvokeEventHandler(Delegate handler, object target) =>
        ((EventHandler<SelectedTextCopyEventArgs>)handler)(target, this);
}
