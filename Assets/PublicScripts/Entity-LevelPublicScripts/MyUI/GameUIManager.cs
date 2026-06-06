using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MyUI;

public class GameUIManager : MonoBehaviour
{
    void Start()
    {
        //DamageStatisticData[] DamageStatisticData = new DamageStatisticData[4]
        //{
        //    new DamageStatisticData("Stward",new float[3]{0,1287341,0},new float[1]{ 0},new float[3]{ 1024,0,0 }),
        //    new DamageStatisticData("Kroos",new float[3]{1672391,0, 0},new float[1]{ 0},new float[3]{ 0,982,0 }),
        //    new DamageStatisticData("Melan",new float[3]{ 1072391, 0,0},new float[1]{ 0},new float[3]{ 58912,58972,0 }),
        //    new DamageStatisticData("Spot",new float[3]{288223,0,0},new float[1]{98611},new float[3]{ 87291,36821,0 })
        //};
        //SettlementPanel.Panel.SetDatas("ZT-EX-8   ”¿∫„÷˜Ã‚", true, 123.3f, DamageStatisticData);
        //PanelManager.Push(SettlementPanel.Panel);
        //PanelManager.Push(new CharacterSelectPanel());

        PanelManager.Push(HomePanel.Panel);
        //PanelManager.Push(MonsterHandbookPanel.Panel);
    }


}
