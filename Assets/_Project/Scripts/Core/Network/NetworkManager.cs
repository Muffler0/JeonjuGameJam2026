using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Random = System.Random;

namespace Project.Core.Network
{
    /// <summary>
    /// 접속 상태.
    /// </summary>
    public enum NetworkState
    {
        Disconnected,   // 서버에 연결되지 않음
        Connecting,     // 연결 시도 중
        InLobby,        // 연결됨, 방에 들어가기 전
        Joining,        // 방 생성 또는 입장 시도 중
        InRoom,         // 방에 있음, 상대 대기 중
        Countdown       // 두 명이 모여 시작 카운트다운 진행 중
    }

    /// <summary>
    /// Photon 연결, 방 생성·입장, 랜덤 매칭, 이탈 감지를 담당하는 싱글톤.
    ///
    /// 사용 예:
    ///     NetworkManager.Instance.OnStateChanged += HandleState;
    ///     NetworkManager.Instance.StartRandomMatch();
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkManager : MonoBehaviourPunCallbacks, IOnEventCallback
    {
        // 서로 다른 빌드끼리 매칭되지 않도록 구분하는 값.
        private const string GameVersion = "1";

        private const int MaxPlayers = 2;
        private const int RoomCodeLength = 5;

        // 5초 카운트다운.
        private const double CountdownSeconds = 5.0;

        // 방 코드 생성에 쓸 문자. 헷갈리는 O, 0, I, 1, S, 5 는 제외
        private const string CodeCharacters = "ABCDEFGHJKLMNPQRTUVWXYZ2346789";

        // RaiseEvent용 코드. PhotonView 없이 메시지를 주고받기 위해 사용한다.
        private const byte GameStartEventCode = 1;

        // -----------------------------------------------
        //  싱글톤
        // -----------------------------------------------

        private static NetworkManager _instance;
        private static bool _isQuitting;

        public static NetworkManager Instance
        {
            get
            {
                if (_isQuitting) return null;
                if (_instance != null) return _instance;

                _instance = FindAnyObjectByType<NetworkManager>();
                if (_instance != null) return _instance;

                var go = new GameObject("[NetworkManager]");
                _instance = go.AddComponent<NetworkManager>();
                return _instance;
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        // 게임 시작과 동시에 서버에 붙지 않는다.
        // 타이틀에서 매칭을 시작하는 시점에 Connect()를 부르는 구조다.

        // -----------------------------------------------
        //  이벤트
        // -----------------------------------------------

        /// <summary>접속 상태가 바뀔 때마다 호출된다.</summary>
        public event Action<NetworkState> OnStateChanged;

        /// <summary>사용자에게 보여줄 오류 메시지.</summary>
        public event Action<string> OnError;

        /// <summary>상대가 방에 들어왔다.</summary>
        public event Action OnOpponentJoined;

        /// <summary>상대가 방을 떠났다. 카운트다운 중이었다면 이미 취소된 상태다.</summary>
        public event Action OnOpponentLeft;

        /// <summary>카운트다운이 시작됐다.</summary>
        public event Action OnCountdownStarted;

        /// <summary>상대 이탈 등으로 카운트다운이 취소됐다.</summary>
        public event Action OnCountdownCancelled;

        /// <summary>게임을 시작할 시점. 인자는 양쪽이 공유하는 랜덤 시드다.</summary>
        public event Action<int> OnGameStart;

        // -----------------------------------------------
        //  상태
        // -----------------------------------------------

        private NetworkState _state = NetworkState.Disconnected;

        public NetworkState State
        {
            get => _state;
            private set
            {
                if (_state == value) return;
                _state = value;
                OnStateChanged?.Invoke(_state);
            }
        }

        /// <summary>현재 방 코드. 방에 있지 않으면 빈 문자열.</summary>
        public string RoomCode => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : string.Empty;

        /// <summary>내가 방장인지. 카운트다운 시작 신호는 방장만 보낸다.</summary>
        public bool IsMaster => PhotonNetwork.IsMasterClient;

        /// <summary>
        /// 상대가 이미 나갔는지. 씬 로딩 중에 이탈이 발생하면 처리할 UI가 없기 때문에,
        /// GameScene에 진입한 직후 이 값을 확인해서 뒤늦게 처리한다.
        /// </summary>
        public bool HasOpponentLeft { get; private set; }

        /// <summary>카운트다운 남은 시간(초). 카운트다운 중이 아니면 0.</summary>
        public double CountdownRemaining =>
            State == NetworkState.Countdown
                ? Math.Max(0.0, _gameStartTime - PhotonNetwork.Time)
                : 0.0;

        private double _gameStartTime;
        private int _sharedSeed;
        private bool _gameStartFired;

        // 서버에 연결되면 이어서 실행할 동작. 연결 전에 버튼을 눌러도 동작하게 한다.
        private Action _pendingAction;

        // -----------------------------------------------
        //  초기화
        // -----------------------------------------------

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            // PUN이 씬을 자동으로 동기화하면 SceneLoader의 페이드가 무시된다.
            // 씬 전환은 게임 시작 신호를 받은 뒤 각자 처리한다.
            PhotonNetwork.AutomaticallySyncScene = false;
            PhotonNetwork.GameVersion = GameVersion;
        }

        private void Update()
        {
            if (State != NetworkState.Countdown || _gameStartFired) return;

            if (PhotonNetwork.Time >= _gameStartTime)
            {
                _gameStartFired = true;
                OnGameStart?.Invoke(_sharedSeed);
            }
        }

        // -----------------------------------------------
        //  공개 API
        // -----------------------------------------------

        /// <summary>서버에 연결한다. 이미 연결돼 있으면 아무 일도 하지 않는다.</summary>
        public void Connect()
        {
            if (PhotonNetwork.IsConnected) return;

            State = NetworkState.Connecting;
            PhotonNetwork.ConnectUsingSettings();
        }

        /// <summary>
        /// 랜덤 매칭을 시작한다.
        /// 들어갈 방이 없으면 공개 방을 만들고 상대를 기다린다.
        /// </summary>
        public void StartRandomMatch()
        {
            RunWhenConnected(() =>
            {
                State = NetworkState.Joining;
                PhotonNetwork.JoinRandomRoom(null, MaxPlayers);
            });
        }

        /// <summary>
        /// 코드로만 들어올 수 있는 비공개 방을 만든다.
        /// 생성에 성공하면 RoomCode로 코드를 읽을 수 있다.
        /// </summary>
        public void CreatePrivateRoom()
        {
            RunWhenConnected(() =>
            {
                State = NetworkState.Joining;
                PhotonNetwork.CreateRoom(GenerateRoomCode(), CreateRoomOptions(isVisible: false));
            });
        }

        /// <summary>코드를 입력해 비공개 방에 들어간다.</summary>
        public void JoinRoomByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                OnError?.Invoke("Enter a room code.");
                return;
            }

            // 소문자로 입력해도 들어가지도록 맞춰준다. 공백도 흔한 실수라 제거한다.
            string normalized = code.Trim().ToUpperInvariant();

            RunWhenConnected(() =>
            {
                State = NetworkState.Joining;
                PhotonNetwork.JoinRoom(normalized);
            });
        }

