namespace DeskMadeline
{
    /// <summary>
    /// One axis of Monocle's VirtualIntegerAxis, at the overlap behaviour Celeste leaves at its
    /// default: press right, then left without letting go of right, and she goes left.
    /// </summary>
    /// <remarks>
    /// TakeNewer, and the way it is written is worth reading twice. Holding both directions does
    /// not work out which key arrived last -- it turns the axis around once, and remembers that
    /// it has, which comes to the same thing for one key added to another and costs no memory of
    /// when anything was pressed. Letting either go clears the turn, so the one still held wins
    /// and pressing the other again turns it round afresh.
    ///
    /// Its one oddity is vanilla's: both directions arriving on the very same frame from nothing
    /// turn a zero around, which is still zero, and she stands still until one is let go.
    ///
    /// Celeste builds MoveX, MoveY, GliderMoveY, Aim and Feather over the same bindings but as
    /// separate inputs, each with its own turn and its own controller deadzone, so the pet keeps
    /// one of these per input rather than sharing an answer between them.
    /// </remarks>
    internal sealed class IntegerAxis
    {
        int value;
        bool turned;

        public int Value => value;

        /// <summary>One frame of it: -1, 0 or 1, from whether each direction is held.</summary>
        public int Update(bool negative, bool positive)
        {
            if (positive && negative)
            {
                if (!turned) { value = -value; turned = true; }
            }
            else if (positive) { turned = false; value = 1; }
            else if (negative) { turned = false; value = -1; }
            else { turned = false; value = 0; }
            return value;
        }
    }
}
