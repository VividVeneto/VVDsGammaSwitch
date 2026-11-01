using System.Reflection;
using GammaSwitch;
using GammaSwitch.Core;

internal static class Program
{
    private static int total,failures;
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        void Test(string name,Action body)
        {
            total++;
            try{body();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex);}
        }
        Test("Native enumeration and baseline reads on every attached display",()=>
        {
            var backend=new WindowsDisplayBackend();var displays=backend.Enumerate();Assert(displays.Count>0,"No displays detected");
            Assert(displays.Select(d=>d.Id).Distinct().Count()==displays.Count,"Duplicate stable IDs");
            foreach(var display in displays){var state=backend.Capture(display);Assert(state.Gamma.Length==768,"Bad native gamma size");Console.WriteLine($"  {display.Name}: {Path.GetFileName(state.EffectiveProfile)} / perUser={state.UsePerUser}");}
        });
        Test("Real OS hotkey conflict detected and released",()=>
        {
            using var first=new TestWindow();using var second=new TestWindow();
            var a=new WindowsHotkeyRegistrar(first.Handle);var b=new WindowsHotkeyRegistrar(second.Handle);var shortcut=new Hotkey(7,0x79);
            a.Register(1,shortcut);
            try{var rejected=false;try{b.Register(2,shortcut);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"Second window acquired conflicting shortcut");}
            finally{a.Unregister(1);}
            b.Register(2,shortcut);b.Unregister(2);
        });
        Test("Hotkey input captures combinations and Delete clears",()=>
        {
            using var input=new HotkeyBox();
            Key(input,Keys.Control|Keys.Alt|Keys.D3);Assert(input.Shortcut==new Hotkey(3,0x33),"Chord capture incorrect");
            Key(input,Keys.ControlKey);Assert(input.Shortcut==new Hotkey(3,0x33),"Modifier-only changed shortcut");
            Key(input,Keys.Delete);Assert(input.Shortcut is null,"Delete did not clear shortcut");
        });
        Test("Real form editor saves, reopens and clears custom shortcut",()=>
        {
            var temp=Path.Combine(Path.GetTempPath(),"GammaSwitchUi-"+Guid.NewGuid().ToString("N"));
            try
            {
                using var main=new MainForm(temp);main.Show();Application.DoEvents();
                var grid=Controls(main).OfType<DataGridView>().Single(g=>g.Columns.Cast<DataGridViewColumn>().Any(c=>c.HeaderText=="自定义快捷键"));
                grid.CurrentCell=grid.Rows[grid.Rows.Count-1].Cells[0];
                EditShortcut(main,editor=>
                {
                    var input=Controls(editor).OfType<HotkeyBox>().Single();Key(input,Keys.Control|Keys.Alt|Keys.Shift|Keys.F10);
                    Assert(input.Shortcut==new Hotkey(7,0x79),"Editor did not capture shortcut");
                });
                var settings=new JsonStore<AppSettings>(Path.Combine(temp,"settings.json")).Load();
                Assert(settings.Actions.Last().Shortcut==new Hotkey(7,0x79),"Shortcut was not persisted");
                grid.CurrentCell=grid.Rows[grid.Rows.Count-1].Cells[0];
                EditShortcut(main,editor=>
                {
                    var input=Controls(editor).OfType<HotkeyBox>().Single();Assert(input.Shortcut==new Hotkey(7,0x79),"Reopened shortcut missing");Key(input,Keys.Delete);
                });
                Assert(new JsonStore<AppSettings>(Path.Combine(temp,"settings.json")).Load().Actions.Last().Shortcut is null,"Cleared shortcut persisted incorrectly");
                Assert(!File.Exists(Path.Combine(temp,"recovery.json")),"Editing shortcuts unexpectedly touched display state");
                Button(main,"退出程序").PerformClick();Application.DoEvents();
            }
            finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
        });
        Test("Paused hotkeys do not block unrelated settings saves",()=>
        {
            var temp=Path.Combine(Path.GetTempPath(),"GammaSwitchPaused-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new JsonStore<AppSettings>(Path.Combine(temp,"settings.json"));
                store.Save(new(){Actions=[new(){Kind=ActionKind.RestoreAll,Name="restore",Shortcut=new(7,0x79)}]});
                using var main=new MainForm(temp);main.Show();Application.DoEvents();
                var tray=typeof(MainForm).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(f=>f.FieldType==typeof(NotifyIcon)).Select(f=>(NotifyIcon)f.GetValue(main)!).Single();
                tray.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="暂停全局快捷键").PerformClick();
                using var other=new TestWindow();var registrar=new WindowsHotkeyRegistrar(other.Handle);registrar.Register(1,new(7,0x79));
                // Call the existing settings commit path without its UI error dialog, so a regression fails instead of blocking the test runner.
                var candidate=store.Load();candidate.RestoreOnExit=false;
                try{typeof(MainForm).GetMethod("Commit",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(main,[candidate]);}
                finally{registrar.Unregister(1);Button(main,"退出程序").PerformClick();}
                Assert(!store.Load().RestoreOnExit,"Unrelated option was not saved while paused");
            }
            finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
        });
        Test("Malformed settings still expose recovery controls without overwriting files",()=>
        {
            var temp=Path.Combine(Path.GetTempPath(),"GammaSwitchRecoveryUi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try
            {
                var path=Path.Combine(temp,"settings.json");File.WriteAllText(path,"{broken");
                var backend=new WindowsDisplayBackend();var snapshot=backend.Capture(backend.Enumerate()[0]);
                new RecoveryStore(Path.Combine(temp,"recovery.json")).Save(new(){Displays=[snapshot]});
                using var main=new MainForm(temp);
                Assert(Button(main,"恢复全部").Enabled,"Recovery is unavailable");
                Assert(!Button(main,"编辑 / 快捷键").Enabled,"Malformed settings should open a recovery-only UI");
                Assert(File.ReadAllText(path)=="{broken","Malformed settings were overwritten");
            }
            finally{if(Directory.Exists(temp))Directory.Delete(temp,true);}
        });
        if(args.Contains("--same-state"))Test("Native ICC A-to-B write and restore preserves both displays",()=>
        {
            var backend=new WindowsDisplayBackend();
            foreach(var display in backend.Enumerate())
            {
                var before=backend.Capture(display);
                var backupPath=Path.Combine(Environment.CurrentDirectory,"artifacts","native-test-recovery.json");
                var backup=new JsonStore<RecoveryState>(backupPath);backup.Save(new(){Displays=[before]});
                try
                {
                    foreach(var filename in new[]{"P27UEA.icm","sRGB Color Space Profile.icm"})
                    {
                        var path=Path.Combine(WindowsDisplayBackend.ColorDirectory,filename);
                        if(File.Exists(path))backend.SetProfile(display,path);
                    }
                    backend.SetGamma(display,before.Gamma);
                }
                finally{backend.Restore(display,before);}
                var after=backend.Capture(display);
                Assert(before.UsePerUser==after.UsePerUser,"Per-user scope not restored");
                Assert(string.Equals(Path.GetFileName(before.EffectiveProfile),Path.GetFileName(after.EffectiveProfile),StringComparison.OrdinalIgnoreCase),"Effective ICC changed");
                Assert(string.Equals(Path.GetFileName(before.UserProfile),Path.GetFileName(after.UserProfile),StringComparison.OrdinalIgnoreCase),"User ICC changed");
                Assert(before.Gamma.Zip(after.Gamma,(a,b)=>Math.Abs((int)a-b)).Max()<=512,"Gamma not restored");
                backup.Save(new());
            }
        });
        Console.WriteLine($"{total-failures}/{total} native tests passed");return failures==0?0:1;
    }
    private static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
    private static IEnumerable<Control> Controls(Control root)
    {
        foreach(Control control in root.Controls){yield return control;foreach(var child in Controls(control))yield return child;}
    }
    private static Button Button(Control root,string text)=>Controls(root).OfType<Button>().Single(b=>b.Text==text);
    private static void Key(HotkeyBox input,Keys keys)=>typeof(HotkeyBox).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(input,[new KeyEventArgs(keys)]);
    private static void EditShortcut(MainForm main,Action<ActionEditor> edit)
    {
        Exception? error=null;
        using var timer=new System.Windows.Forms.Timer{Interval=100};
        timer.Tick+=(_,_)=>
        {
            timer.Stop();var editor=Application.OpenForms.OfType<ActionEditor>().Single();
            try{edit(editor);Button(editor,"保存并生效").PerformClick();}
            catch(Exception ex){error=ex;editor.DialogResult=DialogResult.Cancel;editor.Close();}
        };
        timer.Start();Button(main,"编辑 / 快捷键").PerformClick();Application.DoEvents();
        if(error is not null)throw error;
    }
    private sealed class TestWindow : NativeWindow,IDisposable
    {
        public TestWindow()=>CreateHandle(new CreateParams{Caption="GammaSwitch Native Test",Parent=(nint)(-3)});
        public void Dispose()=>DestroyHandle();
    }
}
