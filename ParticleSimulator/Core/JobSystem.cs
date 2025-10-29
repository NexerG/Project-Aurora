using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArctisAurora.EngineWork
{
    public class Job
    {
        public Action Work;
    }

    internal class JobSystem
    {
        private ConcurrentQueue<Job> jobs = new();
        private List<Thread> workers = new();

        public JobSystem(int threadCount)
        {
            for (int i = 0; i < threadCount; i++)
            {
                Thread thread = new Thread(() =>
                {
                    while (true)
                    {
                        if (jobs.TryDequeue(out Job job))
                        {
                            job.Work();
                        }
                    }
                });
                thread.Start();
                workers.Add(thread);
            }
        }

        public void Schedule(Action work)
        {
            jobs.Enqueue(new Job { Work = work });
        }
    }
}
