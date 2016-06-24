using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyThreadPool
{
    public class ThreadPoolFactory
    {
        public static IThreadPool CreatePool()
        {
            IThreadPool threadPool = null;
            threadPool = new WorkerThreadPool();
            return threadPool;
        }
    }
}
