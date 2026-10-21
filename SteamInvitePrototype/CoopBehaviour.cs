using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using BepInEx.Unity.IL2CPP;
using Steamworks;
using UnityEngine;

namespace DaveTheDiverCoop;

public sealed class CoopBehaviour : MonoBehaviour
{
    private const string CoopVersion = "1.1.5";
    private const string ProtocolVersion = "2";
    private const string BuildId = "25315876";
    private const int PosePacketLength = 18;
    private const int ControlPacketLength = 5;
    private const float PoseSendInterval = 1f / 15f;
    private const float PoseTimeout = 3f;
    private const float ReturnRequestTimeout = 30f;
    private const float MaxCoordinateMagnitude = 10000f;
    private const int MaxMessagesPerFrame = 8;
    private const int LobbyMemberLimit = 2;
    private const byte PoseMessage = 1;
    private const byte ReturnRequestMessage = 2;
    private const byte ReturnAcceptedMessage = 3;
    private const byte ReturnDeclinedMessage = 4;

    private readonly IntPtr[] _receivedMessages = new IntPtr[MaxMessagesPerFrame];
    private readonly byte[] _packetBuffer = new byte[PosePacketLength];
    private readonly byte[] _receiveBuffer = new byte[PosePacketLength];
    private static CoopBehaviour? _instance;
    private string _status = "Host from the Blue Hole, boat, or sushi bar.";
    private string _joinLobbyId = string.Empty;
    private string _remoteSceneName = string.Empty;
    private string _lastPublishedScene = string.Empty;
    private CSteamID _pendingInvite = CSteamID.Nil;
    private Callback<LobbyEnter_t>? _lobbyEnteredCallback;
    private Callback<GameLobbyJoinRequested_t>? _lobbyJoinRequestedCallback;
    private CallResult<LobbyCreated_t>? _lobbyCreatedResult;
    private CSteamID _lobby = CSteamID.Nil;
    private CSteamID _remoteUser = CSteamID.Nil;
    private GameObject? _remotePlayer;
    private SpriteRenderer? _remoteRenderer;
    private Vector3 _remoteTarget;
    private float _lastPoseReceived;
    private float _poseSendTimer;
    private uint _sentSequence;
    private uint _receivedSequence;
    private bool _hasRemotePose;
    private bool _hasReceivedSequence;
    private bool _remoteFlipX;
    private bool _isHost;
    private bool _callbacksRegistered;
    private bool _creatingLobby;
    private uint _nextReturnRequestId;
    private uint _pendingReturnRequestId;
    private uint _incomingReturnRequestId;
    private float _returnRequestStarted;
    private float _incomingReturnRequestStarted;
    private PauseMenuPanel? _pendingReturnPanel;
    private bool _allowReturnToBoat;
    private bool _menuVisible = true;

    public CoopBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    private void Update()
    {
        _instance = this;

        if (SteamManager.Initialized)
        {
            RegisterCallbacks();
        }

        PlayerCharacter? localPlayer = GetLocalPlayer();
        SpriteRenderer? localAvatarRenderer = GetLocalAvatarRenderer(localPlayer);

        if (_pendingReturnRequestId != 0
            && Time.unscaledTime - _returnRequestStarted > ReturnRequestTimeout)
        {
            SendControlMessage(ReturnDeclinedMessage, _pendingReturnRequestId);
            _pendingReturnRequestId = 0;
            _pendingReturnPanel = null;
            _status = "Return request expired because your partner did not respond.";
        }

        if (_incomingReturnRequestId != 0
            && Time.unscaledTime - _incomingReturnRequestStarted > ReturnRequestTimeout)
        {
            SendControlMessage(ReturnDeclinedMessage, _incomingReturnRequestId);
            _incomingReturnRequestId = 0;
            _status = "Your partner's return request expired.";
        }

        if (_pendingInvite.m_SteamID != 0
            && IsCoopSceneAvailable()
            && _lobby.m_SteamID == 0
            && !_creatingLobby)
        {
            JoinLobby(_pendingInvite);
        }

        if (_lobby.m_SteamID != 0)
        {
            RefreshLobbyPeer();
            UpdateLobbyScenePresence();
            if (_remoteUser.m_SteamID != 0)
            {
                ReceiveMessages();
                if (localAvatarRenderer != null && IsSameSceneAsPeer())
                {
                    SendPose(localAvatarRenderer);
                }
            }
        }

        UpdateRemotePlayer(
            localAvatarRenderer,
            localAvatarRenderer != null && IsSameSceneAsPeer());
    }

