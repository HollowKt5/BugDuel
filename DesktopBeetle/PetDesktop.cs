using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Vector2 = System.Numerics.Vector2;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BeetleDesktop.Native
{
    public sealed class PetDesktop : IDisposable
    {
        public PetSimulation Simulation { get; }
        readonly List<PetSprite> pets=new List<PetSprite>();
        readonly bool qa;
        readonly Stopwatch watch=new Stopwatch();
        readonly HeldWheelInput heldWheel=new HeldWheelInput();
        TimeSpan lastRenderingTime=TimeSpan.MinValue;
        BowlSprite bowl;
        CarePanel panel;
        Rect area;
        PetSprite candidate;
        float holdTime;
        Vector petOffset,bowlOffset;
        bool bowlPressed,bowlDragged;
        Point bowlPressAt;
        double saveTime,lastFrame,panelRefreshTime;
        string notice="点食盆加餐 · 抓起后滚轮转向";
        double noticeUntil;

        public PetDesktop(bool qaMode) { qa=qaMode;Simulation=qa?new PetSimulation():PetPersistence.Load(); }
        public void Start()
        {
            area=DesktopCoordinates.WorkArea;
            ConfigurePlacementBounds();
            bowl=new BowlSprite();
            bowl.MouseLeftButtonDown+=(s,e)=>{
                e.Handled=true;BeginBowl(DesktopCoordinates.PointerPosition(bowl));
            };
            bowl.Show();
            panel=new CarePanel(this);panel.Show();
            SyncPets();watch.Start();
            Advance(.001,DesktopCoordinates.PointerPosition(bowl),false,false);
            lastFrame=watch.Elapsed.TotalSeconds;
            CompositionTarget.Rendering+=RenderFrame;
        }
        void RenderFrame(object sender,EventArgs e)
        {
            // WPF can raise Rendering more than once for the same composition.
            // Advance the body and its planted feet together, once per frame.
            var rendering=(RenderingEventArgs)e;
            if(rendering.RenderingTime==lastRenderingTime)return;
            lastRenderingTime=rendering.RenderingTime;
            double now=watch.Elapsed.TotalSeconds;
            double dt=Math.Min(.08,now-lastFrame);lastFrame=now;
            if(dt<=0)return;
            Advance(dt,DesktopCoordinates.PointerPosition(bowl),DesktopCoordinates.LeftHeld,DesktopCoordinates.RightHeld);
        }
        Point ScreenPoint(Vector2 normalized)
        {
            return new Point(area.Left+112+normalized.X*Math.Max(100,area.Width-224),
                area.Bottom-112-normalized.Y*Math.Max(100,area.Height-224));
        }
        Vector2 Normalize(Point point) => new Vector2(
            (float)((point.X-area.Left-112)/Math.Max(100,area.Width-224)),
            (float)((area.Bottom-112-point.Y)/Math.Max(100,area.Height-224)));
        void ConfigurePlacementBounds()
        {
            var screen=DesktopCoordinates.ScreenArea;
            const double overshoot=24;
            Simulation.SetPlacementBounds(
                Normalize(new Point(screen.Left-overshoot,screen.Bottom+overshoot)),
                Normalize(new Point(screen.Right+overshoot,screen.Top-overshoot)));
        }
        void SyncPets()
        {
            while(pets.Count<Simulation.Pets.Count)
            {
                var state=Simulation.Pets[pets.Count];
                var pet=new PetSprite(state.Iridescent,state.Name);
                pet.MouseRightButtonDown+=(s,e)=> { e.Handled=true;BeginHold(pet,DesktopCoordinates.PointerPosition(pet)); };
                pet.MouseLeftButtonDown+=(s,e)=> { e.Handled=true;HelpRightPet(pets.IndexOf(pet)); };
                pets.Add(pet);pet.Show();
            }
            while(pets.Count>Simulation.Pets.Count)
            { var pet=pets[pets.Count-1];pet.Close();pets.RemoveAt(pets.Count-1); }
        }
        void BeginHold(PetSprite pet,Point pointer)
        {
            heldWheel.SetActive(false);
            candidate=pet;holdTime=0;
            petOffset=ScreenPoint(Simulation.Pets[pets.IndexOf(pet)].Position)-pointer;
        }
        void BeginBowl(Point pointer)
        {
            bowlPressed=true;bowlDragged=false;bowlPressAt=pointer;bowlOffset=ScreenPoint(Simulation.BowlPosition)-pointer;
        }
        void Advance(double dt,Point pointer,bool leftHeld,bool rightHeld)
        {
            area=DesktopCoordinates.WorkArea;
            ConfigurePlacementBounds();
            if(candidate!=null)
            {
                int index=pets.IndexOf(candidate);
                if(rightHeld)
                {
                    holdTime+=(float)dt;
                    if(holdTime>=.35f)Simulation.Hold(index,Normalize(pointer+petOffset));
                }
                else
                {
                    heldWheel.SetActive(false);Simulation.Release(index);candidate=null;holdTime=0;
                }
            }
            bool isHeld=candidate!=null && rightHeld && Simulation.Pets[pets.IndexOf(candidate)].Action=="held";
            heldWheel.SetActive(isHeld);
            RotateHeld(heldWheel.ConsumeDelta());
            if(bowlPressed)
            {
                if(leftHeld)
                {
                    if((pointer-bowlPressAt).Length>5)bowlDragged=true;
                    if(bowlDragged)Simulation.MoveBowl(Normalize(pointer+bowlOffset));
                }
                else { if(!bowlDragged)Feed();bowlPressed=false;bowlDragged=false; }
            }
            Simulation.VisualLength=(float)(164/Math.Max(100,area.Height-224));
            Simulation.BowlRadius=(float)(43/Math.Max(100,area.Height-224));
            Simulation.Tick((float)dt,(float)(Math.Max(100,area.Width-224)/Math.Max(100,area.Height-224)));
            SyncPets();
            for(int i=0;i<pets.Count;i++)
            {
                var state=Simulation.Pets[i];Point p=ScreenPoint(state.Position);
                pets[i].PlaceAt(p);
                pets[i].Present(state,dt,Simulation.Paused);
            }
            Point at=ScreenPoint(Simulation.BowlPosition);bowl.Left=at.X-58;bowl.Top=at.Y-58;bowl.Food=Simulation.Food;
            panel.Left=area.Right-panel.Width-18;panel.Top=area.Bottom-panel.Height-18;
            panelRefreshTime-=dt;
            if(panelRefreshTime<=0)
            {
                panel.Refresh(Simulation,watch.Elapsed.TotalSeconds<noticeUntil?notice:Simulation.StatusMessage);
                panelRefreshTime=.2;
            }
            saveTime+=dt;if(saveTime>15){Save();saveTime=0;}
        }
        public void Feed(){Simulation.Feed();Say("果冻加好了，饿了会自己来吃。");Save();}
        public void AddPet(){Say(Simulation.AddPet()?"第二只甲虫来作伴了。":"最多养两只甲虫。");SyncPets();Save();}
        public void RemovePet()
        {
            heldWheel.SetActive(false);
            if(candidate!=null){Simulation.Release(pets.IndexOf(candidate));candidate=null;}
            Simulation.RemovePet(Simulation.Pets.Count-1);SyncPets();Save();
        }
        public void Fight(){Say(Simulation.RequestFight()?"开始友好切磋！抓起一只就能分开。":Simulation.StatusMessage);}
        public void Pause(){Simulation.Paused=!Simulation.Paused;Say(Simulation.Paused?"暂停活动。":"继续散步。");}
        public bool RenamePet(int index,string name)
        {
            bool renamed=Simulation.RenamePet(index,name);Say(Simulation.StatusMessage);
            if(renamed)Save();return renamed;
        }
        public void TogglePetName(int index)
        {
            if(index<0||index>=Simulation.Pets.Count)return;
            var pet=Simulation.Pets[index];pet.ShowName=!pet.ShowName;
            Say(pet.ShowName?"已显示桌面名字。":"已隐藏桌面名字，可在面板重新开启。");Save();
        }
        public void SetPetNameColor(int index,int rgb)
        {
            if(index<0||index>=Simulation.Pets.Count||rgb<0||rgb>0xFFFFFF)return;
            Simulation.Pets[index].NameColorRgb=rgb;Say("名字颜色已保存。");Save();
        }
        bool HelpRightPet(int index)
        {
            bool righted=Simulation.RightPet(index);
            if(righted){Say(Simulation.StatusMessage);Save();}
            return righted;
        }
        bool RotateHeld(int wheelDelta)
        {
            if(wheelDelta==0||candidate==null)return false;
            int index=pets.IndexOf(candidate);
            if(index<0||Simulation.Pets[index].Action!="held")return false;
            var state=Simulation.Pets[index];
            state.Heading=((state.Heading+wheelDelta/120f*15f)%360+360)%360;
            state.TurnRate=state.LookYaw=0;
            return true;
        }
        public void Say(string value){notice=value;noticeUntil=watch.Elapsed.TotalSeconds+5;}
        public void Save(){if(!qa)PetPersistence.Save(Simulation);}
        public void Quit(){Save();Application.Current.Shutdown();}
        public void Dispose(){CompositionTarget.Rendering-=RenderFrame;heldWheel.Dispose();Save();foreach(var pet in pets)pet.Close();bowl?.Close();panel?.Close();}

        public void ScheduleSelfTest()
        {
            var scheduled=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
            scheduled.Tick+=(s,e)=>{scheduled.Stop();RunSelfTest();};scheduled.Start();
        }
        void RunSelfTest()
        {
            CompositionTarget.Rendering-=RenderFrame;var results=new List<string>();
            string folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Artifacts"));Directory.CreateDirectory(folder);
            try
            {
                AddPet();AddPet();Check(Simulation.Pets.Count==2,"two-pet limit",results);
                // Force both appearances only in this isolated renderer fixture, never in the player's save.
                Simulation.Pets[0].Iridescent=false;Simulation.Pets[1].Iridescent=true;
                foreach(var pet in pets)pet.Close();pets.Clear();SyncPets();
                Advance(.01,new Point(),false,false);
                foreach(var pet in pets)Check(pet.IsActuallyTransparent,"native per-pixel alpha window: "+pet.Diagnostics,results);
                Check(bowl.IsActuallyTransparent,"native transparent food bowl: "+bowl.Diagnostics,results);
                Check(!bowl.IsNativeHitAt(new Point(2,2)),"transparent corner passes native hit testing",results);
                Check(bowl.IsNativeHitAt(new Point(58,58)),"opaque bowl receives native hits",results);
                var point=ScreenPoint(Simulation.Pets[0].Position);BeginHold(pets[0],point);
                Advance(.2,point,false,true);Check(Simulation.Pets[0].Action!="held","right press threshold",results);
                Check(!RotateHeld(120),"wheel ignored before grab threshold",results);
                Advance(.17,point,false,true);Check(Simulation.Pets[0].Action=="held","right long-press grabs",results);
                Check(heldWheel.IsActive,"temporary wheel hook active only while held",results);
                Simulation.Pets[0].Heading=350;
                Check(RotateHeld(120)&&Math.Abs(Simulation.Pets[0].Heading-5)<.01,"wheel rotates held pet across 360 degrees",results);
                RotateHeld(-240);Check(Math.Abs(Simulation.Pets[0].Heading-335)<.01,"reverse wheel rotation",results);
                RotateHeld(30);Check(Math.Abs(Simulation.Pets[0].Heading-338.75)<.01,"high-resolution wheel rotation",results);
                Advance(.02,point+new Vector(70,-40),false,true);
                Check((ScreenPoint(Simulation.Pets[0].Position)-(point+new Vector(70,-40))).Length<1,"held pointer-follow drag",results);
                Advance(.02,point,false,false);Check(Simulation.Pets[0].Action!="held","release drops pet",results);
                Check(!heldWheel.IsActive&&!RotateHeld(120),"wheel hook released with pet",results);
                Check(Math.Abs(Simulation.Pets[0].Heading-338.75)<.01,"drop retains rotated orientation",results);
                Check(RenamePet(0," 小甲 ")&&Simulation.Pets[0].Name=="小甲","panel rename updates pet",results);
                Simulation.Pets[0].Overturned=true;Simulation.Pets[0].Action="overturned";
                for(int frame=0;frame<35;frame++)Advance(.033,point,false,false);
                Capture(pets[0],Path.Combine(folder,"overturned-pet.png"),results);
                Check(HelpRightPet(0)&&!Simulation.Pets[0].Overturned,"left click helps overturned pet upright",results);
                Check(!HelpRightPet(0),"left click on upright pet is harmless",results);
                for(int frame=0;frame<35;frame++)Advance(.033,point,false,false);
                Simulation.Food=0;var bp=ScreenPoint(Simulation.BowlPosition);BeginBowl(bp);Advance(.01,bp,false,false);
                Check(Simulation.Food>.5,"food bowl click refills",results);
                BeginBowl(bp);Advance(.02,bp+new Vector(-55,35),true,false);Advance(.02,bp+new Vector(-55,35),false,false);
                Check((ScreenPoint(Simulation.BowlPosition)-(bp+new Vector(-55,35))).Length<1,"food bowl drag",results);
                // Render the actual WPF visual tree: unlike a desktop screenshot this is deterministic and has an alpha channel.
                Capture(pets[0],Path.Combine(folder,"normal-pet.png"),results);
                Capture(pets[1],Path.Combine(folder,"iridescent-pet.png"),results);
                Capture(bowl,Path.Combine(folder,"food-bowl.png"),results);
                panel.SetExpanded(true);panel.Refresh(Simulation,"原生透明桌宠 · 功能检查通过");
                Capture(panel,Path.Combine(folder,"controls.png"),results);
                File.WriteAllLines(Path.Combine(folder,"native-checks.txt"),results);Application.Current.Shutdown(0);
            }
            catch(Exception error){results.Add("FAIL "+error);File.WriteAllLines(Path.Combine(folder,"native-checks.txt"),results);Application.Current.Shutdown(1);}
        }
        static void Check(bool ok,string name,List<string> results){if(!ok)throw new Exception(name);results.Add("PASS "+name);}
        static void Capture(SpriteWindow window,string path,List<string> results)
        {
            window.UpdateLayout();var visual=(FrameworkElement)window.Content;
            var image=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32);
            image.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
            using(var output=File.Create(path))encoder.Save(output);
            byte[] pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);
            int clear=0,visible=0;for(int i=3;i<pixels.Length;i+=4){if(pixels[i]==0)clear++;else visible++;}
            Check(clear>(path.EndsWith("controls.png")?0:image.PixelWidth*image.PixelHeight*.15) && visible>500,"alpha/visible pixels in "+Path.GetFileName(path)+$" ({clear} transparent, {visible} visible)",results);
        }
    }
}
