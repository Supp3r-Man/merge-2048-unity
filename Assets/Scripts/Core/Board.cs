using System;
using System.Collections.Generic;

namespace MergeUp.Core
{
    /// <summary>A square grid of tile values (0 = empty) and the slide-and-merge rules. No engine types.</summary>
    public sealed class Board
    {
        public readonly int Size;
        private readonly int[] cells;

        public Board(int size)
        {
            if (size < 2) throw new ArgumentOutOfRangeException("size", "board must be at least 2x2");
            Size = size;
            cells = new int[size * size];
        }

        public Board(int size, int[] values) : this(size)
        {
            if (values == null || values.Length != size * size)
                throw new ArgumentException("expected " + (size * size) + " cells");
            Array.Copy(values, cells, cells.Length);
        }

        public int Get(int row, int col) { return cells[row * Size + col]; }
        public void Set(int row, int col, int value) { cells[row * Size + col] = value; }

        public int[] ToArray()
        {
            int[] copy = new int[cells.Length];
            Array.Copy(cells, copy, cells.Length);
            return copy;
        }

        public void CopyFrom(int[] values)
        {
            Array.Copy(values, cells, cells.Length);
        }

        public Board Clone() { return new Board(Size, cells); }

        public bool SameAs(Board other)
        {
            if (other == null || other.Size != Size) return false;
            for (int i = 0; i < cells.Length; i++) if (cells[i] != other.cells[i]) return false;
            return true;
        }

        public int EmptyCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < cells.Length; i++) if (cells[i] == 0) n++;
                return n;
            }
        }

        public int MaxTile
        {
            get
            {
                int max = 0;
                for (int i = 0; i < cells.Length; i++) if (cells[i] > max) max = cells[i];
                return max;
            }
        }

        public int Sum
        {
            get
            {
                int s = 0;
                for (int i = 0; i < cells.Length; i++) s += cells[i];
                return s;
            }
        }

        /// <summary>True if any slide would change the board: an empty cell, or two equal neighbours.</summary>
        public bool HasMoves()
        {
            for (int r = 0; r < Size; r++)
            {
                for (int c = 0; c < Size; c++)
                {
                    int v = Get(r, c);
                    if (v == 0) return true;
                    if (c + 1 < Size && Get(r, c + 1) == v) return true;
                    if (r + 1 < Size && Get(r + 1, c) == v) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Slide one line toward index 0, merging equal neighbours. A tile made by a merge cannot merge again
        /// in the same move. Rewrites the line in place and returns the points scored (sum of the merged tiles).
        /// </summary>
        public static int SlideLine(int[] line, out bool moved)
        {
            int n = line.Length;
            int[] result = new int[n];
            int write = 0;
            int pending = 0; // value of the last placed tile if it may still absorb a neighbour, else 0
            int score = 0;
            for (int i = 0; i < n; i++)
            {
                int v = line[i];
                if (v == 0) continue;
                if (pending != 0 && pending == v)
                {
                    result[write - 1] = v * 2;
                    score += v * 2;
                    pending = 0;
                }
                else
                {
                    result[write] = v;
                    write++;
                    pending = v;
                }
            }
            moved = false;
            for (int i = 0; i < n; i++)
            {
                if (result[i] != line[i]) moved = true;
                line[i] = result[i];
            }
            return score;
        }

        /// <summary>The cell index of the i-th tile of line k when sliding in the given direction.</summary>
        private int Index(Direction dir, int k, int i)
        {
            switch (dir)
            {
                case Direction.Left: return k * Size + i;
                case Direction.Right: return k * Size + (Size - 1 - i);
                case Direction.Up: return i * Size + k;
                default: return (Size - 1 - i) * Size + k;
            }
        }

        /// <summary>Slide the whole board. Returns false (and changes nothing) when no tile can move.</summary>
        public bool Move(Direction dir, out int gained)
        {
            gained = 0;
            bool any = false;
            int[] line = new int[Size];
            for (int k = 0; k < Size; k++)
            {
                for (int i = 0; i < Size; i++) line[i] = cells[Index(dir, k, i)];
                bool moved;
                gained += SlideLine(line, out moved);
                if (moved)
                {
                    any = true;
                    for (int i = 0; i < Size; i++) cells[Index(dir, k, i)] = line[i];
                }
            }
            return any;
        }

        /// <summary>
        /// Where every tile goes if the board is slid in this direction, without changing the board.
        /// Written separately from Move so the two can be cross-checked in the tests.
        /// </summary>
        public List<TileMove> Trace(Direction dir)
        {
            List<TileMove> moves = new List<TileMove>();
            for (int k = 0; k < Size; k++)
            {
                int write = 0;
                int pending = 0;
                for (int i = 0; i < Size; i++)
                {
                    int from = Index(dir, k, i);
                    int v = cells[from];
                    if (v == 0) continue;
                    int dest;
                    bool merges;
                    if (pending != 0 && pending == v)
                    {
                        dest = Index(dir, k, write - 1);
                        pending = 0;
                        merges = true;
                    }
                    else
                    {
                        dest = Index(dir, k, write);
                        write++;
                        pending = v;
                        merges = false;
                    }
                    TileMove m;
                    m.FromRow = from / Size;
                    m.FromCol = from % Size;
                    m.ToRow = dest / Size;
                    m.ToCol = dest % Size;
                    m.Value = v;
                    m.Merges = merges;
                    moves.Add(m);
                }
            }
            return moves;
        }
    }
}
