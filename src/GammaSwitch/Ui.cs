using System.ComponentModel;
using System.Drawing.Drawing2D;
using GammaSwitch.Core;

namespace GammaSwitch;

internal sealed record Choice<T>(T Value,string Label)
{
    public override string ToString()=>Label;
}

internal enum Tone { Neutral, Info, Success, Warning, Error }
internal enum ButtonStyle { Normal, Primary, Danger, Chip }

/// <summary>Shared palette, typography, DPI helpers and small drawing primitives.</summary>
internal static class Ui
{
    public static readonly Color Ink=Color.FromArgb(28,36,44);
    public static readonly Color Muted=Color.FromArgb(100,112,122);
    public static readonly Color Faint=Color.FromArgb(150,160,168);
    public static readonly Color Canvas=Color.FromArgb(242,245,248);
    public static readonly Color Surface=Color.White;
    public static readonly Color Border=Color.FromArgb(221,227,232);
    public static readonly Color Accent=Color.FromArgb(14,108,128);
    public static readonly Color AccentHover=Color.FromArgb(10,92,110);
    public static readonly Color AccentPressed=Color.FromArgb(7,74,90);
    public static readonly Color AccentSoft=Color.FromArgb(225,241,245);
    public static readonly Color Success=Color.FromArgb(22,125,82);
    public static readonly Color SuccessSoft=Color.FromArgb(223,243,233);
    public static readonly Color Warning=Color.FromArgb(166,98,0);
    public static readonly Color WarningSoft=Color.FromArgb(253,241,218);
    public static readonly Color Danger=Color.FromArgb(188,46,46);
    public static readonly Color DangerSoft=Color.FromArgb(252,232,232);
    public static readonly Color NeutralSoft=Color.FromArgb(236,240,243);

    public const string Family="Microsoft YaHei UI";
    public static readonly Font Body=new(Family,9.75f);
    public static readonly Font Strong=new(Family,9.75f,FontStyle.Bold);
    public static readonly Font Small=new(Family,8.75f);
    public static readonly Font SmallBold=new(Family,8.75f,FontStyle.Bold);
    public static readonly Font Section=new(Family,11.5f,FontStyle.Bold);
    public static readonly Font Title=new(Family,17f,FontStyle.Bold);
    public static readonly ToolTip Tips=new(){InitialDelay=350,ReshowDelay=100,AutoPopDelay=15000,ShowAlways=true};

    private static readonly float scale=ReadScale();
    private static float ReadScale()
    {
        try{using var g=Graphics.FromHwnd(IntPtr.Zero);return Math.Max(1f,g.DpiX/96f);}
        catch(Exception){return 1f;}
    }
    public static int S(int value)=>(int)Math.Round(value*scale);
    public static Size S(int width,int height)=>new(S(width),S(height));
    public static Padding P(int all)=>new(S(all));
    public static Padding P(int left,int top,int right,int bottom)=>new(S(left),S(top),S(right),S(bottom));

    public static string Kind(ActionKind kind)=>kind switch
    {ActionKind.Apply=>"应用预设 / 多屏场景",ActionKind.Cycle=>"循环切换单屏预设",ActionKind.RestoreDisplay=>"恢复一台显示器",ActionKind.RestoreAll=>"恢复所有显示器",_=>kind.ToString()};
    public static string KindHint(ActionKind kind)=>kind switch
    {
        ActionKind.Apply=>"把每一行的 ICC 同时应用到对应显示器；多屏场景每台屏幕一行。“ICC + 曲线”遇到不含 VCGT 的文件时，使用首次修改前保存的日常曲线。",
        ActionKind.Cycle=>"每执行一次，按行顺序切换到下一个配置。所有行必须指向同一台显示器，可用“上移 / 下移”调整顺序。",
        ActionKind.RestoreDisplay=>"把一台显示器恢复到本程序首次修改前的 ICC、用户设置开关与伽马表。",
        _=>"恢复本程序修改过的所有屏幕，无需选择目标。"
    };
    public static string Mode(ProfileMode mode)=>mode switch
    {ProfileMode.Combined=>"ICC + 曲线",ProfileMode.GammaOnly=>"仅曲线",ProfileMode.IccOnly=>"仅 ICC",_=>mode.ToString()};
    public static (Color Fore,Color Back) Colors(Tone tone)=>tone switch
    {
        Tone.Success=>(Success,SuccessSoft),Tone.Warning=>(Warning,WarningSoft),Tone.Error=>(Danger,DangerSoft),
        Tone.Info=>(Accent,AccentSoft),_=>(Muted,NeutralSoft)
    };
    public static Color BackOf(Control? control)
    {
        while(control is not null && control.BackColor.A<255)control=control.Parent;
        return control?.BackColor??Canvas;
    }

