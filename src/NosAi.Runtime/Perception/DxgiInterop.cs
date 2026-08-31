// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — DXGI Desktop Duplication interop (direct COM vtable)
// ============================================================================
//
// Minimal interop against DXGI/D3D11 with no external dependency. The COM calls
// go through function pointers at a vtable slot: every index is a named,
// commented constant, because a wrong slot is not a compile error but a runtime
// crash.
//
// None of these primitives invents pixels: where acquisition fails, the caller
// gets a negative result with a reason, never a synthetic frame.

using System;
using System.Runtime.InteropServices;

namespace NosAi.Runtime.Perception;

internal static unsafe class DxgiInterop
{
    // --- HRESULT ------------------------------------------------------------
    internal const int S_OK = 0;
    internal const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
    internal const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
    internal const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    internal const int E_ACCESSDENIED = unchecked((int)0x80070005);

    // --- Interface IIDs -----------------------------------------------------
    internal static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    internal static readonly Guid IID_IDXGIOutput1 = new("00cddea8-939b-4b83-a340-a685226666cc");
    internal static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

    // --- vtable slot indices ------------------------------------------------
    // IUnknown occupies slots 0..2 (QueryInterface, AddRef, Release) on every
    // interface; IDXGIObject then occupies 3..6 on the DXGI ones.
    internal const int IUnknown_QueryInterface = 0;
    internal const int IUnknown_Release = 2;

    // IDXGIFactory1 : IDXGIFactory(7..11) : IDXGIObject(3..6) : IUnknown(0..2)
    internal const int IDXGIFactory1_EnumAdapters1 = 12;

    // IDXGIAdapter : IDXGIObject : IUnknown
    internal const int IDXGIAdapter_EnumOutputs = 7;

    // IDXGIOutput1 : IDXGIOutput(7..18) : IDXGIObject : IUnknown
    internal const int IDXGIOutput1_DuplicateOutput = 22;

    // IDXGIOutputDuplication : IDXGIObject : IUnknown
    internal const int IDXGIOutputDuplication_GetDesc = 7;
    internal const int IDXGIOutputDuplication_AcquireNextFrame = 8;
    internal const int IDXGIOutputDuplication_ReleaseFrame = 14;

    // ID3D11Device : IUnknown — CreateBuffer(3), CreateTexture1D(4), CreateTexture2D(5)
    internal const int ID3D11Device_CreateTexture2D = 5;

    // ID3D11DeviceContext : ID3D11DeviceChild(3..6) : IUnknown
    internal const int ID3D11DeviceContext_Map = 14;
    internal const int ID3D11DeviceContext_Unmap = 15;
    internal const int ID3D11DeviceContext_CopyResource = 47;

    // --- D3D11 constants ----------------------------------------------------
    internal const uint D3D11_SDK_VERSION = 7;
    internal const uint D3D_DRIVER_TYPE_UNKNOWN = 0;
    internal const uint D3D11_USAGE_STAGING = 3;
    internal const uint D3D11_CPU_ACCESS_READ = 0x20000;
    internal const uint D3D11_MAP_READ = 1;
    internal const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    [DllImport("dxgi.dll", ExactSpelling = true)]
    internal static extern int CreateDXGIFactory1(in Guid riid, out void* factory);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    internal static extern int D3D11CreateDevice(
        void* adapter,
        uint driverType,
        IntPtr software,
        uint flags,
        uint* featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out void* device,
        uint* featureLevel,
        out void* immediateContext);

    /// <summary>Reads one slot of a COM object's vtable.</summary>
    private static void* Slot(void* comObject, int index) => (*(void***)comObject)[index];

    internal static int QueryInterface(void* comObject, in Guid iid, out void* result)
    {
        fixed (Guid* pIid = &iid)
        fixed (void** pResult = &result)
        {
            return ((delegate* unmanaged[Stdcall]<void*, Guid*, void**, int>)
                Slot(comObject, IUnknown_QueryInterface))(comObject, pIid, pResult);
        }
    }

    internal static void Release(ref void* comObject)
    {
        if (comObject is null) return;
        ((delegate* unmanaged[Stdcall]<void*, uint>)Slot(comObject, IUnknown_Release))(comObject);
        comObject = null;
    }

