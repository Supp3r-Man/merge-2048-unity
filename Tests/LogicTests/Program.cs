// Dependency-free test runner for the engine-free game rules (Assets/Scripts/Core).
// Exit code 0 = all passed, 1 = at least one failed. Lines starting with "info:" are measurements, not checks.
using System;
using System.Collections.Generic;
using MergeUp.Core;

namespace LogicTests
{
    public static class Program
    {
        private static int passed;
        private static int failed;

        private static void Run(string name, Action body)
        {
            try
            {
                body();
                passed++;
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + e.Message);
            }
        }

        private static void True(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(what + ": expected " + expected + " but got " + actual);
        }

        private static void Near(float expected, float actual, float eps, string what)
        {
            if (Math.Abs(expected - actual) > eps)
                throw new Exception(what + ": expected " + expected + " but got " + actual);
        }

        private static string Show(int[] a)
        {
            return "[" + string.Join(",", Array.ConvertAll(a, delegate (int x) { return x.ToString(); })) + "]";
        }

        private static void SameArray(int[] expected, int[] actual, string what)
        {
            bool same = expected.Length == actual.Length;
            for (int i = 0; same && i < expected.Length; i++) if (expected[i] != actual[i]) same = false;
            if (!same) throw new Exception(what + ": expected " + Show(expected) + " but got " + Show(actual));
        }

        private static int[] A(params int[] values) { return values; }

        // ---- helpers ----

        private static void LineCase(string name, int[] input, int[] expected, int score, bool moved)
        {
            Run("line " + name, delegate
            {
                int[] line = (int[])input.Clone();
                bool m;
                int s = Board.SlideLine(line, out m);
                SameArray(expected, line, "result of " + Show(input));
                Eq(score, s, "score of " + Show(input));
                Eq(moved, m, "moved flag of " + Show(input));
            });
        }

        /// <summary>Plain list-based reference slide, structured differently from Board.SlideLine on purpose.</summary>
        private static int[] RefSlide(int[] row, out int score)
        {
            List<int> vals = new List<int>();
            for (int i = 0; i < row.Length; i++) if (row[i] != 0) vals.Add(row[i]);
            List<int> res = new List<int>();
            score = 0;
            int p = 0;
            while (p < vals.Count)
            {
                if (p + 1 < vals.Count && vals[p] == vals[p + 1])
                {
                    res.Add(vals[p] * 2);
                    score += vals[p] * 2;
                    p += 2;
                }
                else
                {
                    res.Add(vals[p]);
                    p++;
                }
            }
            while (res.Count < row.Length) res.Add(0);
            return res.ToArray();
        }

        private static readonly int[] TileChoices = { 0, 0, 2, 4, 8, 16, 32 };

        private static int[] RandomCells(Rng rng, int size)
        {
            int[] cells = new int[size * size];
            for (int i = 0; i < cells.Length; i++) cells[i] = TileChoices[rng.Range(0, TileChoices.Length)];
            return cells;
        }

        private static Board Transpose(Board b)
        {
            Board t = new Board(b.Size);
            for (int r = 0; r < b.Size; r++) for (int c = 0; c < b.Size; c++) t.Set(c, r, b.Get(r, c));
            return t;
        }

        private static Board Mirror(Board b)
        {
            Board t = new Board(b.Size);
            for (int r = 0; r < b.Size; r++) for (int c = 0; c < b.Size; c++) t.Set(r, b.Size - 1 - c, b.Get(r, c));
            return t;
        }

