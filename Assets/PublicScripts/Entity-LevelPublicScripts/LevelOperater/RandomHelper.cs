using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomHelper
{
    private float[] _randoms;
    private int _index;
    private int _randomNum;
    private static RandomHelper _instance;
    public static RandomHelper Helper
    {
        get
        {
            if (_instance == null) _instance = new RandomHelper();
            return _instance;
        }
    }
    private RandomHelper()
    {
        _index = 0;
        _randomNum = 200;
        _randoms = new float[_randomNum];
        for (int i = 0; i < _randomNum; i++) _randoms[i] = Random.Range(0f, 1f);
    }
    // 单一随机源入口:useArray=true 走共享 200 项循环数组,_index 用模运算折回;
    // useArray=false 现取 UnityEngine.Random.Range。返回 [0,1](Unity 文档含上界)。
    private float NextFloat(bool useArray)
    {
        if (useArray)
        {
            _index = (_index + 1) % _randomNum;
            return _randoms[_index];
        }
        return Random.Range(0f, 1f);
    }

    public bool RandomP(float p, bool useArray = true)
    {
        if (p <= 0f) return false;
        if (p >= 1f) return true;
        return NextFloat(useArray) <= p;
    }
    // 均匀浮点。min/max 默认 [0,1] (Random.Range 含上界 1,默认调用与旧签名等价)。
    // max < min 静默 swap,min == max 恒返 min(0 * u == 0 路径自然成立)。
    public float RandomF(float min = 0f, float max = 1f, bool useArray = true)
    {
        if (max < min) (min, max) = (max, min);
        return min + (max - min) * NextFloat(useArray);
    }
    // 从 list 中按均匀分布选 1 个元素。null/空 list → string.Empty。
    // % len 把 Random.Range 含上界 1.0 造成的 (int)(1.0 * len) == len 边角折回 0。
    public string RandomL(string[] list, bool useArray = true)
    {
        if (list == null || list.Length == 0) return string.Empty;
        int idx = (int)(NextFloat(useArray) * list.Length) % list.Length;
        return list[idx];
    }
}
