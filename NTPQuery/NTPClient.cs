namespace NTPQuery {

    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading.Tasks;
    #endregion

    #region Enums
    /// <summary>
    /// Leap indicator field values
    /// 0 - No warning
    /// 1 - Last minute has 61 seconds    
    /// 2 - Last minute has 59 seconds    
    /// 3 - Alarm condition (clock not synchronized) 
    /// </summary>
    public enum LeapIndicatorDescription {
        NoWarning = 0,
        LastMinute61 = 1,
        LastMinute59 = 2,
        Alarm = 3
    }

    /// <summary>
    /// Mode field values
    /// 1 - Symmetric active
    /// 2 - Symmetric pasive
    /// 3 - Client
    /// 4 - Server
    /// 5 - Broadcast
    /// 0, 6, 7 - Reserved
    /// </summary>
    public enum ServerModeDescription {
        Unknown = 0,
        SymmetricActive = 1,
        SymmetricPassive = 2,
        Client = 3,
        Server = 4,
        Broadcast = 5,
    }

    /// <summary>
    /// Stratum field values
    /// 0 - unspecified or unavailable
    /// 1 - primary reference (e.g. radio-clock)
    /// 2-15 - secondary reference (via NTP or SNTP)
    /// 16 - unsynchronzied
    /// 17-255 - reserved
    /// </summary>
    public enum StratumDescription {
        Unspecified = 0,
        PrimaryReference = 1,
        SecondaryReference = 2,
        Unsynchronized = 16
    }
    #endregion

    /// <summary>
    /// Based on an NTP client implementation by Valer Bocan.
    /// Modifications:  
    ///  Use UTC instead of local time.  
    ///  Use Async methods of Socket.  
    ///  Return TimeSpan instead of primitives for time values.
    /// http://www.bocan.ro/sntpclient
    /// </summary>
    public class NTPClient {

        #region Members
        private readonly int DefaultTimeoutSeconds = 20;

        /// <summary>
        /// Returns true if received data is valid and if comes from a NTP-compliant time server.
        /// </summary>
        public bool IsResponseValid {
            get {
                return (NTPData.Length >= SNTPDataLength)
                    && (this.ServerMode == ServerModeDescription.Server);
            }
        }

        /// <summary>
        /// Warns of an impending leap second to be inserted/deleted in the last minute of the current day. 
        /// </summary>
        public LeapIndicatorDescription LeapIndicator {
            get {
                // Isolate the two most significant bits
                byte val = (byte)(NTPData[0] >> 6);
                switch (val) {
                    case 0: return LeapIndicatorDescription.NoWarning;
                    case 1: return LeapIndicatorDescription.LastMinute61;
                    case 2: return LeapIndicatorDescription.LastMinute59;
                    default:
                        return LeapIndicatorDescription.Alarm;
                }
            }
        }

        /// <summary>
        /// The offset of the local clock relative to the primary reference source
        /// </summary>
        public TimeSpan LocalClockOffset {
            get {
                TimeSpan span =
                    (this.ReceiveTimestamp - this.OriginateTimestamp)
                    + (this.TransmitTimestamp - this.DestinationTimestamp);
                return TimeSpan.FromTicks(span.Ticks / 2);
            }
        }

        /// <summary>
        /// NTP Data Structure (as described in RFC 2030)
        /// </summary>
        private byte[] NTPData = new byte[SNTPDataLength];

        /// <summary>
        /// Maximum interval between successive messages
        /// </summary>
        public TimeSpan PollInterval {
            get {
                return TimeSpan.FromSeconds(Math.Pow(2, (sbyte)NTPData[2]));
            }
        }

        /// <summary>
        /// Precision of the clock.
        /// 8-bit signed integer representing the precision of the system clock, in log2 seconds.
        /// For eample, a value of -18 corresponds to a precision of about one microsecond.  
        /// The precision can be determined when the service first starts up as the minimum
        /// time of several iterations to read the system clock.
        /// </summary>
        public double Precision {
            get {
                sbyte precisionByte = (sbyte)NTPData[3];
                double precisionValue = Convert.ToDouble(precisionByte);
                return precisionValue;
            }
        }

        /// <summary>
        /// Reference identifier (either a 4 character string or an IP address)
        /// </summary>
        public string ReferenceID {
            get {
                referenceID = string.Empty;
                switch (this.Stratum) {
                    case StratumDescription.Unspecified:
                    case StratumDescription.PrimaryReference:
                        referenceID += (char)NTPData[OffsetReferenceID + 0];
                        referenceID += (char)NTPData[OffsetReferenceID + 1];
                        referenceID += (char)NTPData[OffsetReferenceID + 2];
                        referenceID += (char)NTPData[OffsetReferenceID + 3];
                        break;
                    case StratumDescription.SecondaryReference:
                        switch (VersionNumber) {
                            case 3:
                                // Version 3, Reference ID is an IPv4 address
                                string ipAddress = $"{NTPData[OffsetReferenceID + 0].ToString()}.{NTPData[OffsetReferenceID + 1].ToString()}.{NTPData[OffsetReferenceID + 2].ToString()}.{NTPData[OffsetReferenceID + 3].ToString()}";
                                try {
                                    IPHostEntry ipHostEntry = Utility.GetHostEntry(ipAddress);
                                    referenceID = ipHostEntry.HostName + " (" + ipAddress + ")";
                                }
                                catch (Exception) {
                                    referenceID = ipAddress;
                                }
                                break;
                            case 4:
                                // Version 4, Reference ID is the timestamp of last update
                                DateTime time = this.ComputeDate(this.GetMilliSeconds(OffsetReferenceID));
                                referenceID = time.ToString("yyyy-MM-dd HH:mm:ss.fffff");
                                break;
                            default:
                                referenceID = "N/A";
                                break;
                        }
                        break;
                }

                return referenceID;
            }
        }
        private string referenceID;

        /// <summary>
        /// Round trip time to the primary reference source.
        /// </summary>
        public TimeSpan RootDelay {
            get {
                int temp = 0;
                temp = 256 * (256 * (256 * NTPData[4] + NTPData[5]) + NTPData[6]) + NTPData[7];
                return TimeSpan.FromSeconds(((double)temp) / 0x10000);
            }
        }

        /// <summary>
        /// Nominal error relative to the primary reference source.
        /// </summary>
        public TimeSpan RootDispersion {
            get {
                int temp = 256 * (256 * (256 * NTPData[8] + NTPData[9]) + NTPData[10]) + NTPData[11];
                return TimeSpan.FromSeconds(((double)temp) / 0x10000);
            }
        }

        /// <summary>
        /// The time between the departure of request and arrival of reply 
        /// </summary>
        public TimeSpan RoundTripDelay {
            get {
                TimeSpan span =
                    (this.DestinationTimestamp - this.OriginateTimestamp)
                    - (this.ReceiveTimestamp - this.TransmitTimestamp);
                return span;
            }
        }

        public ServerModeDescription ServerMode {
            get {
                // Isolate bits 0 - 3
                byte val = (byte)(NTPData[0] & 0x7);
                switch (val) {
                    case 0: goto default;
                    case 6: goto default;
                    case 7: goto default;
                    case 1:
                        return ServerModeDescription.SymmetricActive;
                    case 2:
                        return ServerModeDescription.SymmetricPassive;
                    case 3:
                        return ServerModeDescription.Client;
                    case 4:
                        return ServerModeDescription.Server;
                    case 5:
                        return ServerModeDescription.Broadcast;
                    default:
                        return ServerModeDescription.Unknown;
                }
            }
        }

        /// <summary>
        /// NTP Data Structure Length
        /// </summary>
        private const byte SNTPDataLength = 48;

        /// <summary>
        /// The URL of the time server we're connecting to
        /// </summary>
        public string Source { get; private set; }

        public IPAddress SourceIPAddress { get; private set; }

        public StratumDescription Stratum {
            get {
                byte val = (byte)NTPData[1];
                if (val == 0) return StratumDescription.Unspecified;
                if (val == 1) return StratumDescription.PrimaryReference;
                if (val <= 15) return StratumDescription.SecondaryReference;
                if (val == 16) return StratumDescription.Unsynchronized;
                return StratumDescription.Unspecified;
            }
        }

        private StringBuilder ToStringInfo { get; set; }

        /// <summary>
        /// Version number of the protocol (3 or 4).
        /// </summary>
        public byte VersionNumber {
            get {
                // Isolate bits 3 - 5
                byte val = (byte)((this.NTPData[0] & 0x38) >> 3);
                return val;
            }
        }

        #region Offset constants for timestamps in the data structure
        private const byte OffsetOriginateTimestamp = 24;
        private const byte OffsetReceiveTimestamp = 32;
        private const byte OffsetReferenceID = 12;
        private const byte OffsetReferenceTimestamp = 16;
        private const byte OffsetTransmitTimestamp = 40;
        #endregion

        #region Timestamps
        /// <summary>
        /// Destination Timestamp (T4).  The time when we receive the NTP response from the server.
        /// </summary>
        public DateTime DestinationTimestamp { get; private set; }

        /// <summary>
        /// The time (T1) at which the request departed the client for the server
        /// </summary>
        public DateTime OriginateTimestamp {
            get {
                return ComputeDate(GetMilliSeconds(OffsetOriginateTimestamp));
            }
        }

        /// <summary>
        /// The time at which the clock was last set or corrected
        /// </summary>
        public DateTime ReferenceTimestamp {
            get {
                return this.ComputeDate(GetMilliSeconds(OffsetReferenceTimestamp));
            }
        }

        /// <summary>
        /// The time (T2) at which the request arrived at the server
        /// </summary>
        public DateTime ReceiveTimestamp {
            get {
                return this.ComputeDate(GetMilliSeconds(OffsetReceiveTimestamp));
            }
        }

        /// <summary>
        /// The time (T3) at which the reply departed the server for client
        /// </summary>
        public DateTime TransmitTimestamp {
            get {
                return this.ComputeDate(GetMilliSeconds(OffsetTransmitTimestamp));
            }
            private set {
                this.SetDate(OffsetTransmitTimestamp, value);
            }
        }
        #endregion 
        #endregion

        #region Constructors
        public NTPClient(string hostName) {
            this.Source = hostName;
        }

        public NTPClient(string sourceIpAddress, string sourceHostName)
            : this(IPAddress.Parse(sourceIpAddress), sourceHostName) {
        }

        public NTPClient(IPAddress sourceIpAddress, string sourceHostName) {
            this.SourceIPAddress = sourceIpAddress;
            this.Source = sourceHostName;
        }
        #endregion

        #region Methods
        /// <summary>
        /// Compute date, given the number of milliseconds since January 1, 1900
        /// </summary>
        private DateTime ComputeDate(ulong milliseconds) {
            var span = TimeSpan.FromMilliseconds((double)milliseconds);
            var time = new DateTime(1900, 1, 1);
            time += span;
            return time;
        }

        /// <summary>
        /// Connects to the time server and populates the data structure.
        /// </summary>
        /// <param name="UpdateSystemTime">TRUE if the local time should be updated.</param>
        public void Connect() {
            this.Connect(DefaultTimeoutSeconds);
        }
        public void Connect(int timeoutSeconds) {
            this.Connect(timeoutSeconds, updateSystemTime: false);
        }
        public void Connect(int timeoutSeconds, bool updateSystemTime) {
            if (this.SourceIPAddress == null) {
                this.SourceIPAddress = Utility.GetIpAddress(this.Source, timeoutSeconds);
            }
            if (this.SourceIPAddress == null) return;

            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            using (var sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) {
                var listenEP = new IPEndPoint(IPAddress.Any, 0);
                sendSocket.Bind(listenEP);

                var sendEP = new IPEndPoint(this.SourceIPAddress, 123);
                var epSendEP = (EndPoint)sendEP;

                Initialize();
                // Initialize the transmit timestamp
                this.TransmitTimestamp = DateTime.UtcNow;

                var sendResult = sendSocket.BeginSendTo(NTPData, 0, NTPData.Length, SocketFlags.None, sendEP, null, null);
                if (sendResult.CompletedSynchronously || sendResult.AsyncWaitHandle.WaitOne(timeout)) {
                    this.DestinationTimestamp = DateTime.UtcNow;

                    var receiveResult = sendSocket.BeginReceiveFrom(NTPData, 0, NTPData.Length, SocketFlags.None, ref epSendEP, null, null);
                    if (receiveResult.CompletedSynchronously || receiveResult.AsyncWaitHandle.WaitOne(timeout)) {
                        if (this.IsResponseValid) {
                            if (updateSystemTime) {
                                SetTime();
                            }
                        }
                        else {
                            this.ToStringInfo.AppendLine($"Invalid response from: {this.Source} ");
                        }
                    }
                }
                else {
                    this.ToStringInfo.AppendLine($"Timeout attempting to connect to: {this.Source}");
                }
            }
        }

        /// <summary>
        /// Compute the number of milliseconds, given the offset of a 8-byte array
        /// </summary>
        private ulong GetMilliSeconds(byte offset) {
            ulong intpart = 0, fractpart = 0;

            for (int index = 0; index <= 3; index++) {
                intpart = 256 * intpart + NTPData[offset + index];
            }
            for (int index = 4; index <= 7; index++) {
                fractpart = 256 * fractpart + NTPData[offset + index];
            }
            ulong milliseconds = intpart * 1000 + (fractpart * 1000) / 0x100000000L;
            return milliseconds;
        }

        /// <summary>
        /// Initialize the SNTP client  Sets up data structure and prepares for connection.
        /// </summary>
        private void Initialize() {
            this.ToStringInfo = new StringBuilder();
            // Set version number to 4 and Mode to 3 (client)
            this.NTPData[0] = 0x1B;
            // Initialize all other fields with 0
            for (int index = 1; index < SNTPDataLength; index++) {
                this.NTPData[index] = 0;
            }
        }

        /// <summary>
        /// Set the date part of the SNTP data
        /// </summary>
        /// <param name="offset">Offset at which the date part of the SNTP data is</param>
        /// <param name="date">The date</param>
        private void SetDate(byte offset, DateTime date) {
            ulong fractpart = 0;
            ulong intpart = 0;
            // January 1, 1900 12:00 AM
            var startOfCentury = new DateTime(1900, 1, 1, 0, 0, 0);

            ulong milliseconds = (ulong)(date - startOfCentury).TotalMilliseconds;
            intpart = milliseconds / 1000;
            fractpart = ((milliseconds % 1000) * 0x100000000L) / 1000;

            ulong temp = intpart;
            for (int i = 3; i >= 0; i--) {
                this.NTPData[offset + i] = (byte)(temp % 256);
                temp = temp / 256;
            }

            temp = fractpart;
            for (int i = 7; i >= 4; i--) {
                this.NTPData[offset + i] = (byte)(temp % 256);
                temp = temp / 256;
            }
        }

        public override string ToString() {

            if (this.IsResponseValid && this.ToStringInfo.Length == 0) {

                this.ToStringInfo.AppendLine($"Source: {this.Source}");
                this.ToStringInfo.AppendLine($"Source IP Address: {this.SourceIPAddress}");
                this.ToStringInfo.AppendLine($"Source Server Role: {this.ServerMode}");
                this.ToStringInfo.AppendLine($"Leap Indicator: {this.LeapIndicator}");
                this.ToStringInfo.AppendLine($"Version: {this.VersionNumber}");
                this.ToStringInfo.AppendLine($"Stratum: {this.Stratum}");
                this.ToStringInfo.AppendLine($"Local time (UTC): {this.TransmitTimestamp.YMDHMSFFFFFFriendly()}");
                this.ToStringInfo.AppendLine($"Precision: {this.Precision}");
                this.ToStringInfo.AppendLine($"Poll Interval: {this.PollInterval}");
                this.ToStringInfo.AppendLine($"Root Delay: {this.RootDelay}");
                this.ToStringInfo.AppendLine($"Root Dispersion: {this.RootDispersion}");
                this.ToStringInfo.AppendLine($"Round Trip Delay: {this.RoundTripDelay}");
                this.ToStringInfo.AppendLine($"Local Clock Offset: {this.LocalClockOffset}");
            }

            return this.ToStringInfo.ToString();
        }

        #region Unmanaged code to set the local system time
        /// <summary>
        /// Used by SetSystemTime
        /// </summary>
        [StructLayoutAttribute(LayoutKind.Sequential)]
        private struct SYSTEMTIME {
            public short year;
            public short month;
            public short dayOfWeek;
            public short day;
            public short hour;
            public short minute;
            public short second;
            public short milliseconds;
        }

        /// <summary>
        /// Sets the Local Time. 
        /// Local Time is different from the System Time, and DateTime.Now.
        /// They are different by definition.  DateTime.Now will take the TimeZone and 
        /// Daylight Savings into account.
        /// </summary>
        [DllImport("kernel32.dll")]
        static extern bool SetLocalTime(ref SYSTEMTIME time);

        /// <summary>
        /// Set the LocalTime according to transmit timestamp
        /// </summary>
        private void SetTime() {
            SYSTEMTIME st;
            var trts = DateTime.UtcNow.Add(this.LocalClockOffset);

            st.year = (short)trts.Year;
            st.month = (short)trts.Month;
            st.dayOfWeek = (short)trts.DayOfWeek;
            st.day = (short)trts.Day;
            st.hour = (short)trts.Hour;
            st.minute = (short)trts.Minute;
            st.second = (short)trts.Second;
            st.milliseconds = (short)trts.Millisecond;

            SetLocalTime(ref st);
        }
        #endregion

        #endregion

    }
}
