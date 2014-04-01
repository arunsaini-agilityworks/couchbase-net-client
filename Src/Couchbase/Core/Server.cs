﻿using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using Common.Logging;
using Couchbase.IO;
using Couchbase.IO.Operations;
using Couchbase.N1QL;
using Couchbase.Views;

namespace Couchbase.Core
{
    internal class Server : IServer
    {
        //todo review this as a best practice
        private static readonly ILog Log = LogManager.GetCurrentClassLogger();
        private readonly IOStrategy _ioStrategy;
        private bool _disposed;

        public Server(IOStrategy ioStrategy) : 
            this(ioStrategy, 
            new ViewClient(new HttpClient(), new JsonDataMapper()), 
            new QueryClient(new HttpClient(), new JsonDataMapper()))
        {
        }

        public Server(IOStrategy ioStrategy, IViewClient viewClient)
        {
            _ioStrategy = ioStrategy;
            ViewClient = viewClient;
        }

        public Server(IOStrategy ioStrategy, IViewClient viewClient, IQueryClient queryClient)
        {
            _ioStrategy = ioStrategy;
            ViewClient = viewClient;
            QueryClient = queryClient;
        }

        public IPEndPoint EndPoint { get { return _ioStrategy.EndPoint; } }

        public IConnectionPool ConnectionPool { get { return _ioStrategy.ConnectionPool; } }

        public uint DirectPort { get; private set; }

        public uint ProxyPort { get; private set; }

        public uint Replication { get; private set; }

        public bool Active { get; private set; }

        public bool Healthy { get; private set; }

        public IOperationResult<T> Send<T>(IOperation<T> operation)
        {
            Log.Debug(m=>m("Starting operation for key {0}", operation.Key));

            var result = operation.GetResult();
            var task = _ioStrategy.ExecuteAsync(operation);

            try
            {
                task.Wait(); //TODO provide a timeout
                result = task.Result;
            }
            catch (AggregateException ae)
            {
                ae.Flatten().Handle(e =>
                {
                    Log.Error(e);
                    return true;
                });
            }
            return result;
        }

        public IViewResult<T> Send<T>(IViewQuery query)
        {
            return ViewClient.Execute<T>(query);
        }

        IQueryResult<T> IServer.Send<T>(string query)
        {
            //TODO make right - this isn't.
            var uri = new Uri(string.Concat("http://", EndPoint.Address, ":", 8093, "/query"));
            return QueryClient.Query<T>(uri, query);
        }

        public IQueryClient QueryClient { get; private set; }

        public IViewClient ViewClient { get; private set; }

        public static IPEndPoint GetEndPoint(string server)
        {
            const int maxSplits = 2;
            var address = server.Split(':');
            if (address.Count() != maxSplits)
            {
                throw new ArgumentException("server");
            }
            IPAddress ipAddress;
            if (!IPAddress.TryParse(address[0], out ipAddress))
            {
                throw new ArgumentException("ipAddress");
            }
            int port;
            if (!int.TryParse(address[1], out port))
            {
                throw new ArgumentException("port");
            }
            return new IPEndPoint(ipAddress, port);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        void Dispose(bool disposing)
        {
            if (_disposed)
            {
                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
                
                _ioStrategy.Dispose();
                _disposed = true;
            }
        }

        ~Server()
        {
            Dispose(false);
        }
    }
}