    public static RoundButton Button(string text,EventHandler click,ButtonStyle style=ButtonStyle.Normal,string? tip=null)
    {
        var button=new RoundButton{Text=text,Style=style};
        button.Click+=click;
        if(tip is not null)Tips.SetToolTip(button,tip);
        return button;
    }

    public static DataGridView Grid()
    {
        var grid=new SmoothGrid
        {
            Dock=DockStyle.Fill,BackgroundColor=Surface,BorderStyle=BorderStyle.None,
            CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,
            AutoGenerateColumns=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,
            AllowUserToResizeRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,
            MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            EnableHeadersVisualStyles=false,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,
            GridColor=Color.FromArgb(235,239,242),Margin=Padding.Empty
        };
        grid.ColumnHeadersHeight=S(34);
        grid.RowTemplate.Height=S(38);
        grid.ColumnHeadersDefaultCellStyle=new(){BackColor=Surface,ForeColor=Muted,Font=SmallBold,SelectionBackColor=Surface,SelectionForeColor=Muted,Padding=P(8,0,8,0),Alignment=DataGridViewContentAlignment.MiddleLeft};
        grid.DefaultCellStyle=new(){BackColor=Surface,ForeColor=Ink,Font=Body,SelectionBackColor=AccentSoft,SelectionForeColor=Ink,Padding=P(8,0,8,0),WrapMode=DataGridViewTriState.False};
        return grid;
    }

