using System.Collections.Generic;
using UnityEngine;

namespace MyUI
{
    /// <summary>
    /// 攻击范围小图：以 self 为自身格、range 为模板瓦片，把 baseRange 铺进 area。
    /// LevelMessagePanel 的干员详情与 CharacterSelectPanel 的干员信息共用。
    /// 水平方向默认包围盒居中，leftAlign 时最左瓦片贴区域左缘；垂直方向始终居中。
    /// </summary>
    public class AttackRangeTiles
    {
        private readonly RectTransform _area;
        private readonly RectTransform _selfTile;
        private readonly List<RectTransform> _tiles;
        private readonly bool _leftAlign;

        public AttackRangeTiles(
            RectTransform area,
            RectTransform selfTile,
            RectTransform tileTemplate,
            bool leftAlign = false)
        {
            _area = area;
            _selfTile = selfTile;
            _tiles = new List<RectTransform> { tileTemplate };
            _leftAlign = leftAlign;
        }

        public void Show(List<Vector2Int> baseRange)
        {
            if (baseRange == null || baseRange.Count == 0)
            {
                Clear();
                return;
            }

            int maxX = int.MinValue;
            int maxY = int.MinValue;
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            var range = new (int x, int y)[baseRange.Count];
            for (int i = 0; i < range.Length; i++)
            {
                range[i] = (baseRange[i].y, -baseRange[i].x);
                maxX = Mathf.Max(maxX, range[i].x);
                maxY = Mathf.Max(maxY, range[i].y);
                minX = Mathf.Min(minX, range[i].x);
                minY = Mathf.Min(minY, range[i].y);
            }

            float tileWidth = _area.rect.width / (maxX - minX + 1);
            float tileHeight = _area.rect.height / (maxY - minY + 1);
            float tileSize = Mathf.Min(15, tileWidth, tileHeight);
            float centerX = (minX + maxX) / 2f;
            float centerY = (minY + maxY) / 2f;
            float originX = _leftAlign
                ? _area.rect.width * -0.5f + tileSize * (0.5f - minX)
                : -tileSize * centerX;
            while (_tiles.Count < range.Length)
            {
                _tiles.Add(Object.Instantiate(
                    _tiles[0].gameObject,
                    _area).GetComponent<RectTransform>());
            }
            for (int i = range.Length; i < _tiles.Count; i++)
                _tiles[i].gameObject.SetActive(false);
            for (int i = 0; i < range.Length; i++)
            {
                _tiles[i].sizeDelta = Vector2.one * tileSize * 0.95f;
                _tiles[i].anchoredPosition = new Vector2(
                    originX + tileSize * range[i].x,
                    tileSize * (range[i].y - centerY));
                _tiles[i].gameObject.SetActive(true);
            }
            _selfTile.sizeDelta = Vector2.one * tileSize * 0.95f;
            _selfTile.anchoredPosition = new Vector2(
                originX,
                -tileSize * centerY);
            _selfTile.gameObject.SetActive(true);
        }

        public void Clear()
        {
            _selfTile.gameObject.SetActive(false);
            for (int i = 0; i < _tiles.Count; i++)
                _tiles[i].gameObject.SetActive(false);
        }
    }
}