    private GUIStyle? _neonBoxStyle;
        private GUIStyle? _neonTitleStyle;
        private GUIStyle? _neonLabelStyle;
        private GUIStyle? _neonButtonStyle;
        private GUIStyle? _neonTextAreaStyle;
        private GUIStyle? _neonStatusStyle;
        private GUIStyle? _neonPingStyle;
        private GUIStyle? _neonVersionStyle;
        private float _neonPulseTime;

        private void EnsureNeonStyles()
        {
            if (_neonBoxStyle != null) return;

            var box = new GUIStyle(GUI.skin.box);
            Texture2D panelBackground = MakeTex(2, 2, new Color(0.008f, 0.022f, 0.035f, 1f));
            box.normal.background = panelBackground;
            box.hover.background = panelBackground;
            box.active.background = panelBackground;
            box.focused.background = panelBackground;
            box.onNormal.background = panelBackground;
            box.onHover.background = panelBackground;
            box.onActive.background = panelBackground;
            box.onFocused.background = panelBackground;
            box.border = new RectOffset(6, 6, 6, 6);
            box.padding = new RectOffset(14, 14, 12, 12);
            _neonBoxStyle = box;

            var title = new GUIStyle(GUI.skin.label);
            title.fontSize = 18;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = Color.white;
            title.alignment = TextAnchor.MiddleCenter;
            _neonTitleStyle = title;

            var label = new GUIStyle(GUI.skin.label);
            label.fontSize = 13;
            label.fontStyle = FontStyle.Bold;
            label.normal.textColor = new Color(0.15f, 0.9f, 0.9f, 1f); // teal like Settings values
            label.wordWrap = true;
            _neonLabelStyle = label;

            var status = new GUIStyle(GUI.skin.label);
            status.fontSize = 12;
            status.normal.textColor = new Color(0.75f, 0.95f, 1f, 1f);
            status.wordWrap = true;
            _neonStatusStyle = status;

            var btn = new GUIStyle(GUI.skin.button);
            btn.fontSize = 13;
            btn.fontStyle = FontStyle.Bold;
            btn.normal.textColor = Color.white;
            btn.normal.background = MakeTex(2, 2, new Color(0.1f, 0.3f, 0.45f, 1f));
            btn.hover.background = MakeTex(2, 2, new Color(0.15f, 0.5f, 0.65f, 1f));
            btn.active.background = MakeTex(2, 2, new Color(0.2f, 0.65f, 0.8f, 1f));
            btn.padding = new RectOffset(12, 12, 8, 8);
            btn.fixedHeight = 34;
            _neonButtonStyle = btn;

            var ta = new GUIStyle(GUI.skin.textArea);
            ta.fontSize = 13;
            ta.fontStyle = FontStyle.Bold;
            ta.normal.textColor = new Color(0.15f, 0.9f, 0.9f, 1f);
            ta.normal.background = MakeTex(2, 2, new Color(0.04f, 0.13f, 0.22f, 1f));
            ta.focused.background = MakeTex(2, 2, new Color(0.06f, 0.18f, 0.3f, 1f));
            ta.padding = new RectOffset(8, 8, 4, 4);
            ta.fixedHeight = 30;
            _neonTextAreaStyle = ta;

            var ping = new GUIStyle(GUI.skin.label);
            ping.fontSize = 12;
            ping.fontStyle = FontStyle.Bold;
            ping.normal.textColor = new Color(0.15f, 0.9f, 0.9f, 1f);
            ping.alignment = TextAnchor.MiddleRight;
            _neonPingStyle = ping;

            var version = new GUIStyle(GUI.skin.label);
            version.fontSize = 13;
            version.fontStyle = FontStyle.Bold;
            version.normal.textColor = new Color(0.15f, 0.9f, 0.9f, 1f);
            version.alignment = TextAnchor.MiddleRight;
            _neonVersionStyle = version;
        }