    /// <summary>Title + subtitle on the left, action buttons on the right.</summary>
    public static TableLayoutPanel CardHeader(string title,string? subtitle,params Control[] actions)
    {
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,RowCount=1,Margin=P(0,0,0,10)};
        header.ColumnStyles.Add(new(SizeType.Percent,100));header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.RowStyles.Add(new(SizeType.AutoSize));
        var text=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=Padding.Empty,Anchor=AnchorStyles.Left};
        text.Controls.Add(new Label{Text=title,Font=Section,ForeColor=Ink,AutoSize=true,Margin=Padding.Empty});
        if(subtitle is not null)text.Controls.Add(new Label{Text=subtitle,Font=Small,ForeColor=Muted,AutoSize=true,Margin=P(1,3,0,0)});
        header.Controls.Add(text,0,0);
        var bar=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=P(12,0,0,0),Anchor=AnchorStyles.Right};
        foreach(var action in actions)bar.Controls.Add(action);
        if(actions.Length>0)actions[^1].Margin=Padding.Empty;
        header.Controls.Add(bar,1,0);
        return header;
    }

    public static void Error(IWin32Window owner,Exception ex)=>MessageBox.Show(owner,ex.Message,"GammaSwitch",MessageBoxButtons.OK,MessageBoxIcon.Warning);

    public static GraphicsPath Round(RectangleF r,float radius)
    {
        var path=new GraphicsPath();
        radius=Math.Min(radius,Math.Min(r.Width,r.Height)/2f);
        if(radius<=0.5f){path.AddRectangle(r);return path;}
        var d=radius*2;
        path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);
        path.CloseFigure();return path;
    }

    private const TextFormatFlags Centered=TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;

    /// <summary>Draws a rounded status badge whose left edge and vertical center are given; returns its bounds.</summary>
    public static Rectangle DrawPill(Graphics g,string text,Font font,Tone tone,Point leftCenter)
    {
        var (fore,back)=Colors(tone);
        var size=TextRenderer.MeasureText(g,text,font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
        var rect=new Rectangle(leftCenter.X,leftCenter.Y-(size.Height+S(6))/2,size.Width+S(16),size.Height+S(6));
        var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=Round(rect,rect.Height/2f))using(var brush=new SolidBrush(back))g.FillPath(brush,path);
        g.SmoothingMode=mode;
        TextRenderer.DrawText(g,text,font,rect,fore,back,Centered);
        return rect;
    }

    /// <summary>Draws a shortcut as a row of key caps.</summary>
    public static void DrawKeys(Graphics g,Rectangle area,IReadOnlyList<string>? parts,bool active)
    {
        if(parts is null)
        {
            TextRenderer.DrawText(g,"未绑定",Small,area,Faint,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            return;
        }
        var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
        var x=area.Left;var height=S(24);var top=area.Top+(area.Height-height)/2;
        var face=active?Color.FromArgb(249,250,252):Color.FromArgb(244,246,248);
        var edge=active?Color.FromArgb(196,205,213):Color.FromArgb(220,226,231);
        foreach(var part in parts)
        {
            var size=TextRenderer.MeasureText(g,part,SmallBold,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
            var rect=new Rectangle(x,top,Math.Max(size.Width+S(14),S(26)),height);
            if(rect.Right>area.Right)
            {
                TextRenderer.DrawText(g,"…",SmallBold,new Rectangle(x,top,S(16),height),Muted,Centered);
                break;
            }
            using(var path=Round(rect,S(5)))using(var brush=new SolidBrush(edge))g.FillPath(brush,path);
            var inner=new Rectangle(rect.X+1,rect.Y+1,rect.Width-2,rect.Height-S(3));
            using(var path=Round(inner,S(4)))using(var brush=new SolidBrush(face))g.FillPath(brush,path);
            TextRenderer.DrawText(g,part,SmallBold,inner,active?Ink:Muted,face,Centered);
            x=rect.Right+S(5);
        }
        g.SmoothingMode=mode;
    }

    /// <summary>App logo: a rounded teal tile with a white gamma curve.</summary>
    public static Bitmap RenderLogo(int size,bool muted=false)
    {
        var bitmap=new Bitmap(size,size,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        var tile=new RectangleF(0,0,size,size);
        using(var path=Round(tile,size*0.22f))
        using(var brush=new LinearGradientBrush(tile,muted?Color.FromArgb(150,158,166):Color.FromArgb(28,150,170),muted?Color.FromArgb(92,100,108):Color.FromArgb(9,74,100),45f))
            g.FillPath(brush,path);
        var inset=size*0.22f;var span=size-2*inset;
        if(size>=24)
            using(var reference=new Pen(Color.FromArgb(95,255,255,255),Math.Max(1f,size/40f)){DashStyle=DashStyle.Dash})
                g.DrawLine(reference,inset,size-inset,size-inset,inset);
        var points=Enumerable.Range(0,33).Select(i=>
        {
            var t=i/32f;var v=(float)Math.Pow(t,1/2.2);
            return new PointF(inset+t*span,size-inset-v*span);
        }).ToArray();
        using(var pen=new Pen(Color.White,Math.Max(1.6f,size/10f)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
            g.DrawLines(pen,points);
        return bitmap;
    }
    public static Icon CreateIcon(int size,bool muted=false)
    {
        using var bitmap=RenderLogo(size,muted);
        return Icon.FromHandle(bitmap.GetHicon());
    }
    private static Icon? appIcon;
    public static Icon AppIcon=>appIcon??=CreateIcon(32);

    public static ToolStripRenderer MenuRenderer()=>new ToolStripProfessionalRenderer(new MenuColors()){RoundedEdges=false};
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected=>AccentSoft;
        public override Color MenuItemBorder=>AccentSoft;
        public override Color MenuItemSelectedGradientBegin=>AccentSoft;
        public override Color MenuItemSelectedGradientEnd=>AccentSoft;
        public override Color MenuItemPressedGradientBegin=>AccentSoft;
        public override Color MenuItemPressedGradientEnd=>AccentSoft;
        public override Color MenuBorder=>Border;
        public override Color ToolStripDropDownBackground=>Surface;
        public override Color ImageMarginGradientBegin=>Surface;
        public override Color ImageMarginGradientMiddle=>Surface;
        public override Color ImageMarginGradientEnd=>Surface;
        public override Color SeparatorDark=>Border;
        public override Color SeparatorLight=>Surface;
        public override Color CheckBackground=>AccentSoft;
        public override Color CheckSelectedBackground=>AccentSoft;
        public override Color CheckPressedBackground=>AccentSoft;
        public override Color ButtonSelectedBorder=>AccentSoft;
    }
}

/// <summary>DataGridView with double buffering to avoid flicker while repainting custom cells.</summary>
internal sealed class SmoothGrid : DataGridView
{
    public SmoothGrid(){DoubleBuffered=true;}
}

/// <summary>Flat button with rounded corners, hover / pressed states and a keyboard focus ring.</summary>
internal sealed class RoundButton : Button
{
    private bool hover,pressed;
    private ButtonStyle style;
    private Tone chipTone=Tone.Neutral;
    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;
        AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;Font=Ui.Body;Cursor=Cursors.Hand;
        MinimumSize=new(Ui.S(76),Ui.S(34));Margin=Ui.P(0,0,8,0);Padding=Ui.P(14,0,14,0);
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ButtonStyle Style{get=>style;set{style=value;Font=value is ButtonStyle.Primary?Ui.Strong:Ui.Body;Invalidate();}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Tone ChipTone{get=>chipTone;set{chipTone=value;Invalidate();}}
    public override Size GetPreferredSize(Size proposedSize)
    {
        var text=TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        return new(Math.Max(MinimumSize.Width,text.Width+Padding.Horizontal),Math.Max(MinimumSize.Height,text.Height+Ui.S(14)));
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs mevent){if(mevent.Button==MouseButtons.Left){pressed=true;Invalidate();}base.OnMouseDown(mevent);}
    protected override void OnMouseUp(MouseEventArgs mevent){pressed=false;Invalidate();base.OnMouseUp(mevent);}
    protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Invalidate();}
    private (Color Back,Color Fore,Color Border) Palette()
    {
        if(!Enabled)return (Color.FromArgb(240,243,245),Ui.Faint,Color.FromArgb(229,233,236));
        return style switch
        {
            ButtonStyle.Primary=>(pressed?Ui.AccentPressed:hover?Ui.AccentHover:Ui.Accent,Color.White,Color.Empty),
            ButtonStyle.Danger=>(pressed?Color.FromArgb(246,214,214):hover?Ui.DangerSoft:Ui.Surface,Ui.Danger,Color.FromArgb(236,203,203)),
            ButtonStyle.Chip=>ChipPalette(),
            _=>(pressed?Color.FromArgb(226,233,238):hover?Color.FromArgb(240,245,248):Ui.Surface,Ui.Ink,Ui.Border)
        };
    }
    private (Color,Color,Color) ChipPalette()
    {
        var (fore,back)=Ui.Colors(chipTone);
        if(hover)back=ControlPaint.Dark(back,0.02f);
        if(pressed)back=ControlPaint.Dark(back,0.06f);
        return (back,fore,Color.Empty);
    }
    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g=pevent.Graphics;
        g.Clear(Ui.BackOf(Parent));
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var (back,fore,border)=Palette();
        var radius=style==ButtonStyle.Chip?Height/2f:Ui.S(6);
        var rect=new RectangleF(0.5f,0.5f,Width-1.5f,Height-1.5f);
        using(var path=Ui.Round(rect,radius))
        {
            using(var brush=new SolidBrush(back))g.FillPath(brush,path);
            if(border!=Color.Empty)using(var pen=new Pen(border))g.DrawPath(pen,path);
        }
        if(Focused && ShowFocusCues)
        {
            var ring=RectangleF.Inflate(rect,-2f,-2f);
            using var path=Ui.Round(ring,Math.Max(1f,radius-2f));
            using var pen=new Pen(style==ButtonStyle.Primary?Color.FromArgb(200,255,255,255):Color.FromArgb(170,Ui.Accent),1.5f);
            g.DrawPath(pen,path);
        }
        TextRenderer.DrawText(g,Text,Font,ClientRectangle,fore,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
    }
}

/// <summary>White rounded surface used to group related controls.</summary>
internal sealed class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        BackColor=Ui.Surface;Padding=Ui.P(18,14,18,14);Margin=Ui.P(0,0,0,12);Dock=DockStyle.Fill;
    }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor{get;set;}=Ui.Border;
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Ui.BackOf(Parent));g.SmoothingMode=SmoothingMode.AntiAlias;
        using var path=Ui.Round(new RectangleF(0.5f,0.5f,Width-1.5f,Height-1.5f),Ui.S(10));
        using(var brush=new SolidBrush(BackColor))g.FillPath(brush,path);
        using(var pen=new Pen(BorderColor))g.DrawPath(pen,path);
    }
}

