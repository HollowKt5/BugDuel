using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BeetleDesktop.Native
{
    public sealed class NameColorDialog : Window
    {
        public NameColorDialog(string name,int initialColor,Action<int> saveColor)
        {
            Title="名字颜色";Width=360;SizeToContent=SizeToContent.Height;
            ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            ShowInTaskbar=false;Topmost=true;Background=new SolidColorBrush(Color.FromRgb(20,35,28));
            Foreground=Brushes.Beige;FontFamily=new FontFamily("Microsoft YaHei UI");
            var stack=new StackPanel { Margin=new Thickness(20) };Content=stack;
            var preview=new TextBlock { Text=name,FontSize=24,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,4,0,18) };
            stack.Children.Add(preview);
            var palette=new WrapPanel { Margin=new Thickness(0,0,0,14),HorizontalAlignment=HorizontalAlignment.Center };
            stack.Children.Add(palette);
            var sliders=new Slider[3];var values=new TextBlock[3];
            string[] names={"红","绿","蓝"};int selected=initialColor;
            for(int i=0;i<3;i++)
            {
                var row=new Grid { Margin=new Thickness(0,4,0,4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(36) });
                row.Children.Add(new TextBlock { Text=names[i],VerticalAlignment=VerticalAlignment.Center });
                sliders[i]=new Slider { Minimum=0,Maximum=255,Value=(initialColor>>(16-i*8))&255,IsSnapToTickEnabled=true,TickFrequency=1,SmallChange=1,LargeChange=16,Margin=new Thickness(4,0,8,0) };
                System.Windows.Automation.AutomationProperties.SetName(sliders[i],names[i]+"色分量");
                Grid.SetColumn(sliders[i],1);row.Children.Add(sliders[i]);
                values[i]=new TextBlock { TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center };
                Grid.SetColumn(values[i],2);row.Children.Add(values[i]);stack.Children.Add(row);
            }
            void UpdatePreview()
            {
                int r=(int)sliders[0].Value,g=(int)sliders[1].Value,b=(int)sliders[2].Value;
                selected=(r<<16)|(g<<8)|b;preview.Foreground=new SolidColorBrush(Color.FromRgb((byte)r,(byte)g,(byte)b));
                for(int i=0;i<3;i++)values[i].Text=((int)sliders[i].Value).ToString();
            }
            foreach(var slider in sliders)slider.ValueChanged+=(s,e)=>UpdatePreview();
            int[] colors={0xE5EEDC,0xFFFFFF,0x202020,0xFFD166,0xFF87B7,0x74C7FF,0x80E6BA,0xC5A0FF};
            string[] colorNames={"默认浅色","白色","深灰","金黄","粉色","天蓝","薄荷绿","淡紫"};
            for(int i=0;i<colors.Length;i++)
            {
                int rgb=colors[i];
                var button=new Button { Width=28,Height=28,Margin=new Thickness(3),Padding=new Thickness(3),ToolTip=colorNames[i] };
                button.Content=new Border { Width=18,Height=18,Background=new SolidColorBrush(Color.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb)),CornerRadius=new CornerRadius(3) };
                System.Windows.Automation.AutomationProperties.SetName(button,colorNames[i]);
                button.Click+=(s,e)=>{sliders[0].Value=(rgb>>16)&255;sliders[1].Value=(rgb>>8)&255;sliders[2].Value=rgb&255;};
                palette.Children.Add(button);
            }
            var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0) };
            var cancel=new Button { Content="取消",IsCancel=true,MinWidth=70,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,8,0) };
            var save=new Button { Content="保存",IsDefault=true,MinWidth=70,Padding=new Thickness(10,6,10,6) };
            cancel.Click+=(s,e)=>DialogResult=false;
            save.Click+=(s,e)=>{saveColor(selected);DialogResult=true;};
            buttons.Children.Add(cancel);buttons.Children.Add(save);stack.Children.Add(buttons);UpdatePreview();
        }
    }
}
