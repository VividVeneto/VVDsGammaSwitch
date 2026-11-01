using System.Buffers.Binary;
using System.Text;
using GammaSwitch.Core;

var failures = 0;
var total = 0;
void Test(string name, Action body)
{
    total++;
    try { body(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
void Throws<T>(Action body) where T : Exception
{
    try { body(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
byte[] Fixture(int channels, int entries, int width, ushort[] values)
{
    var bytes = new byte[144 + 18 + channels * entries * width];
    void U32(int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(o, 4), v);
    void U16(int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(o, 2), v);
    U32(0, (uint)bytes.Length); Encoding.ASCII.GetBytes("acsp").CopyTo(bytes, 36);
    U32(128, 1); Encoding.ASCII.GetBytes("vcgt").CopyTo(bytes, 132);
    U32(136, 144); U32(140, (uint)(bytes.Length-144)); Encoding.ASCII.GetBytes("vcgt").CopyTo(bytes, 144);
    U16(156, (ushort)channels); U16(158, (ushort)entries); U16(160, (ushort)width);
    for (var i=0; i<values.Length; i++) { if (width==1) bytes[162+i]=(byte)values[i]; else U16(162+i*2,values[i]); }
    return bytes;
}

Test("16-bit tables retain independent RGB channels", () => {
    var result=IccProfile.Read(Fixture(3,2,2,[0,65535,100,60000,200,50000])).Gamma!;
    Equal(768,result.Length); Equal((ushort)0,result[0]); Equal((ushort)65535,result[255]);
    Equal((ushort)100,result[256]); Equal((ushort)60000,result[511]); Equal((ushort)200,result[512]); Equal((ushort)50000,result[767]);
});
Test("8-bit single channel expands to full RGB with interpolation", () => {
    var ramp=IccProfile.Read(Fixture(1,2,1,[0,255])).Gamma!;
    Equal((ushort)32896,ramp[128]); Equal((ushort)32896,ramp[384]); Equal((ushort)65535,ramp[767]);
});
Test("Valid ICC without VCGT does not invent a ramp", () => {
    var bytes=new byte[132]; BinaryPrimitives.WriteUInt32BigEndian(bytes,(uint)bytes.Length);
    Encoding.ASCII.GetBytes("acsp").CopyTo(bytes,36); Equal<ushort[]?>(null,IccProfile.Read(bytes).Gamma);
});
Test("Malformed tag offset rejected before allocation", () => {
    var bytes=Fixture(1,2,1,[0,255]); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(136),uint.MaxValue);
    Throws<InvalidDataException>(()=>IccProfile.Read(bytes));
});
Test("Truncated curve rejected", () => Throws<InvalidDataException>(()=>IccProfile.Read(Fixture(3,2,2,[0,65535,0,65535,0,65535])[..^1])));
Test("Unsupported formula curve rejected clearly", () => {
    var bytes=Fixture(1,2,1,[0,255]); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(152),1);
    Throws<NotSupportedException>(()=>IccProfile.Read(bytes));
});
Test("Nonmonotonic curve rejected", () => Throws<InvalidDataException>(()=>IccProfile.Read(Fixture(1,3,2,[0,65535,100]))));
Test("Duplicate shortcuts rejected", () => Throws<ArgumentException>(()=>ShortcutValidation.Validate([new(3,0x31),new(3,0x31)])));
Test("Plain letters cannot become global typing traps", () => Throws<ArgumentException>(()=>ShortcutValidation.Validate([new(0,0x41)])));
Test("Function key and unbound shortcut accepted", () => ShortcutValidation.Validate([new(0,0x77),null,new(3,0x31)]));

var temp=Path.Combine(Path.GetTempPath(),"GammaSwitchTests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
var gamePath=Path.Combine(temp,"game.icc");
File.WriteAllBytes(gamePath,Fixture(1,2,2,[0,50000]));
var plainPath=Path.Combine(temp,"plain.icm");
var plain=new byte[132]; BinaryPrimitives.WriteUInt32BigEndian(plain,(uint)plain.Length); Encoding.ASCII.GetBytes("acsp").CopyTo(plain,36);
File.WriteAllBytes(plainPath,plain);
ProfileTarget Target(string id,string? file=null,ProfileMode mode=ProfileMode.Combined)=>new(){DisplayId=id,ProfilePath=file??gamePath,Mode=mode};
try
{
    Test("Settings round trip preserves disabled and unbound actions",()=>{
        var store=new JsonStore<AppSettings>(Path.Combine(temp,"settings.json"));
        store.Save(new(){Actions=[new(){Name="夜图",Enabled=false,Shortcut=null,Targets=[Target("A")]}]});
        var read=store.Load(); Equal("夜图",read.Actions[0].Name); Equal(false,read.Actions[0].Enabled); Equal<Hotkey?>(null,read.Actions[0].Shortcut);
    });
    Test("Corrupt settings are not silently reset",()=>{
        var path=Path.Combine(temp,"corrupt.json");File.WriteAllText(path,"{broken");
        Throws<System.Text.Json.JsonException>(()=>new JsonStore<AppSettings>(path).Load());
        Equal("{broken",File.ReadAllText(path));
    });
    Test("Recovery journal persists before first display mutation",()=>{
        var backend=new FakeBackend();var journal=new MemoryJournal();
        backend.BeforeMutation=()=>{if(journal.State.Displays.Count!=1)throw new Exception("Baseline not saved before mutation");};
        new SwitchService(backend,journal).Apply([Target("A")]); Equal((ushort)50000,backend.States["A"].Gamma[255]);
    });
    Test("Missing screen aborts entire scene before changing any display",()=>{
        var backend=new FakeBackend();var service=new SwitchService(backend,new MemoryJournal());
        Throws<InvalidOperationException>(()=>service.Apply([Target("A"),Target("MISSING")])); Equal(0,backend.Mutations);
    });
    Test("Failure on second screen rolls back both screens",()=>{
        var backend=new FakeBackend(){FailNextGammaFor="B"}; var service=new SwitchService(backend,new MemoryJournal());
        Throws<InvalidOperationException>(()=>service.Apply([Target("A"),Target("B")]));
        Equal("original-A.icm",backend.States["A"].EffectiveProfile); Equal("original-B.icm",backend.States["B"].EffectiveProfile);
        Equal((ushort)65535,backend.States["A"].Gamma[255]); Equal((ushort)65535,backend.States["B"].Gamma[255]);
    });
    Test("ICC without VCGT restores saved baseline rather than current game ramp",()=>{
        var backend=new FakeBackend();var service=new SwitchService(backend,new MemoryJournal());
        service.Apply([Target("A")]); service.Apply([Target("A",plainPath)]);
        Equal((ushort)65535,backend.States["A"].Gamma[255]);Equal((ushort)32896,backend.States["A"].Gamma[128]);
    });
    Test("Gamma-only mode never changes default ICC",()=>{
        var backend=new FakeBackend();new SwitchService(backend,new MemoryJournal()).Apply([Target("A",gamePath,ProfileMode.GammaOnly)]);
        Equal("original-A.icm",backend.States["A"].EffectiveProfile);Equal((ushort)50000,backend.States["A"].Gamma[255]);
    });
    Test("Gamma-only profile without a curve aborts before mutations",()=>{
        var backend=new FakeBackend();var service=new SwitchService(backend,new MemoryJournal());
        Throws<InvalidOperationException>(()=>service.Apply([Target("A",plainPath,ProfileMode.GammaOnly)]));Equal(0,backend.Mutations);
    });
    Test("Disk failure prevents all display writes",()=>{
        var backend=new FakeBackend();var journal=new MemoryJournal(){FailSave=true};
        Throws<IOException>(()=>new SwitchService(backend,journal).Apply([Target("A")])); Equal(0,backend.Mutations);
    });
    Test("Failed restore keeps recovery entry for retry",()=>{
        var backend=new FakeBackend();var journal=new MemoryJournal();var service=new SwitchService(backend,journal);
        service.Apply([Target("A")]);backend.FailRestore=true;
        Throws<InvalidOperationException>(()=>service.RestoreAll());Equal(1,journal.State.Displays.Count);
        backend.FailRestore=false;service.RestoreAll();Equal(0,journal.State.Displays.Count);Equal(false,service.HasRecovery);
    });
    Test("Restart reuses persisted original baseline",()=>{
        var backend=new FakeBackend();var journal=new MemoryJournal();new SwitchService(backend,journal).Apply([Target("A")]);
        new SwitchService(backend,journal).RestoreAll(); Equal("original-A.icm",backend.States["A"].EffectiveProfile);Equal((ushort)65535,backend.States["A"].Gamma[255]);
    });
    Test("Cycle advances only after successful application",()=>{
        var backend=new FakeBackend();var service=new SwitchService(backend,new MemoryJournal());
        var action=new SwitchAction(){Kind=ActionKind.Cycle,Targets=[Target("A"),Target("A",plainPath)]};
        service.Execute(action);Equal((ushort)50000,backend.States["A"].Gamma[255]);
        service.Execute(action);Equal((ushort)65535,backend.States["A"].Gamma[255]);
    });
    Test("Duplicate display in scene rejected before writing",()=>{
        var backend=new FakeBackend();Throws<InvalidOperationException>(()=>new SwitchService(backend,new MemoryJournal()).Apply([Target("A"),Target("A")]));Equal(0,backend.Mutations);
    });
}
finally {Directory.Delete(temp,true);}

Test("Failed OS shortcut registration restores previous shortcuts",()=>{
    var os=new FakeRegistrar(); using var registry=new HotkeyRegistry(os);
    registry.Replace([new(){Id="old",Name="old",Shortcut=new(3,0x31)}]);
    os.BlockedKey=0x32;
    Throws<InvalidOperationException>(()=>registry.Replace([new(){Id="new",Name="new",Shortcut=new(3,0x32)}]));
    Equal(1,os.Keys.Count); Equal(new Hotkey(3,0x31),os.Keys.Values.Single());Equal("old",registry.Actions.Values.Single());
});
Test("Duplicate shortcut edit leaves current OS registrations untouched",()=>{
    var os=new FakeRegistrar();using var registry=new HotkeyRegistry(os);
    registry.Replace([new(){Id="old",Shortcut=new(3,0x31)}]);var calls=os.UnregisterCalls;
    Throws<ArgumentException>(()=>registry.Replace([new(){Shortcut=new(3,0x32)},new(){Shortcut=new(3,0x32)}]));
    Equal(calls,os.UnregisterCalls);Equal(new Hotkey(3,0x31),os.Keys.Values.Single());
});
Test("Disabled and cleared shortcuts are not registered",()=>{
    var os=new FakeRegistrar();using var registry=new HotkeyRegistry(os);
    registry.Replace([new(){Enabled=false,Shortcut=new(3,0x31)},new(){Shortcut=null}]);Equal(0,os.Keys.Count);
});
Console.WriteLine($"{total-failures}/{total} passed");
return failures==0 ? 0 : 1;

sealed class MemoryJournal : IRecoveryStore
{
    public RecoveryState State=new();public bool FailSave;
    public RecoveryState Load()=>JsonStore<RecoveryState>.Copy(State);
    public void Save(RecoveryState state){if(FailSave)throw new IOException("disk full");State=JsonStore<RecoveryState>.Copy(state);}
}
sealed class FakeBackend : IDisplayBackend
{
    public Dictionary<string,DisplaySnapshot> States=new(){["A"]=State("A"),["B"]=State("B")};
    public Action? BeforeMutation; public int Mutations;public string? FailNextGammaFor;public bool FailRestore;
    static DisplaySnapshot State(string id)=>new(id,"original-"+id+".icm",null,false,Enumerable.Range(0,768).Select(i=>(ushort)((i%256)*257)).ToArray());
    public IReadOnlyList<DisplayInfo> Enumerate()=>States.Keys.Select(id=>new DisplayInfo(id,id,id,id,id=="A",false)).ToArray();
    public DisplaySnapshot Capture(DisplayInfo display)=>JsonStore<RecoveryState>.Copy(new(){Displays=[States[display.Id]]}).Displays[0];
    public void SetProfile(DisplayInfo display,string path){BeforeMutation?.Invoke();Mutations++;States[display.Id]=States[display.Id] with{EffectiveProfile=path,UserProfile=path,UsePerUser=true};}
    public void SetGamma(DisplayInfo display,ushort[] gamma){BeforeMutation?.Invoke();Mutations++;if(FailNextGammaFor==display.Id){FailNextGammaFor=null;throw new IOException("driver failure");}States[display.Id]=States[display.Id] with{Gamma=gamma.ToArray()};}
    public void Restore(DisplayInfo display,DisplaySnapshot state){Mutations++;if(FailRestore)throw new IOException("restore driver failure");States[display.Id]=state;}
}
sealed class FakeRegistrar : IHotkeyRegistrar
{
    public Dictionary<int,Hotkey> Keys=[];public uint BlockedKey;public int UnregisterCalls;
    public void Register(int id,Hotkey shortcut){if(shortcut.Key==BlockedKey)throw new InvalidOperationException("occupied");Keys.Add(id,shortcut);}
    public void Unregister(int id){UnregisterCalls++;Keys.Remove(id);}
}
