using UnityEngine.UI;
using UnityEngine;
using System.Collections.Generic;
using DG.Tweening;

public class RadarDataController : Graphic
{
    [Range(0, 360)]
    public float Rotation = 0;
    [SerializeField]
    private float[] _verticesDistances;
    private float[] _datas;
    public float size = 100;

    public void Set6Data(float[] datas)
    {
        _datas = datas;
        float[] targetVerticesD = new float[7];
        //hp-S:80000,atk-S:3500,atkspd-S:2.5,def-S:3000,mgr-S:70,mspd-S:2.5
        //归一化有界映射 1-e^(-1.7916·x/max)，下限 0.06，全量程使用
        float[] maxData = new float[6] { 80000, 3500, 2.5f, 3000, 70, 2.5f };
        for (int i = 0; i < 6; i++)
        {
            targetVerticesD[i] = Mathf.Max(0.06f, 1 - Mathf.Exp(-1.7916f * datas[i] / maxData[i]));
        }
        targetVerticesD[6] = targetVerticesD[0];

        DOTween.To((value) =>
        {
            for (int i = 0; i < _verticesDistances.Length; i++)
            {
                _verticesDistances[i] = (1 - value) * _verticesDistances[i] + value * targetVerticesD[i];
            }
            UpdateGeometry();
        }, 0, 1, 0.3f);
    }


    protected override void OnPopulateMesh(VertexHelper vh)
    {
        base.OnPopulateMesh(vh);
        vh.Clear();
        int sides = _verticesDistances.Length;
        float degree = 360 / sides;
        vh.AddVert(new Vector2(0, 0), new Color(1, 1, 1, 0.25f), Vector4.zero);
        float k2 = (1 - _verticesDistances[0])* (1 - _verticesDistances[0]);
        vh.AddVert(new Vector2(_verticesDistances[0] * size * Mathf.Cos(Rotation * Mathf.Deg2Rad), _verticesDistances[0] * size * Mathf.Sin(Rotation * Mathf.Deg2Rad)), new Color(0.9f, k2, k2, 1), Vector4.zero);
        int i;
        for (i = 1; i < _verticesDistances.Length; i++)
        {
            k2 = (1 - _verticesDistances[i]) * (1 - _verticesDistances[i]);
            vh.AddVert(new Vector2(_verticesDistances[i] * size * Mathf.Cos((i * degree + Rotation) * Mathf.Deg2Rad), _verticesDistances[i] * size * Mathf.Sin((i * degree + Rotation) * Mathf.Deg2Rad)), new Color(0.9f, k2, k2, 1), Vector4.zero);
            vh.AddTriangle(0, i + 1, i);
        }
        vh.AddTriangle(0, 1, i);
    }
}
