using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SignalRChatApp
{
    public abstract class IActionBase
    {

        public virtual bool CanAction()
        {
            return true;
        }

        public async virtual Task DoActions(Agent agent)
        {
            await Task.CompletedTask;
        }
    }
}
