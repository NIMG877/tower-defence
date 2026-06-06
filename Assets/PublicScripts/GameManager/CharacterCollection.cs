using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu]
public class CharacterCollection : ScriptableObject
{
    [Serializable]
    public class CharacterData
    {
        [Tooltip("干员代号")] public string Name;
        [Tooltip("干员英文代号")] public string EnglishName;
        [Tooltip("干员职业")] public string Job;
        [Tooltip("干员职业分支")] public string SubJob;
        [Tooltip("稀有度：1，2，3，4，5，6")] public int Rarity;
    }
    public CharacterData[] CharacterDatas;
    public CharacterData GetCharacterData(string englishName)
    {
        for (int i = 0; i < CharacterDatas.Length; i++)
        {
            if (CharacterDatas[i].EnglishName == englishName)
                return CharacterDatas[i];
        }
        return null;
    }
}
