namespace MergeUp.Core
{
    /// <summary>Row 0 is the top row, so Up moves tiles toward row 0 and Left toward column 0.</summary>
    public enum Direction
    {
        Left,
        Right,
        Up,
        Down
    }

    /// <summary>One tile's journey during a move, used to animate the slide.</summary>
    public struct TileMove
    {
        public int FromRow;
        public int FromCol;
        public int ToRow;
        public int ToCol;
        /// <summary>The tile's value before the move (a merge's result is the sum of its two tiles).</summary>
        public int Value;
        /// <summary>True for the tile that lands on an occupied cell and is absorbed by the merge.</summary>
        public bool Merges;
    }
}
