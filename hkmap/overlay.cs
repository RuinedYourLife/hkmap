using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using MapChanger;
using MapChanger.Map;
using MapChanger.MonoBehaviours;
using GlobalEnums;
using System.Reflection;
using System.Collections.Generic;

namespace hkmap
{
    public class Overlay : MonoBehaviour {
        private Canvas? _canvas;
        private RectTransform? _panel;
        private RectTransform? _playerDot;
        private Text? _sceneText;
        private RawImage? _mapImage;
        private Texture2D? _mapTexture;
        private RectTransform? _roomsContainer;
        private RectTransform? _viewport;
        private bool _roomsDirty;

        private Transform? _hero;

        private const float PanelWidth = 280f;
        private const float PanelHeight = 160f;
        private bool _visible = true;
        private string _lastScene = "";
        private bool _hasOrigin;
        private Vector2 _origin;
        private float _zoom = 5.5f;
        private float _zoneScale = 1f;
        private readonly Dictionary<MapZone, float> _zoneScaleCache = [];
        private bool _debug;
        private float _nextDebugLog;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            CreateUi();
        }

        private void Start()
        {
            FindHero();
            UpdateSceneText();
            _roomsDirty = true;
            SetZoom(_zoom);
            try
            {
                Events.OnEnterGame += OnMcEnterGame;
                Events.OnQuitToMenu += OnMcQuitToMenu;
                Events.OnSetGameMap += OnMcSetGameMap;
            }
            catch (Exception ex)
            {
                Debug.Log($"Failed to register MapChanger events: {ex}");
                throw;
            }
        }

        private void OnEnable()
        {
            FindHero();
        }