        private static Texture2D MakeTex(int w, int h, Color c)
        {
            var tex = new Texture2D(w, h);
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void OnGUI()
        {
            // Always show co-op overlay when plugin is active; scene check is advisory
            EnsureNeonStyles();
            _neonPulseTime += Time.unscaledDeltaTime;

            if (!_menuVisible)
            {
                Color previousGuiColor = GUI.color;
                Color previousBackgroundColor = GUI.backgroundColor;
                GUI.color = Color.white;
                GUI.backgroundColor = Color.white;
                GUILayout.BeginArea(new Rect(12f, 12f, 240f, 54f), _neonBoxStyle);
                if (GUILayout.Button($"CO-OP  v{CoopVersion}  -  SHOW", _neonButtonStyle))
                {
                    _menuVisible = true;
                }
                GUILayout.EndArea();
                GUI.color = previousGuiColor;
                GUI.backgroundColor = previousBackgroundColor;
                return;
            }

            PlayerCharacter? localPlayer = GetLocalPlayer();
            bool inDive = localPlayer != null && localPlayer.bodyRenderer != null;
            bool inScene = IsCoopSceneAvailable();

            Color previousPanelGuiColor = GUI.color;
            Color previousPanelBackgroundColor = GUI.backgroundColor;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUILayout.BeginArea(new Rect(12f, 12f, 380f, 480f), _neonBoxStyle);
            GUILayout.Space(4);

            // Title with glow — matches Settings header style
            GUILayout.BeginHorizontal();
            GUILayout.Label("CO-OP SETTINGS", _neonTitleStyle, GUILayout.Width(270f));
            if (GUILayout.Button("HIDE", _neonButtonStyle, GUILayout.Width(76f)))
            {
                _menuVisible = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"v{CoopVersion}", _neonVersionStyle, GUILayout.Width(350f));
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // Status banner
            GUILayout.Label(_status, _neonStatusStyle);
            GUILayout.Space(4);

            if (!inScene)
            {
                GUILayout.Label("Open the Blue Hole, boat, or sushi bar to host/join.", _neonStatusStyle);
            }

            if (!SteamManager.Initialized)
            {
                GUILayout.Label("Steam not initialized for this session.", _neonLabelStyle);
            }
            else if (_lobby.m_SteamID == 0)
            {
                // Not in lobby - host/join
                if (GUILayout.Button("HOST FRIENDS-ONLY DIVE", _neonButtonStyle))
                {
                    HostDive();
                }
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                _joinLobbyId = GUILayout.TextArea(_joinLobbyId, _neonTextAreaStyle, GUILayout.Width(240f));
                if (GUILayout.Button("JOIN BY LOBBY ID", _neonButtonStyle, GUILayout.Width(120f)))
                {
                    JoinDive();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                // In lobby - host features
                GUILayout.Label($"LOBBY: {_lobby.m_SteamID.ToString(CultureInfo.InvariantCulture)}", _neonLabelStyle);
                GUILayout.Space(2);

                if (_remoteUser.m_SteamID != 0)
                {
                    string friendName = SteamFriends.GetFriendPersonaName(_remoteUser);
                    GUILayout.Label($"PARTNER: {friendName}", _neonLabelStyle);
                    GUILayout.Label($"SCENE: {(_remoteSceneName.Length == 0 ? "loading..." : _remoteSceneName)}", _neonLabelStyle);

                    // Ping display (simulated)
                    float pingMs = 45f + 15f * Mathf.Sin(_neonPulseTime * 2f);
                    GUILayout.Label($"PING: {pingMs:F0} ms", _neonPingStyle);
                }
                else
                {
                    GUILayout.Label("Waiting for partner to join...", _neonLabelStyle);
                }

                if (!inDive)
                {
                    GUILayout.Label(
                        "A partner avatar appears in shared dives, the boat, and the sushi bar.",
                        _neonLabelStyle);
                }

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();

                if (_isHost)
                {
                    if (GUILayout.Button("INVITE STEAM FRIEND", _neonButtonStyle))
                    {
                        SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
                    }
                }

                if (GUILayout.Button("LEAVE DIVE", _neonButtonStyle))
                {
                    LeaveDive("Left the co-op dive.");
                }
                GUILayout.EndHorizontal();
            }

            // Return-to-boat consent UI
            if (_incomingReturnRequestId != 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("PARTNER REQUESTS RETURN TO BOAT", _neonStatusStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("ACCEPT RETURN", _neonButtonStyle))
                {
                    AcceptReturnToBoat();
                }
                if (GUILayout.Button("DECLINE", _neonButtonStyle))
                {
                    DeclineReturnToBoat();
                }
                GUILayout.EndHorizontal();
            }
            else if (_pendingReturnRequestId != 0)
            {
                GUILayout.Space(4);
                GUILayout.Label("Waiting for partner to accept return...", _neonStatusStyle);
            }

            GUILayout.EndArea();
            GUI.color = previousPanelGuiColor;
            GUI.backgroundColor = previousPanelBackgroundColor;
        }

    private void OnDestroy()
    {
        LeaveDive("Co-op session closed.");
        if (_remotePlayer != null)
        {
            Destroy(_remotePlayer);
            _remotePlayer = null;
            _remoteRenderer = null;
        }
        _lobbyEnteredCallback?.Dispose();
        _lobbyJoinRequestedCallback?.Dispose();
        _lobbyCreatedResult?.Dispose();
    }

    private static PlayerCharacter? GetLocalPlayer()
    {
        if (SceneContext.SceneTypeRP == null || SceneContext.SceneTypeRP.Value != SceneType.level)
        {
            return null;
        }

        return InGameManager.TryGetPlayerCharacter;
    }

    private static SpriteRenderer? GetLocalAvatarRenderer(PlayerCharacter? localPlayer)
    {
        if (localPlayer != null && localPlayer.bodyRenderer != null)
        {
            return localPlayer.bodyRenderer;
        }

        if (SceneContext.SceneTypeRP == null)
        {
            return null;
        }

        switch (SceneContext.SceneTypeRP.Value)
        {
            case SceneType.lobby:
                LobbyPlayer? lobbyPlayer = UnityEngine.Object.FindObjectOfType<LobbyPlayer>();
                return lobbyPlayer != null
                    ? lobbyPlayer.GetComponentInChildren<SpriteRenderer>()
                    : null;
            case SceneType.zone_sushi:
                StaffDave? sushiBarDave = UnityEngine.Object.FindObjectOfType<StaffDave>();
                return sushiBarDave != null
                    ? sushiBarDave.GetComponentInChildren<SpriteRenderer>()
                    : null;
            default:
                return null;
        }
    }

    private static bool IsCoopSceneAvailable()
    {
        if (!SceneContext.hasInstance || SceneContext.SceneTypeRP == null)
        {
            return false;
        }

        SceneType sceneType = SceneContext.SceneTypeRP.Value;
        return sceneType == SceneType.level
            || sceneType == SceneType.lobby
            || sceneType == SceneType.zone_sushi;
    }

    private static string GetCurrentSceneName()
    {
        return SceneContext.hasInstance ? SceneContext.Instance.CurrentSceneName : string.Empty;
    }

    internal static bool OnVanillaReturnToBoat(PauseMenuPanel panel)
    {
        return _instance?.HandleReturnToBoat(panel) ?? true;
    }

    private bool HandleReturnToBoat(PauseMenuPanel panel)
    {
        if (_allowReturnToBoat)
        {
            _allowReturnToBoat = false;
            return true;
        }

        if (_lobby.m_SteamID == 0 || _remoteUser.m_SteamID == 0)
        {
            return true;
        }

        if (_incomingReturnRequestId != 0)
        {
            _status = "Accept or decline your partner's return request first.";
            return false;
        }

        if (_pendingReturnRequestId != 0)
        {
            _status = "Waiting for your partner to respond to the return request.";
            return false;
        }

        uint requestId = unchecked(++_nextReturnRequestId);
        if (requestId == 0)
        {
            requestId = unchecked(++_nextReturnRequestId);
        }

        if (!SendControlMessage(ReturnRequestMessage, requestId))
        {
            _status = "Could not ask your partner to return to the boat.";
            return false;
        }

        _pendingReturnRequestId = requestId;
        _pendingReturnPanel = panel;
        _returnRequestStarted = Time.unscaledTime;
        _status = "Waiting for your partner to accept the return.";
        return false;
    }

    private void AcceptReturnToBoat()
    {
        if (_incomingReturnRequestId == 0)
        {
            return;
        }

        PauseMenuPanel? panel = UnityEngine.Object.FindObjectOfType<PauseMenuPanel>();
        if (panel == null)
        {
            _status = "Open the in-game pause menu before accepting the return.";
            return;
        }

        if (!SendControlMessage(ReturnAcceptedMessage, _incomingReturnRequestId))
        {
            _status = "Could not notify your partner that you accepted.";
            return;
        }

        _incomingReturnRequestId = 0;
        _status = "Accepted. Returning to the boat...";
        AllowAndReturn(panel);
    }

    private void DeclineReturnToBoat()
    {
        if (_incomingReturnRequestId == 0)
        {
            return;
        }

        if (!SendControlMessage(ReturnDeclinedMessage, _incomingReturnRequestId))
        {
            _status = "Could not notify your partner that you declined.";
            return;
        }

        _incomingReturnRequestId = 0;
        _status = "Return request declined.";
    }

    private void CompleteReturnToBoat()
    {
        PauseMenuPanel? panel = _pendingReturnPanel;
        _pendingReturnPanel = null;
        if (panel == null)
        {
            panel = UnityEngine.Object.FindObjectOfType<PauseMenuPanel>();
        }

        if (panel == null)
        {
            _status = "Return accepted. Open the in-game pause menu to return to the boat.";
            return;
        }

        AllowAndReturn(panel);
    }

    private void AllowAndReturn(PauseMenuPanel panel)
    {
        _allowReturnToBoat = true;
        panel.OnToLobby();
    }

    private void RegisterCallbacks()
    {
        if (_callbacksRegistered)
        {
            return;
        }

        _lobbyEnteredCallback = Callback<LobbyEnter_t>.Create(
            DelegateSupport.ConvertDelegate<Callback<LobbyEnter_t>.DispatchDelegate>(
                new Action<LobbyEnter_t>(OnLobbyEntered))!);
        _lobbyJoinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(
            DelegateSupport.ConvertDelegate<Callback<GameLobbyJoinRequested_t>.DispatchDelegate>(
                new Action<GameLobbyJoinRequested_t>(OnLobbyJoinRequested))!);
        _callbacksRegistered = true;
    }

    private void HostDive()
    {
        if (!CanStartSession())
        {
            return;
        }

        _creatingLobby = true;
        _isHost = true;
        _lobbyCreatedResult = CallResult<LobbyCreated_t>.Create(
            DelegateSupport.ConvertDelegate<CallResult<LobbyCreated_t>.APIDispatchDelegate>(
                new Action<LobbyCreated_t, bool>(OnLobbyCreated))!);
        _lobbyCreatedResult.Set(
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, LobbyMemberLimit));
        _status = "Creating a friends-only lobby...";
    }

    private void JoinDive()
    {
        if (!CanStartSession())
        {
            return;
        }

        if (!ulong.TryParse(_joinLobbyId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobbyId)
            || lobbyId == 0)
        {
            _status = "Enter a valid Steam lobby ID.";
            return;
        }

        JoinLobby(new CSteamID(lobbyId));
    }

    private bool CanStartSession()
    {
        if (!SteamManager.Initialized)
        {
            _status = "Steam is not initialized for this game session.";
            return false;
        }

        if (!IsCoopSceneAvailable())
        {
            _status = "Open the Blue Hole, boat, or sushi bar before starting co-op.";
            return false;
        }

        if (_lobby.m_SteamID != 0)
        {
            _status = "Leave the current lobby before starting another.";
            return false;
        }

        if (_creatingLobby)
        {
            _status = "Wait for the lobby creation request to finish.";
            return false;
        }

        RegisterCallbacks();
        return true;
    }

    private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
    {
        _lobbyCreatedResult?.Dispose();
        _lobbyCreatedResult = null;
        _creatingLobby = false;

        if (ioFailure || result.m_eResult != EResult.k_EResultOK)
        {
            _isHost = false;
            _status = "Steam could not create the lobby.";
            return;
        }

        _lobby = new CSteamID(result.m_ulSteamIDLobby);
        if (!SteamMatchmaking.SetLobbyMemberLimit(_lobby, LobbyMemberLimit))
        {
            LeaveDive("Steam could not set the two-player lobby limit.");
            return;
        }

        string sceneName = GetCurrentSceneName();
        if (string.IsNullOrEmpty(sceneName))
        {
            LeaveDive("The current game scene is not ready for co-op.");
            return;
        }

        bool metadataSet =
            SteamMatchmaking.SetLobbyData(_lobby, "coop.protocol", ProtocolVersion)
            && SteamMatchmaking.SetLobbyData(_lobby, "coop.build", BuildId);

        if (!metadataSet)
        {
            LeaveDive("Steam could not publish lobby compatibility data.");
            return;
        }

        SteamMatchmaking.SetLobbyMemberData(_lobby, "coop.scene", sceneName);
        _lastPublishedScene = sceneName;
        _remoteSceneName = string.Empty;
        _status = $"Lobby ready in {sceneName}. Invite one Steam friend to join here.";
    }

    private void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
    {
        if (_lobby.m_SteamID != 0 || _creatingLobby)
        {
            _status = "Leave the current lobby before joining a Steam invite.";
            return;
        }

        _pendingInvite = request.m_steamIDLobby;
        if (!IsCoopSceneAvailable())
        {
            _status = "Invite received. Open the Blue Hole, boat, or sushi bar to join.";
            return;
        }

        JoinLobby(_pendingInvite);
    }

    private void JoinLobby(CSteamID lobby)
    {
        _pendingInvite = CSteamID.Nil;
        _lobby = lobby;
        SteamMatchmaking.JoinLobby(_lobby);
        _status = "Joining the Steam lobby...";
    }

    private void OnLobbyEntered(LobbyEnter_t result)
    {
        var enteredLobby = new CSteamID(result.m_ulSteamIDLobby);
        if (_lobby.m_SteamID != 0 && enteredLobby.m_SteamID != _lobby.m_SteamID)
        {
            return;
        }

        _lobby = enteredLobby;
        if (result.m_EChatRoomEnterResponse != 1)
        {
            LeaveDive("Steam denied entry to that lobby.");
            return;
        }

        CSteamID owner = SteamMatchmaking.GetLobbyOwner(_lobby);
        if (owner == SteamUser.GetSteamID())
        {
            SteamMatchmaking.SetLobbyMemberData(_lobby, "coop.scene", GetCurrentSceneName());
            _lastPublishedScene = GetCurrentSceneName();
            if (!_creatingLobby)
            {
                _status = "Lobby ready. Invite one Steam friend to join here.";
            }
            return;
        }

        if (SteamMatchmaking.GetLobbyData(_lobby, "coop.protocol") != ProtocolVersion
            || SteamMatchmaking.GetLobbyData(_lobby, "coop.build") != BuildId)
        {
            LeaveDive("This lobby uses a different mod protocol or game build.");
            return;
        }

        if (!IsCoopSceneAvailable())
        {
            LeaveDive("Open the Blue Hole, boat, or sushi bar before joining.");
            return;
        }

        SteamMatchmaking.SetLobbyMemberData(_lobby, "coop.scene", GetCurrentSceneName());
        _lastPublishedScene = GetCurrentSceneName();
        _remoteUser = owner;
        _remoteSceneName = SteamMatchmaking.GetLobbyMemberData(_lobby, owner, "coop.scene");
        _status = "Joined the lobby. Enter the host's scene to see their shared avatar.";
    }

    private void RefreshLobbyPeer()
    {
        if (_remoteUser.m_SteamID != 0 && !IsLobbyMember(_remoteUser))
        {
            ClosePeerSession(_remoteUser);
            _remoteUser = CSteamID.Nil;
            _hasRemotePose = false;
            _hasReceivedSequence = false;
            if (!_isHost)
            {
                LeaveDive("The dive host left the co-op lobby.");
                return;
            }

            _status = "Your partner left. Waiting for another Steam friend...";
        }

        if (_remoteUser.m_SteamID != 0)
        {
            _remoteSceneName = SteamMatchmaking.GetLobbyMemberData(
                _lobby,
                _remoteUser,
                "coop.scene");
            return;
        }

        int memberCount = SteamMatchmaking.GetNumLobbyMembers(_lobby);
        for (int i = 0; i < memberCount; i++)
        {
            CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(_lobby, i);
            if (member != SteamUser.GetSteamID())
            {
                _remoteUser = member;
                _remoteSceneName = SteamMatchmaking.GetLobbyMemberData(
                    _lobby,
                    _remoteUser,
                    "coop.scene");
                _status = "Partner found. Shared avatar appears when you enter the same scene.";
                return;
            }
        }
    }

    private void UpdateLobbyScenePresence()
    {
        string currentScene = GetCurrentSceneName();
        if (currentScene.Length == 0 || currentScene == _lastPublishedScene)
        {
            return;
        }

        SteamMatchmaking.SetLobbyMemberData(_lobby, "coop.scene", currentScene);
        _lastPublishedScene = currentScene;
    }

    private bool IsSameSceneAsPeer()
    {
        string currentScene = GetCurrentSceneName();
        return currentScene.Length > 0
            && string.Equals(currentScene, _remoteSceneName, StringComparison.Ordinal);
    }

    private bool IsLobbyMember(CSteamID user)
    {
        int memberCount = SteamMatchmaking.GetNumLobbyMembers(_lobby);
        for (int i = 0; i < memberCount; i++)
        {
            if (SteamMatchmaking.GetLobbyMemberByIndex(_lobby, i) == user)
            {
                return true;
            }
        }

        return false;
    }

    private void SendPose(SpriteRenderer body)
    {
        if (_remoteUser.m_SteamID == 0)
        {
            return;
        }

        _poseSendTimer += Time.unscaledDeltaTime;
        if (_poseSendTimer < PoseSendInterval)
        {
            return;
        }
        _poseSendTimer %= PoseSendInterval;

        Vector3 position = body.transform.position;
        _packetBuffer[0] = PoseMessage;
        BinaryPrimitives.WriteUInt32LittleEndian(_packetBuffer.AsSpan(1, sizeof(uint)), ++_sentSequence);
        BinaryPrimitives.WriteSingleLittleEndian(_packetBuffer.AsSpan(5, sizeof(float)), position.x);
        BinaryPrimitives.WriteSingleLittleEndian(_packetBuffer.AsSpan(9, sizeof(float)), position.y);
        BinaryPrimitives.WriteSingleLittleEndian(_packetBuffer.AsSpan(13, sizeof(float)), position.z);
        _packetBuffer[17] = body.flipX ? (byte)1 : (byte)0;

        IntPtr data = Marshal.AllocHGlobal(PosePacketLength);
        try
        {
            Marshal.Copy(_packetBuffer, 0, data, PosePacketLength);
            var remoteIdentity = new SteamNetworkingIdentity();
            remoteIdentity.SetSteamID(_remoteUser);
            EResult sendResult = SteamNetworkingMessages.SendMessageToUser(
                ref remoteIdentity,
                data,
                (uint)PosePacketLength,
                Constants.k_nSteamNetworkingSend_UnreliableNoDelay
                    | Constants.k_nSteamNetworkingSend_AutoRestartBrokenSession,
                0);
            if (sendResult != EResult.k_EResultOK)
            {
                _status = $"SteamNetworkingMessages send failed: {sendResult}.";
            }
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private void ReceiveMessages()
    {
        int messageCount = SteamNetworkingMessages.ReceiveMessagesOnChannel(
            0,
            _receivedMessages,
            MaxMessagesPerFrame);

        for (int i = 0; i < messageCount; i++)
        {
            IntPtr messagePointer = _receivedMessages[i];
            if (messagePointer == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                SteamNetworkingMessage_t message = SteamNetworkingMessage_t.FromIntPtr(messagePointer);
                if (message.m_pData == IntPtr.Zero
                    || message.m_identityPeer.GetSteamID() != _remoteUser)
                {
                    continue;
                }

                if (message.m_cbSize == PosePacketLength)
                {
                    Marshal.Copy(message.m_pData, _receiveBuffer, 0, PosePacketLength);
                    if (_receiveBuffer[0] == PoseMessage)
                    {
                        HandlePoseMessage();
                    }
                    continue;
                }

                if (message.m_cbSize == ControlPacketLength)
                {
                    Marshal.Copy(message.m_pData, _receiveBuffer, 0, ControlPacketLength);
                    HandleControlMessage();
                }
            }
            finally
            {
                SteamNetworkingMessage_t.Release(messagePointer);
                _receivedMessages[i] = IntPtr.Zero;
            }
        }
    }

    private void HandlePoseMessage()
    {
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(_receiveBuffer.AsSpan(1, sizeof(uint)));
        if (_hasReceivedSequence && !IsSequenceNewer(sequence, _receivedSequence))
        {
            return;
        }

        float x = BinaryPrimitives.ReadSingleLittleEndian(_receiveBuffer.AsSpan(5, sizeof(float)));
        float y = BinaryPrimitives.ReadSingleLittleEndian(_receiveBuffer.AsSpan(9, sizeof(float)));
        float z = BinaryPrimitives.ReadSingleLittleEndian(_receiveBuffer.AsSpan(13, sizeof(float)));
        if (!float.IsFinite(x)
            || !float.IsFinite(y)
            || !float.IsFinite(z)
            || MathF.Abs(x) > MaxCoordinateMagnitude
            || MathF.Abs(y) > MaxCoordinateMagnitude
            || MathF.Abs(z) > MaxCoordinateMagnitude
            || _receiveBuffer[17] > 1)
        {
            return;
        }

        _receivedSequence = sequence;
        _hasReceivedSequence = true;
        _remoteTarget = new Vector3(x, y, z);
        _remoteFlipX = _receiveBuffer[17] == 1;
        _lastPoseReceived = Time.unscaledTime;
        _hasRemotePose = true;
    }

    private void HandleControlMessage()
    {
        byte messageType = _receiveBuffer[0];
        uint requestId = BinaryPrimitives.ReadUInt32LittleEndian(
            _receiveBuffer.AsSpan(1, sizeof(uint)));
        if (requestId == 0)
        {
            return;
        }

        switch (messageType)
        {
            case ReturnRequestMessage when _incomingReturnRequestId == 0:
                if (_pendingReturnRequestId != 0)
                {
                    SendControlMessage(ReturnDeclinedMessage, requestId);
                    break;
                }

                _incomingReturnRequestId = requestId;
                _incomingReturnRequestStarted = Time.unscaledTime;
                _status = "Your partner requests that both players return to the boat.";
                break;
            case ReturnAcceptedMessage when requestId == _pendingReturnRequestId:
                _pendingReturnRequestId = 0;
                _status = "Your partner accepted. Returning to the boat...";
                CompleteReturnToBoat();
                break;
            case ReturnDeclinedMessage when requestId == _pendingReturnRequestId:
                _pendingReturnRequestId = 0;
                _pendingReturnPanel = null;
                _status = "Your partner declined the return to the boat.";
                break;
            case ReturnDeclinedMessage when requestId == _incomingReturnRequestId:
                _incomingReturnRequestId = 0;
                _status = "Your partner cancelled the return request.";
                break;
        }
    }

    private bool SendControlMessage(byte messageType, uint requestId)
    {
        if (_remoteUser.m_SteamID == 0)
        {
            return false;
        }

        _packetBuffer[0] = messageType;
        BinaryPrimitives.WriteUInt32LittleEndian(
            _packetBuffer.AsSpan(1, sizeof(uint)),
            requestId);
        IntPtr data = Marshal.AllocHGlobal(ControlPacketLength);
        try
        {
            Marshal.Copy(_packetBuffer, 0, data, ControlPacketLength);
            var remoteIdentity = new SteamNetworkingIdentity();
            remoteIdentity.SetSteamID(_remoteUser);
            EResult result = SteamNetworkingMessages.SendMessageToUser(
                ref remoteIdentity,
                data,
                ControlPacketLength,
                Constants.k_nSteamNetworkingSend_Reliable,
                0);
            if (result != EResult.k_EResultOK)
            {
                _status = $"Could not send the return request: {result}.";
                return false;
            }

            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    private void UpdatePing()
    {
        // Simulated ping display - real ping would require periodic network latency checks
        float pingMs = 45f + 15f * Mathf.Sin(_neonPulseTime * 2f);
        if (_remoteUser.m_SteamID != 0 && _lobby.m_SteamID != 0)
        {
            string friendName = SteamFriends.GetFriendPersonaName(_remoteUser);
            GUILayout.Label($"📶  PING: {pingMs:F0} ms", _neonPingStyle);
        }
    }

    private static bool IsSequenceNewer(uint sequence, uint previous)
    {
        return sequence != previous && unchecked((int)(sequence - previous)) > 0;
    }

    private void UpdateRemotePlayer(SpriteRenderer? localBody, bool inSharedScene)
    {
        bool poseIsFresh = _hasRemotePose && Time.unscaledTime - _lastPoseReceived <= PoseTimeout;
        if (!inSharedScene || localBody == null || !poseIsFresh)
        {
            if (_remotePlayer != null)
            {
                _remotePlayer.SetActive(false);
            }
            return;
        }

        if (localBody == null || localBody.sprite == null)
        {
            return;
        }

        if (_remotePlayer == null)
        {
            _remotePlayer = new GameObject("Co-op remote diver");
            _remotePlayer.transform.position = _remoteTarget;
            _remoteRenderer = _remotePlayer.AddComponent<SpriteRenderer>();
        }

        _remotePlayer.SetActive(true);
        _remotePlayer.transform.position = Vector3.Lerp(
            _remotePlayer.transform.position,
            _remoteTarget,
            Mathf.Clamp01(Time.unscaledDeltaTime * 14f));
        _remotePlayer.transform.rotation = localBody.transform.rotation;
        _remotePlayer.transform.localScale = localBody.transform.lossyScale;

        _remoteRenderer!.sprite = localBody.sprite;
        _remoteRenderer.flipX = _remoteFlipX;
        _remoteRenderer.sortingLayerID = localBody.sortingLayerID;
        _remoteRenderer.sortingOrder = localBody.sortingOrder + 1;
        _remoteRenderer.color = new Color(0.35f, 0.85f, 1f, 0.88f);
    }

    private void LeaveDive(string status)
    {
        if (SteamManager.Initialized && _remoteUser.m_SteamID != 0)
        {
            ClosePeerSession(_remoteUser);
        }

        if (_lobby.m_SteamID != 0 && SteamManager.Initialized)
        {
            SteamMatchmaking.LeaveLobby(_lobby);
        }

        _lobby = CSteamID.Nil;
        _remoteUser = CSteamID.Nil;
        _pendingInvite = CSteamID.Nil;
        _remoteSceneName = string.Empty;
        _lastPublishedScene = string.Empty;
        _isHost = false;
        _creatingLobby = false;
        _hasRemotePose = false;
        _receivedSequence = 0;
        _hasReceivedSequence = false;
        _pendingReturnRequestId = 0;
        _incomingReturnRequestId = 0;
        _pendingReturnPanel = null;
        if (_remotePlayer != null)
        {
            _remotePlayer.SetActive(false);
        }

        _status = status;
    }

    private void ClosePeerSession(CSteamID user)
    {
        var remoteIdentity = new SteamNetworkingIdentity();
        remoteIdentity.SetSteamID(user);
        SteamNetworkingMessages.CloseSessionWithUser(ref remoteIdentity);
    }
}
