using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public enum MatchingState
{
    None,
    FindingMatch,
    PendingPlayer,
    GameStart,
    SelectMode,
}

public class RelayManager : NetworkBehaviour
{
    public static RelayManager Instance { get; private set; }

    [Header("게임 설정")]
    public int MaxPlayers { get; private set; } = 4; // 매칭 시작 시 설정됨

    [Header("Lobby 설정")]
    public string LobbyName = "My Game Room";

    public string TempJoinCode { get; private set; } = "";
    private Lobby _currentLobby;

    // [좀비 플레이어 방지용 출석부] Netcode의 ClientId와 Lobby의 PlayerId를 매핑
    private readonly Dictionary<ulong, string> _clientToPlayerId = new();

    // 의도적으로 나간 것인지 확인하는 Flag
    private bool _isIntentionalDisconnect = false;

    // 매칭이 잡힐 시 이동할 맵이 담긴 변수
    public string MapName { get; private set; }

    // 매칭 상태
    private MatchingState _matchingState;
    public MatchingState MatchingState
    {
        get => _matchingState;
        private set
        {
            if (value == _matchingState)
                return;

            _matchingState = value;
            OnMatchingStateChanged?.Invoke(value);
        }
    }

    public event Action<MatchingState> OnMatchingStateChanged;

    public int GameStartTerm = 3;
    public string[] AvailableMode;

    public void SetIntentionalDisconnect()
    {
        _isIntentionalDisconnect = true;
    }

    private void Awake()
    {
        // 멀티플레이어 필수: Alt+Tab으로 포커스를 잃어도 게임이 멈추지 않게 강제 설정
        Application.runInBackground = true;

        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    async void Start()
    {
        InitializationOptions options = new InitializationOptions();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        string randomProfile = "Player_" + System.Guid.NewGuid().ToString().Substring(0, 8);
        options.SetProfile(randomProfile);
#endif

        // 1. 유니티 게이밍 서비스(UGS) 초기화
        await UnityServices.InitializeAsync(options);

        // 2. 익명 로그인
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        EditorLog.Log("Unity Services 초기화 완료!");
        EditorLog.Log($"Player ID: {AuthenticationService.Instance.PlayerId}");
    }

    // =========================================================
    // [1. 매치메이킹 메인 로직]
    // =========================================================
    public async Task StartMatchmaking(int targetPlayerCount)
    {
        try
        {
            MaxPlayers = targetPlayerCount; // 인원수(모드) 세팅

            EditorLog.Log($"{MaxPlayers}인용 매치메이킹 시작...");
            MatchingState = MatchingState.FindingMatch;

            // 1-1. 빈자리가 있는 방 검색
            Lobby availableLobby = await FindAvailableLobby();

            if (availableLobby != null)
            {
                // 1-2. 방이 있으면 참가
                EditorLog.Log($"기존 방 발견! {availableLobby.Name}");
                await JoinExistingLobby(availableLobby);
            }
            else
            {
                // 1-3. 방이 없으면 내가 방장(Host)이 되어 새로 생성
                EditorLog.Log("대기 중인 방 없음. 새 방 생성...");
                await CreateLobbyWithRelay();
            }
        }
        catch (LobbyServiceException e)
        {
            MatchingState = MatchingState.None;
            EditorLog.LogError($"매치메이킹 실패: {e}");
        }
    }

    // =========================================================
    // [2. 로비 검색 및 참가]
    // =========================================================
    private async Task<Lobby> FindAvailableLobby()
    {
        try
        {
            QueryLobbiesOptions queryOptions = new QueryLobbiesOptions
            {
                Count = 1,
                Filters = new List<QueryFilter>
                {
                    // 빈자리 확인
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "1", QueryFilter.OpOptions.GE),
                    
                    // 버전 확인 (S1)
                    new QueryFilter(QueryFilter.FieldOptions.S1, Application.version, QueryFilter.OpOptions.EQ),
                    
                    // 모드 확인 (N1: 2인용, 3인용 등)
                    new QueryFilter(QueryFilter.FieldOptions.N1, MaxPlayers.ToString(), QueryFilter.OpOptions.EQ)
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(true, QueryOrder.FieldOptions.Created),
                },
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(queryOptions);

            if (response.Results.Count > 0)
            {
                Lobby lobby = response.Results[0];
                EditorLog.Log($"방 찾음: {lobby.Name} ({lobby.Players.Count}/{lobby.MaxPlayers})");
                return lobby;
            }

            return null;
        }
        catch (LobbyServiceException e)
        {
            MatchingState = MatchingState.None;
            EditorLog.LogError($"Lobby 검색 실패: {e}");
            return null;
        }
    }

    private async Task JoinExistingLobby(Lobby lobby)
    {
        try
        {
            Lobby updatedLobby = await LobbyService.Instance.GetLobbyAsync(lobby.Id);
            if (updatedLobby.AvailableSlots <= 0)
            {
                EditorLog.LogWarning("방이 막 꽉 찼습니다. 다시 검색합니다...");
                await StartMatchmaking(MaxPlayers);
                return;
            }

            _currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id);
            MaxPlayers = _currentLobby.MaxPlayers; // 안전장치: 방의 진짜 인원으로 동기화

            EditorLog.Log($"Lobby 참가 성공! {_currentLobby.Players.Count}/{_currentLobby.MaxPlayers}");
            MatchingState = MatchingState.PendingPlayer;

            if (!_currentLobby.Data.ContainsKey("RelayCode"))
            {
                EditorLog.LogError("Relay 코드가 없습니다!");
                return;
            }
            string relayCode = _currentLobby.Data["RelayCode"].Value;

            await JoinRelayAsync(relayCode);
            StartPolling();
        }
        catch (LobbyServiceException e)
        {
            EditorLog.LogWarning($"방 참가 실패. 재검색합니다... 사유: {e.Reason}");

            await Task.Delay(1000);

            if (Application.isPlaying)
            {
                await StartMatchmaking(MaxPlayers);
            }
        }
    }

