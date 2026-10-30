using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BeetleDesktop.Native
{
    public sealed class CarePanel : SpriteWindow
    {
        readonly PetDesktop desktop;
        readonly Grid root=new Grid { Background=Brushes.Transparent };
        Border expanded,collapsed;
        TextBlock count,food,notice;
        readonly StackPanel petRows=new StackPanel();
        readonly TextBlock[] petNames=new TextBlock[2],petNeeds=new TextBlock[2];
        readonly Border[] petCards=new Border[2];
        readonly Button[] nameVisibility=new Button[2];
        readonly Border[] nameColorPreview=new Border[2];
        readonly int[] displayedNameColors={-1,-1};
        Button add,remove,fight,pause;
        bool open;
        static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(230,237,221));
        static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(159,180,160));
        static readonly Brush Surface=new SolidColorBrush(Color.FromArgb(246,20,35,28));
        public CarePanel(PetDesktop desktop)
        {
            this.desktop=desktop;Title="饲育面板";Content=root;
            var small=MakeButton("☰",()=>SetExpanded(true));small.FontSize=18;small.ToolTip="打开饲育面板";
            System.Windows.Automation.AutomationProperties.SetName(small,"打开饲育面板");
            collapsed=new Border { Background=Surface,CornerRadius=new CornerRadius(12),Padding=new Thickness(4),Child=small };
            root.Children.Add(collapsed);
            var stack=new StackPanel { Margin=new Thickness(16,12,16,12) };
            expanded=new Border { Background=Surface,CornerRadius=new CornerRadius(14),Child=stack };
            root.Children.Add(expanded);
            var header=new DockPanel();
            var fold=MakeButton("—",()=>SetExpanded(false));fold.Width=36;fold.Height=28;DockPanel.SetDock(fold,Dock.Right);header.Children.Add(fold);
            count=Label("",14,Ink);count.VerticalAlignment=VerticalAlignment.Center;header.Children.Add(count);
            header.Margin=new Thickness(0,0,0,8);stack.Children.Add(header);
            for(int i=0;i<2;i++)
            {
                int index=i;
                var row=new DockPanel { Margin=new Thickness(8,5,5,5) };
                var rename=MakeButton("改名",()=>Rename(index));rename.Width=44;rename.Height=28;
                rename.ToolTip="给这只甲虫起个名字";DockPanel.SetDock(rename,Dock.Right);row.Children.Add(rename);
                var details=new StackPanel();
                petNames[i]=Label("",12,Ink);petNames[i].TextTrimming=TextTrimming.CharacterEllipsis;
                petNeeds[i]=Label("",10,Muted);details.Children.Add(petNames[i]);details.Children.Add(petNeeds[i]);row.Children.Add(details);
                var card=new StackPanel();card.Children.Add(row);
                var settings=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(5,0,5,6) };
                nameVisibility[i]=MakeButton("名字：开",()=>desktop.TogglePetName(index));
                nameVisibility[i].Width=92;nameVisibility[i].Height=26;nameVisibility[i].ToolTip="开启或关闭这只甲虫的桌面名字";
                settings.Children.Add(nameVisibility[i]);
                var color=MakeButton("",()=>PickNameColor(index));color.Width=118;color.Height=26;
                var colorContent=new StackPanel { Orientation=Orientation.Horizontal };
                nameColorPreview[i]=new Border { Width=12,Height=12,CornerRadius=new CornerRadius(3),Margin=new Thickness(0,0,7,0),VerticalAlignment=VerticalAlignment.Center };
                colorContent.Children.Add(nameColorPreview[i]);colorContent.Children.Add(Label("名字颜色…",11,Ink));color.Content=colorContent;
                System.Windows.Automation.AutomationProperties.SetName(color,"设置名字颜色");
                settings.Children.Add(color);card.Children.Add(settings);
                petCards[i]=new Border { Background=new SolidColorBrush(Color.FromRgb(29,47,36)),CornerRadius=new CornerRadius(7),
                    Margin=new Thickness(0,2,0,3),Child=card };
                petRows.Children.Add(petCards[i]);
            }
            stack.Children.Add(petRows);
            food=Label("",11,Muted);food.Margin=new Thickness(0,0,0,9);stack.Children.Add(food);
            var first=Row();first.Children.Add(MakeButton("加一份果冻",desktop.Feed));
            fight=MakeButton("友好切磋",desktop.Fight);first.Children.Add(fight);stack.Children.Add(first);
            var second=Row();add=MakeButton("添一只",desktop.AddPet);remove=MakeButton("送回一只",desktop.RemovePet);pause=MakeButton("休息",desktop.Pause);
            second.Children.Add(add);second.Children.Add(remove);second.Children.Add(pause);stack.Children.Add(second);
            notice=Label("",11,Muted);notice.TextWrapping=TextWrapping.Wrap;notice.Height=36;notice.Margin=new Thickness(0,7,0,0);stack.Children.Add(notice);
            var footer=new DockPanel();var quit=MakeButton("退出",desktop.Quit);quit.Width=45;quit.Height=26;DockPanel.SetDock(quit,Dock.Right);footer.Children.Add(quit);
            var hint=Label("右键抓起＋滚轮转向\n翻倒时左键扶正",10,Muted);hint.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(hint);stack.Children.Add(footer);
            SetExpanded(false);
        }
        public void SetExpanded(bool value)
        {
            open=value;Width=open?308:44;Height=open?372:40;
            expanded.Visibility=open?Visibility.Visible:Visibility.Collapsed;
            collapsed.Visibility=open?Visibility.Collapsed:Visibility.Visible;
            var work=DesktopCoordinates.WorkArea;Left=work.Right-Width-18;Top=work.Bottom-Height-18;
        }
        public void Refresh(PetSimulation sim,string message)
        {
            count.Text=$"正在养 {sim.Pets.Count} / 2 只";
            for(int i=0;i<2;i++)
            {
                petCards[i].Visibility=i<sim.Pets.Count?Visibility.Visible:Visibility.Collapsed;
                if(i>=sim.Pets.Count)continue;
                var p=sim.Pets[i];petNames[i].Text=p.Name+(p.Iridescent?" · 彩壳":"");
                petNames[i].ToolTip=petNames[i].Text;
                nameVisibility[i].Content=p.ShowName?"名字：开":"名字：关";
                if(displayedNameColors[i]!=p.NameColorRgb)
                {
                    displayedNameColors[i]=p.NameColorRgb;
                    nameColorPreview[i].Background=new SolidColorBrush(Color.FromRgb((byte)(p.NameColorRgb>>16),(byte)(p.NameColorRgb>>8),(byte)p.NameColorRgb));
                }
                petNeeds[i].Text=p.Overturned?"翻倒了 · 左键点甲虫扶正":$"饱腹 {Math.Round((1-p.Hunger)*100)}%    精力 {Math.Round(p.Energy*100)}%";
            }
            food.Text=$"食盆余量 {Math.Round(sim.Food*100)}%  ·  {(sim.FightInProgress?"正在切磋":sim.Paused?"休息中":"自在活动")}";
            add.IsEnabled=sim.Pets.Count<2;remove.IsEnabled=sim.Pets.Count>1;
            fight.IsEnabled=sim.Pets.Count==2&&!sim.Paused&&!sim.FightInProgress&&sim.FightCooldownRemaining<=0;
            foreach(var p in sim.Pets)if(p.Overturned||p.Action=="held"||p.StillRemaining>0f)fight.IsEnabled=false;
            pause.Content=sim.Paused?"继续":"休息";notice.Text=message;
        }
        void Rename(int index)
        {
            if(index<0||index>=desktop.Simulation.Pets.Count)return;
            // Only this explicitly opened dialog activates, so Chinese IME and keyboard editing work.
            var dialog=new RenameDialog(desktop.Simulation.Pets[index].Name,name=>desktop.RenamePet(index,name));
            dialog.Owner=this;dialog.ShowDialog();
        }
        void PickNameColor(int index)
        {
            if(index<0||index>=desktop.Simulation.Pets.Count)return;
            var pet=desktop.Simulation.Pets[index];
            var dialog=new NameColorDialog(pet.Name,pet.NameColorRgb,rgb=>desktop.SetPetNameColor(index,rgb));
            dialog.Owner=this;dialog.ShowDialog();
        }
        static System.Windows.Controls.Primitives.UniformGrid Row()=>new System.Windows.Controls.Primitives.UniformGrid { Rows=1,Margin=new Thickness(-3,0,-3,4) };
        static TextBlock Label(string text,double size,Brush brush)=>new TextBlock {Text=text,FontSize=size,Foreground=brush,FontFamily=new FontFamily("Microsoft YaHei UI")};
        static Button MakeButton(string title,Action action)
        {
            var button=new Button { Content=title,Height=32,Margin=new Thickness(3,0,3,0),Padding=new Thickness(5,2,5,2),
                Background=new SolidColorBrush(Color.FromRgb(43,67,52)),Foreground=Ink,FontSize=11,FontFamily=new FontFamily("Microsoft YaHei UI"),Focusable=false,BorderThickness=new Thickness(0) };
            var template=new ControlTemplate(typeof(Button));
            var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));
            border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));
            var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;
            button.Template=template;
            button.IsEnabledChanged+=(s,e)=>button.Opacity=button.IsEnabled?1:.38;
            button.MouseEnter+=(s,e)=>{if(button.IsEnabled)button.Background=new SolidColorBrush(Color.FromRgb(62,90,65));};
            button.MouseLeave+=(s,e)=>button.Background=new SolidColorBrush(Color.FromRgb(43,67,52));
            button.Click+=(s,e)=>action();return button;
        }
    }
}
