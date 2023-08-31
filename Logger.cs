using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace QuikTester
{
    [DataContract]
    public abstract class Logger
    {
        public Action<string> LogAction;

        public void LogMessage(string message)
        {
            if(LogAction != null) LogAction(message);
        }

    }
}
