#region Copyright notice and license

// Copyright 2014, Google Inc.
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
// 
//     * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//     * Neither the name of Google Inc. nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

#endregion

using System;
using Google.GRPC.Core.Internal;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Google.GRPC.Core.Internal
{
    /// <summary>
    /// Pool of threads polling on the same completion queue.
    /// </summary>
    internal class GrpcThreadPool
    {
        readonly object myLock = new object();
        readonly List<Thread> threads = new List<Thread>();
        readonly int poolSize;
        readonly Action<EventSafeHandle> eventHandler;

        CompletionQueueSafeHandle cq;

        public GrpcThreadPool(int poolSize) {
            this.poolSize = poolSize;
        }

        internal GrpcThreadPool(int poolSize, Action<EventSafeHandle> eventHandler) {
            this.poolSize = poolSize;
            this.eventHandler = eventHandler;
        }

        public void Start() {

            lock (myLock)
            {
                if (cq != null)
                {
                    throw new InvalidOperationException("Already started.");
                }

                cq = CompletionQueueSafeHandle.Create();

                for (int i = 0; i < poolSize; i++)
                {
                    threads.Add(CreateAndStartThread(i));
                }
            }
        }

        public void Stop() {

            lock (myLock)
            {
                cq.Shutdown();

                Console.WriteLine("Waiting for GPRC threads to finish.");
                foreach (var thread in threads)
                {
                    thread.Join();
                }

                cq.Dispose();

            }
        }

        internal CompletionQueueSafeHandle CompletionQueue
        {
            get
            {
                return cq;
            }
        }

        private Thread CreateAndStartThread(int i) {
            Action body;
            if (eventHandler != null)
            {
                body = ThreadBodyWithHandler;
            }
            else
            {
                body = ThreadBodyNoHandler;
            }
            var thread = new Thread(new ThreadStart(body));
            thread.IsBackground = false;
            thread.Start();
            if (eventHandler != null)
            {
                thread.Name = "grpc_server_newrpc " + i;
            }
            else
            {
                thread.Name = "grpc " + i;
            }
            return thread;
        }

        /// <summary>
        /// Body of the polling thread.
        /// </summary>
        private void ThreadBodyNoHandler()
        {
            GRPCCompletionType completionType;
            do
            {
                completionType = cq.NextWithCallback();
            } while(completionType != GRPCCompletionType.GRPC_QUEUE_SHUTDOWN);
            Console.WriteLine("Completion queue has shutdown successfully, thread " + Thread.CurrentThread.Name + " exiting.");
        }

        /// <summary>
        /// Body of the polling thread.
        /// </summary>
        private void ThreadBodyWithHandler()
        {
            GRPCCompletionType completionType;
            do
            {
                using (EventSafeHandle ev = cq.Next(Timespec.InfFuture)) {
                    completionType = ev.GetCompletionType();
                    eventHandler(ev);
                }
            } while(completionType != GRPCCompletionType.GRPC_QUEUE_SHUTDOWN);
            Console.WriteLine("Completion queue has shutdown successfully, thread " + Thread.CurrentThread.Name + " exiting.");
        }
    }

}

