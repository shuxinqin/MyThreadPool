using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyThreadPool
{
    public class WorkerThreadPool : IThreadPool, IDisposable
    {
        readonly object _lockObject = new object();
        bool _disposed = false;

        bool _spin = false; //spin 每调用一次 QueueWorkItem 方法都会将 spin 设为 false，以通知计时线程停止循环了
        double _keepAliveTime = -1;//等待时间，表示从最后一个活动的线程执行完任务后开始计时到一定的时间内都没有接受到任何任务，则释放掉池内的所有线程
        DateTime _releaseTime = DateTime.Now;//释放线程的时间点

        int _threads;
        List<WorkerThread> _allThreads;
        List<WorkerThread> _workingTreads;
        Queue<WorkerThread> _freeTreads;
        Queue<WorkItem> _workQueue;

        List<ReleaseThreadsRecord> _releaseThreadsRecords = new List<ReleaseThreadsRecord>();

        /// <summary>
        /// 创建一个线程池，默认 Threads 为 25
        /// </summary>
        public WorkerThreadPool()
            : this(25)
        {

        }
        /// <summary>
        /// 创建一个大小为 threads 的线程池
        /// </summary>
        /// <param name="threads"></param>
        public WorkerThreadPool(int threads)
        {
            if (threads < 1)
                throw new ArgumentException("threads 小于 1");

            this._threads = threads;
            this._allThreads = new List<WorkerThread>(threads);
            this._workingTreads = new List<WorkerThread>(threads);
            this._freeTreads = new Queue<WorkerThread>(threads);
            this._workQueue = new Queue<WorkItem>();
        }

        /// <summary>
        /// 
        /// </summary>
        ~WorkerThreadPool()
        {
            this.Dispose();
        }

        /// <summary>
        /// 线程池大小
        /// </summary>
        public int Threads
        {
            get
            {
                return this.GetPoolSize();
            }
            set
            {
                this.SetPoolSize(value);
            }
        }

        /// <summary>
        /// 一个以毫秒为单位的值，表示从最后一个活动的线程执行完任务后开始计时，在指定的时间内线程池都没有接受到任何任务，则释放掉池内的所有线程。若设置值小于 0，则不会释放池内线程。如未指定，默认为 -1。
        /// </summary>
        public double KeepAliveTime
        {
            get
            {
                return this._keepAliveTime;
            }
            set
            {
                this._keepAliveTime = value;
            }
        }

        int GetPoolSize()
        {
            return this._threads;
        }
        void SetPoolSize(int threads)
        {
            if (threads < 1)
                throw new ArgumentException("threads 小于 1");

            lock (this._lockObject)
            {
                this._threads = threads;
                this.AdjustPool();

                WorkerThread workerThread = null;
                WorkItem workItem = null;
                while (this.TryGetWorkerThreadAndWorkItem(out  workerThread, out  workItem, false))
                {
                    this.ActivateWorkerThread(workerThread, workItem);
                    workerThread = null;
                    workItem = null;
                }
            }
        }

        /// <summary>
        /// 获取当前线程池内的空闲线程数量
        /// </summary>
        /// <returns></returns>
        public int GetAvailableThreads()
        {
            lock (this._lockObject)
            {
                if (this._threads <= this._workingTreads.Count)
                    return 0;

                int r = this._threads - this._workingTreads.Count;
                return r;
            }
        }

        /// <summary>
        /// 获取当前线程池内工作项总数
        /// </summary>
        /// <returns></returns>
        public int GetWorkCount()
        {
            lock (this._lockObject)
            {
                return this._workQueue.Count + this._workingTreads.Count;
            }
        }

        /// <summary>
        /// 向线程池中添加工作项
        /// </summary>
        /// <param name="callback"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        public bool QueueWorkItem(WaitCallback callback, object state)
        {
            if (callback == null)
                return false;

            WorkerThread workerThread = null;
            WorkItem workItem = null;

            lock (this._lockObject)
            {
                CheckDisposed();

                if (this._workQueue.Count == int.MaxValue)
                    return false;

                this._workQueue.Enqueue(new WorkItem(callback, state));
                this._spin = false;

                if (!this.TryGetWorkerThreadAndWorkItem(out  workerThread, out  workItem, false))
                {
                    return true;
                }
            }

            this.ActivateWorkerThread(workerThread, workItem);
            return true;
        }

        public List<ReleaseThreadsRecord> GetReleaseThreadsRecords()
        {
            lock (this._lockObject)
            {
                List<ReleaseThreadsRecord> list = new List<ReleaseThreadsRecord>(this._releaseThreadsRecords.Count);
                foreach (var releaseThreadsRecord in this._releaseThreadsRecords)
                {
                    list.Add(releaseThreadsRecord);
                }
                return list;
            }
        }

        /// <summary>
        /// 该方法必须在 locked 下执行
        /// </summary>
        /// <param name="workerThread"></param>
        /// <param name="workItem"></param>
        /// <param name="workerThreadCall">是否是当前池内的线程调用该方法</param>
        /// <returns></returns>
        bool TryGetWorkerThreadAndWorkItem(out WorkerThread workerThread, out WorkItem workItem, bool workerThreadCall)
        {
            workerThread = null;
            workItem = null;

            if (this._workQueue.Count > 0)
            {
                if (this._freeTreads.Count > 0)
                {
                    workerThread = this._freeTreads.Dequeue();
                    workItem = this._workQueue.Dequeue();
                    this._workingTreads.Add(workerThread);

                    return true;
                }
                else
                {
                    if (this._allThreads.Count < this._threads)
                    {
                        workerThread = new WorkerThread();
                        workItem = this._workQueue.Dequeue();
                        this._allThreads.Add(workerThread);
                        this._workingTreads.Add(workerThread);

                        return true;
                    }
                    return false;
                }
            }
            else
            {
                if (!workerThreadCall)
                    return false;

                double t = this._keepAliveTime;
                if (t < 0)
                {
                    this._workQueue.TrimExcess();
                    return false;
                }

                //此代码块只有当前池内的线程完成工作了以后访问到，从 QueueWorkItem 方法调用该方法是不会执行此代码块的，因为 this.workQueue.Count > 0
                if (this._freeTreads.Count == this._allThreads.Count && this._workingTreads.Count == 0 && this._freeTreads.Count > 0)
                {
                    /*
                     *能执行到这，说明池内没有了任何任务，并且是最后一个活动线程执行完毕
                     *此时从池中取出一个线程来执行 Tick 方法
                     */
                    DateTime now = DateTime.Now;
                    int threadId = Thread.CurrentThread.ManagedThreadId;
                    if (this._allThreads.Any(a => a.ThreadId == threadId))//既然只有当前池内的线程能访问到这，这句判断是不是有点多余了- -
                    {
                        workerThread = this._freeTreads.Dequeue();//弹出一个 WorkerThread 对象，此时不需将弹出的 WorkerThread 对象放入 workingTreads 队列中，因为该对象是供池内自身计时用，相对外界是不知道的，保证外界调用 GetAvailableThreads 方法能得到一个合理的结果
                        workItem = new WorkItem((state) =>
                        {
                            this.Tick((WorkerThread)state);
                        }, workerThread);

                        this._spin = true;
                        try
                        {
                            this._releaseTime = now.AddMilliseconds(t);//设置待释放线程的时间点
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            this._releaseTime = DateTime.MaxValue;
                        }

                        return true;
                    }
                }

                return false;
            }
        }

        void ActivateWorkerThread(WorkerThread workerThread, WorkItem workItem)
        {
            workerThread.SetWork(workItem.Execute);
            workerThread.Complete += this.WorkComplete;
            workerThread.Activate();
        }
        void WorkComplete(WorkerThread workerThread)
        {
            //避免无法调用终结器，务必将  this.WorkComplete 从 workerThread.Complete 中移除，取出 workerThread 的时候再加上
            workerThread.Complete -= this.WorkComplete;
            if (this._disposed)
                return;

            WorkerThread nextWorkerThread = null;
            WorkItem nextWorkItem = null;

            lock (this._lockObject)
            {
                if (this._disposed)
                    return;

                this._workingTreads.Remove(workerThread);
                this._freeTreads.Enqueue(workerThread);
                this.AdjustPool();

                if (!this.TryGetWorkerThreadAndWorkItem(out  nextWorkerThread, out  nextWorkItem, true))
                {
                    return;
                }
            }

            this.ActivateWorkerThread(nextWorkerThread, nextWorkItem);
        }

        /// <summary>
        /// 该方法必须在 locked 下执行
        /// </summary>
        void AdjustPool()
        {
            while (this._allThreads.Count > this._threads && this._freeTreads.Count > 0)
            {
                WorkerThread workerThread = this._freeTreads.Dequeue();
                this._allThreads.Remove(workerThread);
                workerThread.Dispose();
            }
        }

        /// <summary>
        /// 自旋计时，直到收到停止自旋的消息或者达到了释放池内的线程时刻
        /// </summary>
        /// <param name="workerThread"></param>
        void Tick(WorkerThread workerThread)
        {
            DateTime releaseTimeTemp = this._releaseTime;
            while (true)
            {
                lock (this._lockObject)
                {
                    if (!this._spin)
                    {
                        //spin==false,即说明收到了停止计时的通知                        
                        break;
                    }
                    if (DateTime.Now >= releaseTimeTemp)
                    {
                        //此时停止计时，开始释放线程，在释放线程前先把之前加上的 WorkComplete 给取消，这样就不会执行 WorkComplete 方法了，循环终止后 当前线程也自然执行完毕
                        workerThread.Complete -= this.WorkComplete;

                        //释放所有的线程，也包括当前线程
                        this.ReleaseThreads();
                        this._spin = false;
                        //对于运行到这里，因为所有的线程已经被释放（包括当前线程），所以运行完这个方法，当前线程也自然运行结束了
                        break;
                    }
                }
                Thread.Sleep(1);
            }
        }

        void ReleaseThreads()
        {
            ReleaseThreadsRecord releaseThreadsRecord = new ReleaseThreadsRecord(DateTime.Now);

            foreach (WorkerThread thread in this._allThreads)
            {
                try
                {
                    thread.Dispose();
                }
                catch
                {
                }

                releaseThreadsRecord.ThreadIds.Add(thread.ThreadId);
            }

            releaseThreadsRecord.ThreadIds.TrimExcess();
            this._releaseThreadsRecords.Add(releaseThreadsRecord);

            this._allThreads.Clear();
            this._freeTreads.Clear();
            this._workingTreads.Clear();
            this._workQueue.Clear();
            this._workQueue.TrimExcess();
        }

        void CheckDisposed()
        {
            if (this._disposed)
                throw new ObjectDisposedException(this.GetType().Name);
        }

        /// <summary>
        /// 销毁当前对象资源，调用此方法会强制终止池内的所有线程
        /// </summary>
        public void Dispose()
        {
            if (this._disposed)
                return;

            //释放所有线程
            lock (this._lockObject)
            {
                if (this._disposed)
                    return;
                this.ReleaseThreads();
                this._threads = 0;
                this._disposed = true;
            }

            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="disposing"></param>
        protected virtual void Dispose(bool disposing)
        {
        }


        class WorkItem
        {
            bool _executed = false;
            WaitCallback _callback;
            object _state;
            public WorkItem(WaitCallback callback, object state)
            {
                if (callback == null)
                    throw new ArgumentNullException("callback");

                this._callback = callback;
                this._state = state;
            }

            public void Execute()
            {
                if (this._executed)
                    throw new Exception("当前 WorkItem 已经执行");

                this._executed = true;
                this._callback(this._state);
            }
        }

        public class ReleaseThreadsRecord
        {
            public ReleaseThreadsRecord()
            {
                this.ThreadIds = new List<int>();
            }
            public ReleaseThreadsRecord(DateTime releaseTime)
            {
                this.ReleaseTime = releaseTime;
                this.ThreadIds = new List<int>();
            }

            /// <summary>
            /// 释放时间
            /// </summary>
            public DateTime ReleaseTime { get; set; }
            /// <summary>
            /// 被释放线程的 Id 集合
            /// </summary>
            public List<int> ThreadIds { get; set; }

            public ReleaseThreadsRecord Clone()
            {
                ReleaseThreadsRecord record = new ReleaseThreadsRecord(this.ReleaseTime);
                record.ThreadIds = this.ThreadIds.ToList();

                return record;
            }
        }
    }
}
