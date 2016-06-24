using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyThreadPool
{
    internal class WorkerThread : IDisposable
    {
        Thread _thread;
        AutoResetEvent _waitEvent;
        Action _action;
        bool _disposed = false;

        public WorkerThread()
        {
            this._waitEvent = new AutoResetEvent(false);
            this._thread = new Thread(this.Run);
            this._thread.IsBackground = true;
            this._thread.Start();
        }

        //是否正在执行工作
        public bool IsWorking
        {
            get;
            private set;
        }

        public event Action<WorkerThread> Complete;
        public int ThreadId
        {
            get
            {
                return this._thread.ManagedThreadId;
            }
        }
        public ThreadState ThreadState
        {
            get
            {
                return this._thread.ThreadState;
            }
        }

        public void SetWork(Action act)
        {
            this.CheckDisposed();

            if (this.IsWorking)
                throw new Exception("正在执行工作项");

            this._action = act;
        }

        public void Activate()
        {
            this.CheckDisposed();
            if (this.IsWorking)
                throw new Exception("正在执行工作项");
            if (this._action == null)
                throw new Exception("未设置任何工作项");

            this._waitEvent.Set();
        }

        void Run()
        {
            while (!this._disposed)
            {
                this._waitEvent.WaitOne();
                if (this._disposed)
                    break;

                try
                {
                    this.IsWorking = true;
                    this._action();
                }
                catch (ThreadAbortException)
                {
                    if (!this._disposed)
                        Thread.ResetAbort();
                }
                finally
                {
                    this.IsWorking = false;
                    this._action = null;
                    var completeEvent = this.Complete;
                    if (completeEvent != null)
                    {
                        try
                        {
                            completeEvent(this);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        void CheckDisposed()
        {
            if (this._disposed)
                throw new ObjectDisposedException(this.GetType().Name);
        }

        public void Dispose()
        {
            if (this._disposed)
                return;

            try
            {
                this._disposed = true;
                //注意：只有调用 Dispose() 的不是当前对象维护的线程才调用 Abort
                if (Thread.CurrentThread.ManagedThreadId != this._thread.ManagedThreadId)
                {
                    this._thread.Abort();
                }
            }
            finally
            {
                this._waitEvent.Dispose();
            }
        }
    }
}