        /// <summary>방에서 나간다. 서버 연결은 유지된다.</summary>
        public void LeaveRoom()
        {
            CancelCountdown(notify: false);

            if (PhotonNetwork.InRoom)
            {
                PhotonNetwork.LeaveRoom();
            }
            else
            {
                State = PhotonNetwork.IsConnected ? NetworkState.InLobby : NetworkState.Disconnected;
            }
        }

        /// <summary>서버 연결까지 완전히 끊는다.</summary>
        public void Disconnect()
        {
            CancelCountdown(notify: false);
            PhotonNetwork.Disconnect();
        }

        /// <summary>
        /// 뒤늦게 처리하려고 남겨둔 이탈 플래그를 읽고 지운다.
        /// GameScene 진입 직후에 한 번 호출할 것.
        /// </summary>
        public bool ConsumeOpponentLeftFlag()
        {
            bool left = HasOpponentLeft;
            HasOpponentLeft = false;
            return left;
        }

        // -----------------------------------------------
        //  내부 처리
        // -----------------------------------------------

        private void RunWhenConnected(Action action)
        {
            if (PhotonNetwork.IsConnectedAndReady && State != NetworkState.Connecting)
            {
                action();
                return;
            }

            // 아직 연결 전이면 연결이 끝난 뒤에 이어서 실행한다.
            _pendingAction = action;
            Connect();
        }

        private static RoomOptions CreateRoomOptions(bool isVisible)
        {
            return new RoomOptions
            {
                MaxPlayers = MaxPlayers,
                IsVisible = isVisible,   // 비공개 방은 랜덤 매칭에 잡히지 않는다
                IsOpen = true,
                CleanupCacheOnLeave = true
            };
        }

