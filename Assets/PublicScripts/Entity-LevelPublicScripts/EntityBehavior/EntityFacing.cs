using UnityEngine;
using Spine.Unity;

/// <summary>
/// 实体朝向（左右翻转 + 上下标记）。朝向跨池复用保持原状（skeleton 子物体旋转同样跨复用持久，
/// 两者必须一致地不复位——与拆分前行为等价）。
/// </summary>
public class EntityFacing : MonoBehaviour, IPoolOperation
{
    private SkeletonAnimation _skeleton;
    private (bool left, bool up) _direction;

    public (bool left, bool up) CurrentDirection { get { return _direction; } }

    public void PreWarm()
    {
        _skeleton = transform.GetChild(0).GetComponent<SkeletonAnimation>();
    }

    public void Initialize() { }
    public void Dormancy() { }

    /// <summary>
    /// Sets the entity facing direction based on a world-space target.
    /// </summary>
    /// <param name="target">World-space target position</param>
    public void SetDirection(Vector2 target)
    {
        // Note: the trailing `return;` on the !left branch is intentional and preserved.
        // Removing it would change rotation behavior for that branch.
        void SetDirectionBase()
        {
            float ry = _skeleton.transform.rotation.y;
            if (_direction.left)
            {
                if (ry != 1) _skeleton.transform.Rotate(new Vector3(0, (1 - ry) * 180, 0));
            }
            else
            {
                if (ry != 0) _skeleton.transform.Rotate(new Vector3(0, -ry * 180, 0)); return;
            }
        }
        float dx = target.x - transform.position.x;
        float dy = target.y - transform.position.y;
        if (dy > 0)
        {
            _direction.up = true;
        }
        else if (dy < 0)
        {
            _direction.up = false;
        }
        if (dx > 0)
        {
            _direction.left = false;
            SetDirectionBase();
        }
        else if (dx < 0)
        {
            _direction.left = true;
            SetDirectionBase();
        }
    }
}
