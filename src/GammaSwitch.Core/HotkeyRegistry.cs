namespace GammaSwitch.Core;

public interface IHotkeyRegistrar
{
    void Register(int id,Hotkey shortcut);
    void Unregister(int id);
}

public sealed class HotkeyRegistry(IHotkeyRegistrar registrar) : IDisposable
{
    public IReadOnlyDictionary<int,string> Actions => bindings.ToDictionary(b=>b.Id,b=>b.ActionId);
    private List<Binding> bindings=[];
    private sealed record Binding(int Id,string ActionId,string Name,Hotkey Shortcut);
    public void Replace(IEnumerable<SwitchAction> actions)
    {
        var enabled=actions.Where(a=>a.Enabled && a.Shortcut is not null).ToArray();
        ShortcutValidation.Validate(enabled.Select(a=>a.Shortcut));
        var next=enabled.Select((a,i)=>new Binding(0x4100+i,a.Id,a.Name,a.Shortcut!)).ToList();
        var previous=bindings.ToList();
        foreach(var binding in previous)registrar.Unregister(binding.Id);
        bindings=[];
        try
        {
            foreach(var binding in next)
            {
                try{registrar.Register(binding.Id,binding.Shortcut);}
                catch(Exception ex){throw new InvalidOperationException($"“{binding.Name}”的快捷键注册失败：{ex.Message}",ex);}
                bindings.Add(binding);
            }
        }
        catch(Exception original)
        {
            foreach(var binding in bindings)registrar.Unregister(binding.Id);
            bindings=[];var errors=new List<string>();
            foreach(var binding in previous)
            {
                try{registrar.Register(binding.Id,binding.Shortcut);bindings.Add(binding);}
                catch(Exception ex){errors.Add(binding.Name+"："+ex.Message);}
            }
            throw new InvalidOperationException(original.Message+(errors.Count==0?"\n原有快捷键已恢复。":"\n以下原快捷键也未能恢复："+string.Join("；",errors)),original);
        }
    }
    public void Dispose(){foreach(var binding in bindings)registrar.Unregister(binding.Id);bindings.Clear();}
}
