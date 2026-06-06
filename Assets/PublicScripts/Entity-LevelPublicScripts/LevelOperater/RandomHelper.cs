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
            if (_instance == null)
            {
                _instance = new RandomHelper();
            }
            return _instance;
        }
    }
    private RandomHelper()
    {
        _index = 0;
        _randomNum = 200;
        _randoms = new float[_randomNum];
        for (int i = 0; i < _randomNum; i++)
        {
            _randoms[i] = Random.Range(0f, 1f);
        }
    }
    public bool RandomP(float p, bool useArray = true)
    {
        if (useArray)
        {
            if (_index == _randomNum - 1)
            {
                _index = 0;
            }
            else
            {
                _index++;
            }
            if (_randoms[_index] <= p)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            if (Random.Range(0f, 1f) <= p)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

    }
    public float RandomF(bool useArray = true)
    {
        if (useArray)
        {
            if (_index == _randomNum - 1)
            {
                _index = 0;
            }
            else
            {
                _index++;
            }
            return _randoms[_index];
        }
        else
        {
            return Random.Range(0f, 1f);
        }
    }
}
