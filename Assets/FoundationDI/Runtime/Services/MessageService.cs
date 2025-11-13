using System;
using System.Collections.Concurrent;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using VContainer;


namespace DarkNaku.FoundationDI
{
    public interface IMessageService
    {
        void Publish<T>(T message);
        void PublishAsync<T>(T message);
        IDisposable Subscribe<T>(Action<T> handler);
        IDisposable SubscribeAsync<T>(Func<T, CancellationToken, UniTask> handler);
    }

    public class MessageService : IMessageService
    {
        private readonly IObjectResolver _objectResolver;
        private readonly ConcurrentDictionary<Type, object> _publishers;
        private readonly ConcurrentDictionary<Type, object> _subscribers;

        public MessageService(IObjectResolver objectResolver)
        {
            _objectResolver = objectResolver;
            _publishers = new();
            _subscribers = new();
        }

        public void Publish<T>(T message)
        {
            var publisher = (IPublisher<T>)_publishers.GetOrAdd(typeof(T),
                _ => _objectResolver.Resolve<IPublisher<T>>());

            publisher?.Publish(message);
        }

        public void PublishAsync<T>(T message) 
        {
            var publisher = (IAsyncPublisher<T>)_publishers.GetOrAdd(typeof(T),
                _ => _objectResolver.Resolve<IAsyncPublisher<T>>());

            publisher?.PublishAsync(message);
        }
        
        public IDisposable Subscribe<T>(Action<T> handler)
        {
            var subscriber = (ISubscriber<T>)_subscribers.GetOrAdd(typeof(T),
                _ => _objectResolver.Resolve<ISubscriber<T>>());

            return subscriber?.Subscribe(handler);

        }

        public IDisposable SubscribeAsync<T>(Func<T, CancellationToken, UniTask> handler)
        {
            var subscriber = (IAsyncSubscriber<T>)_subscribers.GetOrAdd(typeof(T),
                _ => _objectResolver.Resolve<IAsyncSubscriber<T>>());

            return subscriber?.Subscribe(handler);
        }
    }
}