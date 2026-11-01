using System.Runtime.InteropServices;
using System.Text;

namespace GammaSwitch;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    internal struct DisplayDevice
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceKey;
        public static DisplayDevice Create() => new(){Size=(uint)Marshal.SizeOf<DisplayDevice>(),DeviceName="",DeviceString="",DeviceId="",DeviceKey=""};
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayDevicesW(string? device,uint index,ref DisplayDevice info,uint flags);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern nint CreateDCW(string driver,string device,string? output,nint initData);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetDeviceGammaRamp(nint dc,[Out,MarshalAs(UnmanagedType.LPArray,SizeConst=768)] ushort[] ramp);
    [DllImport("gdi32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool SetDeviceGammaRamp(nint dc,[In,MarshalAs(UnmanagedType.LPArray,SizeConst=768)] ushort[] ramp);
    [DllImport("gdi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetICMProfileW(nint dc,ref uint length,StringBuilder filename);
    [DllImport("mscms.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsGetUsePerUserProfiles(string device,uint deviceClass,[MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("mscms.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsSetUsePerUserProfiles(string device,uint deviceClass,[MarshalAs(UnmanagedType.Bool)] bool enabled);
    [DllImport("mscms.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsGetDefaultColorProfileSize(uint scope,string device,uint type,uint subtype,uint id,out uint bytes);
    [DllImport("mscms.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsGetDefaultColorProfile(uint scope,string device,uint type,uint subtype,uint id,uint bytes,StringBuilder profile);
    [DllImport("mscms.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsSetDefaultColorProfile(uint scope,string device,uint type,uint subtype,uint id,string? profile);
    [DllImport("mscms.dll",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool WcsAssociateColorProfileWithDevice(uint scope,string profile,string device);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool RegisterHotKey(nint window,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool UnregisterHotKey(nint window,int id);
}
