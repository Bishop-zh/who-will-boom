using LiarsBar;
using UnityEngine;

namespace BombTavern.Runtime
{
    /// <summary>
    /// 极简程序化特效。
    ///
    /// 全部用 ParticleSystem 的默认材质在运行时拼出来，不依赖任何粒子预制体或贴图 ——
    /// 这样即使美术资源没导入，也不会退化成「点了什么都没发生」。
    ///
    /// 定位是「反馈」而不是「火花」：告诉玩家刚才那一下**生效了、作用在哪**。
    /// </summary>
    public static class SimpleVfx
    {
        /// <summary>一次性火花爆发。用于使用道具、踩雷这类需要瞬时反馈的场合。</summary>
        public static void Burst(Vector3 worldPosition, Color color, int count = 38,
            float size = 0.05f, float speed = 2.2f, float lifetime = 0.55f)
        {
            var go = new GameObject("Vfx_Burst");
            go.transform.position = worldPosition;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                color, Color.Lerp(color, Color.white, 0.6f));
            main.startSize = size;
            main.startSpeed = speed;
            main.startLifetime = lifetime;
            main.duration = 0.22f;
            main.loop = false;
            main.gravityModifier = 0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // 不跟着角色动

            var emis = ps.emission;
            emis.rateOverTime = 0f;
            emis.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            ps.Play();
            Object.Destroy(go, lifetime + 0.8f);
        }

        /// <summary>在某个角色身上放一小片光。用于「他刚用了道具」。</summary>
        public static void OnCharacter(BombCharacter character, Color color)
        {
            if (character == null) return;
            Burst(character.transform.position + Vector3.up * 0.72f, color, 34, 0.048f, 2.0f, 0.5f);
        }

        /// <summary>
        /// 在某个座位的面板上撒一片光。
        /// 用它标出「道具作用在这一块」，比只在角色身上冒火花更有指向性。
        /// </summary>
        public static void OnTablet(TabletScreen tablet, Color color)
        {
            if (tablet == null) return;
            Burst(tablet.transform.position + tablet.transform.up * 0.05f,
                color, 30, 0.05f, 1.8f, 0.6f);
        }

        /// <summary>踩雷：偏橙红的一小团爆开。</summary>
        public static void OnExplosion(Vector3 worldPosition)
        {
            Burst(worldPosition + Vector3.up * 0.55f,
                new Color(1f, 0.62f, 0.18f), 70, 0.09f, 3.6f, 0.75f);
        }

        /// <summary>
        /// 在平板某一格上打一束向上的光柱。
        ///
        /// 比通用 Burst 更「有指向性」：粒子沿格子法线向上喷，
        /// 玩家一眼就能看出**道具作用在哪一格**，而不是只看到平板附近闪了一下。
        /// 这是「道具效果太弱」最直接的解法 —— 问题不在强度，在于没有指向。
        /// </summary>
        public static void CellPulse(Vector3 worldPosition, Vector3 normal, Color color,
            float strength = 1f, int count = 34, float speed = 2.6f, float lifetime = 0.55f)
        {
            var go = new GameObject("Vfx_CellPulse");
            go.transform.position = worldPosition + normal * 0.02f;

            // 让发射锥沿着格子法线，粒子才会「从格子里冒出来」而不是横着飘
            if (normal.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(normal.normalized, Vector3.up);

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                color, Color.Lerp(color, Color.white, 0.7f));
            main.startSize = 0.045f * strength;
            main.startSpeed = speed * strength;
            main.startLifetime = lifetime;
            main.duration = 0.18f;
            main.loop = false;
            main.gravityModifier = -0.15f;       // 略带上浮，像能量往上冒
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emis = ps.emission;
            emis.rateOverTime = 0f;
            emis.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.014f;

            ps.Play();
            Object.Destroy(go, lifetime + 0.8f);
        }
    }
}
