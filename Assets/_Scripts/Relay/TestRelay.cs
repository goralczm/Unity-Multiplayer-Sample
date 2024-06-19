using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

public class TestRelay : MonoBehaviour
{
    public static TestRelay Instance;

    [SerializeField] private string _joinCode;

    private Allocation allocation;
    private NetworkDriver hostDriver;
    private NativeList<NetworkConnection> serverConnections;

    private JoinAllocation playerAllocation;
    private NetworkDriver playerDriver;
    private NetworkConnection clientConnection;

    private bool isHost;
    private bool isPlayer;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        DontDestroyOnLoad(this);
        Authenticate($"Player{UnityEngine.Random.Range(100, 1000)}");
    }

    private void Update()
    {
        if (isHost)
        {
            UpdateHost();
        }
        else if (isPlayer)
        {
            UpdatePlayer();
        }
    }

    public async void Authenticate(string playerName)
    {
        InitializationOptions initializationOptions = new InitializationOptions();
        initializationOptions.SetProfile(playerName);

        await UnityServices.InitializeAsync(initializationOptions);

        AuthenticationService.Instance.SignedIn += () =>
        {
            Debug.Log($"Signed as {AuthenticationService.Instance.PlayerId}");
        };

        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public async void CreateRelay()
    {
        isHost = true;
        try
        {
            allocation = await RelayService.Instance.CreateAllocationAsync(5);
            serverConnections = new NativeList<NetworkConnection>(5, Allocator.Persistent);

            Debug.Log("Host - Binding to the Relay server using UTP.");
            var relayServerData = new RelayServerData(allocation, "udp");
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayServerData);

            hostDriver = NetworkDriver.Create(settings);

            if (hostDriver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                Debug.LogError("Host client failed to bind");
            }
            else
            {
                if (hostDriver.Listen() != 0)
                {
                    Debug.LogError("Host client failed to listen");
                }
                else
                {
                    Debug.Log("Host client bound to Relay server");
                }
            }

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            _joinCode = joinCode;
            SceneManager.LoadScene("SampleScene");
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
        }
    }

    public void JoinWithCode()
    {
        JoinRelay(_joinCode);
    }

    public async void JoinRelay(string joinCode)
    {
        isPlayer = true;
        try
        {
            playerAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            Debug.Log("Host - Got join code: " + joinCode);

            Debug.Log("Player - Binding to the Relay server using UTP.");

            // Extract the Relay server data from the Join Allocation response.
            var relayServerData = new RelayServerData(playerAllocation, "udp");

            // Create NetworkSettings using the Relay server data.
            var settings = new NetworkSettings();
            settings.WithRelayParameters(ref relayServerData);

            // Create the Player's NetworkDriver from the NetworkSettings object.
            playerDriver = NetworkDriver.Create(settings);

            // Bind to the Relay server.
            if (playerDriver.Bind(NetworkEndpoint.AnyIpv4) != 0)
            {
                Debug.LogError("Player client failed to bind");
            }
            else
            {
                Debug.Log("Player client bound to Relay server");
            }

            OnConnectPlayer();
            SceneManager.LoadScene("SampleScene");
        }
        catch (RelayServiceException e)
        {
            Debug.Log(e);
        }
    }

    public void OnConnectPlayer()
    {
        Debug.Log("Player - Connecting to Host's client.");

        // Sends a connection request to the Host Player.
        clientConnection = playerDriver.Connect();
    }

    void UpdateHost()
    {
        // Skip update logic if the Host is not yet bound.
        if (!hostDriver.IsCreated || !hostDriver.Bound)
        {
            return;
        }

        Debug.Log("Updating Host");

        // This keeps the binding to the Relay server alive,
        // preventing it from timing out due to inactivity.
        hostDriver.ScheduleUpdate().Complete();

        // Clean up stale connections.
        for (int i = 0; i < serverConnections.Length; i++)
        {
            if (!serverConnections[i].IsCreated)
            {
                Debug.Log("Stale connection removed");
                serverConnections.RemoveAt(i);
                --i;
            }
        }

        // Accept incoming client connections.
        NetworkConnection incomingConnection;
        while ((incomingConnection = hostDriver.Accept()) != default(NetworkConnection))
        {
            // Adds the requesting Player to the serverConnections list.
            // This also sends a Connect event back the requesting Player,
            // as a means of acknowledging acceptance.
            Debug.Log("Accepted an incoming connection.");
            serverConnections.Add(incomingConnection);
        }

        // Process events from all connections.
        for (int i = 0; i < serverConnections.Length; i++)
        {
            Assert.IsTrue(serverConnections[i].IsCreated);

            // Resolve event queue.
            NetworkEvent.Type eventType;
            while ((eventType = hostDriver.PopEventForConnection(serverConnections[i], out var stream)) != NetworkEvent.Type.Empty)
            {
                switch (eventType)
                {
                    // Handle Relay events.
                    case NetworkEvent.Type.Data:
                        FixedString32Bytes msg = stream.ReadFixedString32();
                        Debug.Log($"Server received msg: {msg}");
                        break;

                    // Handle Disconnect events.
                    case NetworkEvent.Type.Disconnect:
                        Debug.Log("Server received disconnect from client");
                        serverConnections[i] = default(NetworkConnection);
                        break;
                }
            }
        }
    }

    void UpdatePlayer()
    {
        // Skip update logic if the Player is not yet bound.
        if (!playerDriver.IsCreated || !playerDriver.Bound)
        {
            return;
        }

        Debug.Log("Updating Player");

        // This keeps the binding to the Relay server alive,
        // preventing it from timing out due to inactivity.
        playerDriver.ScheduleUpdate().Complete();

        // Resolve event queue.
        NetworkEvent.Type eventType;
        while ((eventType = clientConnection.PopEvent(playerDriver, out var stream)) != NetworkEvent.Type.Empty)
        {
            switch (eventType)
            {
                // Handle Relay events.
                case NetworkEvent.Type.Data:
                    FixedString32Bytes msg = stream.ReadFixedString32();
                    Debug.Log($"Player received msg: {msg}");
                    break;

                // Handle Connect events.
                case NetworkEvent.Type.Connect:
                    Debug.Log("Player connected to the Host");
                    break;

                // Handle Disconnect events.
                case NetworkEvent.Type.Disconnect:
                    Debug.Log("Player got disconnected from the Host");
                    clientConnection = default(NetworkConnection);
                    break;
            }
        }
    }

    private void OnDestroy()
    {
        hostDriver.Dispose();
        serverConnections.Dispose();
        playerDriver.Dispose();
    }

    private void OnGUI()
    {
        _joinCode = GUI.TextField(new Rect(10, 25, 200, 20), _joinCode, 25);

        GUILayout.BeginArea(new Rect(30, 10, 300, 300));
        if (GUILayout.Button("Create Relay"))
            CreateRelay();

        if (GUILayout.Button("Join Relay"))
            JoinRelay(_joinCode);

        GUILayout.EndArea();
    }
}
