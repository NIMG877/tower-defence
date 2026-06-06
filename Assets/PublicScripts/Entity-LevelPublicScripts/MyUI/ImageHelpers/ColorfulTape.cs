using UnityEngine.UI;
using UnityEngine;
using TMPro;
using DG.Tweening;
using MyUI;

public class ColorfulTape : MaskableGraphic
{
    [SerializeField] private Color[] Colors;
    private float[] rates;
    [SerializeField] private TextMeshProUGUI[] texts;
    [SerializeField] private TextMeshProUGUI textsTV;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        base.OnPopulateMesh(vh);
        if (rates == null || rates.Length == 0)
            return;
        float width = rectTransform.rect.width;
        float height = rectTransform.rect.height;
        float currentX = 0;
        vh.Clear();
        for (int i = 0; i < Colors.Length; i++)
        {
            if (rates[i] != 0)
            {
                UIVertex[] uIVertices = new UIVertex[4];
                float widthi = width * rates[i];
                float d = texts[i].renderedWidth / 2 + 2;
                float tx;
                if (currentX + widthi / 2 < d)
                {
                    tx = d;
                }
                else if (currentX + widthi / 2 > width - d)
                {
                    tx = width - d;
                }
                else
                {
                    tx = currentX + widthi / 2;
                }
                for (int k = i - 1; k >= 0; k--)
                {
                    if (rates[k] != 0)
                    {
                        if (texts[k].renderedWidth / 2 + d > tx - texts[k].rectTransform.anchoredPosition.x)
                        {
                            tx = texts[k].rectTransform.anchoredPosition.x + texts[k].renderedWidth / 2 + d;
                        }
                        break;
                    }
                }
                texts[i].rectTransform.anchoredPosition = new Vector2(tx, 0);
                for (int j = 0; j < 4; j++)
                {
                    uIVertices[j] = UIVertex.simpleVert;
                    uIVertices[j].color = Colors[i];
                    switch (j)
                    {
                        case 0: uIVertices[j].position = new Vector2(currentX, height); break;
                        case 1: uIVertices[j].position = new Vector2(currentX + widthi, height); break;
                        case 2: uIVertices[j].position = new Vector2(currentX + widthi, 0); break;
                        case 3: uIVertices[j].position = new Vector2(currentX, 0); break;
                        default: break;
                    }
                }
                currentX += widthi;
                vh.AddUIVertexQuad(uIVertices);
            }
        }
        float tvl = textsTV.renderedWidth;
        if (width < tvl)
        {
            textsTV.rectTransform.anchoredPosition = new Vector2(tvl / 2, 0);
        }
        else
        {
            textsTV.rectTransform.anchoredPosition = new Vector2(width - tvl / 2, 0);
        }
    }
    public void SetValues(float[] values, float tv, float tr)
    {
        if (tv == 0)
            return;
        if (texts == null)
        {
            texts = GetComponentsInChildren<TextMeshProUGUI>();
        }
        rates = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == 0)
            {
                texts[i].enabled = false;
            }
            else
            {
                texts[i].enabled = true;
                rates[i] = values[i] / tv;
            }
        }
        DOTween.To((value) =>
        {
            //每个小文字改动
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != 0)
                {
                    texts[i].text = (values[i] * value).ToString("0");
                }
            }
            //总值文字改动
            textsTV.text = (tv * value).ToString("0") + "," + (tr * value * 100).ToString("0.0") + "%";
            //长度改动
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 500 * tr * value);
        }, 0, 1, 0.6f);
    }
}

