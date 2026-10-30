using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BeetleDesktop.Native
{
    public sealed class PetSprite : SpriteWindow
    {
        readonly BeetleVisual model;
        readonly TextBlock label;
        Point desktopPosition;
        int appliedNameColor=-1;
        readonly ScaleTransform heldScale=new ScaleTransform(1,1);
        public PetSprite(bool iridescent,string name)
        {
            Title=name;Width=216;Height=240;
            var grid=new Grid { Background=Brushes.Transparent,Width=216,Height=240,
                HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,
                UseLayoutRounding=false,SnapsToDevicePixels=false };
            model=new BeetleVisual(iridescent) { Width=216,Height=210,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,
                RenderTransform=heldScale,RenderTransformOrigin=new Point(.5,.5) };
            grid.Children.Add(model);
            label=new TextBlock { FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=11,Foreground=new SolidColorBrush(Color.FromRgb(229,238,220)),
                TextAlignment=TextAlignment.Center,Padding=new Thickness(9,3,9,3),MaxWidth=198,TextTrimming=TextTrimming.CharacterEllipsis,
                HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,
                Margin=new Thickness(0,0,0,46),IsHitTestVisible=false };
            grid.Children.Add(label);Content=grid;
        }
        public void PlaceAt(Point center)
        {
            desktopPosition=center;
            // Keep the visual fixed within its window: native movement and WPF
            // render transforms are not presented atomically by the compositor.
            MoveOnDesktop(new Point(center.X-108,center.Y-104));
        }
        public void Present(PetState state,double dt,bool paused=false)
        {
            double playHeight=Math.Max(100,DesktopCoordinates.WorkArea.Height-224);
            model.Present(state.Action,paused?0:state.Speed*playHeight,
                paused&&state.Action!="held"?0:dt,state.Heading,paused?0:state.TurnRate,state.LookYaw,
                state.TravelDistance*playHeight,desktopPosition,state.Overturned);
            double target=state.Action=="held"?1.1:1;
            double scale=heldScale.ScaleX+(target-heldScale.ScaleX)*(1-Math.Exp(-dt*10));
            if(Math.Abs(scale-target)<.0001)scale=target;
            if(heldScale.ScaleX!=scale)heldScale.ScaleX=heldScale.ScaleY=scale;
            label.Text=state.Name;
            label.Visibility=state.ShowName?Visibility.Visible:Visibility.Collapsed;
            if(appliedNameColor!=state.NameColorRgb)
            {
                appliedNameColor=state.NameColorRgb;
                var brush=new SolidColorBrush(Color.FromRgb((byte)(appliedNameColor>>16),(byte)(appliedNameColor>>8),(byte)appliedNameColor));
                brush.Freeze();label.Foreground=brush;
            }
            Title=state.Name;
        }
    }

    public sealed class BowlSprite : SpriteWindow
    {
        readonly BowlDrawing drawing;
        public float Food { get=>drawing.Food;set {if(drawing.Food==value)return;drawing.Food=value;drawing.InvalidateVisual();} }
        public BowlSprite()
        {
            Title="食盆";Width=116;Height=140;drawing=new BowlDrawing();
            Content=drawing;ToolTip="左键点食盆补充果冻，按住左键可拖动食盆";
        }
        sealed class BowlDrawing : FrameworkElement
        {
            public float Food=.65f;
            protected override void OnRender(DrawingContext dc)
            {
                base.OnRender(dc);var center=new Point(58,58);
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(40,0,0,0)),null,new Point(58,62),47,47);
                var rim=new RadialGradientBrush(Color.FromRgb(246,245,218),Color.FromRgb(133,159,132)) { GradientOrigin=new Point(.3,.2) };
                dc.DrawEllipse(rim,new Pen(new SolidColorBrush(Color.FromRgb(238,244,221)),1.5),center,43,43);
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(54,87,69)),new Pen(new SolidColorBrush(Color.FromRgb(109,135,106)),2),center,34,34);
                if(Food>.01)
                {
                    double size=12+17*Math.Sqrt(Food);
                    var jelly=new RadialGradientBrush(Color.FromRgb(255,215,96),Color.FromRgb(217,122,29)){GradientOrigin=new Point(.3,.2)};
                    dc.DrawEllipse(jelly,new Pen(new SolidColorBrush(Color.FromRgb(255,224,132)),1),center,size,size);
                    for(int i=0;i<4;i++)
                    {
                        double a=i*2.4;dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(170,255,242,174)),null,
                            new Point(58+Math.Cos(a)*size*.45,58+Math.Sin(a)*size*.45),3,3);
                    }
                }
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220,22,34,28)),null,new Rect(20,111,76,22),10,10);
                var text=new FormattedText("食盆 "+Math.Round(Food*100)+"%",CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei UI"),11,Brushes.Beige,VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(text,new Point(58-text.Width/2,113));
            }
        }
    }
}
