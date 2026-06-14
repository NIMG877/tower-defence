using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;

[CreateAssetMenu(
    menuName = "TD/Entity/Animation Resources",
    fileName = "AnimationResources")]
public sealed class AnimationResources : ScriptableObject
{
    [Serializable]
    public sealed class DefaultAnimationTemplate
    {
        public AnimationReferenceAsset Default;
        public AnimationReferenceAsset Idle;
        public AnimationReferenceAsset Move;
        public AnimationReferenceAsset Start;
        public AnimationReferenceAsset Die;
        public AnimationReferenceAsset AttackBegin;
        public AnimationReferenceAsset[] AttackRemote = Array.Empty<AnimationReferenceAsset>();
        public AnimationReferenceAsset[] AttackClose = Array.Empty<AnimationReferenceAsset>();
        public AnimationReferenceAsset AttackEnd;
        
    }

    [Serializable]
    public sealed class ChargeAttackAnimationTemplate
    {
        public AnimationReferenceAsset ChargeBegin;
        public AnimationReferenceAsset[] Charge = Array.Empty<AnimationReferenceAsset>();
        public AnimationReferenceAsset ChargeEnd;
    }

    [Serializable]
    public sealed class JumpAnimationTemplate
    {
        public AnimationReferenceAsset Begin;
        public AnimationReferenceAsset Loop;
        public AnimationReferenceAsset End;
    }

    [Serializable]
    private sealed class NamedAnimation
    {
        public string Name;
        public AnimationReferenceAsset Animation;
    }

    [Serializable]
    private sealed class NamedAnimationGroup
    {
        public string Name;
        public AnimationReferenceAsset[] Animations = Array.Empty<AnimationReferenceAsset>();
    }

    [Header("Fixed Templates")]
    [SerializeField] private DefaultAnimationTemplate _defaults = new DefaultAnimationTemplate();
    [SerializeField] private ChargeAttackAnimationTemplate _chargeAttack = new ChargeAttackAnimationTemplate();
    [SerializeField] private JumpAnimationTemplate _jump = new JumpAnimationTemplate();

    [Header("Named Resources")]
    [SerializeField] private List<NamedAnimation> _animations = new List<NamedAnimation>();
    [SerializeField] private List<NamedAnimationGroup> _animationGroups = new List<NamedAnimationGroup>();

    private Dictionary<string, AnimationReferenceAsset> _animationLookup;
    private Dictionary<string, AnimationReferenceAsset[]> _animationGroupLookup;

    public DefaultAnimationTemplate Defaults => _defaults;
    public ChargeAttackAnimationTemplate ChargeAttack => _chargeAttack;
    public JumpAnimationTemplate Jump => _jump;

    public bool TryGetAnimation(string name, out AnimationReferenceAsset animation)
    {
        EnsureLookup();
        return _animationLookup.TryGetValue(name, out animation);
    }

    public AnimationReferenceAsset GetAnimation(string name)
    {
        if (TryGetAnimation(name, out AnimationReferenceAsset animation))
        {
            return animation;
        }

        throw new KeyNotFoundException(
            $"Animation '{name}' was not found in animation library '{this.name}'.");
    }

    public bool TryGetAnimationGroup(string name, out AnimationReferenceAsset[] animations)
    {
        EnsureLookup();
        return _animationGroupLookup.TryGetValue(name, out animations);
    }

    public AnimationReferenceAsset[] GetAnimationGroup(string name)
    {
        if (TryGetAnimationGroup(name, out AnimationReferenceAsset[] animations))
        {
            return animations;
        }

        throw new KeyNotFoundException(
            $"Animation group '{name}' was not found in animation library '{this.name}'.");
    }

    private void OnEnable()
    {
        RebuildLookup();
    }

    private void EnsureLookup()
    {
        if (_animationLookup == null || _animationGroupLookup == null)
        {
            RebuildLookup();
        }
    }

