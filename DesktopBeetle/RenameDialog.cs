using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BeetleDesktop.Native
{
    public sealed class RenameDialog : Window
    {
        public RenameDialog(string currentName,Func<string,bool> rename)
        {
            Title="给甲虫改名";Width=330;SizeToContent=SizeToContent.Height;
            ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
            ShowInTaskbar=false;Topmost=true;Background=new SolidColorBrush(Color.FromRgb(20,35,28));
            Foreground=new SolidColorBrush(Color.FromRgb(230,237,221));FontFamily=new FontFamily("Microsoft YaHei UI");
            var stack=new StackPanel { Margin=new Thickness(20) };Content=stack;
            stack.Children.Add(new TextBlock { Text="给它起个名字",FontSize=18,Margin=new Thickness(0,0,0,12) });
            var input=new TextBox { Text=currentName,FontSize=16,Padding=new Thickness(8),MaxLength=96 };
            stack.Children.Add(input);
            var error=new TextBlock { Text="1–12 个字，按回车保存",FontSize=11,Margin=new Thickness(0,8,0,16) };
            stack.Children.Add(error);
            var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
            var cancel=new Button { Content="取消",IsCancel=true,MinWidth=70,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,8,0) };
            var save=new Button { Content="保存",IsDefault=true,MinWidth=70,Padding=new Thickness(10,6,10,6) };
            cancel.Click+=(s,e)=>DialogResult=false;
            save.Click+=(s,e)=>{
                if(rename(input.Text))DialogResult=true;
                else { error.Text="请输入 1–12 个字，不包含换行或控制字符。";input.Focus(); }
            };
            buttons.Children.Add(cancel);buttons.Children.Add(save);stack.Children.Add(buttons);
            Loaded+=(s,e)=>{input.Focus();input.SelectAll();};
        }
    }
}
