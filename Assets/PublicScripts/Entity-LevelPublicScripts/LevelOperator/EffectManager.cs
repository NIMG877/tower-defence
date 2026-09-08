using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Pool;
using Object = UnityEngine.Object;


public class EffectManager : IManagerStartEnd
{
    private static EffectManager _instance;
    public static EffectManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new EffectManager();
            return _instance;
        }
    }

    /// <summary>一条借出记录：实例、粒子/拖尾组件、autoReturn 时被临时改写的粒子原值。
    /// 原实现用三个平行列表靠索引对齐，归还时 IndexOf + 三表同步删除，现合并为单记录。</summary>
    private sealed class BorrowedEffect
    {
        public GameObject Instance;
        public ParticleSystem[] Particles;
        public TrailRenderer[] Trails;
        public (bool loop, bool prewarm)[] OriginSets;
        public bool AutoReturn;
    }

    /// <summary>单个 prefab 的特效池：空闲集合由 ObjectPool 管理（LIFO），
    /// 粒子/拖尾的重置在借出路径上做，autoReturn 记录与自动回收循环留在本类。</summary>
    private class EffectPool
    {
        public readonly GameObject EffectPrefab;
        private readonly ObjectPool<GameObject> _pool;
        // 归还成功后回调 Manager（移除实例归属映射）：自动归还路径也走这里，
        // 保证映射移除只发生在归还真正落地之后
        private readonly Action<GameObject> _onReturned;
        private readonly List<BorrowedEffect> _borrowed = new List<BorrowedEffect>();
        // 拖尾基准时长按实例缓存：借出时绝对赋值 baseTime/timeScale，
        // 修掉原实现每次出池 /= timeScale 的复用累除 bug
        private readonly Dictionary<GameObject, float[]> _trailBaseTimes =
            new Dictionary<GameObject, float[]>();
        private bool _autoReturnLoopRunning;

        public EffectPool(GameObject effectPrefab, Action<GameObject> onReturned)
        {
            EffectPrefab = effectPrefab;
            _onReturned = onReturned;
            _pool = new ObjectPool<GameObject>(
                () => Object.Instantiate(effectPrefab),
                actionOnRelease: instance => instance.SetActive(false),
                actionOnDestroy: instance => Object.Destroy(instance),
                collectionCheck: true);
        }

        public GameObject CreateEffect(Vector3 worldPosition, Quaternion worldQuaternion, Transform parent, float timeScale, bool autoReturn)
        {
            GameObject newEffect = _pool.Get();
            newEffect.transform.SetParent(parent);
            newEffect.transform.SetPositionAndRotation(worldPosition, worldQuaternion);

            ParticleSystem[] particleSystems = newEffect.GetComponents<ParticleSystem>();
            TrailRenderer[] trailRenderers = newEffect.GetComponents<TrailRenderer>();
            for (int i = 0; i < particleSystems.Length; i++)
            {
                // 设置粒子速度并重新开始播放
                ParticleSystem.MainModule mainModule = particleSystems[i].main;
                mainModule.simulationSpeed = timeScale;
                particleSystems[i].Play();
            }
            if (trailRenderers.Length > 0)
            {
                float[] baseTimes = CacheTrailBaseTimes(newEffect, trailRenderers);
                for (int i = 0; i < trailRenderers.Length; i++)
                {
                    trailRenderers[i].Clear();
                    trailRenderers[i].time = baseTimes[i] / timeScale;
                }
            }

            BorrowedEffect record = new BorrowedEffect
            {
                Instance = newEffect,
                Particles = particleSystems,
                Trails = trailRenderers,
            };
            _borrowed.Add(record);
            if (autoReturn)
            {
                MakeAutoReturn(record);
                EnsureAutoReturnLoop();
            }
            newEffect.SetActive(true);
            return newEffect;
        }

        /// <summary>把借出记录切到"播完自动归还"：关掉 loop/prewarm 保证粒子能播完，
        /// 原值记录在 OriginSets，归还时还原。已在自动归还中的实例直接跳过。</summary>
        public void AddToAutoReturn(GameObject effectInstance)
        {
            BorrowedEffect record = FindRecord(effectInstance);
            if (record == null)
                throw new InvalidOperationException(
                    $"SetEffectAutoReturn 收到了不属于任何借出记录的特效实例: {effectInstance.name}");
            // 已在自动归还中不重复快照——重拍 OriginSets 会用当前值（已被关掉的
            // loop/prewarm）覆盖原始备份，破坏归还时的还原
            if (record.AutoReturn)
                return;
            MakeAutoReturn(record);
            // 标记自动归还必须点火清扫循环：循环在无任何 AutoReturn 记录时退出，
            // 若只打标记不点火，bullet trail 这类由 SetEffectAutoReturn 转入
            // 自动归还的实例会成为无人清扫的孤儿，永不归还
            EnsureAutoReturnLoop();
        }

        /// <summary>保证清扫循环在岗。调用时机必然刚借出/标记了 AutoReturn 记录。</summary>
        private void EnsureAutoReturnLoop()
        {
            if (_autoReturnLoopRunning)
                return;
            _autoReturnLoopRunning = true;
            UpdateAutoDelete().Forget();
        }

        private void MakeAutoReturn(BorrowedEffect record)
        {
            record.OriginSets = new (bool loop, bool prewarm)[record.Particles.Length];
            for (int i = 0; i < record.Particles.Length; i++)
            {
                ParticleSystem.MainModule mainModule = record.Particles[i].main;
                record.OriginSets[i].loop = mainModule.loop;
                record.OriginSets[i].prewarm = mainModule.prewarm;
                mainModule.loop = false;
                mainModule.prewarm = false;
            }
            record.AutoReturn = true;
        }

        public void ReturnEffect(GameObject effectInstance)
        {
            // 归还与借出一一配对：未知实例在此 NRE、双归还由池的 collectionCheck
            // 抛出——都是逻辑错误，响亮失败，不做静默跳过
            BorrowedEffect record = FindRecord(effectInstance);
            effectInstance.SetActive(false);
            for (int i = 0; i < record.Particles.Length; i++)
            {
                record.Particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (record.AutoReturn)
            {
                for (int i = 0; i < record.Particles.Length; i++)
                {
                    ParticleSystem.MainModule mainModule = record.Particles[i].main;
                    mainModule.loop = record.OriginSets[i].loop;
                    mainModule.prewarm = record.OriginSets[i].prewarm;
                }
            }
            _borrowed.Remove(record);
            _pool.Release(effectInstance);
            _onReturned?.Invoke(effectInstance);
        }

        public void ReturnAllEffect()
        {
            for (int i = _borrowed.Count - 1; i >= 0; i--)
            {
                ReturnEffect(_borrowed[i].Instance);
            }
        }

        /// <summary>关末销毁池（池生命周期与关卡对齐，同 EntityPool.Teardown）：
        /// 调用前 ReturnAllEffect 必须先行，全部实例已在空闲集合内，
        /// Clear 触发 actionOnDestroy 逐个 Destroy。</summary>
        public void Teardown()
        {
            _pool.Clear();
        }

        private BorrowedEffect FindRecord(GameObject instance)
        {
            for (int i = 0; i < _borrowed.Count; i++)
            {
                if (_borrowed[i].Instance == instance)
                    return _borrowed[i];
            }
            return null;
        }

        private bool HasAutoReturn()
        {
            for (int i = 0; i < _borrowed.Count; i++)
            {
                if (_borrowed[i].AutoReturn)
                    return true;
            }
            return false;
        }

        private float[] CacheTrailBaseTimes(GameObject instance, TrailRenderer[] trails)
        {
            if (_trailBaseTimes.TryGetValue(instance, out float[] cached) && cached.Length == trails.Length)
                return cached;
            float[] baseTimes = new float[trails.Length];
            for (int i = 0; i < trails.Length; i++)
            {
                baseTimes[i] = trails[i].time;
            }
            _trailBaseTimes[instance] = baseTimes;
            return baseTimes;
        }

        private async UniTaskVoid UpdateAutoDelete()
        {
            int gap = 0;
            try
            {
                while (HasAutoReturn())
                {
                    if (gap < 3)
                    {
                        gap++;
                        if (await UniTask.WaitForFixedUpdate(LevelResourceSharing.LevelCtk).SuppressCancellationThrow())
                            return;
                    }
                    else
                    {
                        gap = 0;
                        for (int i = _borrowed.Count - 1; i >= 0; i--)
                        {
                            BorrowedEffect record = _borrowed[i];
                            if (!record.AutoReturn)
                                continue;
                            bool isEnd = true;
                            for (int j = 0; j < record.Particles.Length; j++)
                            {
                                if (!record.Particles[j].isStopped)
                                {
                                    isEnd = false;
                                    break;
                                }
                            }
                            if (isEnd)
                            {
                                for (int j = 0; j < record.Trails.Length; j++)
                                {
                                    if (record.Trails[j].positionCount > 0)
                                    {
                                        isEnd = false;
                                        break;
                                    }
                                }
                            }
                            if (isEnd)
                            {
                                ReturnEffect(record.Instance);
                            }
                        }
                    }
                }
            }
            finally
            {
                _autoReturnLoopRunning = false;
            }
        }
    }

    private Dictionary<GameObject, EffectPool> _effectPool;
    // 借出实例 -> 所属池：归还/转自动归还按实例路由，不再要求调用方传对 prefab 键
    private readonly Dictionary<GameObject, EffectPool> _poolOfInstance =
        new Dictionary<GameObject, EffectPool>();

    public GameObject CreateEffect(GameObject effectPrefab, Vector3 worldPosition, Quaternion worldQuaternion, Transform transform, float timeScale, bool autoReturn)
    {
        if (!_effectPool.TryGetValue(effectPrefab, out EffectPool pool))
        {
            pool = new EffectPool(effectPrefab, instance => _poolOfInstance.Remove(instance));
            _effectPool.Add(effectPrefab, pool);
        }
        GameObject instance = pool.CreateEffect(worldPosition, worldQuaternion, transform, timeScale, autoReturn);
        _poolOfInstance[instance] = pool;
        return instance;
    }

    public void SetEffectAutoReturn(GameObject effectInstance)
    {
        // 索引器直取：实例必然先经 CreateEffect 借出，缺失即逻辑错误，响亮失败
        _poolOfInstance[effectInstance].AddToAutoReturn(effectInstance);
    }

    public void ReturnEffect(GameObject effectInstance)
    {
        // 映射移除由池的归还回调执行（自动归还路径同样覆盖），保证先归还后摘映射
        _poolOfInstance[effectInstance].ReturnEffect(effectInstance);
    }

    public void Initialize()
    {
        if (_effectPool == null)
            _effectPool = new Dictionary<GameObject, EffectPool>();
    }

    public void ToEnd()
    {
        // 池生命周期与关卡对齐：先归还全部活跃实例，再销毁全部池实例并清空池记录，
        // 下一关由 CreateEffect 按需重建。LM 下不留失活克隆。
        foreach (var kv in _effectPool)
        {
            kv.Value.ReturnAllEffect();
            kv.Value.Teardown();
        }
        _effectPool.Clear();
    }

    public void ToStart()
    {
    }

}
