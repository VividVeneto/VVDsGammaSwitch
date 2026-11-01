using System.ComponentModel;
using System.Runtime.InteropServices;
using GammaSwitch.Core;

namespace GammaSwitch;

internal sealed class WindowsHotkeyRegistrar(nint handle) : IHotkeyRegistrar
{
    public void Register(int id,Hotkey shortcut)
    {
        if(!NativeMethods.RegisterHotKey(handle,id,shortcut.Modifiers|0x4000,shortcut.Key))
            throw new InvalidOperationException(ShortcutText.Format(shortcut)+" 被系统或其他程序占用（"+new Win32Exception(Marshal.GetLastWin32Error()).Message+"）。");
    }
    public void Unregister(int id)=>NativeMethods.UnregisterHotKey(handle,id);
}

internal static class ShortcutText
{
    public static string Format(Hotkey? key)=>key is null?"未绑定":string.Join(" + ",Parts(key));
    public static IReadOnlyList<string> Parts(Hotkey key)
    {
        var parts=new List<string>();
        if((key.Modifiers&2)!=0)parts.Add("Ctrl");
        if((key.Modifiers&1)!=0)parts.Add("Alt");
        if((key.Modifiers&4)!=0)parts.Add("Shift");
        if((key.Modifiers&8)!=0)parts.Add("Win");
        parts.Add(KeyName(key.Key));
        return parts;
    }
    private static string KeyName(uint key)
    {
        if(key is >=0x30 and <=0x39 or >=0x41 and <=0x5A)return ((char)key).ToString();
        if(key is >=0x60 and <=0x69)return "Num "+(key-0x60);
        return (Keys)key switch
        {
            Keys.Multiply=>"Num *",Keys.Add=>"Num +",Keys.Subtract=>"Num -",Keys.Divide=>"Num /",Keys.Decimal=>"Num .",
            Keys.PageUp=>"PageUp",Keys.PageDown=>"PageDown",Keys.Enter=>"Enter",Keys.Back=>"Backspace",Keys.Escape=>"Esc",
            Keys.CapsLock=>"CapsLock",Keys.Space=>"Space",Keys.Insert=>"Ins",Keys.Delete=>"Del",
            Keys.Up=>"↑",Keys.Down=>"↓",Keys.Left=>"←",Keys.Right=>"→",
            Keys.Oemplus=>"=",Keys.OemMinus=>"-",Keys.Oemcomma=>",",Keys.OemPeriod=>".",Keys.OemQuestion=>"/",
            Keys.Oemtilde=>"`",Keys.OemOpenBrackets=>"[",Keys.OemCloseBrackets=>"]",Keys.OemPipe=>"\\",
            Keys.OemSemicolon=>";",Keys.OemQuotes=>"'",
            _=>((Keys)key).ToString()
        };
    }
}

/// <summary>Read-only text box that records the next key chord instead of text.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Hotkey? shortcut;
    public event EventHandler? ShortcutChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey? Shortcut
    {
        get=>shortcut;
        set{shortcut=value;Text=value is null?"":ShortcutText.Format(value);ShortcutChanged?.Invoke(this,EventArgs.Empty);}
    }
    public HotkeyBox()
    {
        ReadOnly=true;ShortcutsEnabled=false;BackColor=Color.White;Cursor=Cursors.Hand;BorderStyle=BorderStyle.FixedSingle;
        AccessibleName="自定义快捷键";PlaceholderText="点击此处，然后直接按下组合键（例如 Ctrl + Alt + 1）";
        GotFocus+=(_,_)=>{BackColor=Ui.AccentSoft;SelectAll();};
        LostFocus+=(_,_)=>BackColor=Color.White;
    }
    protected override bool IsInputKey(Keys keyData)=>keyData!=Keys.Tab || (ModifierKeys!=Keys.None)?true:base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.KeyCode==Keys.Tab && e.Modifiers==Keys.None)return;
        e.SuppressKeyPress=true;e.Handled=true;
        if(e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)return;
        if(e.Modifiers==Keys.None && e.KeyCode==Keys.Escape){FindForm()?.SelectNextControl(this,true,true,true,true);return;}
        if(e.Modifiers==Keys.None && e.KeyCode is Keys.Back or Keys.Delete){Shortcut=null;return;}
        uint modifiers=(e.Control?2U:0)|(e.Alt?1U:0)|(e.Shift?4U:0);
        if((GetKeyState(0x5B)&0x8000)!=0 || (GetKeyState(0x5C)&0x8000)!=0)modifiers|=8;
        Shortcut=new(modifiers,(uint)e.KeyCode);
    }
    [DllImport("user32.dll")]private static extern short GetKeyState(int key);
}
