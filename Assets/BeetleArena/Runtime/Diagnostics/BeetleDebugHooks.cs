#if UNITY_EDITOR
using System;
using UnityEngine;

namespace BeetleArena
{
    /// <summary>仅编辑器编译的观察钩子。没有订阅者时不构造记录、不查询物理、不抽随机数。</summary>
    public static class BeetleDebugHooks
    {
        public static event Action<BeetleMatchController> Stepped;
        public static event Action<BeetleSimulation,int,BeetleAction,BeetleAction,string,int> Transition;
        public static event Action<BeetleSimulation,int,bool,float,string> Decision;
        public static event Action<BeetleSimulation,BeetleContactSolver,float,float,Vector3,Vector3,Vector3,Vector3> ContactAttempt;
        public static event Action<BeetleContactSolver,Collider,Collider,Vector3,float,string> NarrowContact;
        public static void Step(BeetleMatchController match) => Stepped?.Invoke(match);
        public static void Change(BeetleSimulation sim,int actor,BeetleAction from,BeetleAction to,string caller,int line)
            => Transition?.Invoke(sim,actor,from,to,caller,line);
        public static void Choose(BeetleSimulation sim,int actor,bool permit,float choice,string reason)
            => Decision?.Invoke(sim,actor,permit,choice,reason);
        public static void Contact(BeetleSimulation sim,BeetleContactSolver solver,float fraction,float residual,
            Vector3 proposedA,Vector3 proposedB,Vector3 actualA,Vector3 actualB)
            => ContactAttempt?.Invoke(sim,solver,fraction,residual,proposedA,proposedB,actualA,actualB);
        public static void Pair(BeetleContactSolver solver,Collider a,Collider b,Vector3 normal,float depth,string kind)
            => NarrowContact?.Invoke(solver,a,b,normal,depth,kind);
    }
}
#endif