        /// <summary>Rotate 90 degrees clockwise: Up becomes Right, Right becomes Down, Down becomes Left, Left becomes Up.</summary>
        private static Board RotateCw(Board b)
        {
            int n = b.Size;
            Board t = new Board(n);
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) t.Set(r, c, b.Get(n - 1 - c, r));
            return t;
        }

        private static Board Moved(Board b, Direction d, out int gained, out bool moved)
        {
            Board copy = b.Clone();
            moved = copy.Move(d, out gained);
            return copy;
        }

        private static Direction[] AllDirections = { Direction.Left, Direction.Right, Direction.Up, Direction.Down };

        private static Game Cells(int size, int[] cells)
        {
            return Game.FromCells(size, cells, 0, 1, Game.DefaultUndoLimit);
        }

        private static bool PowerOfTwo(int v) { return v >= 2 && (v & (v - 1)) == 0; }

        /// <summary>A fixed-priority corner bot: Left, Down, Right, Up. Returns the finished game.</summary>
        private static Game PlayBot(int size, int seed, int maxActions, out bool sumInvariantHeld)
        {
            Game g = new Game(size, seed, 0);
            Direction[] order = { Direction.Left, Direction.Down, Direction.Right, Direction.Up };
            sumInvariantHeld = true;
            int actions = 0;
            while (actions++ < maxActions)
            {
                if (g.AwaitingDecision) { g.ContinuePlaying(); continue; }
                if (g.IsOver) break;
                int before = g.TileSum;
                bool any = false;
                for (int i = 0; i < order.Length && !any; i++) any = g.Move(order[i]);
                if (!any) break;
                if (g.TileSum != before + g.LastSpawn.Value) sumInvariantHeld = false;
            }
            return g;
        }

        /// <summary>Weight of each cell for the snake heuristic: biggest in the bottom-left corner, falling along a snake.</summary>
        private const double WBase = 2.0;       // weight ratio between neighbouring snake cells
        private const double EmptyBonus = 1000.0; // value of each free cell
        private static double[] SnakeWeights(int size)
        {
            double[] w = new double[size * size];
            int idx = size * size - 1;
            for (int row = size - 1; row >= 0; row--)
            {
                bool leftToRight = ((size - 1 - row) % 2 == 0);
                for (int k = 0; k < size; k++)
                {
                    int col = leftToRight ? k : size - 1 - k;
                    w[row * size + col] = Math.Pow(WBase, idx);
                    idx--;
                }
            }
            return w;
        }

        private static double Evaluate(Board b, double[] weights)
        {
            double s = 0;
            int[] cells = b.ToArray();
            for (int i = 0; i < cells.Length; i++) s += cells[i] * weights[i];
            return s + b.EmptyCount * EmptyBonus;
        }

        private static double Expect(Board b, int movesLeft, double[] w)
        {
            if (movesLeft == 0) return Evaluate(b, w);
            double best = double.NegativeInfinity;
            foreach (Direction d in AllDirections)
            {
                int gained;
                Board next = b.Clone();
                if (!next.Move(d, out gained)) continue;
                double v = gained + Chance(next, movesLeft - 1, w);
                if (v > best) best = v;
            }
            return best == double.NegativeInfinity ? -1e12 : best; // no move: dead end
        }

        private static double Chance(Board b, int movesLeft, double[] w)
        {
            if (movesLeft == 0) return Evaluate(b, w);
            int n = b.Size;
            double sum = 0;
            int empties = 0;
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                {
                    if (b.Get(r, c) != 0) continue;
                    empties++;
                    b.Set(r, c, 2);
                    sum += 0.9 * Expect(b, movesLeft, w);
                    b.Set(r, c, 4);
                    sum += 0.1 * Expect(b, movesLeft, w);
                    b.Set(r, c, 0);
                }
            return empties == 0 ? Evaluate(b, w) : sum / empties;
        }

        /// <summary>A look-ahead bot (expectimax over its own moves and random spawns) on a snake heuristic. Slower and smarter than the corner bot.</summary>
        private static Game PlayHeuristic(int size, int seed, int depth, int maxActions)
        {
            double[] weights = SnakeWeights(size);
            Game g = new Game(size, seed, 0);
            int actions = 0;
            while (actions++ < maxActions)
            {
                if (g.AwaitingDecision) { g.ContinuePlaying(); continue; }
                if (g.IsOver) break;
                Board current = new Board(size, g.Cells);
                Direction best = Direction.Left;
                double bestValue = double.NegativeInfinity;
                bool found = false;
                foreach (Direction d in AllDirections)
                {
                    int gained;
                    Board next = current.Clone();
                    if (!next.Move(d, out gained)) continue;
                    double v = gained + Chance(next, depth - 1, weights);
                    if (v > bestValue) { bestValue = v; best = d; found = true; }
                }
                if (!found) break;
                g.Move(best);
            }
            return g;
        }
        public static int Main()
        {
                        // ---- slide a single line ----
            LineCase("four equal tiles make two merges", A(2, 2, 2, 2), A(4, 4, 0, 0), 8, true);
            LineCase("three equal tiles merge the leading pair", A(2, 2, 2, 0), A(4, 2, 0, 0), 4, true);
            LineCase("gap between equal tiles", A(2, 0, 2, 0), A(4, 0, 0, 0), 4, true);
            LineCase("empty line", A(0, 0, 0, 0), A(0, 0, 0, 0), 0, false);
            LineCase("no merge no move", A(2, 4, 8, 16), A(2, 4, 8, 16), 0, false);
            LineCase("compacted line does not move", A(2, 4, 0, 0), A(2, 4, 0, 0), 0, false);
            LineCase("slide without merge", A(0, 2, 0, 4), A(2, 4, 0, 0), 0, true);
            LineCase("merged tile does not merge again (2,2,4)", A(2, 2, 4, 0), A(4, 4, 0, 0), 4, true);
            LineCase("merged tile does not merge again (4,2,2)", A(4, 2, 2, 0), A(4, 4, 0, 0), 4, true);
            LineCase("two pairs", A(4, 4, 8, 8), A(8, 16, 0, 0), 24, true);
            LineCase("pair at the far end", A(0, 0, 8, 8), A(16, 0, 0, 0), 16, true);
            LineCase("three eights", A(8, 8, 8, 0), A(16, 8, 0, 0), 16, true);
            LineCase("different neighbours stay apart", A(2, 4, 2, 4), A(2, 4, 2, 4), 0, false);
            LineCase("pair after a different tile", A(2, 4, 4, 0), A(2, 8, 0, 0), 8, true);
            LineCase("length two line", A(2, 2), A(4, 0), 4, true);
            LineCase("length five line", A(2, 2, 2, 2, 2), A(4, 4, 2, 0, 0), 8, true);

            // ---- slide cross-checked against the reference ----
            Run("line exhaustive over every 4-cell row of {0,2,4,8}", delegate
            {
                int[] values = { 0, 2, 4, 8 };
                int checkedRows = 0;
                for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) for (int c = 0; c < 4; c++) for (int d = 0; d < 4; d++)
                {
                    int[] row = { values[a], values[b], values[c], values[d] };
                    int refScore;
                    int[] expected = RefSlide(row, out refScore);
                    int[] line = (int[])row.Clone();
                    bool moved;
                    int s = Board.SlideLine(line, out moved);
                    SameArray(expected, line, "row " + Show(row));
                    Eq(refScore, s, "score of " + Show(row));
                    bool reallyMoved = false;
                    for (int i = 0; i < 4; i++) if (row[i] != expected[i]) reallyMoved = true;
                    Eq(reallyMoved, moved, "moved flag of " + Show(row));
                    checkedRows++;
                }
                Eq(256, checkedRows, "rows checked");
            });
            Run("line random rows of length 2 to 8 match the reference", delegate
            {
                Rng rng = new Rng(2024);
                for (int n = 0; n < 4000; n++)
                {
                    int len = rng.Range(2, 9);
                    int[] row = new int[len];
                    for (int i = 0; i < len; i++) row[i] = TileChoices[rng.Range(0, TileChoices.Length)];
                    int refScore;
                    int[] expected = RefSlide(row, out refScore);
                    int[] line = (int[])row.Clone();
                    bool moved;
                    int s = Board.SlideLine(line, out moved);
                    SameArray(expected, line, "row " + Show(row));
                    Eq(refScore, s, "score of " + Show(row));
                }
            });
            Run("line properties: sum kept, no gaps, score even, no tile above the sum", delegate
            {
                Rng rng = new Rng(77);
                for (int n = 0; n < 2000; n++)
                {
                    int[] row = new int[rng.Range(2, 9)];
                    int sum = 0;
                    for (int i = 0; i < row.Length; i++) { row[i] = TileChoices[rng.Range(0, TileChoices.Length)]; sum += row[i]; }
                    int[] line = (int[])row.Clone();
                    bool moved;
                    int s = Board.SlideLine(line, out moved);
                    int after = 0;
                    bool seenZero = false;
                    for (int i = 0; i < line.Length; i++)
                    {
                        after += line[i];
                        if (line[i] == 0) seenZero = true;
                        else True(!seenZero, "gap before a tile in " + Show(line) + " from " + Show(row));
                    }
                    Eq(sum, after, "sum of " + Show(row));
                    True(s % 2 == 0 && s >= 0, "score " + s + " for " + Show(row));
                }
            });

            // ---- whole-board moves and symmetry ----
            Run("board moves in all four directions on a known position", delegate
            {
                int[] start = A(2, 0, 2, 4,
                                0, 4, 0, 4,
                                8, 8, 0, 0,
                                0, 0, 0, 2);
                int g;
                bool m;
                Board b = new Board(4, start);
                Board left = Moved(b, Direction.Left, out g, out m);
                SameArray(A(4, 4, 0, 0, 8, 0, 0, 0, 16, 0, 0, 0, 2, 0, 0, 0), left.ToArray(), "left");
                Eq(4 + 8 + 16, g, "left score");
                Board right = Moved(b, Direction.Right, out g, out m);
                SameArray(A(0, 0, 4, 4, 0, 0, 0, 8, 0, 0, 0, 16, 0, 0, 0, 2), right.ToArray(), "right");
                Eq(4 + 8 + 16, g, "right score");
                Board up = Moved(b, Direction.Up, out g, out m);
                SameArray(A(2, 4, 2, 8, 8, 8, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0), up.ToArray(), "up");
                Eq(8, g, "up score");
                Board down = Moved(b, Direction.Down, out g, out m);
                SameArray(A(0, 0, 0, 0, 0, 0, 0, 0, 2, 4, 0, 8, 8, 8, 2, 2), down.ToArray(), "down");
                Eq(8, g, "down score");
            });
            Run("board move returns false and changes nothing when blocked", delegate
            {
                Board b = new Board(3, A(2, 4, 8, 4, 8, 2, 8, 2, 4));
                int[] before = b.ToArray();
                foreach (Direction d in AllDirections)
                {
                    int g;
                    True(!b.Move(d, out g), "moved " + d);
                    Eq(0, g, "gained " + d);
                    SameArray(before, b.ToArray(), "board after " + d);
                }
            });
            Run("a row that cannot move does not block other rows", delegate
            {
                Board b = new Board(3, A(2, 4, 8,
                                         0, 0, 2,
                                         4, 8, 2));
                int g;
                True(b.Move(Direction.Left, out g), "should move");
                SameArray(A(2, 4, 8, 2, 0, 0, 4, 8, 2), b.ToArray(), "result");
            });
            Run("right equals mirror of left on the mirrored board (all sizes)", delegate
            {
                Rng rng = new Rng(5);
                for (int size = 2; size <= 6; size++)
                    for (int n = 0; n < 150; n++)
                    {
                        Board b = new Board(size, RandomCells(rng, size));
                        int g1, g2; bool m1, m2;
                        Board direct = Moved(b, Direction.Right, out g1, out m1);
                        Board viaMirror = Mirror(Moved(Mirror(b), Direction.Left, out g2, out m2));
                        True(direct.SameAs(viaMirror), "boards differ for " + Show(b.ToArray()));
                        Eq(g2, g1, "score");
                        Eq(m2, m1, "moved");
                    }
            });
            Run("up equals transpose of left on the transposed board", delegate
            {
                Rng rng = new Rng(6);
                for (int size = 2; size <= 6; size++)
                    for (int n = 0; n < 150; n++)
                    {
                        Board b = new Board(size, RandomCells(rng, size));
                        int g1, g2; bool m1, m2;
                        Board direct = Moved(b, Direction.Up, out g1, out m1);
                        Board viaT = Transpose(Moved(Transpose(b), Direction.Left, out g2, out m2));
                        True(direct.SameAs(viaT), "boards differ for " + Show(b.ToArray()));
                        Eq(g2, g1, "score");
                        Eq(m2, m1, "moved");
                    }
            });
            Run("down equals transpose of right on the transposed board", delegate
            {
                Rng rng = new Rng(7);
                for (int size = 2; size <= 6; size++)
                    for (int n = 0; n < 150; n++)
                    {
                        Board b = new Board(size, RandomCells(rng, size));
                        int g1, g2; bool m1, m2;
                        Board direct = Moved(b, Direction.Down, out g1, out m1);
                        Board viaT = Transpose(Moved(Transpose(b), Direction.Right, out g2, out m2));
                        True(direct.SameAs(viaT), "boards differ for " + Show(b.ToArray()));
                        Eq(g2, g1, "score");
                        Eq(m2, m1, "moved");
                    }
            });
            Run("rotating the board clockwise turns up into right, right into down, down into left, left into up", delegate
            {
                Direction[] from = { Direction.Up, Direction.Right, Direction.Down, Direction.Left };
                Direction[] to = { Direction.Right, Direction.Down, Direction.Left, Direction.Up };
                Rng rng = new Rng(8);
                for (int size = 2; size <= 6; size++)
                    for (int n = 0; n < 60; n++)
                    {
                        Board b = new Board(size, RandomCells(rng, size));
                        for (int k = 0; k < 4; k++)
                        {
                            int g1, g2; bool m1, m2;
                            Board rotatedAfter = RotateCw(Moved(b, from[k], out g1, out m1));
                            Board movedRotated = Moved(RotateCw(b), to[k], out g2, out m2);
                            True(rotatedAfter.SameAs(movedRotated), from[k] + " vs " + to[k] + " differ for " + Show(b.ToArray()));
                            Eq(g1, g2, "score");
                            Eq(m1, m2, "moved");
                        }
                    }
            });
            Run("every move keeps the tile sum and scores the merged value", delegate
            {
                Rng rng = new Rng(9);
                for (int n = 0; n < 1000; n++)
                {
                    Board b = new Board(4, RandomCells(rng, 4));
                    foreach (Direction d in AllDirections)
                    {
                        int g; bool m;
                        Board after = Moved(b, d, out g, out m);
                        Eq(b.Sum, after.Sum, "sum for " + d);
                        True(after.MaxTile >= b.MaxTile, "max tile fell");
                        True(g >= 0 && g % 2 == 0, "gained " + g);
                        True(m == !after.SameAs(b), "moved flag disagrees with the board for " + d);
                    }
                }
            });

            // ---- trace (animation data) ----
            Run("trace lists every tile and lands tiles where the move puts them", delegate
            {
                Rng rng = new Rng(11);
                for (int size = 2; size <= 6; size++)
                    for (int n = 0; n < 80; n++)
                    {
                        Board b = new Board(size, RandomCells(rng, size));
                        foreach (Direction d in AllDirections)
                        {
                            List<TileMove> trace = b.Trace(d);
                            int tiles = 0;
                            for (int r = 0; r < size; r++) for (int c = 0; c < size; c++) if (b.Get(r, c) != 0) tiles++;
                            Eq(tiles, trace.Count, "tile count for " + d);

                            int g; bool m;
                            Board after = Moved(b, d, out g, out m);
                            int[] landed = new int[size * size];
                            int mergedPoints = 0;
                            foreach (TileMove t in trace)
                            {
                                Eq(b.Get(t.FromRow, t.FromCol), t.Value, "value at the source");
                                landed[t.ToRow * size + t.ToCol] += t.Value;
                                if (t.Merges) mergedPoints += t.Value * 2;
                            }
                            SameArray(after.ToArray(), landed, "landing cells for " + d + " on " + Show(b.ToArray()));
                            Eq(g, mergedPoints, "merge points for " + d);
                        }
                    }
            });
            Run("trace moves tiles along their own row or column only", delegate
            {
                Rng rng = new Rng(12);
                for (int n = 0; n < 200; n++)
                {
                    Board b = new Board(4, RandomCells(rng, 4));
                    foreach (Direction d in AllDirections)
                        foreach (TileMove t in b.Trace(d))
                        {
                            bool horizontal = d == Direction.Left || d == Direction.Right;
                            if (horizontal) Eq(t.FromRow, t.ToRow, "row changed on " + d);
                            else Eq(t.FromCol, t.ToCol, "column changed on " + d);
                            if (d == Direction.Left) True(t.ToCol <= t.FromCol, "moved right on Left");
                            if (d == Direction.Right) True(t.ToCol >= t.FromCol, "moved left on Right");
                            if (d == Direction.Up) True(t.ToRow <= t.FromRow, "moved down on Up");
                            if (d == Direction.Down) True(t.ToRow >= t.FromRow, "moved up on Down");
                        }
                }
            });

            // ---- board basics ----
            Run("board rejects sizes below 2 and wrong cell counts", delegate
            {
                bool threw = false;
                try { new Board(1); } catch (ArgumentOutOfRangeException) { threw = true; }
                True(threw, "size 1 accepted");
                threw = false;
                try { new Board(3, A(1, 2, 3)); } catch (ArgumentException) { threw = true; }
                True(threw, "wrong cell count accepted");
            });
            Run("board has moves when it has a gap or equal neighbours, not otherwise", delegate
            {
                True(new Board(2, A(2, 4, 4, 0)).HasMoves(), "gap");
                True(new Board(2, A(2, 4, 8, 4)).HasMoves(), "vertical equal neighbours");
                True(new Board(2, A(2, 2, 8, 4)).HasMoves(), "horizontal equal neighbours");
                True(!new Board(2, A(2, 4, 4, 2)).HasMoves(), "stuck board");
                True(!new Board(3, A(2, 4, 2, 4, 2, 4, 2, 4, 2)).HasMoves(), "checkerboard");
            });
            Run("board clone is independent and SameAs compares contents", delegate
            {
                Board a = new Board(2, A(2, 0, 0, 4));
                Board b = a.Clone();
                True(a.SameAs(b), "clone differs");
                b.Set(0, 1, 8);
                True(!a.SameAs(b), "clone shares storage");
                Eq(0, a.Get(0, 1), "original changed");
                Eq(2, a.EmptyCount, "empty count");
                Eq(4, a.MaxTile, "max tile");
            });

            // ---- spawning ----
            Run("a new game has two tiles, each a 2 or a 4", delegate
            {
                for (int seed = 0; seed < 300; seed++)
                {
                    Game g = new Game(4, seed);
                    Eq(14, g.EmptyCount, "empty cells at seed " + seed);
                    foreach (int v in g.Cells) True(v == 0 || v == 2 || v == 4, "tile " + v);
                    Eq(0, g.Score, "score");
                    True(!g.IsOver && !g.HasWon, "flags");
                }
            });
            Run("spawn values are about 90% twos and 10% fours", delegate
            {
                int twos = 0, fours = 0;
                for (int seed = 0; seed < 3000; seed++)
                    foreach (int v in new Game(4, seed).Cells)
                    {
                        if (v == 2) twos++;
                        if (v == 4) fours++;
                    }
                float fourShare = fours / (float)(twos + fours);
                True(fourShare > 0.07f && fourShare < 0.13f, "share of fours was " + fourShare);
            });
            Run("spawn positions cover every cell", delegate
            {
                bool[] seen = new bool[16];
                for (int seed = 0; seed < 400; seed++)
                {
                    int[] cells = new Game(4, seed).Cells;
                    for (int i = 0; i < 16; i++) if (cells[i] != 0) seen[i] = true;
                }
                for (int i = 0; i < 16; i++) True(seen[i], "cell " + i + " never received a tile");
            });
            Run("the spawn after a move fills the only empty cell", delegate
            {
                // Left leaves exactly one gap, at (0,1); the spawn must go there.
                Game g = Game.FromCells(2, A(2, 2, 4, 8), 0, 3, 5);
                True(g.Move(Direction.Left), "move rejected");
                Eq(4, g.Get(0, 0), "merged tile");
                True(g.Get(0, 1) == 2 || g.Get(0, 1) == 4, "spawn missing at (0,1)");
                Eq(0, g.EmptyCount, "board should be full");
                Eq(0, g.LastSpawn.Row, "spawn row");
                Eq(1, g.LastSpawn.Col, "spawn col");
                Eq(g.Get(0, 1), g.LastSpawn.Value, "spawn value");
            });
            Run("exactly one tile spawns per accepted move and it lands on a cell that was empty", delegate
            {
                for (int seed = 0; seed < 100; seed++)
                {
                    Game g = new Game(4, seed);
                    Direction[] order = { Direction.Left, Direction.Down, Direction.Right, Direction.Up };
                    for (int step = 0; step < 40 && !g.IsOver; step++)
                    {
                        int sumBefore = g.TileSum;
                        bool any = false;
                        for (int i = 0; i < 4 && !any; i++) any = g.Move(order[(step + i) % 4]);
                        if (!any) break;
                        Eq(sumBefore + g.LastSpawn.Value, g.TileSum, "sum grows by exactly the spawned tile");
                        Eq(g.LastSpawn.Value, g.Get(g.LastSpawn.Row, g.LastSpawn.Col), "spawned value on the board");
                    }
                }
            });

            // ---- move validity ----
            Run("a move that changes nothing is rejected: no spawn, no score, no log, no undo entry", delegate
            {
                Game g = Cells(4, A(2, 4, 0, 0,
                                    0, 0, 0, 0,
                                    0, 0, 0, 0,
                                    0, 0, 0, 0));
                int[] before = g.Cells;
                True(!g.Move(Direction.Left), "Left moved a packed row");
                True(!g.Move(Direction.Up), "Up moved a top row");
                SameArray(before, g.Cells, "board");
                Eq(0, g.MoveCount, "move count");
                Eq("", g.ActionLog, "log");
                Eq(0, g.UndoDepth, "undo depth");
                True(g.Move(Direction.Right), "Right should move");
            });
            Run("score accumulates the merged tile values", delegate
            {
                Game g = Game.FromCells(4, A(2, 2, 4, 4,
                                             0, 0, 0, 0,
                                             0, 0, 0, 0,
                                             0, 0, 0, 0), 100, 1, 5);
                g.Move(Direction.Left);
                Eq(100 + 4 + 8, g.Score, "score after the first move");
                Eq(1, g.MoveCount, "move count");
            });

            // ---- win and continue ----
            Run("making 2048 wins and blocks moves until the player continues", delegate
            {
                Game g = Cells(4, A(1024, 1024, 0, 0,
                                    0, 0, 0, 0,
                                    0, 0, 0, 0,
                                    0, 0, 0, 2));
                True(g.Move(Direction.Left), "winning move rejected");
                True(g.HasWon, "not won");
                True(g.AwaitingDecision, "not waiting");
                Eq(2048, g.Get(0, 0), "2048 tile");
                int[] frozen = g.Cells;
                foreach (Direction d in AllDirections) True(!g.Move(d), "moved while waiting: " + d);
                SameArray(frozen, g.Cells, "board while waiting");
                True(g.ContinuePlaying(), "continue refused");
                True(!g.AwaitingDecision && g.HasWon, "flags after continue");
                True(g.Move(Direction.Down), "cannot move after continuing");
            });
            Run("making 1024 is not a win, making 2048 is (the threshold is exact)", delegate
            {
                Game g = Cells(4, A(512, 512, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                True(g.Move(Direction.Left), "move rejected");
                Eq(1024, g.Get(0, 0), "tile");
                True(!g.HasWon && !g.AwaitingDecision, "1024 counted as a win");
                // Keep a 1024 on the board through a few more moves: still no win.
                True(g.Move(Direction.Down) || g.Move(Direction.Right), "second move rejected");
                True(!g.HasWon, "1024 on the board counted as a win");
                Game h = Cells(4, A(1024, 1024, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                h.Move(Direction.Left);
                True(h.HasWon, "2048 not counted as a win");
            });
            Run("continue without a pending win does nothing", delegate
            {
                Game g = new Game(4, 1);
                True(!g.ContinuePlaying(), "continue accepted without a win");
                Eq("", g.ActionLog, "log");
            });
            Run("a second 2048 after continuing does not ask again", delegate
            {
                Game g = Cells(4, A(1024, 1024, 0, 0,
                                    0, 0, 0, 0,
                                    1024, 1024, 0, 0,
                                    0, 0, 0, 0));
                g.Move(Direction.Left);
                True(g.AwaitingDecision, "first win not detected");
                g.ContinuePlaying();
                // Both 2048s now sit in column 0; keep sliding and make sure the game never asks again.
                int reached4096 = 0;
                for (int seed = 0; seed < 50; seed++)
                {
                    Game h = Game.FromCells(4, A(1024, 1024, 0, 0, 0, 0, 0, 0, 1024, 1024, 0, 0, 0, 0, 0, 0), 0, seed, 5);
                    h.Move(Direction.Left);
                    h.ContinuePlaying();
                    for (int i = 0; i < 12 && h.MaxTile < 4096; i++)
                    {
                        h.Move(AllDirections[(i + 2) % 4]);
                        True(!h.AwaitingDecision, "asked to continue a second time at seed " + seed);
                    }
                    if (h.MaxTile >= 4096) reached4096++;
                }
                True(reached4096 > 0, "no seed reached 4096, so the check above proved nothing");
            });
            Run("reaching 4096 straight from 2048 tiles is still just one win", delegate
            {
                Game g = Cells(4, A(2048, 2048, 0, 0,
                                    0, 0, 0, 0,
                                    0, 0, 0, 0,
                                    0, 0, 0, 0));
                g.Move(Direction.Left);
                True(g.HasWon && g.AwaitingDecision, "win not flagged for 4096");
                Eq(4096, g.Get(0, 0), "tile");
            });
            Run("a board that already holds 2048 is not a win until a move happens", delegate
            {
                Game g = Cells(4, A(2048, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                True(!g.HasWon, "won at construction");
            });

            // ---- game over ----
            Run("a full board with no equal neighbours is over and rejects every move", delegate
            {
                Game g = Cells(2, A(2, 4, 4, 2));
                True(g.IsOver, "not over");
                int[] before = g.Cells;
                foreach (Direction d in AllDirections) True(!g.Move(d), "moved when over: " + d);
                SameArray(before, g.Cells, "board");
            });
            Run("a full board with an equal neighbour is not over", delegate
            {
                True(!Cells(2, A(2, 2, 4, 8)).IsOver, "horizontal pair");
                True(!Cells(2, A(2, 4, 2, 8)).IsOver, "vertical pair");
            });
            Run("a board with a gap is never over", delegate
            {
                True(!Cells(3, A(2, 4, 2, 4, 2, 4, 2, 4, 0)).IsOver, "gap");
            });
            Run("the move that fills the board with no pairs left ends the game", delegate
            {
                // [4,0 / 8,2] -> Right -> [0,4 / 8,2], then the spawn fills (0,0).
                // A 2 there leaves no equal neighbours (stuck); a 4 sits next to the other 4 (still playable).
                int stuck = 0, playable = 0;
                for (int seed = 0; seed < 200; seed++)
                {
                    Game g = Game.FromCells(2, A(4, 0, 8, 2), 0, seed, 5);
                    True(g.Move(Direction.Right), "move rejected");
                    Eq(0, g.EmptyCount, "board should be full");
                    if (g.LastSpawn.Value == 2) { True(g.IsOver, "stuck board not over at seed " + seed); stuck++; }
                    else { True(!g.IsOver, "playable board marked over at seed " + seed); playable++; }
                }
                True(stuck > 0 && playable > 0, "both outcomes should occur over 200 seeds");
            });
            Run("size 3 and size 5 boards play to a game over", delegate
            {
                foreach (int size in new int[] { 3, 5 })
                {
                    bool ok;
                    Game g = PlayBot(size, 4, 100000, out ok);
                    True(g.IsOver, "size " + size + " never ended");
                    True(ok, "tile sum invariant broke at size " + size);
                }
            });
            Run("game rejects sizes below 2", delegate
            {
                bool threw = false;
                try { new Game(1, 1); } catch (ArgumentOutOfRangeException) { threw = true; }
                True(threw, "size 1 accepted");
            });

            // ---- undo ----
            Run("undo with nothing to undo returns false", delegate
            {
                True(!new Game(4, 1).Undo(), "undo on a fresh game");
            });
            Run("undo restores board, score, move count and log", delegate
            {
                Game g = new Game(4, 12);
                int[] start = g.Cells;
                Direction[] order = { Direction.Left, Direction.Down, Direction.Right, Direction.Up };
                for (int i = 0; i < 4 && !g.Move(order[i]); i++) { }
                True(g.MoveCount == 1, "no move happened");
                True(g.Undo(), "undo failed");
                SameArray(start, g.Cells, "board");
                Eq(0, g.Score, "score");
                Eq(0, g.MoveCount, "move count");
                Eq("", g.ActionLog, "log");
                True(!g.CanUndo, "history not empty");
            });
            Run("undo then the same move gives the identical result (rng state is restored)", delegate
            {
                for (int seed = 0; seed < 60; seed++)
                {
                    Game g = new Game(4, seed);
                    Direction d = Direction.Left;
                    if (!g.Move(d)) d = Direction.Down;
                    if (g.MoveCount == 0 && !g.Move(d)) continue;
                    int[] first = g.Cells;
                    int score = g.Score;
                    True(g.Undo(), "undo");
                    True(g.Move(d), "redo rejected");
                    SameArray(first, g.Cells, "redo board at seed " + seed);
                    Eq(score, g.Score, "redo score");
                }
            });
            Run("undo walks back several moves, newest first", delegate
            {
                Game g = new Game(4, 21);
                List<int[]> boards = new List<int[]>();
                Direction[] order = { Direction.Left, Direction.Down, Direction.Right, Direction.Up };
                int moves = 0;
                while (moves < 5)
                {
                    boards.Add(g.Cells);
                    bool any = false;
                    for (int i = 0; i < 4 && !any; i++) any = g.Move(order[i]);
                    if (!any) break;
                    moves++;
                }
                True(moves == 5, "setup played only " + moves + " moves");
                for (int back = 4; back >= 0; back--)
                {
                    True(g.Undo(), "undo " + back);
                    SameArray(boards[back], g.Cells, "board after undoing to step " + back);
                }
                True(!g.Undo(), "undo past the start");
            });
            Run("the undo limit caps how far back you can go", delegate
            {
                Game g = new Game(4, 33, 3);
                Direction[] order = { Direction.Left, Direction.Down, Direction.Right, Direction.Up };
                int moves = 0;
                while (moves < 8)
                {
                    bool any = false;
                    for (int i = 0; i < 4 && !any; i++) any = g.Move(order[i]);
                    if (!any) break;
                    moves++;
                }
                True(moves == 8, "setup played only " + moves + " moves");
                int undone = 0;
                while (g.Undo()) undone++;
                Eq(3, undone, "undos available");
                Eq(5, g.MoveCount, "moves left after undoing three");
            });
            Run("an undo limit of zero disables undo but still logs moves", delegate
            {
                Game g = new Game(4, 5, 0);
                for (int i = 0; i < 3; i++) { if (!g.Move(Direction.Left)) g.Move(Direction.Down); }
                True(!g.Undo(), "undo worked with limit 0");
                True(g.ActionLog.Length >= 1, "log empty");
            });
            Run("undoing the winning move clears the win", delegate
            {
                Game g = Cells(4, A(1024, 1024, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                g.Move(Direction.Left);
                True(g.HasWon && g.AwaitingDecision, "setup");
                True(g.Undo(), "undo");
                True(!g.HasWon && !g.AwaitingDecision, "win flags survived the undo");
                Eq(1024, g.Get(0, 1), "board restored");
            });
            Run("undoing a continue brings the win prompt back", delegate
            {
                Game g = Cells(4, A(1024, 1024, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                g.Move(Direction.Left);
                g.ContinuePlaying();
                True(g.Undo(), "undo");
                True(g.HasWon && g.AwaitingDecision, "prompt not restored");
                Eq("L", g.ActionLog, "log");
            });
            Run("undo after game over reopens the game", delegate
            {
                Game limited = new Game(4, 3, 10);
                Direction[] order = { Direction.Left, Direction.Down, Direction.Right, Direction.Up };
                int guard = 0;
                while (!limited.IsOver && guard++ < 100000)
                {
                    if (limited.AwaitingDecision) { limited.ContinuePlaying(); continue; }
                    bool any = false;
                    for (int i = 0; i < 4 && !any; i++) any = limited.Move(order[i]);
                    if (!any) break;
                }
                True(limited.IsOver, "game did not end");
                True(limited.Undo(), "undo after game over");
                // The undone move was legal, so the position before it still has moves.
                True(!limited.IsOver, "still over after undoing the last move");
                True(limited.Move(Direction.Left) || limited.Move(Direction.Right) || limited.Move(Direction.Up) || limited.Move(Direction.Down), "no move possible after undo");
            });

            // ---- replay ----
            Run("replay of a recorded game reproduces board, score and flags", delegate
            {
                for (int seed = 0; seed < 40; seed++)
                {
                    Rng chooser = new Rng(seed + 1000);
                    Game g = new Game(4, seed);
                    for (int i = 0; i < 300 && !g.IsOver; i++)
                    {
                        if (g.AwaitingDecision) { g.ContinuePlaying(); continue; }
                        g.Move(AllDirections[chooser.Range(0, 4)]);
                    }
                    Game again = Replay.Run(4, seed, g.ActionLog);
                    SameArray(g.Cells, again.Cells, "board at seed " + seed);
                    Eq(g.Score, again.Score, "score at seed " + seed);
                    Eq(g.HasWon, again.HasWon, "won");
                    Eq(g.IsOver, again.IsOver, "over");
                }
            });
            Run("replay still matches after random undos", delegate
            {
                for (int seed = 0; seed < 40; seed++)
                {
                    Rng chooser = new Rng(seed + 5000);
                    Game g = new Game(4, seed);
                    for (int i = 0; i < 200 && !g.IsOver; i++)
                    {
                        if (g.AwaitingDecision) { g.ContinuePlaying(); continue; }
                        if (chooser.Range(0, 6) == 0) g.Undo();
                        else g.Move(AllDirections[chooser.Range(0, 4)]);
                    }
                    Game again = Replay.Run(4, seed, g.ActionLog);
                    SameArray(g.Cells, again.Cells, "board at seed " + seed);
                    Eq(g.Score, again.Score, "score at seed " + seed);
                }
            });
            Run("replay on a size 3 board", delegate
            {
                Game g = new Game(3, 8);
                string log = "";
                for (int i = 0; i < 30 && !g.IsOver; i++) g.Move(AllDirections[i % 4]);
                log = g.ActionLog;
                SameArray(g.Cells, Replay.Run(3, 8, log).Cells, "board");
            });
            Run("replay of the empty log is the starting position", delegate
            {
                SameArray(new Game(4, 9).Cells, Replay.Run(4, 9, "").Cells, "board");
            });
            Run("replay rejects unknown characters and logs that do not fit the seed", delegate
            {
                bool threw = false;
                try { Replay.Run(4, 1, "LRX"); } catch (ArgumentException) { threw = true; }
                True(threw, "unknown character accepted");
                threw = false;
                try { Replay.Run(4, 1, "C"); } catch (InvalidOperationException) { threw = true; }
                True(threw, "continue without a win accepted");
                True(Replay.IsValidLog("LRUDC") && !Replay.IsValidLog("l") && !Replay.IsValidLog(null), "IsValidLog");
            });
            Run("different seeds give different games", delegate
            {
                int differing = 0;
                for (int seed = 0; seed < 50; seed++)
                {
                    int[] a = new Game(4, seed).Cells, b = new Game(4, seed + 1).Cells;
                    bool same = true;
                    for (int i = 0; i < 16; i++) if (a[i] != b[i]) same = false;
                    if (!same) differing++;
                }
                True(differing > 40, "only " + differing + " of 50 neighbouring seeds differed");
            });
            Run("same seed and same moves give the same game", delegate
            {
                Game a = new Game(4, 77), b = new Game(4, 77);
                for (int i = 0; i < 50; i++)
                {
                    Direction d = AllDirections[i % 4];
                    Eq(a.Move(d), b.Move(d), "accepted at step " + i);
                }
                SameArray(a.Cells, b.Cells, "board");
            });

            // ---- bot over many seeds: invariants and a measurement ----
            Run("corner bot: every game ends, tiles stay powers of two, the tile sum grows by exactly each spawn", delegate
            {
                int won = 0, games = 200;
                int[] best = new int[16];
                for (int seed = 0; seed < games; seed++)
                {
                    bool ok;
                    Game g = PlayBot(4, seed, 100000, out ok);
                    True(g.IsOver, "seed " + seed + " did not end");
                    True(ok, "tile sum invariant broke at seed " + seed);
                    foreach (int v in g.Cells) True(v == 0 || PowerOfTwo(v), "tile " + v);
                    if (g.HasWon) won++;
                    int level = 0;
                    for (int m = g.MaxTile; m > 1; m >>= 1) level++;
                    best[level]++;
                }
                string histogram = "";
                for (int level = 1; level < 16; level++) if (best[level] > 0) histogram += (1 << level) + ":" + best[level] + " ";
                Console.WriteLine("info: corner bot (Left, Down, Right, Up) on 4x4, " + games + " seeds: reached 2048 in " + won + ", best tile histogram " + histogram);
                True(best[1] + best[2] + best[3] + best[4] == 0, "a bot game ended below 32: " + histogram);
            });

            Run("look-ahead bot: reaches 2048 on some seeds, and a won game replays exactly (including the continue)", delegate
            {
                int games = 40, won = 0;
                Game firstWin = null;
                int firstWinSeed = -1;
                int sumMax = 0;
                for (int seed = 0; seed < games; seed++)
                {
                    Game g = PlayHeuristic(4, seed, 2, 100000);
                    sumMax += g.MaxTile;
                    if (g.HasWon)
                    {
                        won++;
                        if (firstWin == null) { firstWin = g; firstWinSeed = seed; }
                    }
                }
                Console.WriteLine("info: look-ahead bot (depth 2, snake weights) on 4x4, " + games + " seeds: reached 2048 in " + won + ", mean best tile " + (sumMax / games));
                True(won > 0, "the look-ahead bot never won, so the win path went untested");
                True(firstWin.ActionLog.IndexOf('C') >= 0, "a won game should contain a continue");
                Game again = Replay.Run(4, firstWinSeed, firstWin.ActionLog);
                SameArray(firstWin.Cells, again.Cells, "replayed board");
                Eq(firstWin.Score, again.Score, "replayed score");
                True(again.HasWon && !again.AwaitingDecision, "replayed win flags");
            });
            // ---- Rng and HighScore ----
            Run("rng same seed same sequence and state round trips", delegate
            {
                Rng a = new Rng(42), b = new Rng(42);
                for (int i = 0; i < 100; i++) Eq(a.NextUInt(), b.NextUInt(), "value " + i);
                uint saved = a.State;
                uint x = a.NextUInt(), y = a.NextUInt();
                a.State = saved;
                Eq(x, a.NextUInt(), "first after restore");
                Eq(y, a.NextUInt(), "second after restore");
            });
            Run("rng ranges stay in bounds and cover every value", delegate
            {
                Rng r = new Rng(9);
                bool[] seen = new bool[4];
                for (int i = 0; i < 2000; i++)
                {
                    int v = r.Range(0, 4);
                    True(v >= 0 && v < 4, "out of range " + v);
                    seen[v] = true;
                    float f = r.NextFloat();
                    True(f >= 0f && f < 1f, "float out of range " + f);
                }
                for (int i = 0; i < 4; i++) True(seen[i], "never produced " + i);
            });
            Run("rng survives the one seed that mixes to a zero state", delegate
            {
                uint k = 2654435761u, c = 0x9E3779B9u, inv = k;
                for (int i = 0; i < 6; i++) inv = inv * (2u - k * inv);
                uint badSeed = (0u - c) * inv;
                True(badSeed * k + c == 0u, "test setup: seed does not mix to zero");
                Rng r = new Rng((int)badSeed);
                uint a = r.NextUInt(), b = r.NextUInt();
                True(a != 0u && b != 0u && a != b, "generator is stuck at zero");
            });
            Run("rng refuses to be set to the all-zero state", delegate
            {
                Rng r = new Rng(1);
                r.State = 0u;
                True(r.NextUInt() != 0u, "stuck at zero after State = 0");
            });
            Run("highscore parse handles junk and update needs a strictly higher score", delegate
            {
                Eq(0, HighScore.Parse(null), "null");
                Eq(0, HighScore.Parse("abc"), "text");
                Eq(0, HighScore.Parse("-5"), "negative");
                Eq(1234, HighScore.Parse("1234"), "valid");
                Eq("0", HighScore.Format(-3), "negative formats as zero");
                bool isNew;
                Eq(100, HighScore.Update(100, 100, out isNew), "tie keeps best");
                True(!isNew, "tie flagged as new");
                Eq(150, HighScore.Update(100, 150, out isNew), "higher replaces");
                True(isNew, "higher not flagged");
            });

            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
