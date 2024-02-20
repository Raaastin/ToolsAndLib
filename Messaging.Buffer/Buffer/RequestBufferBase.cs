﻿using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace Messaging.Buffer.Buffer
{
    /// <summary>
    /// Base class for buffer
    /// </summary>
    public abstract class RequestBufferBase<TRequest, TResponse>
        where TRequest : RequestBase
        where TResponse : ResponseBase
    {

        #region Fields
        /// <summary>
        /// Service base with subscribe and send methods
        /// </summary>
        protected readonly IMessaging _messaging;
        /// <summary>
        /// Buffer timeout (ms). Buffer ends when all responses are received, or after this delay.
        /// </summary>
        public int timeoutMs { get; set; } = 10000;
        /// <summary>
        /// Unique identifier for buffer and request.
        /// </summary>
        public string CorrelationId { get; set; }
        /// <summary>
        /// Reference of the request sent.
        /// </summary>
        public TRequest Request { get; private set; }
        /// <summary>
        /// Collection of TaskCompletionSource. Hold responses.
        /// </summary>
        private ConcurrentDictionary<string, TaskCompletionSource<TResponse>> TcsList { get; set; } = new();
        /// <summary>
        /// Collection of response received
        /// </summary>
        protected List<TResponse> ResponseCollection => TcsList.Where(x => !x.Value.Task.IsCanceled).Select(x => x.Value.Task.Result).ToList();

        private object _lock = new object();

        #endregion

        /// <summary>
        /// Method fired when a response is received
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="value"></param>
        private void OnResponse(object sender, ReceivedEventArgs eventArgs)
        {
            var chargerCacheResponse = JsonConvert.DeserializeObject<TResponse>(eventArgs.Value);
            if (chargerCacheResponse.CorrelationId != CorrelationId)
            {
                // Skip: The response received is not for this Buffer.                
                return;
            }

            lock (_lock)
                foreach (var tcs in TcsList)
                {
                    var responseComplete = tcs.Value.TrySetResult(chargerCacheResponse);
                    if (responseComplete)
                        break;
                }
        }

        /// <summary>
        /// Send the request, wait for all responses, return an aggregated response
        /// </summary>
        /// <returns></returns>
        public async Task<TResponse> SendRequestAsync()
        {
            if (Request is null)
                throw new ArgumentNullException("Request field must be set before sending the request.");

            try
            {
                // Step 1: Subscribe for incoming responses
                await _messaging.SubscribeResponseAsync(CorrelationId, OnResponse);

                lock (_lock)
                {
                    // Step 2: SendRequest
                    var received = _messaging.PublishRequestAsync(CorrelationId, Request);

                    // Add as much TCS as needed
                    for (long i = 0; i < received.Result; i++)
                    {
                        TcsList.TryAdd(Guid.NewGuid().ToString(), new TaskCompletionSource<TResponse>());
                    }
                }

                // Step 3: wait 
                await WaitAllResponse();
            }
            finally
            {
                // Step 3: Unsubscribe for response
                await _messaging.UnsubscribeResponseAsync(CorrelationId);
            }

            // Step 4: Aggregate responses
            var result = Aggregate();

            return result;
        }

        /// <summary>
        /// Wait all TcsList to complete, or timeout
        /// </summary>
        /// <returns></returns>
        protected async Task<bool> WaitAllResponse()
        {
            var result = true;
            var taskDelay = Task.Delay(timeoutMs);
            await Task.WhenAny(Task.WhenAll(TcsList.Select(x => x.Value.Task)), taskDelay);
            if (taskDelay.IsCompleted)
            {
                result = false;
                foreach (var tcs in TcsList.Select(x => x.Value))
                    tcs.TrySetCanceled();
            }
            return result;
        }

        /// <summary>
        /// Combine all TResponse from ResponseCollection into 1 single TResponse
        /// </summary>
        protected abstract TResponse Aggregate();

        public RequestBufferBase(IMessaging messaging, TRequest request)
        {
            _messaging = messaging;
            Request = request;
            CorrelationId = Guid.NewGuid().ToString();
        }
    }
}