        private float _sceneTextNextUpdateTime;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.N))
            {
                ToggleVisible();
            }

            // Zoom controls
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                SetZoom(_zoom * 1.1f);
            }
            else if (Input.GetKeyDown(KeyCode.Minus))
            {
                SetZoom(_zoom / 1.1f);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                SetZoom(1f);
            }

            // Debug toggle
            if (Input.GetKeyDown(KeyCode.F8))
            {
                _debug = !_debug;
                Debug.Log($"[hkmap.debug] debug={( _debug ? "on" : "off" )}");
            }

            if (_hero == null)
            {
                FindHero();
                return;
            }

            UpdatePlayerDot();

            if (Time.unscaledTime >= _sceneTextNextUpdateTime)
            {
                UpdateSceneText();
                _sceneTextNextUpdateTime = Time.unscaledTime + 0.5f;
            }

            if (_roomsDirty && IsRoomsAvailable())
            {
                TryRenderNearbyRooms();
            }

            // Pan map under the centered dot based on hero movement
            UpdatePan();
        }

        private void CreateUi()
        {
            var canvasGo = new GameObject("MinimapCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40000; // on top

            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);
            _panel = panelGo.AddComponent<RectTransform>();
            _panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            _panel.anchorMin = new Vector2(1f, 1f);
            _panel.anchorMax = new Vector2(1f, 1f);
            _panel.pivot = new Vector2(1f, 1f);
            _panel.anchoredPosition = new Vector2(-16f, -16f);

            var img = panelGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.01f);
            var outline = panelGo.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.8f);
            outline.effectDistance = new Vector2(1f, -1f);

            // Viewport with clipping mask
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(_panel, false);
            _viewport = viewportGo.AddComponent<RectTransform>();
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(2f, 2f);
            _viewport.offsetMax = new Vector2(-2f, -2f);
            viewportGo.AddComponent<RectMask2D>();

            var bgGo = new GameObject("MapTexture");
            bgGo.transform.SetParent(_viewport, false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0f);
            bgRect.anchorMax = new Vector2(1f, 1f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            _mapImage = bgGo.AddComponent<RawImage>();
            CreateOrResizeTexture();

            var roomsGo = new GameObject("Rooms");
            roomsGo.transform.SetParent(_viewport, false);
            _roomsContainer = roomsGo.AddComponent<RectTransform>();
            _roomsContainer.anchorMin = new Vector2(0.5f, 0.5f);
            _roomsContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _roomsContainer.anchoredPosition = Vector2.zero;

            var textGo = new GameObject("SceneText");
            textGo.transform.SetParent(_panel, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(6f, -6f);
            _sceneText = textGo.AddComponent<Text>();
            _sceneText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _sceneText.fontSize = 14;
            _sceneText.alignment = TextAnchor.UpperLeft;
            _sceneText.color = Color.white;

            var playerGo = new GameObject("PlayerDot");
            playerGo.transform.SetParent(_panel, false);
            _playerDot = playerGo.AddComponent<RectTransform>();
            _playerDot.sizeDelta = new Vector2(6f, 6f);
            var dotImg = playerGo.AddComponent<Image>();
            dotImg.color = Color.white;
        }

        private void FindHero()
        {
            var heroGo = GameObject.Find("Knight") ?? GameObject.Find("HeroController");
            if (heroGo != null)
            {
                _hero = heroGo.transform;
                if (!_hasOrigin)
                {
                    var p = _hero.position;
                    _origin = new Vector2(p.x, p.y);
                    _hasOrigin = true;
                }
            }
        }

        private void UpdatePlayerDot()
        {
            // Center of the panel is player's position.
            if (_playerDot == null)
            {
                return;
            }
            var posUi = Vector2.zero;

            if (_mapTexture == null || _hero == null)
            {
                _playerDot.anchoredPosition = posUi;
                return;
            }

            // Position player dot on top of room layout if available
            posUi = Vector2.zero;
            _playerDot.anchoredPosition = posUi;
        }

        private void ToggleVisible()
        {
            _visible = !_visible;
            if (_canvas != null)
            {
                _canvas.enabled = _visible;
            }
        }

        private void UpdateSceneText()
        {
            if (_sceneText == null)
            {
                return;
            }
            try
            {
                var gm = GameManager.instance;
                var sceneName = gm != null ? gm.sceneName : string.Empty;
                if (!string.Equals(sceneName, _lastScene, StringComparison.Ordinal))
                {
                    _lastScene = sceneName;
                    ResetForSceneChange();
                }
            }
            catch
            {
                _sceneText.text = _lastScene;
            }
        }

        private Vector2 ComputePlayerUiOffset()
        {
            if (_roomsContainer == null || _hero == null)
            {
                return Vector2.zero;
            }
            var gm = GameManager.instance;
            var scene = gm != null ? gm.sceneName : null;
            if (string.IsNullOrEmpty(scene)) return Vector2.zero;
            if (!string.IsNullOrEmpty(Finder.GetMappedScene(scene))) return Vector2.zero;
            if (!Finder.TryGetTileMapDef(scene, out var tmd)) return Vector2.zero;

            // Determine scale same way as TryRenderNearbyRooms
            var vp = GetViewportSize();
            float maxW = vp.x;
            float maxH = vp.y;
            float scale = Mathf.Min(maxW / (float)tmd.Width, maxH / (float)tmd.Height);
            scale = Mathf.Clamp(scale, 0.2f, 4f);

            // Convert world pos offset relative to origin to UI pixels.
            // We approximate MapChanger's WorldMapPosition by mapping world-space delta proportionally
            // to the TileMapDef width/height, then to the sprite dimensions (w,h).
            var p = _hero.position;
            var relWorld = new Vector2(p.x - _origin.x, p.y - _origin.y);
            var ui = new Vector2(relWorld.x * scale, relWorld.y * scale) * _zoom;
            return ui;
        }

        private void ResetForSceneChange()
        {
            _hasOrigin = false;
            // Use scene center (0,0) as origin to match MapChanger's WorldMapPosition math
            _origin = Vector2.zero;
            _hasOrigin = true;
            CreateOrResizeTexture();
            if (_roomsContainer != null)
            {
                for (int i = _roomsContainer.childCount - 1; i >= 0; i--)
                {
                    Destroy(_roomsContainer.GetChild(i).gameObject);
                }
                // compute initial pan next frame after layout
            }
            _roomsDirty = true;
        }

        private void CreateOrResizeTexture()
        {
            if (_mapImage == null)
            {
                return;
            }
            var vp = GetViewportSize();
            int w = Mathf.Clamp(Mathf.RoundToInt(vp.x), 32, 1024);
            int h = Mathf.Clamp(Mathf.RoundToInt(vp.y), 32, 1024);
            if (_mapTexture == null || _mapTexture.width != w || _mapTexture.height != h)
            {
                _mapTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                _mapTexture.filterMode = FilterMode.Point;
                _mapTexture.wrapMode = TextureWrapMode.Clamp;
            }
            var data = _mapTexture.GetPixels32();
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = clear;
            }
            _mapTexture.SetPixels32(data);
            _mapTexture.Apply(false, false);
            _mapImage.texture = _mapTexture;
        }

        private void TryRenderNearbyRooms()
        {
            if (!TryGetRoomRenderingContext(out var context)) return;
            
            ClearExistingRooms();
            ComputeZoneScale(context);
            RenderRoomsInZone(context);
            FinalizeRoomRendering();
        }

        private bool TryGetRoomRenderingContext(out RoomRenderingContext context)
        {
            context = default;
            if (_roomsContainer == null) return false;
            
            var gm = GameManager.instance;
            var scene = gm?.sceneName;
            if (string.IsNullOrEmpty(scene)) return false;
            
            var baseScene = GameManager.GetBaseSceneName(scene);
            var mappedScene = Finder.GetMappedScene(baseScene);
            if (string.IsNullOrEmpty(mappedScene)) return false;

            var mappedRooms = BuiltInObjects.MappedRooms;
            if (mappedRooms == null || mappedRooms.Count == 0) return false;

            if (!mappedRooms.TryGetValue(mappedScene, out var currentRoom)) return false;
            var currentSr = currentRoom.GetComponentInChildren<SpriteRenderer>();
            if (currentSr == null || currentSr.sprite == null) return false;

            var currentZone = Finder.GetMapZone(mappedScene);
            var roomList = mappedRooms.Values
                .Where(rs => Finder.GetMapZone(rs.Rsd.SceneName) == currentZone)
                .ToList();

            context = new RoomRenderingContext
            {
                MappedScene = mappedScene,
                CurrentRoom = currentRoom,
                CurrentZone = currentZone,
                RoomList = roomList
            };
            return true;
        }

        private void ClearExistingRooms()
        {
            for (int i = _roomsContainer?.childCount - 1 ?? 0; i >= 0; i--)
            {
                Destroy(_roomsContainer?.GetChild(i).gameObject);
            }
        }

        private void ComputeZoneScale(RoomRenderingContext context)
        {
            if (_zoneScaleCache.TryGetValue(context.CurrentZone, out _zoneScale)) return;

            var vp = GetViewportSize();
            float maxW = vp.x;
            float maxH = vp.y;
            var bounds = CalculateZoneBounds(context.RoomList);
            
            if (!bounds.HasValue)
            {
                _zoneScale = 1f;
            }
            else
            {
                var (minX, minY, maxX, maxY) = bounds.Value;
                var unionW = Mathf.Max(0.001f, maxX - minX);
                var unionH = Mathf.Max(0.001f, maxY - minY);
                var fitScale = Mathf.Min(maxW / unionW, maxH / unionH);
                _zoneScale = Mathf.Clamp(fitScale, 0.1f, 10f);
            }
            _zoneScaleCache[context.CurrentZone] = _zoneScale;
        }

        private (float minX, float minY, float maxX, float maxY)? CalculateZoneBounds(List<RoomSprite> roomList)
        {
            bool any = false;
            float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
            
            foreach (var rs in roomList)
            {
                var srTest = rs.GetComponentInChildren<SpriteRenderer>();
                if (srTest == null || srTest.sprite == null) continue;
                
                var size = GetRoomDimensions(rs);
                var c = GetBuiltInMapPos(rs);
                var half = size * 0.5f;
                
                float lx = c.x - half.x, rx = c.x + half.x;
                float by = c.y - half.y, ty = c.y + half.y;
                
                if (!any)
                {
                    minX = lx; maxX = rx; minY = by; maxY = ty; any = true;
                }
                else
                {
                    if (lx < minX) minX = lx;
                    if (rx > maxX) maxX = rx;
                    if (by < minY) minY = by;
                    if (ty > maxY) maxY = ty;
                }
            }
            return any ? (minX, minY, maxX, maxY) : null;
        }

        private void RenderRoomsInZone(RoomRenderingContext context)
        {
            var origin = GetBuiltInMapPos(context.CurrentRoom);
            LogDebugInfo(context, origin);

            foreach (var rs in context.RoomList)
            {
                var sr = rs.GetComponentInChildren<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;
                CreateRoomVisual(rs, origin, context.MappedScene);
            }
        }

        private void CreateRoomVisual(RoomSprite rs, Vector2 origin, string mappedScene)
        {
            var go = new GameObject($"Room_{rs.Rsd.SceneName}");
            go.transform.SetParent(_roomsContainer, false);
            var r = go.AddComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);

            var basePos = GetBuiltInMapPos(rs);
            var delta = basePos - origin;
            r.anchoredPosition = delta * _zoneScale;

            var img = go.AddComponent<Image>();
            var sr = rs.GetComponentInChildren<SpriteRenderer>();
            img.sprite = sr.sprite;
            img.color = rs.Rsd.SceneName == mappedScene ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            
            var dims = GetRoomDimensions(rs);
            r.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, dims.x * _zoneScale);
            r.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, dims.y * _zoneScale);
        }

        private void LogDebugInfo(RoomRenderingContext context, Vector2 origin)
        {
            if (!_debug) return;
            var curDims = GetRoomDimensions(context.CurrentRoom);
            Debug.Log($"[hkmap.debug] build zone={context.CurrentZone} mapped={context.MappedScene} rooms={context.RoomList.Count} zoneScale={_zoneScale:F3} origin={origin} curDims={curDims}");
        }

        private void FinalizeRoomRendering()
        {
            _roomsDirty = false;
            _origin = Vector2.zero;
            _hasOrigin = true;
            UpdatePan();
        }

        private static Vector2 GetRoomDimensions(RoomSprite rs)
        {
            var pi = typeof(RoomSprite).GetProperty("Dimensions", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Vector2)pi.GetValue(rs);
        }

        private struct RoomRenderingContext
        {
            public string MappedScene;
            public RoomSprite CurrentRoom;
            public MapZone CurrentZone;
            public List<RoomSprite> RoomList;
        }

        private void UpdatePan()
        {
            if (_roomsContainer == null || _hero == null)
            {
                return;
            }
            var gm = GameManager.instance;
            var scene = gm != null ? gm.sceneName : null;
            if (string.IsNullOrEmpty(scene)) return;
            var baseScene = GameManager.GetBaseSceneName(scene);
            var mappedScene = Finder.GetMappedScene(baseScene);
            if (string.IsNullOrEmpty(mappedScene)) return;

            var mappedRooms = BuiltInObjects.MappedRooms;
            if (mappedRooms == null || !mappedRooms.TryGetValue(mappedScene, out var currentRoom)) return;

            // Room size in map space (internal), via reflection
            var pi = typeof(RoomSprite).GetProperty("Dimensions", BindingFlags.NonPublic | BindingFlags.Instance);
            var dims = (Vector2)pi.GetValue(currentRoom);
            if (!Finder.TryGetTileMapDef(mappedScene, out var tmd)) return;

            // Hero world delta since origin (scene center)
            var p = (Vector2)_hero.position;
            var deltaWorld = p - _origin;

            // Convert world delta -> map pixels within room, align to room center by subtracting half-dims
            float kx = (tmd.Width != 0) ? dims.x / tmd.Width : 0f;
            float ky = (tmd.Height != 0) ? dims.y / tmd.Height : 0f;
            var roomRelative = new Vector2(deltaWorld.x * kx, deltaWorld.y * ky);
            roomRelative -= dims * 0.5f; // match WorldMapPosition center alignment
            var deltaMap = roomRelative * _zoneScale;

            // Move map opposite to hero movement so dot stays centered
            _roomsContainer.anchoredPosition = -deltaMap * _zoom;

            if (_debug && Time.unscaledTime >= _nextDebugLog)
            {
                Debug.Log($"[hkmap.debug] hero={p} origin={_origin} mapped={mappedScene} dims={dims} tmd=({tmd.Width},{tmd.Height}) k=({kx:F3},{ky:F3}) deltaWorld={deltaWorld} roomRel={roomRelative} deltaMap={deltaMap} container={_roomsContainer.anchoredPosition} zoneScale={_zoneScale:F3} zoom={_zoom:F2}");
                _nextDebugLog = Time.unscaledTime + 0.5f;
            }
        }

        private void OnGUI()
        {
            if (_canvas == null || !_canvas.enabled) return;
            int count;
            TryGetMappedRoomsCount(out count);
            var gm = GameManager.instance;
            var scene = gm != null ? gm.sceneName : "";
            var msg = $"hkmap: scene={scene} zoom={_zoom:F2} roomsReady={IsRoomsAvailable()} mappedRooms={count} hero={( _hero==null ? "null" : _hero.position.ToString())}";
            // Draw at bottom left
            float width = 900, height = 22;
            float x = 8;
            float y = Screen.height - height - 8;
            GUI.Label(new Rect(x, y, width, height), msg);
        }

        private bool IsRoomsAvailable()
        {
            try
            {
                return BuiltInObjects.MappedRooms != null && BuiltInObjects.MappedRooms.Count > 0;
            }
            catch { return false; }
        }

        private bool TryGetMappedRoomsCount(out int count)
        {
            try
            {
                var dict = BuiltInObjects.MappedRooms;
                count = dict != null ? dict.Count : 0;
                return true;
            }
            catch { count = 0; return false; }
        }

        private void OnMcEnterGame()
        {
            _roomsDirty = true;
        }

        private void OnMcQuitToMenu()
        {
            _roomsDirty = false;
        }

        private void OnMcSetGameMap(GameObject go)
        {
            _roomsDirty = true;
        }

        private void SetZoom(float value)
        {
            _zoom = Mathf.Clamp(value, 0.25f, 6f);
            if (_roomsContainer != null)
            {
                _roomsContainer.localScale = new Vector3(_zoom, _zoom, 1f);
            }
        }

        private static Vector2 GetBuiltInMapPos(RoomSprite rs)
        {
            // Match BuiltInObjects.TryGetMapRoomPosition logic
            var parent = rs.transform.parent != null ? (Vector2)rs.transform.parent.localPosition : Vector2.zero;
            return parent + (Vector2)rs.transform.localPosition;
        }

        private Vector2 GetViewportSize()
        {
            if (_viewport != null)
            {
                return _viewport.rect.size;
            }
            return new Vector2(PanelWidth - 4f, PanelHeight - 4f);
        }
    }
}