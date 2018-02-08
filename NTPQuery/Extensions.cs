namespace NTPQuery {

    #region Usings
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks; 
    #endregion

    internal static class Extensions {

        [DebuggerStepThroughAttribute]
        public static string VerboseExceptionString<T>(this T exception) where T : Exception {
            var exceptionstring = new StringBuilder();

            exceptionstring.AppendLine($" Exception: {exception.GetType().Name} Message: {exception.Message ?? "NULL"}");
            exceptionstring.AppendLine($" StackTrace: {exception.StackTrace ?? "NULL"}");
            exceptionstring.AppendLine($" TargetSite: {(exception.TargetSite != null ? exception.TargetSite.ToString() : "NULL")}");

            if (exception.InnerException != null) {
                exceptionstring.AppendLine();
                exceptionstring.AppendLine("Inner Exception:");
                exceptionstring.AppendLine(exception.InnerException.VerboseExceptionString());
            }

            return exceptionstring.ToString();
        }

        [DebuggerStepThroughAttribute]
        public static string YMDHMSFriendly(this DateTime dateTime) {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
        }

        [DebuggerStepThroughAttribute]
        public static string YMDHMSFFFFFFriendly(this DateTime dateTime) {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFF");
        }
    }
}
