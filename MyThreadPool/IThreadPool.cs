using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MyThreadPool
{
    public interface IThreadPool : IDisposable
    {
        /// <summary>
        /// 线程池大小
        /// </summary>
        int Threads { get; set; }
        /// <summary>
        /// 一个以毫秒为单位的值，表示从最后一个活动的线程执行完任务后开始计时，在指定的时间内线程池都没有接收到任何任务，则释放掉池内的所有线程。若设置值小于 0，则不会释放池内线程。如未指定，默认为 -1。
        /// </summary>
        double KeepAliveTime { get; set; }
        /// <summary>
        /// 获取当前线程池内的空闲线程数量
        /// </summary>
        /// <returns></returns>
        int GetAvailableThreads();
        /// <summary>
        /// 获取当前线程池内工作项总数
        /// </summary>
        /// <returns></returns>
        int GetWorkCount();
        /// <summary>
        /// 向线程池中添加工作项
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        bool QueueWorkItem(WaitCallback callback, object state);
    }
}
