using System;

namespace AbilitySystem.Components
{
    [RegisterComponent("AttackRangeOverride")]
    public class AttackRangeOverrideComponent : IAbilityComponent
    {
        private (int x, int y)[] _range = Array.Empty<(int, int)>();

        public void OnInit(AbilityContext ctx, ParamList p)
        {
            var csv = p.GetString("range", "");
            if (string.IsNullOrEmpty(csv)) return;
            // semicolon separates cells, comma separates (x,y) within a cell.
            var cells = csv.Split(';');
            _range = new (int, int)[cells.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                var xy = cells[i].Split(',');
                if (xy.Length == 2 &&
                    int.TryParse(xy[0].Trim(), out var x) &&
                    int.TryParse(xy[1].Trim(), out var y))
                {
                    _range[i] = (x, y);
                }
            }
        }

        public void OnTrigger(AbilityContext ctx)
        {
            if (ctx.entity == null) return;
            ctx.entity.Vision.Range = _range;
        }

        public void OnTick(AbilityContext ctx, float dt) { }
        public void OnTeardown(AbilityContext ctx) { }
    }
}
