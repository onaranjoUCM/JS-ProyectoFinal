using System;
using System.Collections.Generic;

namespace AssetPackage
{
    public class ConcurrentQueue<T>
    {
        private readonly object syncLock = new object();
        private readonly LinkedList<T> queue;

        public ConcurrentQueue()
        {
            queue = new LinkedList<T>();
        }

        public int Count
        {
            get
            {
                lock (syncLock)
                {
                    return queue.Count;
                }
            }
        }

        public T[] Peek(uint n = 1)
        {
            lock (syncLock)
            {
                n = Math.Min((uint) queue.Count, n);

                var tmp = new T[n];

                var it = queue.First;
                for (uint i = 0; i < n; i++)
                {
                    tmp[i] = it.Value;
                    it = it.Next;
                }

                return tmp;
            }
        }

        public void Enqueue(T obj)
        {
            lock (syncLock)
            {
                queue.AddLast(obj);
            }
        }

        public T Dequeue()
        {
            lock (syncLock)
            {
                var tmp = queue.First.Value;
                queue.RemoveFirst();
                return tmp;
            }
        }

        public void Dequeue(int n)
        {
            lock (syncLock)
            {
                for (var i = 0; i < n; i++)
                {
                    queue.RemoveFirst();
                }
            }
        }


        public void Clear()
        {
            lock (syncLock)
            {
                queue.Clear();
            }
        }

        public T[] CopyToArray()
        {
            lock (syncLock)
            {
                if (queue.Count == 0)
                {
                    return new T[0];
                }

                var values = new T[queue.Count];
                queue.CopyTo(values, 0);
                return values;
            }
        }

        public static ConcurrentQueue<T> InitFromArray(IEnumerable<T> initValues)
        {
            var queue = new ConcurrentQueue<T>();

            if (initValues == null)
            {
                return queue;
            }

            foreach (var val in initValues)
            {
                queue.Enqueue(val);
            }

            return queue;
        }
    }
}