    // =========================================================
    // [3. 로비 및 릴레이 생성 (방장)]
    // =========================================================
    private async Task CreateLobbyWithRelay()
    {
        try
        {
            await CreateRelayAsync();

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayCode", new DataObject(DataObject.VisibilityOptions.Public, TempJoinCode) },
                    { "Version", new DataObject(DataObject.VisibilityOptions.Public, Application.version, DataObject.IndexOptions.S1) },
                    { "GameMode", new DataObject(DataObject.VisibilityOptions.Public, MaxPlayers.ToString(), DataObject.IndexOptions.N1) },
                },
            };

            _currentLobby = await LobbyService.Instance.CreateLobbyAsync(LobbyName, MaxPlayers, options);

            EditorLog.Log($"Lobby 생성 완료! Lobby Code: {_currentLobby.LobbyCode} / Relay Code: {TempJoinCode}");
            MatchingState = MatchingState.PendingPlayer;

            StartHeartbeat();
            StartPolling();
        }
        catch (LobbyServiceException e)
        {
            MatchingState = MatchingState.None;
            EditorLog.LogError($"Lobby 생성 실패: {e}");
        }
    }

    private async Task CreateRelayAsync()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
            TempJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();
        }
        catch (RelayServiceException e)
        {
            MatchingState = MatchingState.None;
            EditorLog.LogError($"Relay 생성 실패: {e}");
            throw;
        }
    }

    private async Task JoinRelayAsync(string joinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetClientRelayData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();
        }
        catch (RelayServiceException e)
        {
            MatchingState = MatchingState.None;
            EditorLog.LogError($"Relay 참가 실패: {e}");
            throw;
        }
    }

    private async Task<bool> TryMergeLobbyAsync()
    {
        if (!IsServer || _currentLobby == null || _currentLobby.Players.Count >= MaxPlayers)
            return false;

        int myRoomPlayerCount = _currentLobby.Players.Count;

        try
        {
            QueryLobbiesOptions options = new QueryLobbiesOptions
            {
                Count = 5,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, myRoomPlayerCount.ToString(), QueryFilter.OpOptions.GE),
                    new QueryFilter(QueryFilter.FieldOptions.S1, Application.version, QueryFilter.OpOptions.EQ),
                    new QueryFilter(QueryFilter.FieldOptions.N1, MaxPlayers.ToString(), QueryFilter.OpOptions.EQ)
                },
                Order = new List<QueryOrder>
                {
                    new QueryOrder(true, QueryOrder.FieldOptions.Created),
                },
            };

            QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);

            Lobby targetLobby = null;
            foreach (var lobby in response.Results)
            {
                if (lobby.Id != _currentLobby.Id)
                {
                    targetLobby = lobby;
                    break;
                }
            }

            if (targetLobby == null)
                return false;

            bool amIOlder =
                _currentLobby.Created < targetLobby.Created
                || (
                    _currentLobby.Created == targetLobby.Created
                    && string.Compare(_currentLobby.Id, targetLobby.Id) < 0
                );

            if (amIOlder)
                return false;

            int jitterDelay = UnityEngine.Random.Range(500, 1500);
            await Task.Delay(jitterDelay);

            Lobby doubleCheckLobby = null;
            try
            {
                doubleCheckLobby = await LobbyService.Instance.GetLobbyAsync(targetLobby.Id);
            }
            catch (LobbyServiceException e)
            {
                if (e.Reason == LobbyExceptionReason.LobbyNotFound)
                    return false;
                throw;
            }

            if (doubleCheckLobby.AvailableSlots < myRoomPlayerCount)
                return false;

            EditorLog.Log($"대장 방({targetLobby.Name}) 발견! {myRoomPlayerCount}명이 다 함께 이사합니다.");

            string targetRelayCode = targetLobby.Data["RelayCode"].Value;

            if (myRoomPlayerCount > 1)
            {
                MigrateClientsClientRpc(targetRelayCode, targetLobby.Id);
            }

            await Task.Delay(500);

            _isIntentionalDisconnect = true;

            NetworkManager.Singleton.Shutdown();
            await DeleteLobby();
            await Task.Delay(500);

            await JoinExistingLobby(targetLobby);

            return true;
        }
        catch (LobbyServiceException e)
        {
            EditorLog.LogError($"Merge 검색 실패: {e}");
        }

        return false;
    }

    [ClientRpc]
    private void MigrateClientsClientRpc(string newRelayCode, string newLobbyId)
    {
        if (IsServer)
            return;

        EditorLog.Log($"방장이 새 방으로 이사 간답니다! 새 릴레이 코드: {newRelayCode}");

        _isIntentionalDisconnect = true;
        NetworkManager.Singleton.Shutdown();

        ExecuteMigrationAsync(newRelayCode, newLobbyId);
    }

    private async void ExecuteMigrationAsync(string newRelayCode, string newLobbyId)
    {
        await Task.Delay(500);

        try
        {
            _currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(newLobbyId);
            MaxPlayers = _currentLobby.MaxPlayers; // 🚨 클라이언트도 병합 후 이사 간 방의 모드로 갱신

            await JoinRelayAsync(newRelayCode);
        }
        catch (LobbyServiceException e)
        {
            EditorLog.LogError($"클라이언트 강제 이사 중 로비 전입 실패: {e}");
        }
    }

    // =========================================================
    // [4. 로비 유지 보수 루프]
    // =========================================================
    private void StartHeartbeat() => HeartbeatLoopAsync();

    private async void HeartbeatLoopAsync()
    {
        while (_currentLobby != null && Application.isPlaying)
        {
            await Task.Delay(25000);

            if (_currentLobby != null && Application.isPlaying)
            {
                bool isHost = _currentLobby.HostId == AuthenticationService.Instance.PlayerId;

                if (!isHost)
                {
                    EditorLog.Log("더 이상 방장이 아니므로 하트비트 전송 루프를 완전히 종료합니다.");
                    break;
                }

                try
                {
                    await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
                }
                catch (LobbyServiceException e)
                {
                    EditorLog.LogError($"Heartbeat 실패: {e}");
                    break;
                }
            }
        }
    }

    private void StartPolling() => PollLoopAsync();

    private async void PollLoopAsync()
    {
        while (_currentLobby != null && Application.isPlaying)
        {
            await Task.Delay(3000);
            if (_currentLobby != null && Application.isPlaying)
            {
                try
                {
                    if (IsServer && _currentLobby.Players.Count < MaxPlayers)
                    {
                        if (await TryMergeLobbyAsync())
                            break;
                    }

                    _currentLobby = await LobbyService.Instance.GetLobbyAsync(_currentLobby.Id);
                }
                catch (LobbyServiceException e)
                {
                    EditorLog.LogError($"Polling 실패: {e}");
                    break;
                }
            }
        }
    }

    // =========================================================
    // [5. 로비 퇴장 및 삭제 처리]
    // =========================================================
    public async Task LeaveLobby()
    {
        _isIntentionalDisconnect = true;
        MatchingState = MatchingState.None;

        if (_currentLobby != null)
        {
            try
            {
                string playerId = AuthenticationService.Instance.PlayerId;
                await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, playerId);
                _currentLobby = null;
                EditorLog.Log("Lobby에서 나갔습니다.");
            }
            catch (LobbyServiceException e)
            {
                if (e.Reason == LobbyExceptionReason.LobbyNotFound)
                {
                    EditorLog.Log("방이 이미 폭파되었습니다. 정상적으로 퇴장 처리합니다.");
                }
                else
                {
                    EditorLog.LogError($"Lobby 나가기 실패: {e}");
                }
            }
        }

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
    }

    public async Task DeleteLobby()
    {
        if (_currentLobby != null)
        {
            try
            {
                await LobbyService.Instance.DeleteLobbyAsync(_currentLobby.Id);
                _currentLobby = null;
                EditorLog.Log("Lobby 삭제 완료");
            }
            catch (LobbyServiceException e)
            {
                EditorLog.LogError($"Lobby 삭제 실패: {e}");
            }
        }
    }

    // =========================================================
    // [6. 네트워크 스폰 및 좀비 플레이어 방지 로직]
    // =========================================================
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isIntentionalDisconnect = false;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;

            _clientToPlayerId[NetworkManager.Singleton.LocalClientId] = AuthenticationService.Instance.PlayerId;
        }
        else
        {
            ReportPlayerIdRpc(AuthenticationService.Instance.PlayerId);
        }
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response
    )
    {
        int currentPlayers = NetworkManager.Singleton.ConnectedClients.Count;

        if (currentPlayers >= MaxPlayers)
        {
            EditorLog.LogWarning("정원 초과! 추가 접속자의 입장을 거부합니다.");
            response.Approved = false;
            response.Reason = "방이 꽉 찼습니다.";
            return;
        }

        response.Approved = true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportPlayerIdRpc(string authPlayerId, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        _clientToPlayerId[clientId] = authPlayerId;
        EditorLog.Log($"[명부 등록] 클라이언트 {clientId}번의 Auth ID: {authPlayerId}");
    }

    private void OnClientConnected(ulong clientId)
    {
        int currentPlayers = NetworkManager.Singleton.ConnectedClients.Count;
        if (currentPlayers >= MaxPlayers)
        {
            EditorLog.Log("방이 꽉 찼습니다! 게임 시작 가능");

            if (IsServer)
                StartGameAsync();
        }
    }

    private async void StartGameAsync()
    {
        if (_currentLobby == null)
            return;

        try
        {
            UpdateLobbyOptions options = new UpdateLobbyOptions() { IsLocked = true };
            await LobbyService.Instance.UpdateLobbyAsync(_currentLobby.Id, options);
            UpdateMatchingStateClientRpc(MatchingState.GameStart);

            await Task.Delay(GameStartTerm * 1000);

            StartRouletteClientRpc(
                AvailableMode[UnityEngine.Random.Range(0, AvailableMode.Length)]
            );
        }
        catch (LobbyServiceException e)
        {
            EditorLog.LogError($"게임 시작 시퀀스 실패: {e}");
        }
    }

    [ClientRpc]
    private void UpdateMatchingStateClientRpc(MatchingState state)
    {
        MatchingState = state;
    }

    [ClientRpc]
    private void StartRouletteClientRpc(string mapName)
    {
        MapName = mapName;
        MatchingState = MatchingState.SelectMode;
    }

    private async void OnClientDisconnected(ulong clientId)
    {
        if (IsServer && _currentLobby != null)
        {
            if (_clientToPlayerId.TryGetValue(clientId, out string authPlayerId))
            {
                try
                {
                    await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, authPlayerId);
                    _clientToPlayerId.Remove(clientId);
                    EditorLog.Log("Lobby 서버에서 좀비 플레이어 완벽 삭제!");
                }
                catch (Unity.Services.Lobbies.LobbyServiceException e)
                {
                    EditorLog.LogError($"Lobby 추방 실패: {e}");
                }
            }
        }
        else
        {
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                if (!_isIntentionalDisconnect)
                {
                    EditorLog.LogWarning("서버와의 연결이 비정상적으로 끊겼습니다! 메인 화면으로 돌아갑니다.");

                    if (_currentLobby != null)
                    {
                        try
                        {
                            await LobbyService.Instance.RemovePlayerAsync(
                                _currentLobby.Id,
                                AuthenticationService.Instance.PlayerId
                            );
                        }
                        catch { }
                        _currentLobby = null;
                    }

                    if (NetworkManager.Singleton != null)
                        NetworkManager.Singleton.Shutdown();

                    UnityEngine.SceneManagement.SceneManager.LoadScene("MainScene");
                }
                else
                {
                    EditorLog.Log("정상적인 연결 종료입니다. (씬 이동은 다른 매니저가 알아서 함)");
                    _isIntentionalDisconnect = false;
                }
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
        }
    }

    // ========================================
    // 앱 종료 시 정리
    // ========================================
    private async void OnApplicationQuit()
    {
        _isIntentionalDisconnect = true;
        bool wasHost = IsServer;

        if (wasHost)
        {
            await DeleteLobby();
            EditorLog.Log("호스트 종료: 로비 폭파 완료");
        }
        else
        {
            if (_currentLobby != null)
            {
                try
                {
                    await LobbyService.Instance.RemovePlayerAsync(
                        _currentLobby.Id,
                        AuthenticationService.Instance.PlayerId
                    );
                }
                catch { }
                _currentLobby = null;
            }
            EditorLog.Log("클라이언트 종료: 로비 퇴장 완료");
        }

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }
}