    private void RebuildLookup()
    {
        EnsureSerializedFields();
        _animationLookup = new Dictionary<string, AnimationReferenceAsset>(StringComparer.Ordinal);
        _animationGroupLookup = new Dictionary<string, AnimationReferenceAsset[]>(StringComparer.Ordinal);

        foreach (NamedAnimation entry in _animations)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!_animationLookup.ContainsKey(entry.Name))
            {
                _animationLookup.Add(entry.Name, entry.Animation);
            }
        }

        foreach (NamedAnimationGroup entry in _animationGroups)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!_animationGroupLookup.ContainsKey(entry.Name))
            {
                _animationGroupLookup.Add(entry.Name, entry.Animations);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureSerializedFields();
        ValidateNamedAnimations();
        ValidateFixedTemplates();
        RebuildLookup();
    }

    private void ValidateNamedAnimations()
    {
        var animationNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < _animations.Count; i++)
        {
            NamedAnimation entry = _animations[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            {
                Debug.LogWarning($"[{name}] Named animation at index {i} has an empty name.", this);
                continue;
            }
            if (!animationNames.Add(entry.Name))
            {
                Debug.LogWarning($"[{name}] Duplicate named animation '{entry.Name}'.", this);
            }
            if (entry.Animation == null)
            {
                Debug.LogWarning($"[{name}] Named animation '{entry.Name}' has no resource.", this);
            }
        }

        var groupNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < _animationGroups.Count; i++)
        {
            NamedAnimationGroup entry = _animationGroups[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
            {
                Debug.LogWarning($"[{name}] Named animation group at index {i} has an empty name.", this);
                continue;
            }
            if (!groupNames.Add(entry.Name))
            {
                Debug.LogWarning($"[{name}] Duplicate named animation group '{entry.Name}'.", this);
            }
            ValidateGroup(entry.Animations, $"named animation group '{entry.Name}'", false);
        }
    }

    private void ValidateFixedTemplates()
    {
        ValidateRequiredAnimation(_defaults.Default, "default animation");
        ValidateRequiredAnimation(_defaults.Idle, "idle animation");
        ValidateRequiredAnimation(_defaults.Move, "move animation");
        ValidateGroup(_defaults.AttackRemote, "default remote attack group", true);
        ValidateGroup(_defaults.AttackClose, "default close attack group", true);

        ValidateGroup(_chargeAttack.Charge, "charge animation group", false);
        bool hasAnyChargeAttack =
            _chargeAttack.ChargeBegin != null ||
            HasAnimations(_chargeAttack.Charge) ||
            _chargeAttack.ChargeEnd != null;
        bool hasAllChargeAttack =
            _chargeAttack.ChargeBegin != null &&
            HasAnimations(_chargeAttack.Charge) &&
            _chargeAttack.ChargeEnd != null;
        if (hasAnyChargeAttack && !hasAllChargeAttack)
        {
            Debug.LogWarning(
                $"[{name}] Charge attack template must configure Charge Begin, Charge, and Charge End together.",
                this);
        }

        bool hasAnyJump = _jump.Begin != null || _jump.Loop != null || _jump.End != null;
        bool hasAllJump = _jump.Begin != null && _jump.Loop != null && _jump.End != null;
        if (hasAnyJump && !hasAllJump)
        {
            Debug.LogWarning($"[{name}] Jump template must configure Begin, Loop, and End together.", this);
        }
    }

    private static bool HasAnimations(AnimationReferenceAsset[] animations)
    {
        return animations != null && animations.Length > 0;
    }

    private void ValidateRequiredAnimation(AnimationReferenceAsset animation, string label)
    {
        if (animation == null)
        {
            Debug.LogWarning($"[{name}] The {label} is empty.", this);
        }
    }

    private void ValidateGroup(AnimationReferenceAsset[] animations, string label, bool required)
    {
        if (animations == null || animations.Length == 0)
        {
            if (required)
            {
                Debug.LogWarning($"[{name}] The {label} is empty.", this);
            }
            return;
        }

        for (int i = 0; i < animations.Length; i++)
        {
            if (animations[i] == null)
            {
                Debug.LogWarning($"[{name}] The {label} contains an empty resource at index {i}.", this);
            }
        }
    }
#endif

    private void EnsureSerializedFields()
    {
        if (_defaults == null) _defaults = new DefaultAnimationTemplate();
        if (_chargeAttack == null) _chargeAttack = new ChargeAttackAnimationTemplate();
        if (_jump == null) _jump = new JumpAnimationTemplate();
        if (_animations == null) _animations = new List<NamedAnimation>();
        if (_animationGroups == null) _animationGroups = new List<NamedAnimationGroup>();
    }
}
