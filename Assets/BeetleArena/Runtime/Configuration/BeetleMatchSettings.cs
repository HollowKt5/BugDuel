using UnityEngine;

namespace BeetleArena
{
    [CreateAssetMenu(menuName = "Beetle Arena/Match Settings")]
    // 集中配置资产：Inspector 调整比赛尺寸、运动、节奏、鼓励和挑飞参数。
    // 修改共享资产会影响后续比赛；这些默认值是游戏调校，不是现实生物测量结果。
    public sealed class BeetleMatchSettings : ScriptableObject
    {
        [Header("Arena / world units")]
        // 圆形台面半径，世界单位；与场景树桩和落台/足部支撑计算对应。
        [Min(3.5f)] public float arenaRadius = 5.2f;
        // 每只甲虫距离中心的初始距离，双方实际初始间距为它的两倍。
        [Min(1f)] public float spawnDistance = 2.3f;
        // 基础模型视觉缩放，最终再乘个体 size；改变后需要重新检查触及距离与碰撞。
        [Min(.1f)] public float visualScale = .70f;
        [Tooltip("Model-local nominal foot contacts, in left-front/middle/hind then right-front/middle/hind order.")]
        // 正式模型的六个实测脚位。空数组保留旧模型的备用支撑布局。
        public Vector3[] supportFootOffsets = new Vector3[0];
        // 根节点低于台面这一深度才标记落台，避免擦边或短暂腾空立即判负。
        [Min(.1f)] public float fallDepth = .75f;
        // 台外地面 Y 高度（台面为 0），用于已落台对象的沉降和地面生成。
        public float groundHeight = -3.4f;
        // 竖直运动的游戏重力加速度；数值借用常见重力值，但整体世界比例不是毫米单位。
        [Min(1f)] public float gravity = 9.81f;
        [Header("Matched size variation")]
        // 个体体型随机区间下界，1 表示基准大小。
        [Range(.85f, 1f)] public float minimumSize = .96f;
        // 个体体型随机区间上界；配对时还会额外受 maximumSizeDifference 限制。
        [Range(1f, 1.15f)] public float maximumSize = 1.04f;
        [Range(0, .15f), Tooltip("Maximum (larger/smaller - 1); 0.08 means 8%.")]
        // 较大体型/较小体型-1 的上限，0.08 表示双方相差最多约 8%。
        public float maximumSizeDifference = .08f;
        // 基准展示体长（毫米），按 size 线性缩放；不作为场景 Transform 的直接长度。
        [Min(1)] public float referenceLengthMm = 55f;
        // 基准展示质量（克），按 size³ 缩放；模拟运动使用独立归一化质量。
        [Min(1)] public float referenceMassGrams = 8f;
        [Header("Locomotion and contest")]
        // 基础平面移动速度，实际速度还受灵活性、体力和动作倍率影响。
        [Min(.1f)] public float walkSpeed = .95f;
        // 基础转向速度，单位度/秒；承诺动作期间会进一步降低。
        [Min(1)] public float turnSpeed = 145f;
        // 推顶冲量基准，最终乘发力、退让程度与比赛升级系数。
        [Min(.1f)] public float shoveImpulse = 2.7f;
        // 托举力量基准，4.3 为标准力量；按比例影响是否能克服抓地，不直接产生后推冲量。
        [Min(.1f)] public float pryImpulse = 4.3f;
        // 完全恢复动作每秒恢复的体力点数，撑地或退让使用它的部分比例。
        [Min(.1f)] public float staminaRecovery = 7.5f;
        // 发力/受威胁期间每秒积累的疲劳基准，比赛升级后可放大。
        [Range(0, 2)] public float fatiguePerSecond = .12f;
        [Header("Bout pacing / seconds, not a forced result timer")]
        // 长局升级开始前的秒数；与每轮 probingSeconds 的短试探阶段不同。
        [Min(0)] public float openingSeconds = 90f;
        // 从开局推力系数平滑过渡到最终系数所需的秒数，不是结束比赛的倒计时。
        [Min(10)] public float escalationSeconds = 90f;
        // 开局攻击位移倍率，让早期推挤较克制；有效落台仍会立即进入结算。
        [Range(.05f, 1f)] public float openingDisplacement = .10f;
        // 长局后期攻击位移倍率，增加推出台面的机会，不能保证固定时长结束。
        [Range(1f, 3f)] public float finalDisplacement = 2.4f;
        [Header("Exchange rhythm / seconds")]
        // 短周期里的轻触试探持续秒数。
        [Min(2)] public float probingSeconds = 7f;
        // 短周期里的激烈交锋持续秒数，允许冲顶、角力和抬举。
        [Min(2)] public float clashSeconds = 9f;
        // 每轮交锋后拉开/恢复的阶段时长。
        [Min(1)] public float exchangeRecoverySeconds = 3.5f;
        // 轻触试探相对普通推顶的发力倍率，试探不进入挑飞判定。
        [Range(.1f, .6f)] public float probeForceMultiplier = .3f;
        // 重冲顶的额外发力倍率；仍需对准、进入有效窗口且实际接触才结算。
        [Range(1f, 2f)] public float chargeForceMultiplier = 1.35f;
        // 冲顶准备时长基准，实际选择动作时加入小幅随机变化。
        [Min(.2f)] public float chargeWindupSeconds = .65f;
        // 一次顶角僵持的目标持续时间；分离或对手腾空时可提前结束。
        [Min(.3f)] public float hornLockSeconds = 1.15f;
        [Range(.05f,.6f), Tooltip("Normalized pry phase at which actual contact may transfer the lifting impulse.")]
        // 正式模型长角抬起后很快离开接触点，发力窗口应在抬举起始段开启。
        public float pryContactStart = .16f;
        [Header("Horn-supported lift / seconds and degrees")]
        // 托举先围绕接触点抬起前部，再持续承重。释放后才进入自由下落/稀有挑飞。
        [Range(.3f, 1f)] public float liftRaiseSeconds = .65f;
        [Range(.25f, 1.2f)] public float liftHoldSeconds = .9f;
        [Range(20, 55)] public float liftedBodyPitch = 42f;
        [Range(.5f, 4f)] public float liftFollowSpeed = 2.8f;
        // 累计实际离开地面的时间，含角支撑阶段；0.7 秒之后逐渐六足挣扎。
        [Min(0)] public float liftStruggleDelay = .7f;
        // 频率单位为每秒周期数。失去支撑 0.7 秒后，从缓慢试探逐渐加快。
        [Min(.1f)] public float struggleStartHz = .65f;
        [Min(.1f)] public float struggleMaxHz = 3.2f;
        [Min(.1f)] public float struggleRampSeconds = 1.1f;
        // 动画造成的接触分离总位移预算（世界单位/秒），不是单次迭代上限。
        // 超限时缩小本步姿态变化，再重新检测全部碰撞体，而非截断推出距离后留下穿模。
        [Range(1, 6)] public float contactMotionSpeed = 4f;
        // 顶举持续受阻时主动收角，不让双方冻结到原托举计时结束。
        [Range(.1f, .4f)] public float liftBlockedReleaseSeconds = .16f;
        [Header("Contest stamina")]
        // 重冲顶状态的体力消耗速率（点/秒），不是一次动作的固定扣除额。
        [Min(1)] public float chargeStaminaCost = 27f;
        // 持续角力期间的体力消耗速率（点/秒）。
        [Min(1)] public float hornLockStaminaCost = 8f;
        [Header("Rare horn toss / game tuning, not measured biological odds")]
        [Range(0, 1), Tooltip("One roll per eligible, connected pry; not a per-frame or per-match probability.")]
        // 每次满足全部条件的有效抬举只抽一次概率；不是每帧概率，也不是每局发生率。
        // 设为 0 可关闭随机挑飞，普通抬举仍然可能让目标小幅离地。
        public float tossChance = .08f;
        // 挑飞要求的剩余体力/初始耐力最低比例；还需付得起固定挑飞消耗。
        [Range(0, 1)] public float tossMinimumStamina = .35f;
        // 挑飞成功后额外扣除的体力点数，与按秒计算的普通动作消耗不同。
        [Min(0)] public float tossStaminaCost = 12f;
        // 成功挑飞后双方都获得的冷却秒数，防止紧接着连续挑飞。
        [Min(0)] public float tossCooldownSeconds = 12f;
        // 挑飞附加水平冲量，叠加在普通抬举效果上；按受击者质量换算速度变化。
        [Range(1, 5)] public float tossHorizontalImpulse = 2.6f;
        // 挑飞向上速度基准，实际再除受击者归一化质量的平方根。
        [Range(2, 6)] public float tossVerticalSpeed = 4.3f;
        [Header("Encouragement: intentionally a game mechanic")]
        // 一次鼓励后再次允许点击的间隔秒数。
        [Min(.1f)] public float cheerCooldown = 6f;
        // 鼓励发力加成持续秒数，与按钮冷却独立。
        [Min(.1f)] public float cheerDuration = 3.5f;
        // 鼓励对攻击发挥的乘法加成，0.13 对应乘以 1.13。
        [Range(0, .3f)] public float cheerEffortBonus = .13f;
        // 鼓励被接受时立即恢复的体力点数，不超过当前疲劳限制后的体力上限。
        [Range(0, 15)] public float cheerStamina = 5f;
        [Header("Match")]
        // 开局准备时间；倒计时期间不能鼓励或进行正常 AI 战斗。
        [Min(0)] public float countdownSeconds = 3f;
        // 首次落台后等待另一方也落台的窗口；支持双方近乎同时掉落判平局。
        [Range(.5f, 3f)] public float doubleFallWindow = 1.2f;
        [Tooltip("0 chooses a fresh seed each session. A nonzero seed reproduces a pairing.")]
        // 配置 0 时由控制器选择新种子；非零复用同一初始随机起点，不能等同于记录整场录像。
        public int initialSeed = 0;

