using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.FlightSimulator.SimConnect;

namespace MsfsPhysicsCamera
{
    public class SimConnectHandler : IDisposable
    {
        private const int WM_USER_SIMCONNECT = 0x0402;
        private SimConnect? simconnect = null;
        private volatile bool _isConnected = false;
        private readonly object _dataLock = new object();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread? _messageThread;

        public enum DEFINITIONS { TelemetryData, UserPosition }
        public enum DATA_REQUESTS { TelemetryRequest, CurrentUserRequest }

        // Field order and types must match the AddToDataDefinition calls in Connect().
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TelemetryData
        {
            public double AccelX;
            public double AccelY;
            public double AccelZ;
            public double VelX;
            public double VelY;
            public double VelZ;
            public double Pitch;
            public double Bank;
            public int CameraState;
            public int SimOnGround;        // 0 = airborne
            public double GroundVelocity;  // knots
            public double Latitude;        // degrees
            public double Longitude;       // degrees
            public double Altitude;        // feet
            public int CrashFlag;          // logged only: reported to stay 0 for the user aircraft
            public int CrashSequence;      // logged only, same
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UserPosition
        {
            public double Latitude;   // degrees
            public double Longitude;  // degrees
            public double Altitude;   // feet
        }

        // Where "the user" is. Telemetry is always about the aircraft (SIMCONNECT_OBJECT_ID_USER is the
        // aircraft), and in MSFS 2024 walkaround mode CAMERA STATE stays 2 (cockpit). The current user object
        // (SIMCONNECT_OBJECT_ID_USER_CURRENT, MSFS 2024) is the aircraft while flying and the pilot avatar
        // on foot, so its position moving away from the aircraft's means the pilot got out.
        public struct UserSnapshot
        {
            public bool HasPosition;
            public UserPosition Position;
            public uint UserObjectId;      // dwObjectID of the current-user data, logged for diagnosis
            public uint AircraftObjectId;  // dwObjectID of the aircraft data, logged for diagnosis
        }

        private TelemetryData _currentData;
        public TelemetryData CurrentData
        {
            get
            {
                lock (_dataLock)
                {
                    return _currentData;
                }
            }
        }

        private UserSnapshot _currentUser;
        public UserSnapshot CurrentUser
        {
            get
            {
                lock (_dataLock)
                {
                    return _currentUser;
                }
            }
        }

        public bool IsConnected => _isConnected;

        public void Connect()
        {
            if (_isConnected) return;

            // Clean up any previous connection before reconnecting
            _cts.Cancel();
            _messageThread?.Join(500);
            simconnect?.Dispose();
            simconnect = null;
            _cts = new CancellationTokenSource();
            lock (_dataLock)
            {
                _currentUser = default;
            }

            try
            {
                simconnect = new SimConnect("MsfsPhysicsCamera", IntPtr.Zero, WM_USER_SIMCONNECT, null, 0);
                
                // Define SimVars
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "ACCELERATION BODY X", "feet per second squared", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "ACCELERATION BODY Y", "feet per second squared", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "ACCELERATION BODY Z", "feet per second squared", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "VELOCITY BODY X", "feet per second", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "VELOCITY BODY Y", "feet per second", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "VELOCITY BODY Z", "feet per second", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "PLANE PITCH DEGREES", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "PLANE BANK DEGREES", "radians", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "CAMERA STATE", "enum", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "CRASH FLAG", "enum", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.TelemetryData, "CRASH SEQUENCE", "enum", SIMCONNECT_DATATYPE.INT32, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                simconnect.RegisterDataDefineStruct<TelemetryData>(DEFINITIONS.TelemetryData);

                simconnect.AddToDataDefinition(DEFINITIONS.UserPosition, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.UserPosition, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
                simconnect.AddToDataDefinition(DEFINITIONS.UserPosition, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

                simconnect.RegisterDataDefineStruct<UserPosition>(DEFINITIONS.UserPosition);

                simconnect.OnRecvOpen += Simconnect_OnRecvOpen;
                simconnect.OnRecvQuit += Simconnect_OnRecvQuit;
                simconnect.OnRecvException += Simconnect_OnRecvException;
                simconnect.OnRecvSimobjectData += Simconnect_OnRecvSimobjectData;

                // Request data every frame
                simconnect.RequestDataOnSimObject(DATA_REQUESTS.TelemetryRequest, DEFINITIONS.TelemetryData, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SIM_FRAME, 0, 0, 0, 0);
                // Same frame rate as the telemetry, so both positions are from (nearly) the same frame.
                simconnect.RequestDataOnSimObject(DATA_REQUESTS.CurrentUserRequest, DEFINITIONS.UserPosition, SimConnect.SIMCONNECT_OBJECT_ID_USER_CURRENT, SIMCONNECT_PERIOD.SIM_FRAME, 0, 0, 0, 0);

                Console.WriteLine("Connected to MSFS.");
                _isConnected = true;

                // Start a thread to process SimConnect messages
                var token = _cts.Token;
                var sc = simconnect;
                _messageThread = new Thread(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            sc?.ReceiveMessage();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("SimConnect ReceiveMessage Error: " + ex.Message);
                            _isConnected = false;
                            break;
                        }
                        Thread.Sleep(1);
                    }
                })
                { IsBackground = true };
                _messageThread.Start();
            }
            catch (COMException ex)
            {
                Console.WriteLine($"SimConnect Connection Failed: {ex.Message}");
                _isConnected = false;
                simconnect?.Dispose();
                simconnect = null;
            }
        }

        private void Simconnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwRequestID == (uint)DATA_REQUESTS.TelemetryRequest)
            {
                lock (_dataLock)
                {
                    _currentData = (TelemetryData)data.dwData[0];
                    _currentUser.AircraftObjectId = data.dwObjectID;
                }
            }
            else if (data.dwRequestID == (uint)DATA_REQUESTS.CurrentUserRequest)
            {
                lock (_dataLock)
                {
                    _currentUser.Position = (UserPosition)data.dwData[0];
                    _currentUser.UserObjectId = data.dwObjectID;
                    _currentUser.HasPosition = true;
                }
            }
        }

        private void Simconnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Console.WriteLine($"SimConnect Exception: {data.dwException}");
        }

        private void Simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            Console.WriteLine("MSFS exited. Disconnecting...");
            _isConnected = false;
            _cts.Cancel();
        }

        private void Simconnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            Console.WriteLine("SimConnect Open event received.");
            _isConnected = true;
        }

        public void Dispose()
        {
            _isConnected = false;
            _cts.Cancel();
            _messageThread?.Join(500);

            if (simconnect != null)
            {
                simconnect.Dispose();
                simconnect = null;
            }
        }
    }
}
