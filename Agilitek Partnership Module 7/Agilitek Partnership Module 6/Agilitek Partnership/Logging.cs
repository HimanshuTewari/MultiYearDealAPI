using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
