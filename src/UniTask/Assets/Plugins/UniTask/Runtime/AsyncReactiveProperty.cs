﻿using Cysharp.Threading.Tasks.Linq;
using System;
using System.Threading;

namespace Cysharp.Threading.Tasks
{
    public interface IReadOnlyAsyncReactiveProperty<T> : IUniTaskAsyncEnumerable<T>
    {
        T Value { get; }
        IUniTaskAsyncEnumerable<T> WithoutCurrent();
    }

    public interface IAsyncReactiveProperty<T> : IReadOnlyAsyncReactiveProperty<T>
    {
        new T Value { get; set; }
    }

    [Serializable]
    public class AsyncReactiveProperty<T> : IAsyncReactiveProperty<T>, IDisposable
    {
        TriggerEvent<T> triggerEvent;

#if UNITY_2018_3_OR_NEWER
        [UnityEngine.SerializeField]
#endif
        T latestValue;

        public T Value
        {
            get
            {
                return latestValue;
            }
            set
            {
                this.latestValue = value;
                triggerEvent.SetResult(value);
            }
        }

        public AsyncReactiveProperty(T value)
        {
            this.latestValue = value;
            this.triggerEvent = default;
        }

        public IUniTaskAsyncEnumerable<T> WithoutCurrent()
        {
            return new WithoutCurrentEnumerable(this);
        }

        public IUniTaskAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken)
        {
            return new Enumerator(this, cancellationToken, true);
        }

        public void Dispose()
        {
            triggerEvent.SetCompleted();
        }

        class WithoutCurrentEnumerable : IUniTaskAsyncEnumerable<T>
        {
            readonly AsyncReactiveProperty<T> parent;

            public WithoutCurrentEnumerable(AsyncReactiveProperty<T> parent)
            {
                this.parent = parent;
            }

            public IUniTaskAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                return new Enumerator(parent, cancellationToken, false);
            }
        }

        sealed class Enumerator : MoveNextSource, IUniTaskAsyncEnumerator<T>, ITriggerHandler<T>
        {
            static Action<object> cancellationCallback = CancellationCallback;

            readonly AsyncReactiveProperty<T> parent;
            readonly CancellationToken cancellationToken;
            readonly CancellationTokenRegistration cancellationTokenRegistration;
            T value;
            bool isDisposed;
            bool firstCall;

            public Enumerator(AsyncReactiveProperty<T> parent, CancellationToken cancellationToken, bool publishCurrentValue)
            {
                this.parent = parent;
                this.cancellationToken = cancellationToken;
                this.firstCall = publishCurrentValue;

                parent.triggerEvent.Add(this);
                TaskTracker.TrackActiveTask(this, 3);

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationTokenRegistration = cancellationToken.RegisterWithoutCaptureExecutionContext(cancellationCallback, this);
                }
            }

            public T Current => value;

            public UniTask<bool> MoveNextAsync()
            {
                // raise latest value on first call.
                if (firstCall)
                {
                    firstCall = false;
                    value = parent.Value;
                    return CompletedTasks.True;
                }

                completionSource.Reset();
                return new UniTask<bool>(this, completionSource.Version);
            }

            public UniTask DisposeAsync()
            {
                if (!isDisposed)
                {
                    isDisposed = true;
                    TaskTracker.RemoveTracking(this);
                    completionSource.TrySetCanceled(cancellationToken);
                    parent.triggerEvent.Remove(this);
                }
                return default;
            }

            public void OnNext(T value)
            {
                this.value = value;
                completionSource.TrySetResult(true);
            }

            public void OnCanceled(CancellationToken cancellationToken)
            {
                DisposeAsync().Forget();
            }

            public void OnCompleted()
            {
                completionSource.TrySetResult(false);
            }

            public void OnError(Exception ex)
            {
                completionSource.TrySetException(ex);
            }

            static void CancellationCallback(object state)
            {
                var self = (Enumerator)state;
                self.DisposeAsync().Forget();
            }
        }
    }
}