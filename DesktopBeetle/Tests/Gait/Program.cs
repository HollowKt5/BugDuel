using System;
using System.Linq;
using System.Windows;
using BeetleDesktop.Native;

internal static class Program
{
    [STAThread] static int Main()
    {
        new Application();
        try
        {
            foreach(var scenario in new[] { ("stroll",15.6,0.0), ("food",29.5,0.0), ("curve",15.6,50.0), ("pivot",0.0,60.0), ("fast",60.0,0.0) })
            {
                var model=new BeetleVisual { Width=216,Height=210 };
                double x=0,y=0,heading=0,travel=0,error=0,drift=0,length=0;int support=6;
                model.Present("wander",0,0,0,0,0,0,new Point());
                for(int frame=0;frame<480;frame++)
                {
                    const double dt=1d/60;
                    heading+=scenario.Item3*dt;
                    x+=Math.Sin(heading*Math.PI/180)*scenario.Item2*dt;
                    y-=Math.Cos(heading*Math.PI/180)*scenario.Item2*dt;travel+=scenario.Item2*dt;
                    model.Present("wander",scenario.Item2,dt,heading,scenario.Item3,0,travel,new Point(x,y));
                    var d=model.FootDiagnostics;
                    error=Math.Max(error,d.MaximumTargetErrorPixels);drift=Math.Max(drift,d.MaximumSupportDriftPixels);
                    length=Math.Max(length,d.MaximumJointLengthError);support=Math.Min(support,d.SupportCount);
                }
                if(!double.IsFinite(error)||error>1.5||drift>.15||support<3||length>1e-8)
                    throw new Exception($"{scenario.Item1}: invalid contact / joint metrics: {error}, {drift}, {support}, {length}");
                for(int frame=0;frame<90;frame++)model.Present("idle",0,1d/60,heading,0,0,travel,new Point(x,y));
                var before=model.FootDiagnostics.WorldFeet.ToArray();int steps=model.FootDiagnostics.StepCount;
                for(int frame=0;frame<90;frame++)model.Present("idle",0,1d/60,heading,0,0,travel,new Point(x,y));
                double idleDelta=before.Select((p,i)=>(model.FootDiagnostics.WorldFeet[i]-p).Length).Max();
                if(model.FootDiagnostics.SupportCount!=6||model.FootDiagnostics.StepCount!=steps||idleDelta>1e-6)
                    throw new Exception(scenario.Item1+": feet keep moving after stopping");
                Console.WriteLine($"PASS {scenario.Item1}: drift {drift:F3}px, target error {error:F3}px, support >= {support}; idle feet settle");
            }
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
