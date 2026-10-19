using System;

namespace BugDuel.Domain
{
    [Serializable]
    public sealed class CareHabitatProfile
    {
        public double activityMultiplier=1, restPreference=.5;
        // Unity serializes null inline objects as all-zero records. An entirely empty record means legacy defaults.
        public bool IsEmpty => activityMultiplier==0 && restPreference==0;
        public double Activity => IsEmpty ? 1 : activityMultiplier;
        public double Rest => IsEmpty ? .5 : restPreference;
        public void Validate()
        {
            if(IsEmpty)return;
            if(double.IsNaN(activityMultiplier)||double.IsInfinity(activityMultiplier)||activityMultiplier<.1||activityMultiplier>2||
                double.IsNaN(restPreference)||double.IsInfinity(restPreference)||restPreference<0||restPreference>1)
                throw new InvalidOperationException("养殖环境行为参数无效");
        }
    }
    /// <summary>Game pacing, not biological measurements. Optional in older content packs.</summary>
    [Serializable]
    public sealed class CareBehaviorProfile
    {
        public bool nocturnal = true;
        public double activity = .7, walkSpeed = .24;
        public double idleMin = 2, idleMax = 5, walkSeconds = 5;
        public double inspectSeconds = 2, eatSeconds = 4, groomSeconds = 3;
        public double restSeconds = 9, recoverSeconds = 2, startleSeconds = 1.6;
        public double hungerThreshold = 65;
        public bool IsEmpty => !nocturnal && activity==0 && walkSpeed==0 && idleMin==0 && idleMax==0 && walkSeconds==0 &&
            inspectSeconds==0 && eatSeconds==0 && groomSeconds==0 && restSeconds==0 && recoverSeconds==0 && startleSeconds==0 && hungerThreshold==0;
        public static CareBehaviorProfile Resolve(CareBehaviorProfile value) => value==null||value.IsEmpty ? new CareBehaviorProfile() : value;

        public void Validate()
        {
            if(IsEmpty)return;
            Range(activity, .05, 1); Range(walkSpeed, .05, .35);
            Range(idleMin, .2, 60); Range(idleMax, idleMin, 120); Range(walkSeconds, 1, 60);
            Range(inspectSeconds, .2, 30); Range(eatSeconds, 1, 30); Range(groomSeconds, .2, 30);
            Range(restSeconds, 1, 120); Range(recoverSeconds, .2, 10); Range(startleSeconds, .2, 10);
            Range(hungerThreshold, 10, 95);
        }
        static void Range(double value, double min, double max)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new InvalidOperationException("养殖行为参数超出范围");
        }
    }
}
