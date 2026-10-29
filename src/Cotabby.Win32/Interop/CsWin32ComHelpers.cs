using System.Runtime.InteropServices;
using Windows.Win32.System.Com;

namespace Windows.Win32;

internal static unsafe partial class ComHelpers
{
    static partial void PopulateIUnknownImpl<TComInterface>(IUnknown.Vtbl* vtable) where TComInterface : unmanaged
    {
        ComWrappers.GetIUnknownImpl(out nint query, out nint add, out nint release);
        vtable->QueryInterface_1 = (delegate* unmanaged[Stdcall]<IUnknown*, Guid*, void**, Foundation.HRESULT>)query;
        vtable->AddRef_2 = (delegate* unmanaged[Stdcall]<IUnknown*, uint>)add;
        vtable->Release_3 = (delegate* unmanaged[Stdcall]<IUnknown*, uint>)release;
    }
}
