using System.Collections.Generic;
using System.Drawing;

namespace DeskMadeline
{
    /// <summary>Desktop equivalent of Celeste.Holdable's player-facing contract.</summary>
    public interface IPetHoldable
    {
        PointF Pos { get; }
        PointF Speed { get; }
        Player Holder { get; }
        bool IsHeld { get; }
        bool BeingDragged { get; }
        bool SlowRun { get; }
        bool SlowFall { get; }
        bool CanPickup(Player player);
        bool Pickup(Player player);
        void Carry(PointF position);
        void Release(PointF force, IList<Solid> solids = null);
    }
}
