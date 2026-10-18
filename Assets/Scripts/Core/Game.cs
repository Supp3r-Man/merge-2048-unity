using System;
using System.Collections.Generic;
using System.Text;

namespace MergeUp.Core
{
    /// <summary>Where the most recent tile appeared. Row is -1 when nothing spawned.</summary>
    public struct SpawnInfo
    {
        public int Row;
        public int Col;
        public int Value;
    }

    /// <summary>
    /// A game of Merge Up: board, score, spawning, win/continue, game over, undo and an action log
    /// that (with the seed) reproduces the game exactly. No engine types.
    /// </summary>
    public sealed class Game
    {
        public const int WinTile = 2048;
        public const float SpawnTwoChance = 0.9f;
        public const int DefaultUndoLimit = 10;

        /// <summary>Log characters: one per accepted action.</summary>
        public const char LogLeft = 'L';
        public const char LogRight = 'R';
        public const char LogUp = 'U';
        public const char LogDown = 'D';
        public const char LogContinue = 'C';

        private sealed class Snapshot
        {
            public int[] Cells;
            public int Score;
            public uint RngState;
            public bool HasWon;
            public bool AwaitingDecision;
            public int MoveCount;
        }

        private readonly Board board;
        private readonly Rng rng;
        private readonly List<Snapshot> history = new List<Snapshot>();
        private readonly List<char> log = new List<char>();
        private readonly int undoLimit;

        public int Seed { get; private set; }
        public int Score { get; private set; }
        public int MoveCount { get; private set; }
        public bool HasWon { get; private set; }
        /// <summary>True from the moment a 2048 tile appears until the player chooses to keep playing.</summary>
        public bool AwaitingDecision { get; private set; }
        public bool IsOver { get; private set; }
        public SpawnInfo LastSpawn { get; private set; }

        public Game(int size, int seed) : this(size, seed, DefaultUndoLimit)
        {
        }

        public Game(int size, int seed, int undoLimit)
        {
            board = new Board(size);
            rng = new Rng(seed);
            Seed = seed;
            this.undoLimit = Math.Max(0, undoLimit);
            Spawn();
            Spawn();
            Refresh();
        }

        private Game(Board start, int score, int seed, int undoLimit)
        {
            board = start;
            rng = new Rng(seed);
            Seed = seed;
            Score = score;
            this.undoLimit = Math.Max(0, undoLimit);
            LastSpawn = NoSpawn();
            Refresh();
        }

        /// <summary>A game starting from a given position, with no initial spawns. For puzzles and tests.</summary>
        public static Game FromCells(int size, int[] cells, int score, int seed, int undoLimit)
        {
            return new Game(new Board(size, cells), score, seed, undoLimit);
        }

        public int Size { get { return board.Size; } }
        public int Get(int row, int col) { return board.Get(row, col); }
        public int[] Cells { get { return board.ToArray(); } }
        public int MaxTile { get { return board.MaxTile; } }
        public int TileSum { get { return board.Sum; } }
        public int EmptyCount { get { return board.EmptyCount; } }
        public int UndoDepth { get { return history.Count; } }
        public bool CanUndo { get { return history.Count > 0; } }

        /// <summary>Every accepted action so far (L, R, U, D, C), minus any that were undone.</summary>
        public string ActionLog
        {
            get
            {
                StringBuilder sb = new StringBuilder(log.Count);
                for (int i = 0; i < log.Count; i++) sb.Append(log[i]);
                return sb.ToString();
            }
        }

        /// <summary>Where each tile would go for this move, for animation. Does not change the game.</summary>
        public List<TileMove> Preview(Direction dir)
        {
            return board.Trace(dir);
        }

        private static SpawnInfo NoSpawn()
        {
            SpawnInfo none;
            none.Row = -1;
            none.Col = -1;
            none.Value = 0;
            return none;
        }

        private static char LogChar(Direction dir)
        {
            switch (dir)
            {
                case Direction.Left: return LogLeft;
                case Direction.Right: return LogRight;
                case Direction.Up: return LogUp;
                default: return LogDown;
            }
        }

        /// <summary>
        /// Slide in a direction. Returns true if any tile moved; only then does a new tile spawn.
        /// Rejected (false, nothing changes) when the game is over or waiting on the win decision.
        /// </summary>
        public bool Move(Direction dir)
        {
            if (IsOver || AwaitingDecision) return false;

            Snapshot before = Capture();
            int gained;
            if (!board.Move(dir, out gained)) return false;

            Push(before, LogChar(dir));
            Score += gained;
            MoveCount++;
            Spawn();
            if (!HasWon && board.MaxTile >= WinTile)
            {
                HasWon = true;
                AwaitingDecision = true;
            }
            Refresh();
            return true;
        }

        /// <summary>After reaching 2048, keep playing. Returns false if there was no pending decision.</summary>
        public bool ContinuePlaying()
        {
            if (!AwaitingDecision) return false;
            Push(Capture(), LogContinue);
            AwaitingDecision = false;
            return true;
        }

        /// <summary>Take back the last action (a move or a continue). Returns false when there is nothing to undo.</summary>
        public bool Undo()
        {
            if (history.Count == 0) return false;
            Snapshot s = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            log.RemoveAt(log.Count - 1);

            board.CopyFrom(s.Cells);
            Score = s.Score;
            rng.State = s.RngState;
            HasWon = s.HasWon;
            AwaitingDecision = s.AwaitingDecision;
            MoveCount = s.MoveCount;
            LastSpawn = NoSpawn();
            Refresh();
            return true;
        }

        private Snapshot Capture()
        {
            Snapshot s = new Snapshot();
            s.Cells = board.ToArray();
            s.Score = Score;
            s.RngState = rng.State;
            s.HasWon = HasWon;
            s.AwaitingDecision = AwaitingDecision;
            s.MoveCount = MoveCount;
            return s;
        }

        private void Push(Snapshot s, char action)
        {
            log.Add(action);
            if (undoLimit == 0) return;
            history.Add(s);
            if (history.Count > undoLimit) history.RemoveAt(0);
        }

        private void Refresh()
        {
            IsOver = !board.HasMoves();
        }

        /// <summary>Put a 2 (90%) or a 4 (10%) in a uniformly random empty cell.</summary>
        private void Spawn()
        {
            int empty = board.EmptyCount;
            if (empty == 0)
            {
                LastSpawn = NoSpawn();
                return;
            }
            int pick = rng.Range(0, empty);
            int value = rng.NextFloat() < SpawnTwoChance ? 2 : 4;
            for (int r = 0; r < board.Size; r++)
            {
                for (int c = 0; c < board.Size; c++)
                {
                    if (board.Get(r, c) != 0) continue;
                    if (pick == 0)
                    {
                        board.Set(r, c, value);
                        SpawnInfo info;
                        info.Row = r;
                        info.Col = c;
                        info.Value = value;
                        LastSpawn = info;
                        return;
                    }
                    pick--;
                }
            }
        }
    }
}
