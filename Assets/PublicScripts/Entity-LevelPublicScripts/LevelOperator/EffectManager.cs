using Codice.CM.Client.Differences.Merge;
using Codice.CM.Common;
using Cysharp.Threading.Tasks;

using System.Collections.Generic;
using UnityEngine;


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
    private class EffectPool
    {
        public GameObject EffectPrefab;
        private List<(ParticleSystem[] particals, TrailRenderer[] trails, (bool loop, bool prewarm)[] originParticleSet)> _effectAutoMessages;
        private List<GameObject> _effectAutoDelete;
        private List<GameObject> _effectInstanceNotAutoDelete;
        private List<GameObject> _effectsInPool;
        public EffectPool(GameObject effectPrefab)
        {
            EffectPrefab = effectPrefab;
            _effectAutoMessages = new List<(ParticleSystem[] particals, TrailRenderer[] trails, (bool loop, bool prewarm)[] originParticleSet)>();
            _effectAutoDelete = new List<GameObject>();
            _effectInstanceNotAutoDelete = new List<GameObject>();
            _effectsInPool = new List<GameObject>();
        }
        private async UniTaskVoid UpdateAutoDelete()
        {
            int gap = 0;
            while (_effectAutoDelete.Count > 0)
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
                    for (int i = _effectAutoMessages.Count - 1; i >= 0; i--)
                    {
                        bool isEnd = true;
                        (ParticleSystem[] pi, TrailRenderer[] ti, (bool loop, bool prewarm)[] ps) = _effectAutoMessages[i];
                        for (int j = 0; j < pi.Length; j++)
                        {
                            if (!pi[j].isStopped)
                            {
                                isEnd = false;
                                break;
                            }
                        }
                        if (isEnd)
                        {
                            for (int j = 0; j < ti.Length; j++)
                            {
                                if (ti[j].positionCount > 0)
                                {
                                    isEnd = false;
                                    break;
                                }
                            }
                        }
                        if (isEnd)
                        {
                            ReturnEffect(_effectAutoDelete[i]);
                        }
                    }
                }

            }
        }
        public void AddToAutoReturn(GameObject effectInstance, ParticleSystem[] particleSystems, TrailRenderer[] trailRenderers)
        {
            if (!_effectAutoDelete.Contains(effectInstance))
            {
                (bool loop, bool prewarm)[] particleSets = new (bool loop, bool prewarm)[particleSystems.Length];
                for (int i = 0; i < particleSystems.Length; i++)
                {
                    ParticleSystem.MainModule mainModule = particleSystems[i].main;
                    particleSets[i].loop = mainModule.loop;
                    particleSets[i].prewarm = mainModule.prewarm;
                    mainModule.loop = false;
                    mainModule.prewarm = false;
                }
                _effectInstanceNotAutoDelete.Remove(effectInstance);
                _effectAutoDelete.Add(effectInstance);
                _effectAutoMessages.Add((particleSystems, trailRenderers, particleSets));
                if (_effectAutoDelete.Count == 1)
                {
                    UpdateAutoDelete().Forget();
                }
            }
        }
        public GameObject CreateEffect(Vector3 worldPosition, Quaternion worldQuaternion, Transform transform, float timeScale, bool autoReturn)
        {
            GameObject newEffect;
            if (_effectsInPool.Count > 0)
            {
                newEffect = _effectsInPool[0];
                _effectsInPool.RemoveAt(0);
                newEffect.transform.parent = transform;
                newEffect.transform.SetPositionAndRotation(worldPosition, worldQuaternion);
            }
            else
            {
                newEffect = Object.Instantiate(EffectPrefab, worldPosition, Quaternion.identity, transform);
            }
            ParticleSystem[] particleSystems = newEffect.GetComponents<ParticleSystem>();
            TrailRenderer[] trailRenderers = newEffect.GetComponents<TrailRenderer>();
            for (int i = 0; i < particleSystems.Length; i++)
            {
                // ���������ٶ�
                ParticleSystem.MainModule mainModule = particleSystems[i].main;
                mainModule.simulationSpeed = timeScale;  // ���ò����ٶ�

                // ���¿�ʼ����ϵͳ
                particleSystems[i].Play();
            }
            for (int i = 0; i < trailRenderers.Length; i++)
            {
                trailRenderers[i].Clear();
                trailRenderers[i].time /= timeScale;
            }
            if (autoReturn)
            {
                AddToAutoReturn(newEffect, particleSystems, trailRenderers);
            }
            else
            {
                _effectInstanceNotAutoDelete.Add(newEffect);
            }
            newEffect.SetActive(true);
            return newEffect;
        }
        public void ReturnEffect(GameObject effectInstance)
        {
            effectInstance.SetActive(false);
            ParticleSystem[] particleSystems = effectInstance.GetComponents<ParticleSystem>();
            TrailRenderer[] trailRenderers = effectInstance.GetComponents<TrailRenderer>();
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (_effectAutoDelete.Contains(effectInstance))
            {
                int index = _effectAutoDelete.IndexOf(effectInstance);
                for (int i = 0; i < particleSystems.Length; i++)
                {
                    ParticleSystem.MainModule mainModule = particleSystems[i].main;
                    mainModule.loop = _effectAutoMessages[index].originParticleSet[i].loop;
                    mainModule.prewarm = _effectAutoMessages[index].originParticleSet[i].prewarm;
                }
                _effectAutoDelete.RemoveAt(index);
                _effectAutoMessages.RemoveAt(index);
            }
            else
            {
                _effectInstanceNotAutoDelete.Remove(effectInstance);
            }
            _effectsInPool.Add(effectInstance);
        }
        public void ReturnAllEffect()
        {
            for (int i = _effectInstanceNotAutoDelete.Count - 1; i >= 0; i--)
            {
                ReturnEffect(_effectInstanceNotAutoDelete[i]);
            }
            for (int i = _effectAutoDelete.Count - 1; i >= 0; i--)
            {
                ReturnEffect(_effectAutoDelete[i]);
            }
        }
    }
    private List<EffectPool> _effectPool;

    public GameObject CreateEffect(GameObject effectPrefab, Vector3 worldPosition, Quaternion worldQuaternion, Transform transform, float timeScale, bool autoReturn)
    {
        for (int i = 0; i < _effectPool.Count; i++)
        {
            if (_effectPool[i].EffectPrefab == effectPrefab)
            {
                return _effectPool[i].CreateEffect(worldPosition, worldQuaternion, transform, timeScale, autoReturn);
            }
        }
        EffectPool newPool = new EffectPool(effectPrefab);
        _effectPool.Add(newPool);
        return newPool.CreateEffect(worldPosition, worldQuaternion, transform, timeScale, autoReturn);
    }
    public void SetEffectAutoReturn(GameObject effectPrefab, GameObject effectInstance)
    {
        for (int i = 0; i < _effectPool.Count; i++)
        {

            if (_effectPool[i].EffectPrefab == effectPrefab)
            {
                _effectPool[i].AddToAutoReturn(effectInstance, effectInstance.GetComponents<ParticleSystem>(), effectInstance.GetComponents<TrailRenderer>());
            }
        }
    }
    public void ReturnEffect(GameObject effectPrefab, GameObject effectInstance)
    {
        for (int i = 0; i < _effectPool.Count; i++)
        {

            if (_effectPool[i].EffectPrefab == effectPrefab)
            {
                _effectPool[i].ReturnEffect(effectInstance);
            }
        }
    }
    public void Initialize()
    {
        if (_effectPool == null)
            _effectPool = new List<EffectPool>();
    }

    public void ToEnd()
    {
        for (int i = 0; i < _effectPool.Count; i++)
        {
            _effectPool[i].ReturnAllEffect();
        }
    }

    public void ToStart()
    {
    }

}
