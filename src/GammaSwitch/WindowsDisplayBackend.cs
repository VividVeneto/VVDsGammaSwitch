using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GammaSwitch.Core;
using static GammaSwitch.NativeMethods;

namespace GammaSwitch;

internal sealed class WindowsDisplayBackend : IDisplayBackend
{
    private const uint MonitorClass=0x6D6E7472; // CLASS_MONITOR ('mntr'), Windows SDK icm.h.
    public static string ColorDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"spool","drivers","color");
    private static Exception Error(string operation) => new InvalidOperationException(operation+"："+new Win32Exception(Marshal.GetLastWin32Error()).Message);

    public IReadOnlyList<DisplayInfo> Enumerate()
    {
        var result = new List<DisplayInfo>();
        for(uint i=0;;i++)
        {
            var adapter=DisplayDevice.Create();
            if(!EnumDisplayDevicesW(null,i,ref adapter,0))break;
            if((adapter.StateFlags&1)==0 || (adapter.StateFlags&8)!=0)continue;
            var monitors=new List<DisplayDevice>();
            for(uint j=0;;j++)
            {
                var monitor=DisplayDevice.Create();
                if(!EnumDisplayDevicesW(adapter.DeviceName,j,ref monitor,1))break;
                if((monitor.StateFlags&1)!=0)monitors.Add(monitor);
            }
            foreach(var monitor in monitors)
            {
                if(string.IsNullOrWhiteSpace(monitor.DeviceId))continue;
                var screen=Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==adapter.DeviceName);
                var name=$"{adapter.DeviceName.Replace(@"\\.\","")} · {monitor.DeviceString}";
                if(screen is not null)name+=$" · {screen.Bounds.Width}×{screen.Bounds.Height}";
                result.Add(new(monitor.DeviceId,adapter.DeviceName,monitor.DeviceKey,name,(adapter.StateFlags&4)!=0,monitors.Count>1));
            }
        }
        return result;
    }
    private static T WithDc<T>(DisplayInfo display,Func<nint,T> action)
    {
        var dc=CreateDCW("DISPLAY",display.DeviceName,null,0);
        if(dc==0)throw Error("无法打开显示器 "+display.Name);
        try{return action(dc);}finally{DeleteDC(dc);}
    }
    private static string? DefaultProfile(DisplayInfo display,uint scope)
    {
        if(!WcsGetDefaultColorProfileSize(scope,display.ColorDeviceKey,0,4,0,out var bytes))
        {
            var error=Marshal.GetLastWin32Error();
            if(error is 0 or 2 or 3 or 1168 or 2015)return null;
            throw Error("读取 ICC 默认值失败");
        }
        if(bytes<2 || bytes>32768)throw new InvalidDataException("Windows 返回的 ICC 文件名长度无效。");
        var buffer=new StringBuilder((int)(bytes/2+1));
        if(!WcsGetDefaultColorProfile(scope,display.ColorDeviceKey,0,4,0,bytes,buffer))throw Error("读取 ICC 默认值失败");
        var path=buffer.ToString();
        return string.IsNullOrWhiteSpace(path)?null:Path.IsPathRooted(path)?path:Path.Combine(ColorDirectory,path);
    }
    public DisplaySnapshot Capture(DisplayInfo display)
    {
        if(!WcsGetUsePerUserProfiles(display.ColorDeviceKey,MonitorClass,out var perUser))throw Error("读取用户色彩设置失败");
        var user=DefaultProfile(display,1);
        var effective=perUser?user:DefaultProfile(display,0);
        return WithDc(display,dc=>
        {
            var ramp=new ushort[768];
            if(!GetDeviceGammaRamp(dc,ramp))throw Error("驱动不支持读取伽马表");
            if(effective is null)
            {
                uint length=32768;var buffer=new StringBuilder((int)length);
                if(GetICMProfileW(dc,ref length,buffer))effective=buffer.ToString();
            }
            return new DisplaySnapshot(display.Id,effective,user,perUser,ramp);
        });
    }
    public void SetProfile(DisplayInfo display,string path)
    {
        var installed=Path.Combine(ColorDirectory,Path.GetFileName(path));
        if(!File.Exists(installed) || !File.ReadAllBytes(installed).AsSpan().SequenceEqual(File.ReadAllBytes(path)))
            throw new InvalidOperationException("ICC 尚未安装："+Path.GetFileName(path)+"。请先在 Windows 颜色管理中添加，或改用“仅曲线”模式。");
        if(!WcsSetUsePerUserProfiles(display.ColorDeviceKey,MonitorClass,true))throw Error("启用用户色彩设置失败");
        if(!WcsAssociateColorProfileWithDevice(1,installed,display.ColorDeviceKey) && Marshal.GetLastWin32Error()!=183)
            throw Error("关联 ICC 失败");
        if(!WcsSetDefaultColorProfile(1,display.ColorDeviceKey,0,4,0,installed))throw Error("切换默认 ICC 失败");
        var actual=DefaultProfile(display,1);
        if(!string.Equals(Path.GetFileName(actual),Path.GetFileName(installed),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ICC 默认值回读与目标不一致。");
    }
    public void SetGamma(DisplayInfo display,ushort[] gamma)
    {
        if(gamma.Length!=768)throw new ArgumentException("伽马表必须包含 768 个数值。");
        WithDc(display,dc=>
        {
            if(!SetDeviceGammaRamp(dc,gamma))throw Error("驱动拒绝写入伽马表");
            var actual=new ushort[768];
            if(!GetDeviceGammaRamp(dc,actual))throw Error("写入后无法回读伽马表");
            var max=actual.Zip(gamma,(a,b)=>Math.Abs((int)a-b)).Max();
            if(max>512)throw new InvalidOperationException($"伽马表回读不匹配（最大差值 {max}/65535），可能被驱动或游戏覆盖。");
            return true;
        });
    }
    public void Restore(DisplayInfo display,DisplaySnapshot state)
    {
        // Attempt gamma restoration even when restoring the ICC association fails.
        var errors=new List<string>();
        try
        {
            // An inactive per-user list has no active user default: WCS reports null for it.
            // Restore its original system scope instead of trying to clear that hidden list.
            if(state.UsePerUser && !WcsSetDefaultColorProfile(1,display.ColorDeviceKey,0,4,0,state.UserProfile))
            {
                var failure=Error("恢复 ICC 默认值失败");
                // Windows 10 may clear a null user default but still return ERROR_INVALID_PARAMETER.
                // Accept that result only when an independent read proves the desired state exists.
                var actual=DefaultProfile(display,1);
                if(!string.Equals(Path.GetFileName(actual),Path.GetFileName(state.UserProfile),StringComparison.OrdinalIgnoreCase))throw failure;
            }
        }
        catch(Exception ex){errors.Add(ex.Message);}
        try
        {
            // Restoring system scope must still run if restoring the user default failed.
            if(!WcsSetUsePerUserProfiles(display.ColorDeviceKey,MonitorClass,state.UsePerUser))throw Error("恢复用户色彩设置失败");
        }
        catch(Exception ex){errors.Add(ex.Message);}
        try{SetGamma(display,state.Gamma);}catch(Exception ex){errors.Add(ex.Message);}
        if(errors.Count>0)throw new InvalidOperationException(display.Name+"："+string.Join("；",errors));
    }
}