        // Inspector 修改资产后立即执行边界约束，避免误填参数导致明显无效配置。
        void OnValidate() => Validate();
        // 将参数限制到当前模型和规则能处理的范围；Inspector 的 Min/Range 之外也做代码保护。
        // 调用方在正式启动比赛时也会执行一次；本函数不重新生成已经存在的场景几何体。
        public void Validate()
        {
            arenaRadius = Mathf.Max(3.5f, arenaRadius);
            spawnDistance = Mathf.Clamp(spawnDistance, 1.7f, arenaRadius - 1.2f);
            visualScale = Mathf.Clamp(visualScale, .4f, .9f);
            minimumSize = Mathf.Clamp(minimumSize, .85f, 1f);
            maximumSize = Mathf.Clamp(maximumSize, 1f, 1.15f);
            maximumSizeDifference = Mathf.Clamp(maximumSizeDifference, 0, .15f);
            referenceLengthMm = Mathf.Max(1, referenceLengthMm);
            referenceMassGrams = Mathf.Max(1, referenceMassGrams);
            walkSpeed = Mathf.Max(.1f, walkSpeed);
            turnSpeed = Mathf.Max(1, turnSpeed);
            shoveImpulse = Mathf.Max(.1f, shoveImpulse);
            pryImpulse = Mathf.Max(.1f, pryImpulse);
            staminaRecovery = Mathf.Max(.1f, staminaRecovery);
            fatiguePerSecond = Mathf.Clamp(fatiguePerSecond, 0, 2);
            openingSeconds = Mathf.Max(0, openingSeconds);
            escalationSeconds = Mathf.Max(10, escalationSeconds);
            openingDisplacement = Mathf.Clamp(openingDisplacement, .05f, 1f);
            finalDisplacement = Mathf.Clamp(finalDisplacement, 1f, 3f);
            probingSeconds = Mathf.Max(2, probingSeconds);
            clashSeconds = Mathf.Max(2, clashSeconds);
            exchangeRecoverySeconds = Mathf.Max(1, exchangeRecoverySeconds);
            probeForceMultiplier = Mathf.Clamp(probeForceMultiplier, .1f, .6f);
            chargeForceMultiplier = Mathf.Clamp(chargeForceMultiplier, 1, 2);
            chargeWindupSeconds = Mathf.Max(.2f, chargeWindupSeconds);
            hornLockSeconds = Mathf.Max(.3f, hornLockSeconds);
            pryContactStart = Mathf.Clamp(pryContactStart,.05f,.6f);
            liftRaiseSeconds = Mathf.Clamp(liftRaiseSeconds, .3f, 1);
            liftHoldSeconds = Mathf.Clamp(liftHoldSeconds, .25f, 1.2f);
            liftedBodyPitch = Mathf.Clamp(liftedBodyPitch, 20, 55);
            liftFollowSpeed = Mathf.Clamp(liftFollowSpeed, .5f, 4);
            liftStruggleDelay = Mathf.Max(0, liftStruggleDelay);
            struggleStartHz = Mathf.Max(.1f, struggleStartHz);
            struggleMaxHz = Mathf.Max(struggleStartHz, struggleMaxHz);
            struggleRampSeconds = Mathf.Max(.1f, struggleRampSeconds);
            contactMotionSpeed = Mathf.Clamp(contactMotionSpeed, 1, 6);
            liftBlockedReleaseSeconds = Mathf.Clamp(liftBlockedReleaseSeconds, .1f, .4f);
            chargeStaminaCost = Mathf.Max(1, chargeStaminaCost);
            hornLockStaminaCost = Mathf.Max(1, hornLockStaminaCost);
            tossChance = Mathf.Clamp01(tossChance);
            tossMinimumStamina = Mathf.Clamp01(tossMinimumStamina);
            tossStaminaCost = Mathf.Max(0, tossStaminaCost);
            tossCooldownSeconds = Mathf.Max(0, tossCooldownSeconds);
            tossHorizontalImpulse = Mathf.Clamp(tossHorizontalImpulse, 1, 5);
            tossVerticalSpeed = Mathf.Clamp(tossVerticalSpeed, 2, 6);
            fallDepth = Mathf.Max(.1f, fallDepth);
            groundHeight = Mathf.Min(-1.5f, groundHeight);
            gravity = Mathf.Max(1, gravity);
            cheerCooldown = Mathf.Max(.1f, cheerCooldown);
            cheerDuration = Mathf.Max(.1f, cheerDuration);
            cheerEffortBonus = Mathf.Clamp(cheerEffortBonus, 0, .3f);
            cheerStamina = Mathf.Clamp(cheerStamina, 0, 15);
            countdownSeconds = Mathf.Max(0, countdownSeconds);
            doubleFallWindow = Mathf.Clamp(doubleFallWindow, .5f, 3f);
        }
    }
}