        private static string GenerateRoomCode()
        {
            // Code by Claude Opus
            var random = new Random(Guid.NewGuid().GetHashCode());
            var buffer = new char[RoomCodeLength];

            for (int i = 0; i < RoomCodeLength; i++)
            {
                buffer[i] = CodeCharacters[random.Next(CodeCharacters.Length)];
            }

            return new string(buffer);
        }

        /// <summary>두 명이 모이면 방장이 시작 시각과 시드를 정해 모두에게 알린다.</summary>
        private void TryStartCountdown()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (PhotonNetwork.CurrentRoom.PlayerCount < MaxPlayers) return;
            if (State == NetworkState.Countdown) return;

            // 카운트다운 도중 다른 사람이 들어오는 것을 막는다.
            PhotonNetwork.CurrentRoom.IsOpen = false;

            double startTime = PhotonNetwork.Time + CountdownSeconds;
            int seed = Environment.TickCount;

            // PhotonNetwork.Time은 서버 기준이라 두 클라이언트의 카운트다운이 일치한다.
            PhotonNetwork.RaiseEvent(
                GameStartEventCode,
                new object[] { startTime, seed },
                new RaiseEventOptions { Receivers = ReceiverGroup.All },
                SendOptions.SendReliable);
        }

        private void BeginCountdown(double startTime, int seed)
        {
            _gameStartTime = startTime;
            _sharedSeed = seed;
            _gameStartFired = false;

            State = NetworkState.Countdown;
            OnCountdownStarted?.Invoke();
        }

        private void CancelCountdown(bool notify)
        {
            if (State != NetworkState.Countdown) return;

            _gameStartFired = false;
            State = PhotonNetwork.InRoom ? NetworkState.InRoom : NetworkState.InLobby;

            if (notify) OnCountdownCancelled?.Invoke();
        }

        // -----------------------------------------------
        //  Photon 콜백
        // -----------------------------------------------

        public override void OnConnectedToMaster()
        {
            State = NetworkState.InLobby;

            var action = _pendingAction;
            _pendingAction = null;
            action?.Invoke();
        }

        public override void OnJoinedRoom()
        {
            HasOpponentLeft = false;
            State = NetworkState.InRoom;

            // 내가 들어갔을 때 이미 상대가 있는 경우(코드 입장, 랜덤 매칭 성공).
            if (PhotonNetwork.CurrentRoom.PlayerCount >= MaxPlayers)
            {
                TryStartCountdown();
            }
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            OnOpponentJoined?.Invoke();
            TryStartCountdown();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            // 게임이 시작된 뒤라면 씬 로딩 중일 수 있다. 플래그로 남겨 뒤늦게 처리한다.
            HasOpponentLeft = true;

            CancelCountdown(notify: true);

            // 상대가 빠졌으니 다시 들어올 수 있도록 방을 연다.
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.CurrentRoom.IsOpen = true;
            }

            OnOpponentLeft?.Invoke();
        }

        public override void OnJoinRandomFailed(short returnCode, string message)
        {
            // 들어갈 방이 없다는 뜻이다. 실패가 아니라 정상 흐름이다.
            PhotonNetwork.CreateRoom(GenerateRoomCode(), CreateRoomOptions(isVisible: true));
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            State = NetworkState.InLobby;

            // 코드가 틀렸는지 방이 찼는지 구분해서 알려줘야 사용자가 다음 행동을 안다.
            string reason = returnCode == ErrorCode.GameFull
                ? "This room is full."
                : "Room not found. Check the code.";

            OnError?.Invoke(reason);
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            // 코드가 우연히 겹친 경우다. 새 코드로 한 번 더 시도한다.
            if (returnCode == ErrorCode.GameIdAlreadyExists)
            {
                PhotonNetwork.CreateRoom(GenerateRoomCode(), CreateRoomOptions(isVisible: false));
                return;
            }

            State = NetworkState.InLobby;
            OnError?.Invoke("Failed to create a room. Try again.");
        }

        public override void OnLeftRoom()
        {
            HasOpponentLeft = false;
            State = PhotonNetwork.IsConnected ? NetworkState.InLobby : NetworkState.Disconnected;
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            _pendingAction = null;
            _gameStartFired = false;
            HasOpponentLeft = false;
            State = NetworkState.Disconnected;

            // 사용자가 직접 나간 경우까지 오류로 띄우지는 않는다.
            if (cause == DisconnectCause.DisconnectByClientLogic) return;

            OnError?.Invoke("Disconnected from the server.");
        }

        // -----------------------------------------------
        //  커스텀 이벤트 수신
        // -----------------------------------------------

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != GameStartEventCode) return;

            var data = (object[])photonEvent.CustomData;
            BeginCountdown((double)data[0], (int)data[1]);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
