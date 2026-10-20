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
    private string _status = "Host or join from any in-game scene.";
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
        bool inDive = localPlayer != null && localPlayer.bodyRenderer != null;

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

        if (_pendingInvite.m_SteamID != 0 && IsInGameScene() && _lobby.m_SteamID == 0 && !_creatingLobby)
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
                if (inDive && IsSameSceneAsPeer())
                {
                    SendPose(localPlayer!);
                }
            }
        }

        UpdateRemotePlayer(localPlayer, inDive && IsSameSceneAsPeer());
    }

    private void OnGUI()
    {
        if (!IsInGameScene())
        {
            return;
        }

        PlayerCharacter? localPlayer = GetLocalPlayer();
        bool inDive = localPlayer != null && localPlayer.bodyRenderer != null;

        GUILayout.BeginArea(new Rect(12f, 12f, 370f, 330f), GUI.skin.box);
        GUILayout.Label("Co-op Settings (experimental)", Array.Empty<GUILayoutOption>());
        GUILayout.Label(_status, Array.Empty<GUILayoutOption>());

        if (!SteamManager.Initialized)
        {
            GUILayout.Label("Steam is not initialized for this game session.", Array.Empty<GUILayoutOption>());
        }
        else if (_lobby.m_SteamID == 0)
        {
            if (GUILayout.Button("Host friends-only session", Array.Empty<GUILayoutOption>()))
            {
                HostDive();
            }

            GUILayout.BeginHorizontal(Array.Empty<GUILayoutOption>());
            _joinLobbyId = GUILayout.TextArea(
                _joinLobbyId,
                new[] { GUILayout.Width(230f), GUILayout.Height(25f) });
            if (GUILayout.Button("Join by lobby ID", Array.Empty<GUILayoutOption>()))
            {
                JoinDive();
            }
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label(
                $"Lobby ID: {_lobby.m_SteamID.ToString(CultureInfo.InvariantCulture)}",
                Array.Empty<GUILayoutOption>());
            if (_remoteUser.m_SteamID != 0)
            {
                string friendName = SteamFriends.GetFriendPersonaName(_remoteUser);
                GUILayout.Label($"Friend: {friendName}", Array.Empty<GUILayoutOption>());
                GUILayout.Label(
                    $"Friend's scene: {(_remoteSceneName.Length == 0 ? "loading..." : _remoteSceneName)}",
                    Array.Empty<GUILayoutOption>());
            }

            if (!inDive)
            {
                GUILayout.Label(
                    "A second diver is only shown during the same Blue Hole dive.",
                    Array.Empty<GUILayoutOption>());
            }

            GUILayout.BeginHorizontal(Array.Empty<GUILayoutOption>());
            if (_isHost && GUILayout.Button("Invite Steam friend", Array.Empty<GUILayoutOption>()))
            {
                SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
            }
            if (GUILayout.Button("Leave dive", Array.Empty<GUILayoutOption>()))
            {
                LeaveDive("Left the co-op dive.");
            }
            GUILayout.EndHorizontal();
        }

        if (_incomingReturnRequestId != 0)
        {
            GUILayout.Label("Your partner requests that both players return to the boat.");
            GUILayout.BeginHorizontal(Array.Empty<GUILayoutOption>());
            if (GUILayout.Button("Accept return", Array.Empty<GUILayoutOption>()))
            {
                AcceptReturnToBoat();
            }
            if (GUILayout.Button("Decline", Array.Empty<GUILayoutOption>()))
            {
                DeclineReturnToBoat();
            }
            GUILayout.EndHorizontal();
        }
        else if (_pendingReturnRequestId != 0)
        {
            GUILayout.Label("Waiting for your partner to accept the return.");
        }

        GUILayout.EndArea();
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

    private static bool IsInGameScene()
    {
        return SceneContext.hasInstance && SceneContext.Instance.IsCurrentSceneIngameType;
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

        if (!IsInGameScene())
        {
            _status = "Open the game world before starting a co-op session.";
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
        _status = "Lobby ready. Invite one Steam friend to join.";
    }

    private void OnLobbyJoinRequested(GameLobbyJoinRequested_t request)
    {
        if (_lobby.m_SteamID != 0 || _creatingLobby)
        {
            _status = "Leave the current lobby before joining a Steam invite.";
            return;
        }

        _pendingInvite = request.m_steamIDLobby;
        if (!IsInGameScene())
        {
            _status = "Steam invite received. Open the game world to join.";
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
                _status = "Lobby ready. Invite one Steam friend to join.";
            }
            return;
        }

        if (SteamMatchmaking.GetLobbyData(_lobby, "coop.protocol") != ProtocolVersion
            || SteamMatchmaking.GetLobbyData(_lobby, "coop.build") != BuildId)
        {
            LeaveDive("This lobby uses a different mod protocol or game build.");
            return;
        }

        if (!IsInGameScene())
        {
            LeaveDive("Open the game world before joining the co-op session.");
            return;
        }

        SteamMatchmaking.SetLobbyMemberData(_lobby, "coop.scene", GetCurrentSceneName());
        _lastPublishedScene = GetCurrentSceneName();
        _remoteUser = owner;
        _remoteSceneName = SteamMatchmaking.GetLobbyMemberData(_lobby, owner, "coop.scene");
        _status = "Joined the lobby. Your friend can be in another game scene.";
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
                _status = "Partner found. Sharing game-scene presence.";
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

    private void SendPose(PlayerCharacter localPlayer)
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

        SpriteRenderer body = localPlayer.bodyRenderer;
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

    private static bool IsSequenceNewer(uint sequence, uint previous)
    {
        return sequence != previous && unchecked((int)(sequence - previous)) > 0;
    }

    private void UpdateRemotePlayer(PlayerCharacter? localPlayer, bool inDive)
    {
        bool poseIsFresh = _hasRemotePose && Time.unscaledTime - _lastPoseReceived <= PoseTimeout;
        if (!inDive || localPlayer == null || !poseIsFresh)
        {
            if (_remotePlayer != null)
            {
                _remotePlayer.SetActive(false);
            }
            return;
        }

        SpriteRenderer localBody = localPlayer.bodyRenderer;
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
