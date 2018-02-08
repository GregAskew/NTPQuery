namespace NTPQuery {

    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    #endregion

    class Program {
        static void Main(string[] args) {

            #region Get command line arguments
            string ipAddress = null;
            string hostName = null;

            if (args.Length == 0) {
                PrintUsage();
                return;
            }
            if (args.Length == 1) {
                hostName = args[0].Trim();
            }
            else if (args.Length > 1) {
                ipAddress = args[0].Trim();
                hostName = args[1].Trim();
            } 

            if (string.IsNullOrWhiteSpace(hostName)) {
                PrintUsage();
                return;
            }
            #endregion

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            var ntpClient = (ipAddress == null) 
                ? new NTPClient(hostName)
                : new NTPClient(ipAddress, hostName);

            ntpClient.Connect();

            if (ntpClient.IsResponseValid) {
                Console.WriteLine($"NTP Server response:{Environment.NewLine}{ntpClient.ToString()}");
            }
            else {
                Console.WriteLine("Invalid NTP Server response.");
            }
        }

        private static void PrintUsage() {
            Console.WriteLine("Usage:");
            Console.WriteLine("ntpquery hostname");
            Console.WriteLine("ntpquery ipaddress hostname");
        }

        /// <summary>
        /// Unhandled Exception Logger
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            Exception exception = e.ExceptionObject as Exception;
            Console.WriteLine($"Unhandled Exception: {exception.VerboseExceptionString()}");
        }
    }
}
