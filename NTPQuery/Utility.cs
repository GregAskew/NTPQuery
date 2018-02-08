namespace NTPQuery {

    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Diagnostics;
    using System.Linq;
    using System.Net;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks; 
    #endregion

    public static class Utility {

        #region Members
        private static readonly int DNSLookupTimeoutSeconds;
        #endregion

        #region Constructor
        static Utility() {
            DNSLookupTimeoutSeconds = ConfigurationManager.AppSettings["DNSLookupTimeoutSeconds"] != null
                ? Convert.ToInt32(ConfigurationManager.AppSettings["DNSLookupTimeoutSeconds"])
                : 5;
        }
        #endregion

        #region Methods
        [DebuggerStepThroughAttribute]
        [MethodImpl(MethodImplOptions.NoOptimization)]
        public static string CurrentMethodName() {
            var frame = new StackFrame(1);
            var method = frame.GetMethod();
            var type = method.DeclaringType;
            var name = method.Name;

            return type + "::" + name + "(): ";
        }

        public static IPHostEntry GetHostEntry(string hostName) {
            return GetHostEntry(hostName, DNSLookupTimeoutSeconds);
        }

        public static IPHostEntry GetHostEntry(string hostName, int timeoutSeconds) {
            Debug.WriteLine($"[ThreadID: {Thread.CurrentThread.ManagedThreadId}] {Utility.CurrentMethodName()} DC: {hostName} TimeoutSeconds: {timeoutSeconds}");

            IPHostEntry ipHostEntry = null;
            try {
                var result = Dns.BeginGetHostEntry(hostName, null, null);
                if (result.CompletedSynchronously || result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds), exitContext: false)) {
                    ipHostEntry = Dns.EndGetHostEntry(result);
                }
            }
            catch (Exception e) {
                Console.WriteLine($"[ThreadID: {Thread.CurrentThread.ManagedThreadId}] {Utility.CurrentMethodName()} WARNING: hostName: {hostName} {e.VerboseExceptionString()}");
            }

            return ipHostEntry;
        }

        public static IPAddress GetIpAddress(string hostName) {
            return GetIpAddress(hostName, DNSLookupTimeoutSeconds);
        }

        public static IPAddress GetIpAddress(string hostName, int timeoutSeconds) {
            IPAddress ipAddress = null;
            try {
                IPHostEntry ipHostEntry = GetHostEntry(hostName, timeoutSeconds);
                if ((ipHostEntry != null) && (ipHostEntry.AddressList != null) && (ipHostEntry.AddressList.Length > 0)) {

                    if (ipHostEntry.AddressList.Length > 1) {
                        Console.WriteLine($"[ThreadID: {Thread.CurrentThread.ManagedThreadId}] {Utility.CurrentMethodName()} WARNING: hostName: {hostName} Multiple ip addresses returned:");

                        foreach (var address in ipHostEntry.AddressList) {
                            Console.WriteLine($"[ThreadID: {Thread.CurrentThread.ManagedThreadId}] {Utility.CurrentMethodName()} WARNING: hostName: {hostName} Address: {address.ToString()}");
                        }
                    }
                    else {
                        ipAddress = ipHostEntry.AddressList[0];
                    }
                }
                else {
                    Console.WriteLine($"[ThreadID: {Thread.CurrentThread.ManagedThreadId}] {Utility.CurrentMethodName()} WARNING: Unable to resolve hostName: {hostName} to an IP Address.");
                }
            }
            catch (Exception e) {
                Console.WriteLine($"[ThreadID: {Thread.CurrentThread.ManagedThreadId}] {Utility.CurrentMethodName()} WARNING: hostName: {hostName} {e.VerboseExceptionString()}");
            }

            return ipAddress;
        } 
        #endregion
    }
}
