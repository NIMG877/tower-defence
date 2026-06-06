using UnityEngine;
using UnityEngine.UI;

public enum FadeMode
{
    LeftTORight,
    UpToDown,
    LeftUpTORightDown,
    LeftDownTORightUp,
}

public class FadeImageR2L : Image
{
    protected override void OnPopulateMesh(VertexHelper toFill)
    {
        base.OnPopulateMesh(toFill);
        UIVertex vertex = new UIVertex();
        for (int i = 0; i < toFill.currentVertCount; i++)
        {
            toFill.PopulateUIVertex(ref vertex, i);
            if (i == 0 || i == 1)
            {
                vertex.color = new Color32(vertex.color.r, vertex.color.g, vertex.color.b, 60);
                toFill.SetUIVertex(vertex, i);
            }
            else
            {
                vertex.color = new Color32(vertex.color.r, vertex.color.g, vertex.color.b, 255);
                toFill.SetUIVertex(vertex, i);
            }
        }
    }
}
