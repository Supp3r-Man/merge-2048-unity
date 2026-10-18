using UnityEngine;
using MergeUp.Core;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace MergeUp
{
    /// <summary>
    /// One place that reads the keyboard, so the game works whichever Active Input Handling the
    /// project uses (Input Manager (Old), Input System Package (New) or Both).
    /// </summary>
    public static class InputReader
    {
        /// <summary>A slide pressed this frame (arrow keys or WASD), if any.</summary>
        public static bool TryGetMove(out Direction dir)
        {
            dir = Direction.Left;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) { dir = Direction.Left; return true; }
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) { dir = Direction.Right; return true; }
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) { dir = Direction.Up; return true; }
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) { dir = Direction.Down; return true; }
            return false;
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            if (k == null) return false;
            if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) { dir = Direction.Left; return true; }
            if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) { dir = Direction.Right; return true; }
            if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) { dir = Direction.Up; return true; }
            if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) { dir = Direction.Down; return true; }
            return false;
#else
            return false;
#endif
        }

        public static bool UndoPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.U) || Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace);
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            return k != null && (k.uKey.wasPressedThisFrame || k.zKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame);
#else
            return false;
#endif
        }

        public static bool NewGamePressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.R);
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            return k != null && (k.nKey.wasPressedThisFrame || k.rKey.wasPressedThisFrame);
#else
            return false;
#endif
        }

        /// <summary>Enter or Space: "keep playing" on the win prompt.</summary>
        public static bool ConfirmPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
#elif ENABLE_INPUT_SYSTEM
            Keyboard k = Keyboard.current;
            return k != null && (k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame);
#else
            return false;
#endif
        }
    }
}
