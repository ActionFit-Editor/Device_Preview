using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class DevicePreviewWindow : EditorWindow
{
    // ─── Device Data ───────────────────────────────────────────────────────────

    private class DeviceInfo
    {
        public string name;
        public int    w, h;
        public int    safeTop, safeBottom, safeLeft, safeRight;
        public int    CW(bool ls) => ls ? h : w;
        public int    CH(bool ls) => ls ? w : h;
    }

    private static readonly Color SafeColor = new Color(1f, 0.15f, 0.15f, 0.38f);

    // ─── State ─────────────────────────────────────────────────────────────────

    private List<DeviceInfo> _devices;
    private List<Texture2D>  _previews   = new();
    private List<Texture2D>  _snapshots  = new();
    private Vector2          _scroll;
    private string           _status     = "새로고침을 눌러 미리보기를 생성하세요.";
    private bool             _landscape;
    private bool             _safeArea   = true;
    private bool             _comparing;
    private int              _removeIdx  = -1;

    private bool   _showAdd;
    private string _addName = "", _addW = "1080", _addH = "1920", _addST = "0", _addSB = "0";

    private float   _matchWH          = 0f;
    private int     _matchModeOverride = -1; // -1=Auto, 0=W↔H, 1=Expand, 2=Shrink
    private int     _refW             = 1080;
    private int     _refH             = 1920;

    // 단축키
    private KeyCode _shortcutKey  = KeyCode.F5;
    private bool    _shortcutCtrl = false;
    private bool    _shortcutShift= false;
    private bool    _shortcutAlt  = false;
    private bool    _capturingKey = false;

    private static readonly FieldInfo _globalEventField =
        typeof(EditorApplication).GetField("globalEventHandler",
            BindingFlags.Static | BindingFlags.NonPublic);

    private bool _isCapturing;
    private EditorApplication.CallbackFunction _updateCapture;

    [MenuItem("Tools/JH Tools/Each Device Check UI Preview #&P")]
    static void Open() => GetWindow<DevicePreviewWindow>("Each Device Check UI Preview");

    // ─── Lifecycle ─────────────────────────────────────────────────────────────

    void OnEnable()
    {
        _devices = new List<DeviceInfo>
        {
            new DeviceInfo { name="Galaxy Flip3",      w=1080, h=2640, safeTop=80, safeBottom=0  },
            new DeviceInfo { name="Galaxy Fold2 Phone", w=816,  h=2260, safeTop=64, safeBottom=0  },
            new DeviceInfo { name="Galaxy Fold2 Tab",  w=1768, h=2208, safeTop=64, safeBottom=0  },
            new DeviceInfo { name="iPad Pro 12.9",     w=2048, h=2732, safeTop=24, safeBottom=20 },
        };
        SyncList();
        _safeArea  = EditorPrefs.GetBool ("DevPrev_Safe",     true);
        _landscape = EditorPrefs.GetBool ("DevPrev_Land",     false);
        _matchWH           = EditorPrefs.GetFloat("DevPrev_MatchWH",   0f);
        _matchModeOverride = EditorPrefs.GetInt  ("DevPrev_MatchMode", -1);
        _refW              = EditorPrefs.GetInt  ("DevPrev_RefW",      1080);
        _refH              = EditorPrefs.GetInt  ("DevPrev_RefH",      1920);
        _shortcutKey  = (KeyCode)EditorPrefs.GetInt("DevPrev_ShortcutKey",  (int)KeyCode.F5);
        _shortcutCtrl = EditorPrefs.GetBool("DevPrev_ShortcutCtrl", false);
        _shortcutShift= EditorPrefs.GetBool("DevPrev_ShortcutShift",false);
        _shortcutAlt  = EditorPrefs.GetBool("DevPrev_ShortcutAlt",  false);
        RegisterGlobalShortcut();

        PrefabStage.prefabStageOpened  += OnPrefabStageOpened;
        PrefabStage.prefabStageClosing += OnPrefabStageClosing;
    }

    void OnDisable()
    {
        EditorPrefs.SetBool ("DevPrev_Safe",     _safeArea);
        EditorPrefs.SetBool ("DevPrev_Land",     _landscape);
        EditorPrefs.SetFloat("DevPrev_MatchWH",   _matchWH);
        EditorPrefs.SetInt  ("DevPrev_MatchMode", _matchModeOverride);
        EditorPrefs.SetInt  ("DevPrev_RefW",      _refW);
        EditorPrefs.SetInt  ("DevPrev_RefH",      _refH);
        EditorPrefs.SetInt  ("DevPrev_ShortcutKey",  (int)_shortcutKey);
        EditorPrefs.SetBool ("DevPrev_ShortcutCtrl", _shortcutCtrl);
        EditorPrefs.SetBool ("DevPrev_ShortcutShift",_shortcutShift);
        EditorPrefs.SetBool ("DevPrev_ShortcutAlt",  _shortcutAlt);
        UnregisterGlobalShortcut();
        if (_updateCapture != null) { EditorApplication.update -= _updateCapture; _updateCapture = null; }
        _isCapturing = false;
        ClearPreviews();

        PrefabStage.prefabStageOpened  -= OnPrefabStageOpened;
        PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
    }

    void OnDestroy() => ClearPreviews();

    void Update()
    {
        if (_removeIdx >= 0)
        {
            int idx = _removeIdx; _removeIdx = -1;
            if (idx < _devices.Count)
            {
                _devices.RemoveAt(idx);
                if (idx < _previews.Count)  { var t = _previews[idx];  if (t) DestroyImmediate(t); _previews.RemoveAt(idx); }
                if (idx < _snapshots.Count) { var t = _snapshots[idx]; if (t) DestroyImmediate(t); _snapshots.RemoveAt(idx); }
            }
            Repaint();
        }
    }

    // ─── OnGUI ─────────────────────────────────────────────────────────────────

    void OnGUI()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None && _capturingKey)
        {
            if (e.keyCode != KeyCode.Escape)
            {
                _shortcutKey   = e.keyCode;
                _shortcutCtrl  = e.control;
                _shortcutShift = e.shift;
                _shortcutAlt   = e.alt;
                // 변경된 단축키로 글로벌 핸들러 재등록
                UnregisterGlobalShortcut();
                RegisterGlobalShortcut();
            }
            _capturingKey = false;
            e.Use();
            Repaint();
        }

        DrawToolbar();
        if (_showAdd) DrawAddPanel();
        DrawThumbnails();
    }

    // ─── Toolbar ───────────────────────────────────────────────────────────────

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(72)))
            EditorApplication.delayCall += Refresh;

        GUI.enabled = _previews.Any(t => t != null);
        if (GUILayout.Button("스냅샷", EditorStyles.toolbarButton, GUILayout.Width(52)))
            TakeSnapshot();
        GUI.enabled = true;

        GUI.enabled = _snapshots.Any(t => t != null);
        bool nc = GUILayout.Toggle(_comparing, "비교", EditorStyles.toolbarButton, GUILayout.Width(38));
        if (nc != _comparing) { _comparing = nc; Repaint(); }
        GUI.enabled = true;

        GUILayout.Space(4);

        bool nl = GUILayout.Toggle(_landscape, _landscape ? "⟷ 가로" : "↕ 세로", EditorStyles.toolbarButton, GUILayout.Width(58));
        if (nl != _landscape) { _landscape = nl; EditorApplication.delayCall += Refresh; }

        bool ns = GUILayout.Toggle(_safeArea, "SafeArea", EditorStyles.toolbarButton, GUILayout.Width(62));
        if (ns != _safeArea) { _safeArea = ns; Repaint(); }

        GUILayout.Space(4);
        GUILayout.Label("Ref", EditorStyles.miniLabel, GUILayout.Width(20));
        string nrwStr = EditorGUILayout.DelayedTextField(_refW.ToString(), GUILayout.Width(38));
        GUILayout.Label("×", EditorStyles.miniLabel, GUILayout.Width(10));
        string nrhStr = EditorGUILayout.DelayedTextField(_refH.ToString(), GUILayout.Width(38));
        if (int.TryParse(nrwStr, out int nrw) && nrw > 0 && nrw != _refW) { _refW = nrw; EditorApplication.delayCall += Refresh; }
        if (int.TryParse(nrhStr, out int nrh) && nrh > 0 && nrh != _refH) { _refH = nrh; EditorApplication.delayCall += Refresh; }

        GUILayout.Space(4);
        var modeLabels = new[] { "Auto", "W↔H", "Expand", "Shrink" };
        var modeValues = new[] { -1, 0, 1, 2 };
        int selIdx = Array.IndexOf(modeValues, _matchModeOverride);
        if (selIdx < 0) selIdx = 0;
        int newSelIdx = EditorGUILayout.Popup(selIdx, modeLabels, EditorStyles.toolbarDropDown, GUILayout.Width(62));
        if (newSelIdx != selIdx) { _matchModeOverride = modeValues[newSelIdx]; EditorApplication.delayCall += Refresh; }

        bool showSlider = _matchModeOverride == -1 || _matchModeOverride == 0;
        if (showSlider)
        {
            GUILayout.Label("W↔H", EditorStyles.miniLabel, GUILayout.Width(28));
            float nmw = GUILayout.HorizontalSlider(_matchWH, 0f, 1f, GUILayout.Width(64));
            if (!Mathf.Approximately(nmw, _matchWH)) { _matchWH = nmw; EditorApplication.delayCall += Refresh; }
            GUILayout.Label($"{_matchWH:F2}", EditorStyles.miniLabel, GUILayout.Width(26));
        }

        GUILayout.FlexibleSpace();

        // 단축키 설정 버튼
        string shortcutLabel = _capturingKey ? "키 입력..." : ShortcutLabel();
        var captureStyle = new GUIStyle(EditorStyles.toolbarButton);
        if (_capturingKey) captureStyle.normal.textColor = new Color(1f, 0.6f, 0f);
        if (GUILayout.Button(shortcutLabel, captureStyle, GUILayout.Width(80)))
        {
            _capturingKey = !_capturingKey;
            Repaint();
        }

        if (GUILayout.Button("저장", EditorStyles.toolbarButton, GUILayout.Width(38)))
            SaveAll();

        bool nAdd = GUILayout.Toggle(_showAdd, "+ 기기", EditorStyles.toolbarButton, GUILayout.Width(44));
        if (nAdd != _showAdd) _showAdd = nAdd;

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(_status, EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    // ─── Add Panel ─────────────────────────────────────────────────────────────

    void DrawAddPanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("기기 추가", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("이름", GUILayout.Width(28)); _addName = EditorGUILayout.TextField(_addName, GUILayout.Width(88));
        GUILayout.Label("W",    GUILayout.Width(12)); _addW    = EditorGUILayout.TextField(_addW,    GUILayout.Width(46));
        GUILayout.Label("H",    GUILayout.Width(12)); _addH    = EditorGUILayout.TextField(_addH,    GUILayout.Width(46));
        GUILayout.Label("상단", GUILayout.Width(28)); _addST   = EditorGUILayout.TextField(_addST,   GUILayout.Width(36));
        GUILayout.Label("하단", GUILayout.Width(28)); _addSB   = EditorGUILayout.TextField(_addSB,   GUILayout.Width(36));
        if (GUILayout.Button("추가", GUILayout.Width(36))
            && !string.IsNullOrEmpty(_addName)
            && int.TryParse(_addW, out int nw) && nw > 0
            && int.TryParse(_addH, out int nh) && nh > 0)
        {
            int.TryParse(_addST, out int st); int.TryParse(_addSB, out int sb);
            _devices.Add(new DeviceInfo { name=_addName, w=nw, h=nh, safeTop=st, safeBottom=sb });
            _previews.Add(null);
            _addName = "";
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // ─── Thumbnail Grid ────────────────────────────────────────────────────────

    void DrawThumbnails()
    {
        int cnt = _devices.Count;
        if (cnt == 0) return;

        bool compare = _comparing && _snapshots.Any(t => t != null);
        float cellW  = Mathf.Max(1f, (position.width - 8f) / cnt);

        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.BeginHorizontal();

        for (int i = 0; i < cnt; i++)
        {
            DeviceInfo dev  = _devices[i];
            int        cw   = dev.CW(_landscape), ch = dev.CH(_landscape);
            float      cellH = cellW * (float)ch / cw;
            Texture2D  cur  = i < _previews.Count  ? _previews[i]  : null;
            Texture2D  snap = i < _snapshots.Count ? _snapshots[i] : null;

            GUILayout.BeginVertical(GUILayout.Width(cellW));

            // 기기명 + 삭제 버튼
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{dev.name}\n{cw}×{ch}", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(cellW - 18));
            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(16), GUILayout.Height(16)))
                _removeIdx = i;
            EditorGUILayout.EndHorizontal();

            if (compare)
            {
                // 이전 / 현재 나란히
                float half = cellW * 0.5f;
                float halfH = half * (float)ch / cw;

                EditorGUILayout.BeginHorizontal();
                DrawThumbCell(snap, dev, cw, ch, half, halfH, "이전");
                DrawThumbCell(cur,  dev, cw, ch, half, halfH, "현재");
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                Rect img = GUILayoutUtility.GetRect(cellW, cellH, GUILayout.Width(cellW));
                if (cur != null) { GUI.DrawTexture(img, cur, ScaleMode.ScaleToFit); if (_safeArea) DrawSafeOverlay(img, dev, cw, ch); }
                else EditorGUI.DrawRect(img, new Color(0.12f, 0.12f, 0.12f));
            }

            GUILayout.EndVertical();
        }

        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
    }

    void DrawThumbCell(Texture2D tex, DeviceInfo dev, int cw, int ch, float w, float h, string label)
    {
        GUILayout.BeginVertical(GUILayout.Width(w));
        GUILayout.Label(label, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(w));
        Rect img = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w));
        if (tex != null) { GUI.DrawTexture(img, tex, ScaleMode.ScaleToFit); if (_safeArea) DrawSafeOverlay(img, dev, cw, ch); }
        else EditorGUI.DrawRect(img, new Color(0.12f, 0.12f, 0.12f));
        GUILayout.EndVertical();
    }

    // ─── Safe Area Overlay ─────────────────────────────────────────────────────

    void DrawSafeOverlay(Rect imgRect, DeviceInfo dev, int cw, int ch)
    {
        float aspect = (float)cw / ch;
        float dw = imgRect.width, dh = imgRect.height;
        if (dw / dh > aspect) dw = dh * aspect; else dh = dw / aspect;
        float ox = imgRect.x + (imgRect.width  - dw) * 0.5f;
        float oy = imgRect.y + (imgRect.height - dh) * 0.5f;

        int st = _landscape ? dev.safeLeft   : dev.safeTop;
        int sb = _landscape ? dev.safeRight  : dev.safeBottom;
        int sl = _landscape ? dev.safeBottom : dev.safeLeft;
        int sr = _landscape ? dev.safeTop    : dev.safeRight;

        float px = dw / cw, py = dh / ch;
        if (st > 0) EditorGUI.DrawRect(new Rect(ox,                oy,                dw,      st * py), SafeColor);
        if (sb > 0) EditorGUI.DrawRect(new Rect(ox,                oy + dh - sb * py, dw,      sb * py), SafeColor);
        if (sl > 0) EditorGUI.DrawRect(new Rect(ox,                oy + st * py,      sl * px, dh - (st + sb) * py), SafeColor);
        if (sr > 0) EditorGUI.DrawRect(new Rect(ox + dw - sr * px, oy + st * py,      sr * px, dh - (st + sb) * py), SafeColor);
    }

    // ─── Snapshot ─────────────────────────────────────────────────────────────

    void TakeSnapshot()
    {
        for (int i = 0; i < _snapshots.Count; i++)
            if (_snapshots[i] != null) DestroyImmediate(_snapshots[i]);
        _snapshots.Clear();

        foreach (var src in _previews)
        {
            if (src == null) { _snapshots.Add(null); continue; }
            var dst = new Texture2D(src.width, src.height, src.format, false);
            Graphics.CopyTexture(src, dst);
            _snapshots.Add(dst);
        }
        _comparing = true;
        SetStatus("스냅샷 저장 완료. 새로고침 후 비교하세요.");
    }

    // ─── Save ──────────────────────────────────────────────────────────────────

    void SaveAll()
    {
        if (_previews.All(t => t == null)) { SetStatus("먼저 새로고침을 눌러주세요."); return; }
        string folder = EditorUtility.SaveFolderPanel("스크린샷 저장 위치", "", "DevicePreviews");
        if (string.IsNullOrEmpty(folder)) return;
        int saved = 0;
        for (int i = 0; i < _devices.Count && i < _previews.Count; i++)
        {
            if (_previews[i] == null) continue;
            string safe = string.Concat(_devices[i].name.Split(Path.GetInvalidFileNameChars()));
            File.WriteAllBytes(Path.Combine(folder, $"{safe}_{(_landscape?"land":"port")}.png"), _previews[i].EncodeToPNG());
            saved++;
        }
        SetStatus(saved > 0 ? $"{saved}개 저장 완료 → {folder}" : "저장 실패");
    }

    // ─── Capture ───────────────────────────────────────────────────────────────

    void Refresh()
    {
        if (_isCapturing) return;

        SyncList();

        PrefabStage ps       = PrefabStageUtility.GetCurrentPrefabStage();
        bool        inPrefab = ps != null;

        Canvas[] roots = (inPrefab
            ? ps.prefabContentsRoot.GetComponentsInChildren<Canvas>(true)
            : (IEnumerable<Canvas>)Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            .Where(c => c.isRootCanvas && c.gameObject.activeInHierarchy)
            .ToArray();

        if (roots.Length == 0 && inPrefab)
            roots = GetInScene<Canvas>(ps.scene)
                .Where(c => c.isRootCanvas && c.gameObject.activeInHierarchy)
                .ToArray();

        // Canvas 없는 UI 패널 프리팹: RectTransform 루트에 임시 Canvas 주입
        Canvas tempCanvas = null;
        if (roots.Length == 0 && inPrefab
            && ps.prefabContentsRoot != null
            && ps.prefabContentsRoot.GetComponent<RectTransform>() != null)
        {
            tempCanvas = ps.prefabContentsRoot.AddComponent<Canvas>();
            roots = new[] { tempCanvas };
        }

        if (roots.Length == 0) { SetStatus("활성화된 Canvas가 없습니다."); return; }

        string modeName  = inPrefab
            ? $"Prefab: {Path.GetFileNameWithoutExtension(ps.assetPath)}"
            : (Application.isPlaying ? "Play" : "Edit");
        var devicesSnap  = _devices.ToList();
        var previewsSnap = _previews;
        bool landscape   = _landscape;
        float matchWH      = _matchWH;
        int   modeOverride = _matchModeOverride;
        int   refW         = _refW;
        int   refH         = _refH;

        var origMode  = roots.Select(c => c.renderMode).ToArray();
        var origWCam  = roots.Select(c => c.worldCamera).ToArray();
        var origPos   = roots.Select(c => c.transform.position).ToArray();
        var origScale = roots.Select(c => c.transform.localScale).ToArray();
        var origSize  = roots.Select(c => c.GetComponent<RectTransform>().sizeDelta).ToArray();
        var scalers   = roots.Select(c => c.GetComponent<CanvasScaler>()).ToArray();
        var scalerOn  = scalers.Select(s => s != null && s.enabled).ToArray();

        // SceneView 카메라를 납치해서 RT로 리다이렉트.
        // 커스텀 카메라는 URP 2D Renderer에서 CanvasRenderer 메시를 렌더하지 못함.
        // SceneView 카메라는 이미 프리팹 스테이지 캔버스를 올바르게 렌더하고 있으므로 이를 활용.
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) { SetStatus("씬뷰를 열고 새로고침하세요."); return; }
        var cam = sv.camera;

        // SceneView 카메라 원본 상태 저장
        var svOrigPos    = cam.transform.position;
        var svOrigRot    = cam.transform.rotation;
        var svOrigOrtho  = cam.orthographic;
        var svOrigSize   = cam.orthographicSize;
        var svOrigAspect = cam.aspect;
        var svOrigTarget = cam.targetTexture;
        var svOrigClear  = cam.clearFlags;
        var svOrigBg     = cam.backgroundColor;
        var svOrigMask   = cam.cullingMask;
        var svOrigNear   = cam.nearClipPlane;
        var svOrigFar    = cam.farClipPlane;

        // WorldSpace로 전환 (1회, 루프 밖)
        // → 캔버스가 씬 지오메트리(MeshRenderer)로 존재 → cam.Render()가 URP에서도 캡처
        // ScreenSpaceCamera는 게임 루프의 UI batch submission에 의존 → 에디터 모드 불가
        for (int ci = 0; ci < roots.Length; ci++)
        {
            if (scalers[ci] != null) scalers[ci].enabled = false;
            roots[ci].renderMode  = RenderMode.WorldSpace;
            roots[ci].worldCamera = cam;
            var rtr = roots[ci].GetComponent<RectTransform>();
            rtr.localScale = Vector3.one;
            rtr.position   = Vector3.zero;
        }

        _isCapturing = true;
        Texture2D[] captured = new Texture2D[devicesSnap.Count];

        void RestoreAndFinish(bool success)
        {
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || roots[i] == tempCanvas) continue;
                var rtr               = roots[i].GetComponent<RectTransform>();
                roots[i].renderMode   = origMode[i];
                roots[i].worldCamera  = origWCam[i];
                roots[i].transform.position   = origPos[i];
                roots[i].transform.localScale  = origScale[i];
                rtr.sizeDelta = origSize[i];
                if (scalers[i] != null) scalers[i].enabled = scalerOn[i];
            }
            if (tempCanvas != null) DestroyImmediate(tempCanvas);
            foreach (var root in roots)
                if (root != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(root.GetComponent<RectTransform>());
            Canvas.ForceUpdateCanvases();

            // SceneView 카메라 원본 복원
            cam.orthographic     = svOrigOrtho;
            cam.orthographicSize = svOrigSize;
            cam.aspect           = svOrigAspect;
            cam.targetTexture    = svOrigTarget;
            cam.clearFlags       = svOrigClear;
            cam.backgroundColor  = svOrigBg;
            cam.cullingMask      = svOrigMask;
            cam.nearClipPlane    = svOrigNear;
            cam.farClipPlane     = svOrigFar;
            cam.transform.SetPositionAndRotation(svOrigPos, svOrigRot);
            sv.Repaint();

            if (success)
            {
                for (int d = 0; d < devicesSnap.Count && d < captured.Length; d++)
                {
                    if (d < previewsSnap.Count && previewsSnap[d] != null)
                        DestroyImmediate(previewsSnap[d]);
                    if (d < previewsSnap.Count)
                        previewsSnap[d] = captured[d];
                }
                SetStatus($"캡처 완료  {DateTime.Now:HH:mm:ss}  [{modeName}]  {(landscape ? "가로" : "세로")}");
            }
            _isCapturing = false;
            SceneView.RepaintAll();
            Repaint();
        }

        if (_updateCapture != null) EditorApplication.update -= _updateCapture;
        _updateCapture = () =>
        {
            EditorApplication.update -= _updateCapture;
            _updateCapture = null;

            RenderTexture rt = null;
            try
            {
                for (int d = 0; d < devicesSnap.Count; d++)
                {
                    int dw = devicesSnap[d].CW(landscape), dh = devicesSnap[d].CH(landscape);

                    // CanvasScaler 논리 해상도 계산 (없으면 fallback ref 사용)
                    Vector2 logical = ComputeLogicalSize(scalers, dw, dh, matchWH, modeOverride, refW, refH);
                    float logW = logical.x, logH = logical.y;

                    rt = new RenderTexture(dw, dh, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                    rt.Create();

                    // 기기별: SceneView 카메라를 RT로 리다이렉트 (논리 해상도 기준)
                    cam.orthographic     = true;
                    cam.orthographicSize = logH * 0.5f;
                    cam.aspect           = logW / logH;
                    cam.nearClipPlane    = 0.1f;
                    cam.farClipPlane     = 2000f;
                    cam.clearFlags       = CameraClearFlags.SolidColor;
                    cam.backgroundColor  = Color.clear;
                    cam.cullingMask      = -1;
                    cam.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1000f), Quaternion.identity);
                    cam.targetTexture    = rt;

                    // 기기별 캔버스 크기 적용 (논리 해상도)
                    foreach (var root in roots)
                        root.GetComponent<RectTransform>().sizeDelta = new Vector2(logW, logH);

                    foreach (var root in roots)
                        LayoutRebuilder.ForceRebuildLayoutImmediate(root.GetComponent<RectTransform>());
                    Canvas.ForceUpdateCanvases();

                    cam.Render();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(dw, dh, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, dw, dh), 0, 0);
                    tex.Apply();
                    captured[d] = tex;
                    RenderTexture.active = null;

                    cam.targetTexture = null;
                    rt.Release();
                    DestroyImmediate(rt);
                    rt = null;
                }
                RestoreAndFinish(true);
            }
            catch (Exception e)
            {
                if (rt != null) { rt.Release(); DestroyImmediate(rt); }
                SetStatus($"오류: {e.Message}");
                Debug.LogError($"[DevicePreview] {e}");
                try { RestoreAndFinish(false); } catch { _isCapturing = false; }
            }
        };
        EditorApplication.update += _updateCapture;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    void RegisterGlobalShortcut()
    {
        if (_globalEventField == null) return;
        var cur = (EditorApplication.CallbackFunction)_globalEventField.GetValue(null);
        cur -= HandleGlobalKey; // 중복 방지
        cur += HandleGlobalKey;
        _globalEventField.SetValue(null, cur);
    }

    void UnregisterGlobalShortcut()
    {
        if (_globalEventField == null) return;
        var cur = (EditorApplication.CallbackFunction)_globalEventField.GetValue(null);
        cur -= HandleGlobalKey;
        _globalEventField.SetValue(null, cur);
    }

    void HandleGlobalKey()
    {
        var e = Event.current;
        if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None) return;
        if (_capturingKey) return; // 키 설정 중에는 무시
        if (e.keyCode   == _shortcutKey
            && e.control  == _shortcutCtrl
            && e.shift    == _shortcutShift
            && e.alt      == _shortcutAlt)
        {
            EditorApplication.delayCall += Refresh;
            e.Use();
        }
    }

    string ShortcutLabel()
    {
        var parts = new System.Text.StringBuilder();
        if (_shortcutCtrl)  parts.Append("Ctrl+");
        if (_shortcutShift) parts.Append("Shift+");
        if (_shortcutAlt)   parts.Append("Alt+");
        parts.Append(_shortcutKey.ToString());
        return parts.ToString();
    }

    void OnPrefabStageOpened(PrefabStage _)  => EditorApplication.delayCall += Refresh;
    void OnPrefabStageClosing(PrefabStage _) { ClearPreviews(); SetStatus("새로고침을 눌러 미리보기를 생성하세요."); }

    void SyncList()
    {
        while (_previews.Count  < _devices.Count) _previews.Add(null);
        while (_previews.Count  > _devices.Count) { int l = _previews.Count-1;  if (_previews[l])  DestroyImmediate(_previews[l]);  _previews.RemoveAt(l); }
        while (_snapshots.Count < _devices.Count) _snapshots.Add(null);
        while (_snapshots.Count > _devices.Count) { int l = _snapshots.Count-1; if (_snapshots[l]) DestroyImmediate(_snapshots[l]); _snapshots.RemoveAt(l); }
    }

    void ClearPreviews()
    {
        foreach (var t in _previews)  if (t) DestroyImmediate(t); _previews.Clear();
        foreach (var t in _snapshots) if (t) DestroyImmediate(t); _snapshots.Clear();
    }

    void SetStatus(string msg) { _status = msg; if (this) Repaint(); }

    static Vector2 ComputeLogicalSize(CanvasScaler[] scalers, int deviceW, int deviceH,
        float matchWHOverride = -1f, int matchModeOverride = -1, int fallbackRefW = 1080, int fallbackRefH = 1920)
    {
        var cs = scalers?.FirstOrDefault(s => s != null);

        float refW, refH, matchT;
        CanvasScaler.ScreenMatchMode matchMode;

        if (cs != null && cs.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            refW      = cs.referenceResolution.x;
            refH      = cs.referenceResolution.y;
            matchMode = cs.screenMatchMode;
            matchT    = matchWHOverride >= 0f ? matchWHOverride : cs.matchWidthOrHeight;
        }
        else
        {
            // CanvasScaler 없음(팝업 프리팹 등) → 창에서 설정한 reference resolution으로 fallback
            refW      = fallbackRefW;
            refH      = fallbackRefH;
            matchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            matchT    = matchWHOverride >= 0f ? matchWHOverride : 0f;
        }

        // 툴바 모드 오버라이드 적용 (-1 이면 CanvasScaler 값 그대로 사용)
        if (matchModeOverride >= 0)
            matchMode = (CanvasScaler.ScreenMatchMode)matchModeOverride;

        float scale;
        switch (matchMode)
        {
            case CanvasScaler.ScreenMatchMode.MatchWidthOrHeight:
                scale = Mathf.Pow(deviceW / refW, 1f - matchT) * Mathf.Pow(deviceH / refH, matchT);
                break;
            case CanvasScaler.ScreenMatchMode.Expand:
                scale = Mathf.Min(deviceW / refW, deviceH / refH);
                break;
            case CanvasScaler.ScreenMatchMode.Shrink:
                scale = Mathf.Max(deviceW / refW, deviceH / refH);
                break;
            default:
                scale = deviceW / refW;
                break;
        }
        return new Vector2(deviceW / scale, deviceH / scale);
    }

    static T[] GetInScene<T>(UnityEngine.SceneManagement.Scene scene) where T : Component
    {
        var list = new List<T>();
        foreach (var go in scene.GetRootGameObjects())
            list.AddRange(go.GetComponentsInChildren<T>(true));
        return list.ToArray();
    }
}
