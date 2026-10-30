using System;
using System.Collections;
using System.Linq;
using BeetleArena;
using BeetleArena.Campaign;
using UnityEngine;

/// <summary>Exercises the installed exterior against the isolated play fixture, never a player's save.</summary>
public static class HusbandrySeasonalTreeValidation
{
    public static IEnumerator Run(CampaignApp app,RenderTexture frame,Action<bool,string> check)
    {
        string fixtureRoot=System.IO.Path.GetFullPath(HusbandryPolishValidation.Output+"/Save-");
        check(!string.IsNullOrEmpty(CampaignApp.StorageOverride)&&
            System.IO.Path.GetFullPath(CampaignApp.StorageOverride).StartsWith(fixtureRoot,StringComparison.OrdinalIgnoreCase),
            "seasonal checks use only the isolated preview save");
        var tree=UnityEngine.Object.FindFirstObjectByType<HusbandrySeasons>();
        check(tree&&tree.gameObject.name=="WindowTreeExterior"&&tree.daylight,"seasonal controller belongs to the outdoor geometry");
        var clock=app.Session.State.clock;
        double originalDays=clock.elapsedDays;bool originalPaused=clock.paused,originalEnabled=tree.enabled;
        var effects=new[]{tree.petals,tree.fallingLeaves,tree.snowflakes};
        check(tree.foliage.Length>0&&tree.snowCaps.Length>0&&tree.buds.Length>0&&effects.All(p=>p),
            "tree has foliage, branch snow, buds and three weather systems");
        var materials=tree.foliage.Select(r=>r.sharedMaterial).Distinct().ToArray();
        var originalColors=materials.Select(m=>m.GetColor("_SeasonTint")).ToArray();
        var originalCoverage=materials.Select(m=>m.GetFloat("_LeafCoverage")).ToArray();
        var coverage=new float[4];var canopyColors=new Color[4];
        var properties=new MaterialPropertyBlock();
        var particles=new ParticleSystem.Particle[effects.Max(p=>p.main.maxParticles)];
        int observedParticles=0;
        try
        {
            clock.paused=true;tree.enabled=true;
            string[] names={"Spring","Summer","Autumn","Winter"};
            double[] dates={30.5,120.5,220.5,305.5};
            for(int season=0;season<dates.Length;season++)
            {
                clock.elapsedDays=dates[season];
                yield return new WaitForSecondsRealtime(.35f);
                check(tree.Season==season&&Mathf.Abs(tree.DayOfYear-(float)dates[season])<.001f,
                    names[season]+" tree follows the paused campaign date");
                coverage[season]=tree.LeafCoverage;
                tree.foliage[0].GetPropertyBlock(properties);canopyColors[season]=properties.GetColor("_SeasonTint");
                check(Mathf.Abs(properties.GetFloat("_LeafCoverage")-tree.LeafCoverage)<.001f&&
                    tree.foliage.All(r=>r.enabled&&r.GetComponent<MeshFilter>()&&r.GetComponent<MeshFilter>().sharedMesh.vertexCount>100),
                    names[season]+" coverage reaches real leaf geometry");
                bool winter=season==3;
                check(winter?tree.SnowAmount>.8f&&tree.snowCaps.All(r=>r.enabled):tree.SnowAmount<.01f&&tree.snowCaps.All(r=>!r.enabled),
                    names[season]+" branch snow matches visible seasonal conditions");
                if(season==0)check(tree.buds.Any(r=>r.enabled),"spring buds are visible in spring");
                int expected=season==0?0:season==2?1:season==3?2:-1;
                for(int i=0;i<effects.Length;i++)
                {
                    var effect=effects[i];bool active=i==expected;
                    check(effect.emission.enabled==active&&(active?effect.emission.rateOverTime.constant>0:effect.particleCount==0),
                        names[season]+" enables only the appropriate weather ("+effect.name+")");
                    if(active)
                    {
                        // Advance an entire particle lifetime, inspecting actual emitted positions instead
                        // of assuming that a distant emitter or its configured velocity guarantees safety.
                        effect.Simulate(4,false,true,true);
                        check(effect.particleCount>0,names[season]+" weather actually emits visible particles");
                        for(int sample=0;sample<28;sample++)
                        {
                            int count=effect.GetParticles(particles);observedParticles+=count;
                            for(int p=0;p<count;p++)
                            {
                                Vector3 world=WorldPosition(effect,particles[p].position);
                                if(world.z-particles[p].GetCurrentSize(effect)*.6f<=tree.daylight.windowCenter.z)
                                    check(false,"weather crosses the window plane: "+effect.name+" at "+world);
                            }
                            effect.Simulate(.5f,false,false,true);
                        }
                        effect.Simulate(4,false,true,true);effect.Play(false);
                    }
                }
                yield return new WaitForSecondsRealtime(.35f);
                HusbandryPolishDriver.Capture(frame,"Tree-"+names[season]);
            }
            check(coverage[0]>.6f&&coverage[1]>.9f&&coverage[2]<coverage[1]-.2f&&coverage[3]<.1f,
                "spring fills in, summer is full, autumn thins and winter exposes branches");
            check(canopyColors[1].g>canopyColors[1].r&&canopyColors[2].r>canopyColors[2].g,
                "summer foliage is green and autumn foliage turns warm");
            check(observedParticles>100,"emitted petals, leaves and snow stay beyond the window over their full lifetime ("+observedParticles+" samples)");

            // The calendar is paused, but the room remains alive: shader time and existing flakes move.
            tree.foliage[0].GetPropertyBlock(properties);float windTime=properties.GetFloat("_WindTime");
            double frozenDay=clock.elapsedDays;float frozenCoverage=tree.LeafCoverage;
            int beforeCount=tree.snowflakes.GetParticles(particles);
            uint trackedSeed=particles[0].randomSeed;Vector3 beforePosition=WorldPosition(tree.snowflakes,particles[0].position);
            yield return new WaitForSecondsRealtime(.65f);
            tree.foliage[0].GetPropertyBlock(properties);
            check(clock.elapsedDays==frozenDay&&tree.LeafCoverage==frozenCoverage&&
                properties.GetFloat("_WindTime")>windTime+.2f&&properties.GetFloat("_WindStrength")>0,
                "paused calendar keeps its season while the leaf breeze advances");
            int afterCount=tree.snowflakes.GetParticles(particles);bool moved=false;
            for(int i=0;i<afterCount;i++)if(particles[i].randomSeed==trackedSeed)
                moved=Vector3.Distance(beforePosition,WorldPosition(tree.snowflakes,particles[i].position))>.02f;
            check(beforeCount>0&&moved,"existing snowflakes continue to drift while the calendar is paused");

            tree.snowflakes.GetComponent<Renderer>().GetPropertyBlock(properties);
            check(properties.GetFloat("_Daylight")>.9f,"daytime weather renderer receives the daytime lighting level");
            clock.elapsedDays=305+23d/24;
            yield return new WaitForSecondsRealtime(.75f);
            HusbandryPolishDriver.Capture(frame,"Tree-Winter-Night");
            tree.foliage[0].GetPropertyBlock(properties);
            check(tree.daylight.Daylight<.01f&&properties.GetFloat("_Daylight")<.01f,
                "winter foliage receives night lighting from the campaign clock");
            tree.snowflakes.GetComponent<Renderer>().GetPropertyBlock(properties);
            int nightCount=tree.snowflakes.GetParticles(particles);bool keptAcrossNight=false;
            for(int i=0;i<nightCount;i++)if(particles[i].randomSeed==trackedSeed)keptAcrossNight=true;
            check(properties.GetFloat("_Daylight")<.01f&&keptAcrossNight,
                "already falling snow receives night lighting immediately without being cleared or reborn");

            tree.enabled=false;yield return null;
            check(effects.All(p=>!p.isPlaying&&p.particleCount==0),"disabling seasonal presentation clears every weather system");
            tree.enabled=true;yield return new WaitForSecondsRealtime(.3f);
            tree.snowflakes.Simulate(4,false,true,true);tree.snowflakes.Play(false);
            tree.foliage[0].GetPropertyBlock(properties);
            check(tree.Season==3&&tree.snowCaps.All(r=>r.enabled)&&tree.snowflakes.particleCount>0&&
                Mathf.Abs(properties.GetFloat("_LeafCoverage")-coverage[3])<.001f,
                "re-enabling restores the current winter date, branch snow and particles");
            check(materials.Select((material,i)=>material.GetColor("_SeasonTint")==originalColors[i]&&
                material.GetFloat("_LeafCoverage")==originalCoverage[i]).All(v=>v),
                "seasonal presentation leaves shared material assets unchanged");

            var sky=tree.daylight.sky;
            check(sky.sharedMaterial.GetFloat("_MountainEnabled")==1&&sky.sharedMaterial.GetTexture("_MountainTex")&&
                sky.sharedMaterial.GetFloat("_CloudTextureEnabled")==1&&sky.sharedMaterial.GetTexture("_CloudTex"),
                "painted mountain detail and separate animated cloud layer are installed");
            int fogDay=-1,clearDay=-1;
            for(int day=0;day<90;day++)
            {
                float amount=HusbandrySeasons.FogAtDay(day+.3);
                if(amount>.5f&&fogDay<0)fogDay=day;
                if(amount<.001f&&clearDay<0)clearDay=day;
            }
            check(fogDay>=0&&clearDay>=0,"calendar has both misty mornings and clear days");
            clock.elapsedDays=fogDay+.3;
            yield return new WaitForSecondsRealtime(.6f);
            sky.GetPropertyBlock(properties);
            float fogAmount=tree.FogAmount;
            check(fogAmount>.5f&&Mathf.Abs(properties.GetFloat("_OutdoorFog")-fogAmount)<.001f,
                "a misty morning reaches the outdoor mountain material");
            HusbandryPolishDriver.Capture(frame,"Tree-Fog-Morning");
            yield return new WaitForSecondsRealtime(.4f);
            check(tree.FogAmount==fogAmount&&Mathf.Abs(tree.daylight.Hour-7.2f)<.01f,
                "pausing preserves the forecast without disturbing the daylight clock");
            clock.elapsedDays=fogDay+.6;
            yield return new WaitForSecondsRealtime(.35f);
            check(tree.FogAmount<.001f,"morning mist dissipates later in the same day");
            clock.elapsedDays=clearDay+.3;
            yield return new WaitForSecondsRealtime(.35f);
            check(tree.FogAmount<.001f,"a clear morning does not receive another day's fog");
            clock.elapsedDays=fogDay+.3;
            yield return new WaitForSecondsRealtime(.35f);
            check(tree.FogAmount==fogAmount,"the same saved date reproduces its weather exactly");
        }
        finally
        {
            clock.elapsedDays=originalDays;clock.paused=originalPaused;
            tree.enabled=originalEnabled;
            if(tree.enabled)tree.PresentDay(originalDays);
        }
    }

    static Vector3 WorldPosition(ParticleSystem system,Vector3 position)
    {
        var main=system.main;
        if(main.simulationSpace==ParticleSystemSimulationSpace.World)return position;
        Transform space=main.simulationSpace==ParticleSystemSimulationSpace.Custom?main.customSimulationSpace:system.transform;
        return space?space.TransformPoint(position):position;
    }
}
