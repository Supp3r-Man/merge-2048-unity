using System;

namespace MergeUp.Core
{
    /// <summary>Re-run a game from its seed and action log (L, R, U, D, C).</summary>
    public static class Replay
    {
        public static bool IsValidLog(string actions)
        {
            if (actions == null) return false;
            for (int i = 0; i < actions.Length; i++)
            {
                char a = actions[i];
                if (a != Game.LogLeft && a != Game.LogRight && a != Game.LogUp && a != Game.LogDown && a != Game.LogContinue)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Plays the log on a fresh game. Throws if the log has an unknown character or an action the game rejects
        /// (a recorded log only ever contains accepted actions, so a rejection means the log does not belong to this seed/size).
        /// </summary>
        public static Game Run(int size, int seed, string actions)
        {
            if (!IsValidLog(actions)) throw new ArgumentException("action log contains an unknown character");
            Game game = new Game(size, seed);
            for (int i = 0; i < actions.Length; i++)
            {
                bool ok;
                switch (actions[i])
                {
                    case Game.LogLeft: ok = game.Move(Direction.Left); break;
                    case Game.LogRight: ok = game.Move(Direction.Right); break;
                    case Game.LogUp: ok = game.Move(Direction.Up); break;
                    case Game.LogDown: ok = game.Move(Direction.Down); break;
                    default: ok = game.ContinuePlaying(); break;
                }
                if (!ok) throw new InvalidOperationException("action " + i + " ('" + actions[i] + "') was rejected during replay");
            }
            return game;
        }
    }
}
