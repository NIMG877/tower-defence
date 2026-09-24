using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPoolOperation
{
    /// <summary>
    /// 实体池预热加载函数【不用额外调用】
    /// </summary>
    void PreWarm();
    /// <summary>
    /// 初始化，每次实体出池时调用【不用额外调用】
    /// </summary>
    void Initialize();
    /// <summary>
    /// 休眠，每次实体入池时调用【不用额外调用】
    /// </summary>
    void Dormancy();
}