/// <summary>Status line with a colored state dot.</summary>
internal sealed class StatusLine : Control
{
    private Tone tone=Tone.Neutral;
    public StatusLine()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        Font=Ui.Body;MinimumSize=new(0,Ui.S(30));
    }
    public void Set(string text,Tone value){Text=text;tone=value;Ui.Tips.SetToolTip(this,text);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;var back=Ui.BackOf(Parent);g.Clear(back);g.SmoothingMode=SmoothingMode.AntiAlias;
        var dot=Ui.S(8);var color=tone==Tone.Neutral?Ui.Faint:Ui.Colors(tone).Fore;
        using(var brush=new SolidBrush(color))g.FillEllipse(brush,0,(Height-dot)/2f,dot,dot);
        var textRect=new Rectangle(dot+Ui.S(8),0,Math.Max(0,Width-dot-Ui.S(8)),Height);
        TextRenderer.DrawText(g,Text,Font,textRect,tone==Tone.Error?Ui.Danger:Ui.Ink,back,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
    }
}

/// <summary>Plots a 3×256 gamma ramp against the linear reference.</summary>
internal sealed class CurveView : Control
{
    private ushort[]? ramp;
    private string caption="伽马曲线",empty="";
    public CurveView()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        BackColor=Ui.Surface;Font=Ui.Small;Margin=Padding.Empty;
    }
    public void SetCurve(ushort[]? values,string title,string emptyText="无曲线数据")
    {
        ramp=values is {Length:768}?values:null;caption=title;empty=emptyText;
        Ui.Tips.SetToolTip(this,ramp is null?title+"\n"+emptyText:title+"\n虚线为线性参考；R / G / B 不同时分别绘制。");
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Ui.BackOf(this));
        var header=Ui.S(22);
        TextRenderer.DrawText(g,caption,Ui.SmallBold,new Rectangle(0,0,Width,header),Ui.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        var side=Math.Min(Width-1,Height-header-Ui.S(4)-1);
        if(side<Ui.S(48))return;
        var plot=new Rectangle((Width-1-side)/2,header+Ui.S(4),side,side);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using(var background=new SolidBrush(Color.FromArgb(249,250,252)))g.FillRectangle(background,plot);
        using(var grid=new Pen(Color.FromArgb(233,237,241)))
            for(var i=1;i<4;i++)
            {
                var x=plot.Left+plot.Width*i/4f;var y=plot.Top+plot.Height*i/4f;
                g.DrawLine(grid,x,plot.Top,x,plot.Bottom);g.DrawLine(grid,plot.Left,y,plot.Right,y);
            }
        using(var reference=new Pen(Color.FromArgb(186,195,203)){DashStyle=DashStyle.Dash})g.DrawLine(reference,plot.Left,plot.Bottom,plot.Right,plot.Top);
        using(var border=new Pen(Ui.Border))g.DrawRectangle(border,plot);
        if(ramp is not {} values)
        {
            var box=Rectangle.Inflate(plot,-Ui.S(12),-Ui.S(12));
            using(var veil=new SolidBrush(Color.FromArgb(225,249,250,252)))g.FillRectangle(veil,box);
            TextRenderer.DrawText(g,empty,Ui.Small,box,Ui.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            return;
        }
        var same=true;
        for(var i=0;i<256 && same;i++)same=Math.Abs(values[i]-values[256+i])<=96 && Math.Abs(values[i]-values[512+i])<=96;
        if(same)Draw(g,plot,values,0,Ui.Accent,Math.Max(2f,Ui.S(2)));
        else
        {
            Draw(g,plot,values,0,Color.FromArgb(215,212,60,60),1.6f);
            Draw(g,plot,values,1,Color.FromArgb(215,34,150,84),1.6f);
            Draw(g,plot,values,2,Color.FromArgb(215,46,98,214),1.6f);
        }
    }
    private static void Draw(Graphics g,Rectangle plot,ushort[] values,int channel,Color color,float width)
    {
        var points=new PointF[256];
        for(var i=0;i<256;i++)points[i]=new(plot.Left+i*plot.Width/255f,plot.Bottom-values[channel*256+i]*plot.Height/65535f);
        using var pen=new Pen(color,width){LineJoin=LineJoin.Round};
        g.DrawLines(pen,points);
    }
}
