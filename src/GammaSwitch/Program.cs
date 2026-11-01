using System.Text.Json;
using GammaSwitch.Core;

namespace GammaSwitch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Length==2 && args[0]=="--diagnose")return Diagnose(args[1]);
        var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GammaSwitch");
        if(args.Length==2 && args[0]=="--data-dir")directory=Path.GetFullPath(args[1]);
        else if(args.Length!=0){MessageBox.Show("参数：--diagnose 输出.json 或 --data-dir 配置目录","GammaSwitch");return 2;}
        using var mutex=new Mutex(true,@"Local\GammaSwitch-"+Environment.UserName,out var created);
        if(!created){MessageBox.Show("GammaSwitch 已在运行，请从系统托盘打开设置。","GammaSwitch");return 0;}
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException+=(_,e)=>MessageBox.Show(e.Exception.Message+"\n恢复文件将保留。","GammaSwitch · 错误",MessageBoxButtons.OK,MessageBoxIcon.Error);
        try{Application.Run(new MainForm(directory));return 0;}
        catch(Exception ex){MessageBox.Show("启动失败："+ex.Message+"\n配置文件未被覆盖。\n"+directory,"GammaSwitch",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
    private static int Diagnose(string output)
    {
        var backend=new WindowsDisplayBackend();var records=new List<object>();var failed=false;
        try
        {
            foreach(var display in backend.Enumerate())
            {
                try
                {
                    var state=backend.Capture(display);
                    records.Add(new{Display=display,State=new{state.EffectiveProfile,state.UserProfile,state.UsePerUser,GammaLength=state.Gamma.Length,GammaSamples=new[]{state.Gamma[0],state.Gamma[32],state.Gamma[128],state.Gamma[255]}}});
                }
                catch(Exception ex){failed=true;records.Add(new{Display=display,Error=ex.ToString()});}
            }
            var profiles=new List<object>();
            foreach(var name in new[]{"P27UEA.icm","tarkov.icc","tarkov_brighter.icc"})
            {
                var path=Path.Combine(WindowsDisplayBackend.ColorDirectory,name);
                if(!File.Exists(path))continue;
                try{var profile=IccProfile.Load(path);profiles.Add(new{File=name,HasVcgt=profile.Gamma is not null,Entries=profile.Gamma?.Length??0});}
                catch(Exception ex){failed=true;profiles.Add(new{File=name,Error=ex.Message});}
            }
            var target=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target,JsonSerializer.Serialize(new{ReadOnly=true,Timestamp=DateTimeOffset.Now,Displays=records,Profiles=profiles},JsonStore<AppSettings>.Options));
            return failed?1:0;
        }
        catch(Exception ex){File.WriteAllText(output,ex.ToString());return 1;}
    }
}
