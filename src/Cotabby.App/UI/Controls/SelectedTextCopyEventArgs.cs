using System.Windows;

namespace Cotabby.App.UI.Controls;

/// <summary>Bubbles a frozen user Copy intent to the owning card; the control performs no OS write.</summary>
public sealed class SelectedTextCopyEventArgs(RoutedEvent routedEvent, SelectedTextCopyLease lease) : RoutedEventArgs(routedEvent)
{
    public SelectedTextCopyLease Lease { get; } = lease;
    protected override void InvokeEventHandler(Delegate handler, object target) =>
        ((EventHandler<SelectedTextCopyEventArgs>)handler)(target, this);
}
