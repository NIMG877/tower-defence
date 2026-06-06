using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LevelBuffManager : IManagerStartEnd
{
    private static LevelBuffManager _instance;
    public static LevelBuffManager Manager
    {
        get
        {
            if (_instance == null)
                _instance = new LevelBuffManager();
            return _instance;
        }
    }
    public void Initialize()
    {

    }
    public void ToStart()
    {
    }
    public void ToEnd()
    {
    }
}
