using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace DwarfCorp
{
    /// <summary>
    /// The Service architecutre divides a task into servers (Service) and subscribers (Subscriber).
    /// The server looks at a list of requests, processes them, and then broadcasts the results
    /// to subscribers. This all happens in parallel.
    /// </summary>
    /// <typeparam Name="TRequest">The type of the request.</typeparam>
    /// <typeparam Name="TResponse">The type of the response</typeparam>
    [JsonObject(IsReference = true)]
    public class Service <TRequest, TResponse> : IDisposable
    {
        [JsonIgnore]
        public ConcurrentQueue<KeyValuePair<uint, TRequest>> Requests = new ConcurrentQueue<KeyValuePair<uint, TRequest>>();

        private readonly Object subscriberlock = new object();

        protected List<Subscriber<TRequest, TResponse>> Subscribers = new List<Subscriber<TRequest, TResponse>>();

        [JsonIgnore]
        public AutoResetEvent NeedsServiceEvent = new AutoResetEvent(false);

        private List<Thread> WorkerThreads = new List<Thread>();

        private bool ExitThreads = false;

        public int ThreadCount;
        public String ServiceName;

        public Service(String ServiceName, int ThreadCount)
        {
            this.ServiceName = ServiceName;
            this.ThreadCount = ThreadCount;
            Restart();
        }

        public void AddSubscriber(Subscriber<TRequest, TResponse> subscriber)
        {
            lock(subscriberlock)
            {
                Subscribers.Add(subscriber);
            }
        }

        public void RemoveSubscriber(Subscriber<TRequest, TResponse> subscriber)
        {
            lock(subscriberlock)
            {
                Subscribers.Remove(subscriber);
            }
        }

        public void Restart()
        {
            try
            {
                Die();
                ExitThreads = false;

                WorkerThreads.Clear();

                for (int i = 0; i < ThreadCount; i++)
                {
                    var thread = new Thread(this.ServiceThread);
                    thread.Name = string.Format("{0} : {1}", ServiceName, i);
                    thread.Start();
                    WorkerThreads.Add(thread);
                }
            }
            catch (global::System.AccessViolationException e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }
               
        public void Die()
        {
            ExitThreads = true;

            if(WorkerThreads != null)
                foreach (var thread in WorkerThreads)
                    thread.Join();
        }

        public bool AddRequest(TRequest request, uint subscriberID)
        {
            Requests.Enqueue(new KeyValuePair<uint, TRequest>(subscriberID, request));
            return true;
        }

        private void ServiceThread()
        {
            EventWaitHandle[] waitHandles =
            {
                NeedsServiceEvent,
                Program.ShutdownEvent
            };

            while (!DwarfGame.ExitGame && !ExitThreads)
            {
                EventWaitHandle wh;
                try
                {
                    wh = Datastructures.WaitFor(waitHandles);
                }
                catch (Exception e)
                {
                    CrashBreadcrumbs.Push("Service[" + ServiceName + "] WaitFor threw: " + e.GetType().Name + " — " + e.Message);
                    Console.Error.WriteLine("[Service:" + ServiceName + "] " + e);
                    continue;
                }

                if (wh == Program.ShutdownEvent)
                    break;

                // Previously Update() had no guard — any request handler exception would crash
                // the worker thread silently and stall the whole service. Catch + log + continue
                // so other requests keep flowing.
                try
                {
                    Update();
                }
                catch (Exception e)
                {
                    CrashBreadcrumbs.Push("Service[" + ServiceName + "] Update threw: " + e.GetType().Name + " — " + e.Message);
                    Console.Error.WriteLine("[Service:" + ServiceName + "] Update: " + e);
                }
            }
        }

        public virtual TResponse HandleRequest(TRequest request)
        {
            return default(TResponse);
        }

        public void BroadcastResponse(TResponse response, uint id)
        {
            lock(subscriberlock)
            {
                foreach(Subscriber<TRequest, TResponse> subscriber in Subscribers.Where(subscriber => subscriber.ID == id))
                {
                    subscriber.Responses.Enqueue(response);
                }
            }
        }

        private void Update()
        {
            while (Requests.Count > 0)
            {
                KeyValuePair<uint, TRequest> req;
                if (!Requests.TryDequeue(out req))
                    break;
 
                TResponse response = HandleRequest(req.Value);
                BroadcastResponse(response, req.Key);
                Thread.Yield();
            }
        }

        public void Dispose()
        {
            if(NeedsServiceEvent != null)
                NeedsServiceEvent.Dispose();
        }
    }
}
