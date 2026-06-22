using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

public class SlimeTalent1 : Talent
{
    [SerializeField] private EntityID spawnEntityID;
    [SerializeField] private int spawnNum;
    [SerializeField] private float spawnGap;
    public override void Initialize()
    {
        base.Initialize();
        _thisEntity.OnBeforeDieAnimation += new Entity.OperationsBeforeDieAnimation(async () =>
        {
            Vector2 thisP = new Vector2((int)(this.transform.position.x + 0.5), (int)(this.transform.position.y + 0.5));
            for (int i = 0; i < spawnNum; i++)
            {
                Vector2 pos = new Vector2(Random.Range(-0.24f, 0.24f), Random.Range(-0.24f, 0.24f));
                EntityManager.Manager.SetMovableEntity(spawnEntityID, pos + thisP, _thisEntity.Camp, _thisEntity.MoveBase.CurrentPathSerial).MoveBase.SetMoveParameters(_thisEntity.MoveBase.CurrentPathSerial, _thisEntity.MoveBase.CurrentSectionSerial, 0);
                await UniTask.WaitForSeconds(spawnGap);
            }
        });
    }

}