    internal static int EnumAdapters1(void* factory, uint index, out void* adapter)
    {
        fixed (void** pAdapter = &adapter)
        {
            return ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)
                Slot(factory, IDXGIFactory1_EnumAdapters1))(factory, index, pAdapter);
        }
    }

    internal static int EnumOutputs(void* adapter, uint index, out void* output)
    {
        fixed (void** pOutput = &output)
        {
            return ((delegate* unmanaged[Stdcall]<void*, uint, void**, int>)
                Slot(adapter, IDXGIAdapter_EnumOutputs))(adapter, index, pOutput);
        }
    }

    internal static int DuplicateOutput(void* output1, void* device, out void* duplication)
    {
        fixed (void** pDuplication = &duplication)
        {
            return ((delegate* unmanaged[Stdcall]<void*, void*, void**, int>)
                Slot(output1, IDXGIOutput1_DuplicateOutput))(output1, device, pDuplication);
        }
    }

    internal static int GetDuplicationDesc(void* duplication, out DxgiOutduplDesc desc)
    {
        fixed (DxgiOutduplDesc* pDesc = &desc)
        {
            // GetDesc returns void; the shim keeps one calling convention for callers.
            ((delegate* unmanaged[Stdcall]<void*, DxgiOutduplDesc*, void>)
                Slot(duplication, IDXGIOutputDuplication_GetDesc))(duplication, pDesc);
            return S_OK;
        }
    }

    internal static int AcquireNextFrame(void* duplication, uint timeoutMs, out DxgiOutduplFrameInfo info, out void* desktopResource)
    {
        fixed (DxgiOutduplFrameInfo* pInfo = &info)
        fixed (void** pResource = &desktopResource)
        {
            return ((delegate* unmanaged[Stdcall]<void*, uint, DxgiOutduplFrameInfo*, void**, int>)
                Slot(duplication, IDXGIOutputDuplication_AcquireNextFrame))(duplication, timeoutMs, pInfo, pResource);
        }
    }

    internal static int ReleaseFrame(void* duplication) =>
        ((delegate* unmanaged[Stdcall]<void*, int>)
            Slot(duplication, IDXGIOutputDuplication_ReleaseFrame))(duplication);

    internal static int CreateTexture2D(void* device, in D3D11Texture2DDesc desc, out void* texture)
    {
        fixed (D3D11Texture2DDesc* pDesc = &desc)
        fixed (void** pTexture = &texture)
        {
            return ((delegate* unmanaged[Stdcall]<void*, D3D11Texture2DDesc*, void*, void**, int>)
                Slot(device, ID3D11Device_CreateTexture2D))(device, pDesc, null, pTexture);
        }
    }

    internal static void CopyResource(void* context, void* destination, void* source) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void>)
            Slot(context, ID3D11DeviceContext_CopyResource))(context, destination, source);

    internal static int Map(void* context, void* resource, uint subresource, uint mapType, out D3D11MappedSubresource mapped)
    {
        fixed (D3D11MappedSubresource* pMapped = &mapped)
        {
            return ((delegate* unmanaged[Stdcall]<void*, void*, uint, uint, uint, D3D11MappedSubresource*, int>)
                Slot(context, ID3D11DeviceContext_Map))(context, resource, subresource, mapType, 0, pMapped);
        }
    }

    internal static void Unmap(void* context, void* resource, uint subresource) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, uint, void>)
            Slot(context, ID3D11DeviceContext_Unmap))(context, resource, subresource);
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiSampleDesc
{
    public uint Count;
    public uint Quality;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11Texture2DDesc
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public uint Format;
    public DxgiSampleDesc SampleDesc;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct D3D11MappedSubresource
{
    public void* Data;
    public uint RowPitch;
    public uint DepthPitch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiOutduplPointerPosition
{
    public int PositionX;
    public int PositionY;
    public int Visible;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiOutduplFrameInfo
{
    public long LastPresentTime;
    public long LastMouseUpdateTime;
    public uint AccumulatedFrames;
    public int RectsCoalesced;
    public int ProtectedContentMaskedOut;
    public DxgiOutduplPointerPosition PointerPosition;
    public uint TotalMetadataBufferSize;
    public uint PointerShapeBufferSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiRational
{
    public uint Numerator;
    public uint Denominator;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiModeDesc
{
    public uint Width;
    public uint Height;
    public DxgiRational RefreshRate;
    public uint Format;
    public uint ScanlineOrdering;
    public uint Scaling;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DxgiOutduplDesc
{
    public DxgiModeDesc ModeDesc;
    public uint Rotation;
    public int DesktopImageInSystemMemory;
}
