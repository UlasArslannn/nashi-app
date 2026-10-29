using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace Nashi.Windows;

/// A quiet corner badge instead of a banner: the pear mark fills with gold from
/// the bottom as work progresses, with a small percentage underneath. Details and
/// the Cancel/Show in Explorer action live in a flyout that slides open on hover,
/// so nothing large sits over the screen while media downloads. The window never
/// activates, so clicking it does not raise Nashi.
public sealed class ToastWindow:Window
{
    [DllImport("user32.dll")]private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")]private static extern int SetWindowLong(IntPtr window,int index,int value);
    private const double IconSize=46;
    private readonly TextBlock label=new(){FontSize=13,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock detail=new(){FontSize=12,Margin=new(0,3,0,10),TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock percent=new(){FontSize=11,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,2,0,0)};
    private readonly Button action=new(){Content="Cancel",MinWidth=85,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly System.Windows.Shapes.Rectangle fill;
    private readonly Border flyout,queuedChip;
    private readonly TextBlock queuedText=new(){FontSize=10,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(43,54,28))};
    private readonly Action? cancel;private bool complete,pulsing;
    private static bool Animations=>WheelPresentation.Preferences.Animations&&SystemParameters.ClientAreaAnimation;
    public ToastWindow(Window owner,Action? cancel)
    {
        Theme.Attach(this);this.cancel=cancel;L10n.Content(action);
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=true;SizeToContent=SizeToContent.Height;Width=250;
        var pear=FileGarden.Artwork("NashiMark");
        var dim=new Image{Source=pear,Width=IconSize,Height=IconSize,Opacity=.32};
        fill=new System.Windows.Shapes.Rectangle{Width=IconSize,Height=IconSize,OpacityMask=new ImageBrush(pear){Stretch=Stretch.Uniform},Clip=new RectangleGeometry(new Rect(0,IconSize,IconSize,0))};
        fill.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Gold");
        queuedChip=new Border{CornerRadius=new(9),Padding=new(5,1,5,1),Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Child=queuedText};
        queuedChip.SetResourceReference(Border.BackgroundProperty,"Gold");
        var icon=new Grid{Width=IconSize,Height=IconSize};icon.Children.Add(dim);icon.Children.Add(fill);icon.Children.Add(queuedChip);
        percent.SetResourceReference(TextBlock.ForegroundProperty,"Ink");label.SetResourceReference(TextBlock.ForegroundProperty,"Ink");detail.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
        var badge=new Border{CornerRadius=new(16),BorderThickness=new(1),Padding=new(8,7,8,5),HorizontalAlignment=HorizontalAlignment.Right};
        badge.SetResourceReference(Border.BackgroundProperty,"Surface");badge.SetResourceReference(Border.BorderBrushProperty,"Line");
        var badgeContent=new StackPanel();badgeContent.Children.Add(icon);badgeContent.Children.Add(percent);badge.Child=badgeContent;
        var flyoutContent=new StackPanel();flyoutContent.Children.Add(label);flyoutContent.Children.Add(detail);flyoutContent.Children.Add(action);
        flyout=new Border{CornerRadius=new(14),BorderThickness=new(1),Padding=new(14),Margin=new(0,8,0,0),Width=230,Opacity=0,Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Right,RenderTransform=new TranslateTransform(0,-6),Child=flyoutContent};
        flyout.SetResourceReference(Border.BackgroundProperty,"Surface");flyout.SetResourceReference(Border.BorderBrushProperty,"Line");
        var root=new StackPanel();root.Children.Add(badge);root.Children.Add(flyout);Content=root;
        Loaded+=(_,_)=>{var area=SystemParameters.WorkArea;Left=area.Right-Width-12;Top=area.Top+12;};
        // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: clicks reach the badge without activating any
        // window, so interacting with it never brings Nashi forward over the current app.
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x08000000|0x80);};
        MouseEnter+=(_,_)=>ShowFlyout(true);MouseLeave+=(_,_)=>ShowFlyout(false);
        action.Click+=(_,_)=>{if(!complete){this.cancel?.Invoke();action.IsEnabled=false;}};Update(new(null,"A fresh file is on its way…"));
    }
    private void ShowFlyout(bool open)
    {
        if(open)flyout.Visibility=Visibility.Visible;
        if(Animations)
        {
            var fade=new DoubleAnimation(open?1:0,TimeSpan.FromMilliseconds(160));
            if(!open)fade.Completed+=(_,_)=>{if(flyout.Opacity<.05)flyout.Visibility=Visibility.Collapsed;};
            flyout.BeginAnimation(OpacityProperty,fade);
            ((TranslateTransform)flyout.RenderTransform).BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(open?0:-6,TimeSpan.FromMilliseconds(160)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
        }
        else{flyout.Opacity=open?1:0;flyout.Visibility=open?Visibility.Visible:Visibility.Collapsed;}
    }
    private void StartPulse(){if(pulsing)return;pulsing=true;fill.Clip=null;if(Animations)fill.BeginAnimation(OpacityProperty,new DoubleAnimation(.15,.5,TimeSpan.FromMilliseconds(900)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});else fill.Opacity=.3;}
    private void StopPulse(){if(pulsing){pulsing=false;fill.BeginAnimation(OpacityProperty,null);}fill.Opacity=1;}
    /// Shows "+N" on the badge while N further downloads wait in the queue.
    public void SetQueued(int count){queuedText.Text="+"+count;queuedChip.Visibility=count>0?Visibility.Visible:Visibility.Collapsed;}
    public void Update(Nashi.Core.OperationProgress progress)
    {
        L10n.SetText(label,progress.Label);
        if(progress.Fraction is {} value)
        {
            var fraction=Math.Clamp(value,0,1);StopPulse();
            fill.Clip=new RectangleGeometry(new Rect(0,IconSize*(1-fraction),IconSize,IconSize*fraction));
            percent.Text=fraction.ToString("P0");L10n.SetText(detail,$"{fraction:P0}");
        }
        else{StartPulse();percent.Text="…";L10n.SetText(detail,"Working…");}
    }
    public void Complete(IReadOnlyList<string> outputs)
    {
        complete=true;StopPulse();fill.Clip=null;percent.Text="✓";
        L10n.SetText(label,outputs.Count==0?"All set. You’re ready to go.":$"Nicely done. {outputs.Count} {(outputs.Count==1?"file is":"files are")} ready.");detail.Text=outputs.Count>0?Path.GetFileName(outputs[0]):"Ready.";L10n.Bind(action,ContentControl.ContentProperty,"Show in Explorer");action.IsEnabled=true;
        // Replace the cancel handler by disabling its callback after completion.
        action.Click+=(_,_)=>{if(outputs.Count>0&&(File.Exists(outputs[0])||Directory.Exists(outputs[0])))MainWindow.Reveal(outputs[0]);};
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
        timer.Tick+=(_,_)=>{if(IsMouseOver)return;timer.Stop();if(Animations){var fade=new DoubleAnimation(0,TimeSpan.FromMilliseconds(280));fade.Completed+=(_,_)=>Close();BeginAnimation(OpacityProperty,fade);}else Close();};timer.Start();
    }
}
