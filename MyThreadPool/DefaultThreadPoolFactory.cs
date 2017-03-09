namespace MyThreadPool
{
    public class DefaultThreadPoolFactory : IThreadPoolFactory
    {
        int _threads;
        /// <summary>
        /// 创建一个大小为 25 的线程池
        /// </summary>
        public DefaultThreadPoolFactory()
            : this(25)
        {
        }

        /// <summary>
        /// 创建一个大小为 threads 的线程池
        /// </summary>
        /// <param name="threads"></param>
        public DefaultThreadPoolFactory(int threads)
        {
            this._threads = threads;
        }

        public IThreadPool CreatePool()
        {
            IThreadPool threadPool = null;
            threadPool = new WorkerThreadPool(this._threads);
            return threadPool;
        }
    }
}
