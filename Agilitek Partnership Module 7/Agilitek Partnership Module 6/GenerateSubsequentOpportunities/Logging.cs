using Microsoft.Xrm.Sdk;
using System;

namespace Agilitek_Partnership
{
    public static class Logging
    {
        public static void Log(string message, ITracingService tracingService)
        {
            if(tracingService != null)
            {
                tracingService.Trace(message);
                return;
            }
            Console.WriteLine(message);
           
        }
    }
}
