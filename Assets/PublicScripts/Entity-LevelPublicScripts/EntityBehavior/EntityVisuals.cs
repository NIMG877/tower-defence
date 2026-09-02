using System;
using DG.Tweening;
using UnityEngine;
using Spine.Unity;

/// <summary>
/// 实体纯视觉反馈：受击闪红、淡入/淡出。自订阅 OnAfterHurt（Entity.Dormancy 置空事件，无需手动退订）。
/// 回收由 Entity 决定：FadeOut 的完成回调交给调用方，本组件不发起回池。
/// </summary>
public class EntityVisuals : MonoBehaviour, IPoolOperation
{
    // applyType 约定值：2=无受击表现（与 EntityStats.ApplyDamage 调用方的约定一致，原 AnimationMachine 同判据）
    private const int ApplyTypeSuppressHurtFlash = 2;

    private Entity _entity;
    private SkeletonAnimation _skeleton;
    private Tween _tween;
    // Snapshotted g-channel at the moment FlashRed starts; the tween body
    // reads this each frame instead of capturing a closure.
    private float _flashRedBaselineG;

    public void PreWarm()
    {
        _entity = GetComponent<Entity>();
        _skeleton = transform.GetChild(0).GetComponent<SkeletonAnimation>();
    }

    public void Initialize()
    {
        FadeIn(0.2f);
        _entity.OnAfterHurt += HandleAfterHurt;
    }

    public void Dormancy()
    {
        // 中断未完成的补间：防止跨池复用的陈旧 FadeOut 完成回调触发二次回池
        if (_tween != null && _tween.IsActive()) _tween.Kill();
        _tween = null;
    }

    public void FadeIn(float duration)
    {
        _tween = DOTween.To(FadeAlpha, 0, 1, duration);
    }

    public void FadeOut(float duration, Action onComplete)
    {
        _tween = DOTween.To(FadeAlpha, 1, 0, duration).OnComplete(() => onComplete?.Invoke());
    }

    public void FlashRed(float duration)
    {
        _flashRedBaselineG = _skeleton.skeleton.GetColor().g;
        _tween = DOTween.To(ApplyFlashRed, 0, 2, duration);
    }

    // Method group (cached, no per-checkout closure) subscribed to OnAfterHurt in Initialize.
    private void HandleAfterHurt(Entity origin, float damage, float multiplyer, float defPenetrate, float mgrPenetrate, float defPenetrate_value, float mgrPenetrate_value, int damageType, int applyType, bool isDeadly)
    {
        if (applyType != ApplyTypeSuppressHurtFlash)
        {
            FlashRed(0.2f);
        }
    }

    // FlashRed tween body: a 0→2 ramp mapped to a red→white→red pulse that
    // returns to the baseline green channel captured at tween start.
    private void ApplyFlashRed(float value)
    {
        if (value < 1)
        {
            value = Math.Max(-value + _flashRedBaselineG, 0);
        }
        else
        {
            value = value - 1;
        }
        _skeleton.skeleton.SetColor(new Color(1, value, value));
    }

    // Applies a greyscale-with-double-alpha curve used by fade-in / fade-out.
    private void FadeAlpha(float value)
    {
        _skeleton.skeleton.SetColor(new Color(value, value, value, Math.Min(value * 2, 1)));
    }
}
