using System;
using System.Collections.Generic;
using UnityEngine;
using MergeUp.Core;

namespace MergeUp
{
    /// <summary>
    /// Unity side of Merge Up: builds the camera and board sprites, reads input, animates slides and
    /// draws the HUD. All rules (sliding, merging, scoring, spawning, win, game over, undo) live in
    /// MergeUp.Core, which has no engine dependency and is covered by Tests/LogicTests.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        private const int BoardSize = 4;
        private const float TileScale = 0.92f;      // tile size as a fraction of the cell pitch (the rest is the gap)
        private const float SlideSeconds = 0.12f;
        private const float PopSeconds = 0.12f;
        private const string BestKey = "mergeup.best";

        /// <summary>A static tile resting on the board.</summary>
        private sealed class TileView
        {
            public GameObject Go;
            public int Row, Col, Value;
        }

        /// <summary>A tile in flight during a slide.</summary>
        private sealed class Mover
        {
            public GameObject Go;
            public Vector3 From, To;
            public int Value;
            public Vector3 Current;
        }

        private readonly List<TileView> tiles = new List<TileView>();
        private readonly List<Mover> movers = new List<Mover>();
        private readonly List<Transform> boardParts = new List<Transform>();

        private Game game;
        private Sfx sfx;
        private Camera cam;
        private int best;
        private float slideLeft;
        private float popLeft;
        private bool hasQueuedMove;
        private Direction queuedMove;
        private GUIStyle titleStyle, labelStyle, tileStyle, buttonStyle, bigStyle;

        private bool Animating { get { return slideLeft > 0f; } }

        private void Awake()
        {
            sfx = new Sfx(gameObject);
            SetupCamera();
            SetupBoard();
            NewGame();
        }

        // ---- setup ----

        private void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.transform.rotation = Quaternion.identity;
            FitCamera();
        }

        /// <summary>Keep the whole board in view, with room above and below for the HUD, at any window shape.</summary>
        private void FitCamera()
        {
            float half = BoardSize * 0.5f;
            float forHeight = half + 1.3f;
            float forWidth = (half + 0.6f) / Mathf.Max(0.1f, cam.aspect);
            cam.orthographicSize = Mathf.Max(forHeight, forWidth);
        }

        private SpriteRenderer MakeSprite(string objectName, Color color, int order, Vector3 position, float scale)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteFactory.RoundedSquare();
            sr.color = color;
            sr.sortingOrder = order;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * scale;
            return sr;
        }

        private void SetupBoard()
        {
            var bg = MakeSprite("Board", Palette.BoardColor, 0, Vector3.zero, BoardSize + 0.3f);
            boardParts.Add(bg.transform);
            for (int r = 0; r < BoardSize; r++)
                for (int c = 0; c < BoardSize; c++)
                {
                    var cell = MakeSprite("Cell", Palette.EmptyCell, 1, CellPosition(r, c), TileScale);
                    boardParts.Add(cell.transform);
                }
        }

        private static Vector3 CellPosition(int row, int col)
        {
            float half = (BoardSize - 1) * 0.5f;
            return new Vector3(col - half, half - row, 0f);
        }

        // ---- game flow ----

        private void NewGame()
        {
            SavePrefs();
            game = new Game(BoardSize, Environment.TickCount);
            best = HighScore.Parse(PlayerPrefs.GetString(BestKey, ""));
            slideLeft = 0f;
            popLeft = 0f;
            hasQueuedMove = false;
            ClearMovers();
            RebuildTiles();
        }

        private void ClearMovers()
        {
            foreach (var m in movers) Destroy(m.Go);
            movers.Clear();
        }

        private void RebuildTiles()
        {
            foreach (var t in tiles) Destroy(t.Go);
            tiles.Clear();
            for (int r = 0; r < game.Size; r++)
                for (int c = 0; c < game.Size; c++)
                {
                    int v = game.Get(r, c);
                    if (v == 0) continue;
                    var sr = MakeSprite("Tile" + v, Palette.TileColor(v), 5, CellPosition(r, c), TileScale);
                    tiles.Add(new TileView { Go = sr.gameObject, Row = r, Col = c, Value = v });
                }
        }

        private void StartMove(Direction dir)
        {
            if (game.IsOver || game.AwaitingDecision) return;

            // Ask the core where every tile goes before the move changes the board.
            List<TileMove> trace = game.Preview(dir);
            int scoreBefore = game.Score;
            if (!game.Move(dir)) return;

            foreach (var t in tiles) Destroy(t.Go);
            tiles.Clear();
            ClearMovers();
            foreach (TileMove m in trace)
            {
                var sr = MakeSprite("Mover" + m.Value, Palette.TileColor(m.Value), 6, CellPosition(m.FromRow, m.FromCol), TileScale);
                movers.Add(new Mover
                {
                    Go = sr.gameObject,
                    From = CellPosition(m.FromRow, m.FromCol),
                    To = CellPosition(m.ToRow, m.ToCol),
                    Value = m.Value,
                    Current = CellPosition(m.FromRow, m.FromCol)
                });
            }
            slideLeft = SlideSeconds;

            if (game.Score > scoreBefore) sfx.Merge(); else sfx.Slide();
            if (game.AwaitingDecision) sfx.Win();
            if (game.IsOver) sfx.GameOver();
            UpdateBest();
            if (game.IsOver) SavePrefs();
        }

        private void FinishSlide()
        {
            slideLeft = 0f;
            ClearMovers();
            RebuildTiles();
            popLeft = PopSeconds;
        }

        private void UpdateBest()
        {
            bool isNew;
            best = HighScore.Update(best, game.Score, out isNew);
            if (isNew) PlayerPrefs.SetString(BestKey, HighScore.Format(best));
        }

        private void SavePrefs()
        {
            PlayerPrefs.Save();
        }

        private void DoUndo()
        {
            if (Animating) FinishSlide();
            if (!game.Undo()) return;
            sfx.Undo();
            popLeft = 0f;
            hasQueuedMove = false;
            RebuildTiles();
        }

        private void Update()
        {
            FitCamera();

            if (InputReader.NewGamePressed()) { NewGame(); return; }
            if (InputReader.UndoPressed()) DoUndo();
            if (game.AwaitingDecision && InputReader.ConfirmPressed()) game.ContinuePlaying();

            Direction dir;
            if (InputReader.TryGetMove(out dir))
            {
                // One move can wait in line while a slide plays, so quick key presses are not dropped.
                if (Animating) { hasQueuedMove = true; queuedMove = dir; }
                else StartMove(dir);
            }

            if (Animating)
            {
                slideLeft -= Time.deltaTime;
                float t = Mathf.Clamp01(1f - slideLeft / SlideSeconds);
                float eased = t * t * (3f - 2f * t);
                foreach (var m in movers)
                {
                    m.Current = new Vector3(
                        m.From.x + (m.To.x - m.From.x) * eased,
                        m.From.y + (m.To.y - m.From.y) * eased,
                        0f);
                    m.Go.transform.position = m.Current;
                }
                if (slideLeft <= 0f)
                {
                    FinishSlide();
                    if (hasQueuedMove)
                    {
                        hasQueuedMove = false;
                        StartMove(queuedMove);
                    }
                }
            }
            else if (popLeft > 0f)
            {
                // The newly spawned tile grows into place.
                popLeft -= Time.deltaTime;
                float grow = Mathf.Clamp01(1f - popLeft / PopSeconds);
                SpawnInfo s = game.LastSpawn;
                foreach (var t in tiles)
                    if (t.Row == s.Row && t.Col == s.Col)
                        t.Go.transform.localScale = Vector3.one * (TileScale * grow);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) SavePrefs();
        }

        private void OnApplicationQuit()
        {
            SavePrefs();
        }

        // ---- HUD (IMGUI: no UI package or canvas needed) ----

        private void EnsureStyles()
        {
            int h = Screen.height;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
                tileStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                bigStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            }
            titleStyle.fontSize = Mathf.Max(18, h / 18);
            labelStyle.fontSize = Mathf.Max(13, h / 32);
            bigStyle.fontSize = Mathf.Max(28, h / 11);
            buttonStyle.fontSize = Mathf.Max(13, h / 34);
        }

        private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            Color old = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        private Vector2 ToGui(Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            return new Vector2(sp.x, Screen.height - sp.y);
        }

        private void DrawTileNumber(Vector3 world, int value, float pixelsPerUnit)
        {
            Vector2 p = ToGui(world);
            float size = pixelsPerUnit * TileScale;
            int digits = value.ToString().Length;
            float factor = digits <= 2 ? 0.42f : digits == 3 ? 0.34f : digits == 4 ? 0.28f : 0.22f;
            tileStyle.fontSize = Mathf.Max(8, (int)(size * factor));
            Shadowed(new Rect(p.x - size / 2f, p.y - size / 2f, size, size), value.ToString(), tileStyle, Palette.TextColor(value));
        }

        private void OnGUI()
        {
            if (game == null || cam == null) return;
            EnsureStyles();

            float w = Screen.width, h = Screen.height;
            float pixelsPerUnit = h / (2f * cam.orthographicSize);

            // Numbers on tiles: resting ones from the board, in-flight ones at their animated position.
            foreach (var t in tiles)
            {
                if (popLeft > 0f && t.Row == game.LastSpawn.Row && t.Col == game.LastSpawn.Col) continue;
                DrawTileNumber(CellPosition(t.Row, t.Col), t.Value, pixelsPerUnit);
            }
            foreach (var m in movers) DrawTileNumber(m.Current, m.Value, pixelsPerUnit);

            // Header.
            Shadowed(new Rect(0f, h * 0.01f, w, h * 0.08f), "Merge Up", titleStyle, new Color(0.55f, 0.95f, 0.95f));
            Shadowed(new Rect(w * 0.04f, h * 0.10f, w * 0.45f, h * 0.06f), "Score " + game.Score, labelStyle, Color.white);
            Shadowed(new Rect(w * 0.04f, h * 0.15f, w * 0.45f, h * 0.06f), "Best " + Mathf.Max(best, game.Score), labelStyle, new Color(1f, 0.85f, 0.4f));

            // Buttons along the bottom.
            float bw = Mathf.Min(w * 0.3f, h * 0.35f), bh = h * 0.07f, by = h * 0.91f;
            GUI.enabled = game.CanUndo;
            if (GUI.Button(new Rect(w * 0.5f - bw - 8f, by, bw, bh), "Undo (U)", buttonStyle)) DoUndo();
            GUI.enabled = true;
            if (GUI.Button(new Rect(w * 0.5f + 8f, by, bw, bh), "New game (N)", buttonStyle)) NewGame();

            if (game.AwaitingDecision && !Animating)
            {
                DrawShade(w, h);
                Shadowed(new Rect(0f, h * 0.30f, w, h * 0.2f), "2048!", bigStyle, new Color(1f, 0.9f, 0.4f));
                if (GUI.Button(new Rect(w * 0.5f - bw - 8f, h * 0.55f, bw, bh), "Keep going", buttonStyle)) game.ContinuePlaying();
                if (GUI.Button(new Rect(w * 0.5f + 8f, h * 0.55f, bw, bh), "New game", buttonStyle)) NewGame();
            }
            else if (game.IsOver && !Animating)
            {
                DrawShade(w, h);
                Shadowed(new Rect(0f, h * 0.28f, w, h * 0.2f), "No moves left", bigStyle, new Color(1f, 0.5f, 0.5f));
                Shadowed(new Rect(0f, h * 0.48f, w, h * 0.07f), "Score " + game.Score + "   Best " + best, titleStyle, Color.white);
                if (GUI.Button(new Rect(w * 0.5f - bw - 8f, h * 0.57f, bw, bh), "Undo (U)", buttonStyle)) DoUndo();
                if (GUI.Button(new Rect(w * 0.5f + 8f, h * 0.57f, bw, bh), "New game (N)", buttonStyle)) NewGame();
            }
        }

        private static void DrawShade(float w, float h)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
